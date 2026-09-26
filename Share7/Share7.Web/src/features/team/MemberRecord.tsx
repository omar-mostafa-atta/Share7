import { useEffect, useState } from 'react'
import { KeyRound, LogOut, MonitorSmartphone, PauseCircle, PlayCircle, ShieldCheck, UserX } from 'lucide-react'
import { Badge, Button, SkeletonRows, Subhead } from '../../components/ui/primitives'
import { Def, DefList, Note, Segmented } from '../../components/ui/bits'
import { Field, Input, Switch } from '../../components/ui/form'
import { Modal } from '../../components/ui/Modal'
import { ApiError } from '../../lib/errors'
import { useResource } from '../../lib/resource'
import { formatDateTime, formatRelative } from '../../lib/time'
import { toast } from '../../store/toast'
import {
  OUTCOME_LABEL,
  STATUS_INFO,
  STUDIO_ROLES,
  studioAddress,
  team,
  useScopeOptions,
  type MemberDraft,
  type TeamMemberDetail,
} from './data'
import { AccessFields, CredentialsHandover, PasswordField, draftFromScope, draftProblems, staffPasswordProblems } from './MemberForm'

// ===========================================================================
// One member, opened in place under their row
//
// Three columns a SuperAdmin reads across — who they are, what they may do,
// how they sign in — and, under them, the acts that change the account itself,
// with the one that cannot be undone set apart at the far end. Every act here
// answers with the member as they now are, so the record redraws from the
// server's word rather than from a guess.
// ===========================================================================

type Editing = 'profile' | 'access' | null

