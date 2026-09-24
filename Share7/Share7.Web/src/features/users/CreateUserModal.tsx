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
 * Create an account with a username, a password and one role.
 *
 * Two steps in one dialog: the form, then — once the server has accepted it — the credentials to
 * hand over. The second step exists because the admin is almost always creating the account for
 * someone else. A generated password that vanished the moment the dialog closed would leave an
 * account nobody can sign in to, and there is no password reset to recover it with.
 */
export function CreateUserModal({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (user: AdminUserListItemDto) => void
}) {
  const assignable = useAssignableRoles(open)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState('')
  const [reveal, setReveal] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [created, setCreated] = useState<Created | null>(null)

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
  }, [open])

  // The content team is the reason this form exists, so it is the default when on offer.
  useEffect(() => {
    if (role || !assignable.data.length) return
    setRole(assignable.data.includes(Role.ContentTeam) ? Role.ContentTeam : assignable.data[0])
  }, [assignable.data, role])

  const trimmed = username.trim()
  const usernameError = usernameProblem(trimmed)
  const problems = passwordProblems(password)
  const ready = !!trimmed && !usernameError && !problems.length && !!role

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!ready || busy) return

    setBusy(true)
    setError(null)

    try {
      const user = await createUser({ username: trimmed, password, role })
      setCreated({ username: user.userName, password, role })
      onCreated(user)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The account could not be created.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<UserPlus size={18} />}
      title={created ? 'Account created' : 'Add a user'}
      footer={
        created ? (
          <>
            <CopyDetails created={created} />
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
              disabled={!ready}
              style={{ marginInlineStart: 'auto' }}
            >
              {busy ? null : <UserPlus size={15} />}
              {busy ? 'Creating…' : 'Create account'}
            </Button>
          </>
        )
      }
    >
      {created ? (
        <Handover created={created} />
      ) : (
        <form id={FORM_ID} onSubmit={submit} className="s7-stack">
          <Field label="Username" error={usernameError} hint="What they type to sign in.">
            <Input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              invalid={!!usernameError}
              autoComplete="off"
              spellCheck={false}
              placeholder="e.g. layla.content"
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
                  onChange={setRole}
                  options={assignable.data.map((r) => ({ value: r, label: roleInfo(r).label }))}
                />
              </div>
            )}
          </Field>

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
