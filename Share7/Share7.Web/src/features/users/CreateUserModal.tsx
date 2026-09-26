import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { AlertCircle, Check, Copy, Eye, EyeOff, UserPlus, WandSparkles } from 'lucide-react'
import { Button } from '../../components/ui/primitives'
import { Def, DefList, Note, Segmented } from '../../components/ui/bits'
import { Field, Input } from '../../components/ui/form'
import { Modal } from '../../components/ui/Modal'
import { Role, roleInfo } from '../../lib/access'
import { ApiError } from '../../lib/errors'
import { landingFor } from '../../lib/portals'
import { createUser, useAssignableRoles } from './data'
import { generatePassword, passwordProblems } from './password'
import { studioAddress, team, useScopeOptions, type CreatedTeamMember, type MemberDraft } from '../team/data'
import { AccessFields, CredentialsHandover, WhoFields, draftProblems, emptyDraft } from '../team/MemberForm'
import type { AdminUserListItemDto } from '../../types/api'

// Identity's default username alphabet — the server does not configure its own. Checked here so a
// space is caught while typing rather than after a round trip.
const USERNAME_PATTERN = /^[A-Za-z0-9._@+-]+$/

/** Why a (trimmed) username will be refused, or null. Nothing typed yet is not an error. */
function usernameProblem(username: string): string | null {
  if (!username) return null
  if (username.length < 3) return 'At least 3 characters.'
  if (!USERNAME_PATTERN.test(username)) return 'Letters, digits and - . _ @ + only — no spaces.'
  return null
}

const FORM_ID = 's7-create-user'

interface Created {
  username: string
  password: string
  role: string
}

/**
 * Create an account of any role the caller may give: every role but Super Admin for an Admin, all of
 * them for a Super Admin (decided 2026-09-26).
 *
 * The role comes first, because it decides what the rest of the form is. Every account gets a
 * username and a password the admin sets. A content-team member also needs a Studio role and the
 * part of the curriculum they work on, so choosing "Content team" turns the dialog into the same
 * member form Team & Access uses. There is no link and no activation (decided 2026-09-26): the member
 * signs in at the Studio's one fixed address straight away.
 *
 * Either way the dialog ends by showing what to hand over, because the admin is almost always
 * creating the account for someone else: credentials that vanished when the dialog closed would
 * leave an account nobody can sign in to.
 */