export function MemberRecord({
  userId,
  onChanged,
  address,
}: {
  userId: string
  onChanged: () => void

  /** The Studio's one address, for the sign-in details handed over after a new password. */
  address: string | null
}) {
  const { data, loading, reload } = useResource<TeamMemberDetail | null>(`/api/admin/team/${userId}`, null)
  const [member, setMember] = useState<TeamMemberDetail | null>(null)
  const [editing, setEditing] = useState<Editing>(null)
  const [busy, setBusy] = useState(false)
  const [handover, setHandover] = useState<string | null>(null)
  const [closing, setClosing] = useState(false)
  const [suspending, setSuspending] = useState(false)
  const [settingPassword, setSettingPassword] = useState(false)

  useEffect(() => setMember(data), [data])

  if (loading && !member) return <SkeletonRows rows={4} />
  if (!member) return <span className="s7-hint">This member could not be loaded.</span>

  /** Runs one act, takes the member it answers with, and tells the list to refresh. */
  async function act<T>(work: () => Promise<T>, done: string, after?: (result: T) => void) {
    setBusy(true)
    try {
      const result = await work()
      if (result && typeof result === 'object' && 'userId' in result) setMember(result as unknown as TeamMemberDetail)
      else await reload()
      after?.(result)
      toast.success(done)
      onChanged()
      return true
    } catch (error) {
      toast.error('That did not go through', error instanceof ApiError ? error.message : undefined)
      if (error instanceof ApiError && error.status === 409) await reload()
      return false
    } finally {
      setBusy(false)
    }
  }

  const closed = member.status === 'Deactivated'
  const status = STATUS_INFO[member.status]

  return (
    <div className="s7-record">
      <div className="s7-record-grid">
        {/* ---- who they are ---- */}
        <section className="s7-stack" style={{ gap: '0.75rem' }}>
          <Subhead>Who they are</Subhead>
          {editing === 'profile' ? (
            <ProfileEditor
              member={member}
              busy={busy}
              onCancel={() => setEditing(null)}
              onSave={(profile) =>
                act(() => team.updateProfile(member.userId, { ...profile, rowVersion: member.rowVersion }), 'Profile saved', () =>
                  setEditing(null),
                )
              }
            />
          ) : (
            <>
              <DefList>
                <Def label="Username">
                  <code className="s7-key">{member.username}</code>
                </Def>
                <Def label="Job title">{member.jobTitle}</Def>
                <Def label="Work email">{member.workEmail}</Def>
                <Def label="Studio opens in">{member.interfaceLanguage === 'ar' ? 'العربية' : 'English'}</Def>
                <Def label="Added">
                  {formatDateTime(member.createdAtUtc)}
                  {member.createdBy ? ` by ${member.createdBy.name}` : ''}
                </Def>
                <Def label="Status">
                  <span className="s7-inline">
                    <Badge tone={status.tone}>{status.label}</Badge>
                    {member.statusChangedAtUtc && member.status !== 'Active' && member.status !== 'Invited' ? (
                      <span className="s7-hint">
                        {formatRelative(member.statusChangedAtUtc)}
                        {member.statusChangedBy ? ` by ${member.statusChangedBy.name}` : ''}
                        {member.statusReason ? ` — “${member.statusReason}”` : ''}
                      </span>
                    ) : null}
                  </span>
                </Def>
              </DefList>
              {closed ? null : (
                <div>
                  <Button variant="ghost" onClick={() => setEditing('profile')}>
                    Change their details
                  </Button>
                </div>
              )}
            </>
          )}
        </section>

        {/* ---- what they may do ---- */}
        <section className="s7-stack" style={{ gap: '0.75rem' }}>
          <Subhead>What they may do</Subhead>
          {editing === 'access' ? (
            <AccessEditor
              member={member}
              busy={busy}
              onCancel={() => setEditing(null)}
              onSave={(draft) =>
                act(() => team.updateAccess(member.userId, draft, member.rowVersion), 'Access changed — it applies to their next click', () =>
                  setEditing(null),
                )
              }
            />
          ) : (
            <>
              <DefList>
                <Def label="Studio role">
                  <span className="s7-stack-tight">
                    <strong>{member.studioRole}</strong>
                    <span className="s7-hint">{STUDIO_ROLES.find((r) => r.value === member.studioRole)?.description}</span>
                  </span>
                </Def>
                <Def label="Works on">
                  {member.scope.allNodes ? (
                    'All of the curriculum'
                  ) : (
                    <ul className="s7-plain-list">
                      {member.scope.nodes.map((node) => (
                        <li key={node.id} className={node.exists ? undefined : 's7-muted'}>
                          {node.trail.map((t) => t.en).join(' › ')}
                          {node.exists ? null : ' (no longer in the curriculum)'}
                        </li>
                      ))}
                    </ul>
                  )}
                </Def>
                <Def label="Languages">
                  {member.scope.allLanguages ? 'Every language' : member.scope.languages.map((l) => l.name).join(', ')}
                </Def>
              </DefList>
              {closed ? null : (
                <div>
                  <Button variant="ghost" onClick={() => setEditing('access')}>
                    Change their access
                  </Button>
                </div>
              )}
            </>
          )}
        </section>

        {/* ---- how they sign in ---- */}
        <section className="s7-stack" style={{ gap: '0.75rem' }}>
          <Subhead>How they sign in</Subhead>
          <DefList>
            <Def label="2-step">
              {member.security.twoStepEnabled ? (
                <span className="s7-inline">
                  <Badge tone="success">On</Badge>
                  <span className="s7-hint">{member.security.recoveryCodesLeft} recovery codes left</span>
                </span>
              ) : (
                <Badge tone="muted">Off</Badge>
              )}
            </Def>
            <Def label="Password">{member.security.hasPassword ? 'Chosen' : 'Not chosen yet'}</Def>
            {member.security.lockedOutUntilUtc ? (
              <Def label="Locked out">
                until {formatDateTime(member.security.lockedOutUntilUtc)} after {member.security.failedAttempts} wrong tries
              </Def>
            ) : null}
            <Def label="Last active">{member.lastActiveAtUtc ? formatRelative(member.lastActiveAtUtc) : 'Never'}</Def>
          </DefList>

          {member.setupLink ? (
            <Note>
              A {member.setupLink.purpose === 'Reset' ? 'reset' : 'setup'} link is waiting to be used, until{' '}
              {formatDateTime(member.setupLink.expiresAtUtc)}.{' '}
              <button
                type="button"
                className="s7-linklike"
                disabled={busy}
                onClick={() => void act(() => team.revokeSetupLink(member.userId), 'The link no longer works')}
              >
                Cancel the link
              </button>
            </Note>
          ) : null}

          <div className="s7-stack" style={{ gap: '0.4rem' }}>
            <span className="s7-label">
              <MonitorSmartphone size={13} aria-hidden /> Signed in on
            </span>
            {member.sessions.length === 0 ? (
              <span className="s7-hint">No device right now.</span>
            ) : (
              <ul className="s7-plain-list">
                {member.sessions.map((session) => (
                  <li key={session.id} className="s7-session">
                    <span>
                      {session.device ?? 'Unknown device'}
                      <span className="s7-hint">
                        {' '}
                        · {session.ipAddress ?? 'no address'} · last seen {formatRelative(session.lastSeenAtUtc)}
                      </span>
                    </span>
                    <button
                      type="button"
                      className="s7-linklike"
                      disabled={busy}
                      onClick={() => void act(() => team.revokeSession(member.userId, session.id), 'Signed out on that device')}
                    >
                      Sign out
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {member.recentSignIns.length ? (
            <details className="s7-details">
              <summary>Recent sign-ins</summary>
              <ul className="s7-plain-list">
                {member.recentSignIns.slice(0, 10).map((attempt, i) => (
                  <li key={i}>
                    <span className={attempt.outcome.startsWith('Succeeded') || attempt.outcome === 'Activated' ? undefined : 's7-danger-text'}>
                      {OUTCOME_LABEL[attempt.outcome]}
                    </span>
                    <span className="s7-hint">
                      {' '}
                      · {formatDateTime(attempt.occurredAtUtc)}
                      {attempt.ipAddress ? ` · ${attempt.ipAddress}` : ''}
                    </span>
                  </li>
                ))}
              </ul>
            </details>
          ) : null}
        </section>
      </div>

      <Notes member={member} busy={busy} onSave={(notes) => act(() => team.updateNotes(member.userId, notes), 'Notes saved')} />

      {/* ---- the account itself ---- */}
      {closed ? (
        <Note>
          This account is closed. Their name stays on everything they did; nobody can sign in to it again.
        </Note>
      ) : (
        <div className="s7-record-acts">
          <Button variant="ghost" disabled={busy} onClick={() => setSettingPassword(true)}>
            <KeyRound size={15} /> {member.security.hasPassword ? 'Set a new password' : 'Set their password'}
          </Button>
          {member.sessions.length > 0 ? (
            <Button
              variant="ghost"
              disabled={busy}
              onClick={() => void act(() => team.signOutEverywhere(member.userId), 'Signed out everywhere')}
            >
              <LogOut size={15} /> Sign out everywhere
            </Button>
          ) : null}
          {member.status === 'Suspended' ? (
            <Button variant="ghost" disabled={busy} onClick={() => void act(() => team.reactivate(member.userId), 'They can sign in again')}>
              <PlayCircle size={15} /> Let them back in
            </Button>
          ) : (
            <Button variant="ghost" disabled={busy} onClick={() => setSuspending(true)}>
              <PauseCircle size={15} /> Suspend
            </Button>
          )}
          <Button variant="danger" disabled={busy} onClick={() => setClosing(true)} style={{ marginInlineStart: 'auto' }}>
            <UserX size={15} /> Close the account
          </Button>
        </div>
      )}

      <PasswordModal
        open={settingPassword}
        member={member}
        busy={busy}
        onClose={() => setSettingPassword(false)}
        onSet={(password, clearTwoStep) =>
          act(() => team.setPassword(member.userId, password, clearTwoStep), 'Password set — signed out everywhere', () => {
            setSettingPassword(false)
            setHandover(password)
          })
        }
      />

      <Modal
        open={!!handover}
        onClose={() => setHandover(null)}
        icon={<KeyRound size={18} />}
        title="Their sign-in details"
        footer={
          <Button variant="ghost" onClick={() => setHandover(null)} style={{ marginInlineStart: 'auto' }}>
            Done
          </Button>
        }
      >
        {handover ? (
          <CredentialsHandover name={member.fullName} username={member.username} password={handover} address={studioAddress(address)} />
        ) : null}
      </Modal>

      <SuspendModal
        open={suspending}
        name={member.fullName}
        busy={busy}
        onClose={() => setSuspending(false)}
        onSuspend={(reason) => act(() => team.suspend(member.userId, reason), 'Suspended — signed out everywhere', () => setSuspending(false))}
      />

      <CloseModal
        open={closing}
        member={member}
        busy={busy}
        onClose={() => setClosing(false)}
        onConfirm={(reason, username) =>
          act(() => team.deactivate(member.userId, reason, username), 'Account closed', () => setClosing(false))
        }
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Editors
// ---------------------------------------------------------------------------

function ProfileEditor({
  member,
  busy,
  onCancel,
  onSave,
}: {
  member: TeamMemberDetail
  busy: boolean
  onCancel: () => void
  onSave: (profile: { fullName: string; jobTitle: string | null; workEmail: string | null; interfaceLanguage: string }) => void
}) {
  const [fullName, setFullName] = useState(member.fullName)
  const [jobTitle, setJobTitle] = useState(member.jobTitle ?? '')
  const [workEmail, setWorkEmail] = useState(member.workEmail ?? '')
  const [language, setLanguage] = useState<'en' | 'ar'>(member.interfaceLanguage === 'ar' ? 'ar' : 'en')

  return (
    <form
      className="s7-stack"
      style={{ gap: '0.75rem' }}
      onSubmit={(e) => {
        e.preventDefault()
        onSave({ fullName: fullName.trim(), jobTitle: jobTitle.trim() || null, workEmail: workEmail.trim() || null, interfaceLanguage: language })
      }}
    >
      <Field label="Full name" error={fullName.trim() ? null : 'Their name, as the team knows them.'}>
        <Input value={fullName} onChange={(e) => setFullName(e.target.value)} autoFocus />
      </Field>
      <Field label="Job title">
        <Input value={jobTitle} onChange={(e) => setJobTitle(e.target.value)} />
      </Field>
      <Field label="Work email">
        <Input type="email" value={workEmail} onChange={(e) => setWorkEmail(e.target.value)} />
      </Field>
      <Field label="Studio opens in">
        <div>
          <Segmented
            layoutId={`profile-language-${member.userId}`}
            value={language}
            onChange={setLanguage}
            options={[
              { value: 'en', label: 'English' },
              { value: 'ar', label: 'العربية' },
            ]}
          />
        </div>
      </Field>
      <div className="s7-inline">
        <Button type="submit" loading={busy} disabled={!fullName.trim()}>
          Save
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}

function AccessEditor({
  member,
  busy,
  onCancel,
  onSave,
}: {
  member: TeamMemberDetail
  busy: boolean
  onCancel: () => void
  onSave: (draft: MemberDraft) => void
}) {
  const [draft, setDraft] = useState(() => draftFromScope(member.studioRole, member.scope))
  const problems = draftProblems(draft, { who: false, username: false, access: true })

  return (
    <form
      className="s7-stack"
      style={{ gap: '0.75rem' }}
      onSubmit={(e) => {
        e.preventDefault()
        if (Object.keys(problems).length === 0) onSave(draft)
      }}
    >
      <AccessFields draft={draft} onChange={setDraft} touched idPrefix={`access-${member.userId}`} />
      <div className="s7-inline">
        <Button type="submit" loading={busy} disabled={Object.keys(problems).length > 0}>
          Save access
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}

function Notes({ member, busy, onSave }: { member: TeamMemberDetail; busy: boolean; onSave: (notes: string) => void }) {
  const [notes, setNotes] = useState(member.notes ?? '')
  const changed = notes.trim() !== (member.notes ?? '').trim()

  return (
    <div className="s7-stack" style={{ gap: '0.4rem' }}>
      <label className="s7-label" htmlFor={`notes-${member.userId}`}>
        Notes for other super admins
      </label>
      <textarea
        id={`notes-${member.userId}`}
        className="s7-textarea"
        rows={2}
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        placeholder="Anything worth knowing about this account. The member never sees it."
      />
      {changed ? (
        <div>
          <Button variant="ghost" loading={busy} onClick={() => onSave(notes)}>
            Save notes
          </Button>
        </div>
      ) : null}
    </div>
  )
}

// ---------------------------------------------------------------------------
// The acts that need a moment's thought
// ---------------------------------------------------------------------------

/**
 * A new password, set by the SuperAdmin (decided 2026-09-26: no links). For when a member has
 * forgotten theirs, or never had one. They are signed out everywhere, and sign in with this.
 */
function PasswordModal({
  open,
  member,
  busy,
  onClose,
  onSet,
}: {
  open: boolean
  member: TeamMemberDetail
  busy: boolean
  onClose: () => void
  onSet: (password: string, clearTwoStep: boolean) => void
}) {
  const options = useScopeOptions(open)
  const rules = options.data.passwordRules
  const [password, setPassword] = useState('')
  const [clearTwoStep, setClearTwoStep] = useState(false)
  const [tried, setTried] = useState(false)

  useEffect(() => {
    if (!open) return
    setPassword('')
    setClearTwoStep(false)
    setTried(false)
  }, [open])

  const missing = staffPasswordProblems(password, member.username, rules)
  const error = tried && missing.length ? `Needs ${missing.join(', ')}.` : null

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<KeyRound size={18} />}
      title={member.security.hasPassword ? 'Set a new password' : 'Set their password'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button
            loading={busy}
            disabled={tried && missing.length > 0}
            onClick={() => {
              setTried(true)
              if (missing.length === 0) onSet(password, clearTwoStep)
            }}
            style={{ marginInlineStart: 'auto' }}
          >
            Set the password
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <p className="s7-muted" style={{ margin: 0 }}>
          {member.security.hasPassword
            ? `${member.fullName} is signed out on every device and signs in with this from now on. Their old password stops working.`
            : `${member.fullName} can sign in to the Studio with this straight away.`}
        </p>
        <PasswordField value={password} onChange={setPassword} rules={rules} username={member.username} error={error} label="New password" />
        {member.security.twoStepEnabled ? (
          <Switch
            checked={clearTwoStep}
            onChange={setClearTwoStep}
            label="Also turn off their 2-step sign-in — only if they have lost their phone and their recovery codes"
          />
        ) : null}
      </div>
    </Modal>
  )
}

function SuspendModal({
  open,
  name,
  busy,
  onClose,
  onSuspend,
}: {
  open: boolean
  name: string
  busy: boolean
  onClose: () => void
  onSuspend: (reason: string) => void
}) {
  const [reason, setReason] = useState('')
  useEffect(() => {
    if (open) setReason('')
  }, [open])

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<PauseCircle size={18} />}
      title="Suspend"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button loading={busy} onClick={() => onSuspend(reason)} style={{ marginInlineStart: 'auto' }}>
            Suspend
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <p className="s7-muted" style={{ margin: 0 }}>
          {name} is signed out at once and cannot sign in until you let them back in. Nothing they wrote changes, and their drafts stay
          where they are.
        </p>
        <Field label="Why" hint="Optional. Other super admins see it on this record.">
          <Input value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
        </Field>
      </div>
    </Modal>
  )
}

function CloseModal({
  open,
  member,
  busy,
  onClose,
  onConfirm,
}: {
  open: boolean
  member: TeamMemberDetail
  busy: boolean
  onClose: () => void
  onConfirm: (reason: string, username: string) => void
}) {
  const [reason, setReason] = useState('')
  const [typed, setTyped] = useState('')
  useEffect(() => {
    if (open) {
      setReason('')
      setTyped('')
    }
  }, [open])

  const ready = reason.trim() !== '' && typed === member.username

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<ShieldCheck size={18} />}
      title="Close the account"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button variant="danger" loading={busy} disabled={!ready} onClick={() => onConfirm(reason, typed)} style={{ marginInlineStart: 'auto' }}>
            Close it for good
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <Note tone="danger">
          This cannot be undone. {member.fullName} is signed out and can never sign in again. Their name stays on everything they did.
        </Note>
        <Field label="Why">
          <Input value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />
        </Field>
        <Field label={`Type their username, ${member.username}, to confirm`}>
          <Input value={typed} onChange={(e) => setTyped(e.target.value)} autoComplete="off" spellCheck={false} />
        </Field>
      </div>
    </Modal>
  )
}