export function CreateUserModal({
  open,
  onClose,
  onCreated,
  contentTeamOnly,
}: {
  open: boolean
  onClose: () => void
  onCreated: (user: AdminUserListItemDto | null) => void

  /** Team & Access's "Add member": the member form alone, with no role to choose. */
  contentTeamOnly?: boolean
}) {
  const assignable = useAssignableRoles(open && !contentTeamOnly)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('')
  const [reveal, setReveal] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [created, setCreated] = useState<Created | null>(null)

  const [member, setMember] = useState<MemberDraft>(emptyDraft)
  const [touched, setTouched] = useState(false)
  const [joined, setJoined] = useState<{ created: CreatedTeamMember; password: string } | null>(null)

  // Every open starts clean. Reopening onto the last account's password would be a leak waiting
  // for a screen share.
  useEffect(() => {
    if (!open) return
    setUsername('')
    setPassword('')
    setRole('')
    setReveal(false)
    setError(null)
    setCreated(null)
    setMember(emptyDraft())
    setTouched(false)
    setJoined(null)
  }, [open])

  // The content team is the reason this form exists, so it is the default when on offer.
  useEffect(() => {
    if (contentTeamOnly && !role) {
      setRole(Role.ContentTeam)
      return
    }
    if (role || !assignable.data.length) return
    setRole(assignable.data.includes(Role.ContentTeam) ? Role.ContentTeam : assignable.data[0])
  }, [assignable.data, role, contentTeamOnly])

  const forTeam = role === Role.ContentTeam
  const options = useScopeOptions(open && forTeam)

  const trimmed = username.trim()
  const usernameError = usernameProblem(trimmed)
  const problems = passwordProblems(password)
  const memberProblems = draftProblems(member, { who: true, username: true, access: true, password: options.data.passwordRules })

  const ready = forTeam
    ? Object.keys(memberProblems).length === 0
    : !!trimmed && !usernameError && !problems.length && !!role

  async function submit(event: FormEvent) {
    event.preventDefault()
    setTouched(true)
    if (!ready || busy) return

    setBusy(true)
    setError(null)

    try {
      if (forTeam) {
        const result = await team.create(member)
        setJoined({ created: result, password: member.password })
        onCreated(null)
      } else {
        const user = await createUser({ username: trimmed, password, role })
        setCreated({ username: user.userName, password, role })
        onCreated(user)
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The account could not be created.')
    } finally {
      setBusy(false)
    }
  }

  const done = created || joined

  return (
    <Modal
      open={open}
      onClose={onClose}
      wide={forTeam && !joined}
      icon={<UserPlus size={18} />}
      title={joined ? 'Member added' : created ? 'Account created' : contentTeamOnly ? 'Add a member' : 'Add a user'}
      footer={
        done ? (
          <>
            {created ? <CopyDetails created={created} /> : null}
            <Button variant="ghost" onClick={onClose} style={{ marginInlineStart: 'auto' }}>
              Done
            </Button>
          </>
        ) : (
          <>
            <Button variant="ghost" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button
              type="submit"
              form={FORM_ID}
              loading={busy}
              // A member's form only disables once it has been tried, so the first press shows
              // what is missing rather than leaving a grey button that says nothing.
              disabled={forTeam ? touched && !ready : !ready}
              style={{ marginInlineStart: 'auto' }}
            >
              {busy ? null : <UserPlus size={15} />}
              {busy ? 'Creating…' : forTeam ? 'Add member' : 'Create account'}
            </Button>
          </>
        )
      }
    >
      {joined ? (
        <CredentialsHandover
          name={joined.created.member.fullName}
          username={joined.created.member.username}
          password={joined.password}
          address={studioAddress(options.data.studioAddress)}
        />
      ) : created ? (
        <Handover created={created} />
      ) : (
        <form id={FORM_ID} onSubmit={submit} className="s7-stack">
          {contentTeamOnly ? null : (
          <Field label="Role" hint={role ? roleInfo(role).description : undefined}>
            {assignable.loading ? (
              <span className="s7-hint">Loading roles…</span>
            ) : !assignable.data.length ? (
              <span className="s7-hint">No roles could be loaded.</span>
            ) : (
              <div>
                <Segmented
                  layoutId="create-user-role"
                  value={role}
                  onChange={(next) => {
                    setRole(next)
                    setError(null)
                  }}
                  options={assignable.data.map((r) => ({ value: r, label: roleInfo(r).label }))}
                />
              </div>
            )}
          </Field>
          )}

          {forTeam ? (
            <>
              <WhoFields draft={member} onChange={setMember} withUsername touched={touched} rules={options.data.passwordRules} />
              <AccessFields draft={member} onChange={setMember} touched={touched} idPrefix="create-user" />
            </>
          ) : (
            <>
              <Field label="Username" error={usernameError} hint="What they type to sign in.">
                <Input
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  invalid={!!usernameError}
                  autoComplete="off"
                  spellCheck={false}
                  placeholder="e.g. layla.admin"
                  autoFocus
                />
              </Field>

              <Field
                label="Password"
                hint={
                  !password
                    ? 'At least 8 characters, with an upper-case letter, a lower-case letter and a digit.'
                    : problems.length
                      ? `Needs ${problems.join(', ')}.`
                      : 'Meets the rules.'
                }
              >
                <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
                  <div className="s7-auth-reveal" style={{ flex: '1 1 auto' }}>
                    <Input
                      type={reveal ? 'text' : 'password'}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      // A browser offering to save this as the *admin's* password would be wrong.
                      autoComplete="new-password"
                      mono={reveal}
                    />
                    <button
                      type="button"
                      onClick={() => setReveal((v) => !v)}
                      aria-label={reveal ? 'Hide password' : 'Show password'}
                      title={reveal ? 'Hide password' : 'Show password'}
                      tabIndex={-1}
                    >
                      {reveal ? <EyeOff size={15} /> : <Eye size={15} />}
                    </button>
                  </div>
                  <Button
                    variant="ghost"
                    onClick={() => {
                      setPassword(generatePassword())
                      // Shown, because the next thing the admin does is read it back to someone.
                      setReveal(true)
                    }}
                  >
                    <WandSparkles size={15} /> Generate
                  </Button>
                </div>
              </Field>
            </>
          )}

          {error ? (
            <div className="s7-auth-error">
              <AlertCircle size={15} style={{ flex: 'none', marginTop: 1 }} />
              <span>{error}</span>
            </div>
          ) : null}
        </form>
      )}
    </Modal>
  )
}

// ---------------------------------------------------------------------------
// Step two — the credentials to hand over
// ---------------------------------------------------------------------------

/** Where this role signs in, as a full URL, or null for a role that signs in to the game. */
function signInUrl(role: string): string | null {
  const home = landingFor([role])
  return home ? new URL(home, window.location.origin).href : null
}

function Handover({ created }: { created: Created }) {
  const url = signInUrl(created.role)

  return (
    <div className="s7-stack">
      <Note>
        Send these to the person the account is for. This is the only time the password is shown —
        copy it before closing.
      </Note>

      <DefList>
        <Def label="Username">
          <code className="s7-key">{created.username}</code>
        </Def>
        <Def label="Password">
          <code className="s7-key">{created.password}</code>
        </Def>
        <Def label="Role">{roleInfo(created.role).label}</Def>
        <Def label="Signs in at">
          {url ? <code className="s7-key">{url}</code> : 'The game app — this role has no console access.'}
        </Def>
      </DefList>
    </div>
  )
}

function CopyDetails({ created }: { created: Created }) {
  const [copied, setCopied] = useState(false)

  async function copy() {
    const url = signInUrl(created.role)
    const text = [
      `Username: ${created.username}`,
      `Password: ${created.password}`,
      url ? `Sign in at: ${url}` : null,
    ]
      .filter(Boolean)
      .join('\n')

    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard access can be refused. Everything is on screen to copy by hand.
    }
  }

  return (
    <Button onClick={() => void copy()}>
      {copied ? <Check size={15} /> : <Copy size={15} />}
      {copied ? 'Copied' : 'Copy details'}
    </Button>
  )
}
