import { AnimatePresence, motion } from 'motion/react'
import {
  AlertCircle,
  BarChart3,
  Eye,
  EyeOff,
  LogIn,
  Monitor,
  Moon,
  Network,
  ShieldCheck,
  Sparkles,
  Sun,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/primitives'
import { BrandBadge, BrandCharacter } from '../components/ui/Logo'
import { Field, Input, Select } from '../components/ui/form'
import { api } from '../lib/client'
import { ApiError } from '../lib/errors'
import { useAuth } from '../store/auth'
import { useLanguages } from '../store/languages'
import { usePrefs } from '../store/prefs'
import type { ThemeChoice } from '../store/prefs'
import type { AuthResult, LoginRequest } from '../types/api'

// ===========================================================================
// Sign in
//
// Two panels: a brand side saying what this console is, and a form side that
// works in both themes.
//
// The previous card was `rgba(255,255,255,0.97)` with no colour of its own, so
// it took --s7-ink for its text. Correct while the palette was light-only;
// once dark landed, --s7-ink went near-white and the card rendered white on
// white. The replacement paints an explicit surface and ink.
// ===========================================================================

const POINTS = [
  { icon: Network, label: 'Curriculum & questions' },
  { icon: Sparkles, label: 'Signal economy' },
  { icon: BarChart3, label: 'Leaderboards & runs' },
  { icon: ShieldCheck, label: 'Cheat review' },
]

export function Login() {
  const accessToken = useAuth((s) => s.accessToken)
  const setSession = useAuth((s) => s.setSession)

  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)
  const loadLanguages = useLanguages((s) => s.load)
  const selectLanguage = useLanguages((s) => s.select)

  const theme = usePrefs((s) => s.theme)
  const setTheme = usePrefs((s) => s.setTheme)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [reveal, setReveal] = useState(false)
  const [capsOn, setCapsOn] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const navigate = useNavigate()
  const location = useLocation() as { state?: { from?: string } }

  useEffect(() => {
    // The picker is a convenience; a server that cannot answer should not block sign-in. The old
    // console swallowed this failure for the same reason.
    void loadLanguages().catch(() => undefined)
  }, [loadLanguages])

  // Already signed in — nothing to do here. `replace` keeps the login screen out of history, so
  // Back from the dashboard does not land on it and bounce straight back.
  if (accessToken) {
    return <Navigate to={location.state?.from ?? '/'} replace />
  }

  const nextTheme: Record<ThemeChoice, ThemeChoice> = {
    system: 'light',
    light: 'dark',
    dark: 'system',
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)

    try {
      // `silent` because this form renders its own error inline — a toast as well would say the
      // same thing twice.
      const auth = await api.post<AuthResult>(
        '/api/auth/login',
        { username: username.trim(), password } satisfies LoginRequest,
        { silent: true },
      )

      if (!auth.accessToken) {
        setError(auth.errors?.join(' ') || 'Sign-in failed. Check credentials and try again.')
        return
      }

      // Signing in as a non-admin succeeds at the API and then fails on every
      // screen, because every admin route is role-gated. Saying so here beats
      // dropping them into a dashboard where each panel 403s on its own.
      const privileged = auth.roles?.some((r) => r === 'Admin' || r === 'SuperAdmin')
      if (!privileged) {
        setError('That account signed in, but it is not an Admin or SuperAdmin — this console would be empty.')
        return
      }

      setSession(auth)
      navigate(location.state?.from ?? '/', { replace: true })
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : 'Sign-in failed. Check credentials and try again.'
      setError(message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="s7-auth">
      <aside className="s7-auth-side">
        <motion.div
          className="s7-auth-brand"
          initial={{ opacity: 0, y: -8 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, ease: [0.16, 1, 0.3, 1] }}
        >
          <BrandBadge size={40} />
          <div>
            <strong>شارع العلوم</strong>
            <span>Admin Console</span>
          </div>
        </motion.div>

        <motion.div
          className="s7-auth-pitch"
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, delay: 0.1, ease: [0.16, 1, 0.3, 1] }}
        >
          <h2>Everything the platform runs on, in one place.</h2>
          <p>
            Curriculum and games, the currencies and rules that pay players, and the operational
            surfaces where a human still has to decide.
          </p>

          <div className="s7-auth-points">
            {POINTS.map((point, i) => (
              <motion.span
                key={point.label}
                className="s7-auth-point"
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.4, delay: 0.25 + i * 0.07 }}
              >
                <point.icon size={13} />
                {point.label}
              </motion.span>
            ))}
          </div>
        </motion.div>

        {/* Hidden below 1200px: the panel gets tight and the character is the
            first thing that can go without losing meaning. */}
        <motion.div
          className="s7-auth-character"
          initial={{ opacity: 0, x: -20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.7, delay: 0.25, ease: [0.16, 1, 0.3, 1] }}
        >
          <BrandCharacter height={230} />
        </motion.div>

        <div className="s7-auth-foot">Served by the API itself — every call is same-origin.</div>
      </aside>

      <main className="s7-auth-main">
        <div className="s7-auth-tools">
          <button
            type="button"
            className="s7-btn s7-btn-ghost s7-btn-icon"
            title={`Theme: ${theme}`}
            aria-label={`Theme: ${theme}. Switch.`}
            onClick={() => setTheme(nextTheme[theme])}
          >
            {theme === 'system' ? <Monitor size={15} /> : theme === 'light' ? <Sun size={15} /> : <Moon size={15} />}
          </button>
        </div>

        <motion.div
          className="s7-auth-card"
          initial={{ opacity: 0, y: 18, scale: 0.98 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.5, ease: [0.16, 1, 0.3, 1] }}
        >
          <h1>Sign in</h1>
          <p>
            Use an <code className="s7-key">Admin</code> or{' '}
            <code className="s7-key">SuperAdmin</code> account.
          </p>

          <form onSubmit={submit} className="s7-auth-fields">
            <Field label="Username">
              <Input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                placeholder="admin"
                autoFocus
                required
              />
            </Field>

            <Field
              label="Password"
              hint={
                capsOn ? (
                  <span className="s7-auth-hint">
                    <AlertCircle size={12} /> Caps Lock is on
                  </span>
                ) : undefined
              }
            >
              <div className="s7-auth-reveal">
                <Input
                  type={reveal ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  // Caps Lock is the single most common cause of a password that
                  // "definitely is right" being rejected. Read from the event
                  // rather than tracked, so it is correct even if the key was
                  // pressed while this field was not focused.
                  onKeyUp={(e) => setCapsOn(e.getModifierState?.('CapsLock') ?? false)}
                  onKeyDown={(e) => setCapsOn(e.getModifierState?.('CapsLock') ?? false)}
                  onBlur={() => setCapsOn(false)}
                  autoComplete="current-password"
                  placeholder="••••••••"
                  required
                />
                <button
                  type="button"
                  onClick={() => setReveal((v) => !v)}
                  aria-label={reveal ? 'Hide password' : 'Show password'}
                  title={reveal ? 'Hide password' : 'Show password'}
                  // Not focusable: tabbing from the password field should reach
                  // the language picker, not a decoration.
                  tabIndex={-1}
                >
                  {reveal ? <EyeOff size={15} /> : <Eye size={15} />}
                </button>
              </div>
            </Field>

            <Field label="Content language" hint="Which language names come back in.">
              <Select
                value={selectedLangId}
                onChange={(e) => selectLanguage(e.target.value)}
                disabled={!languages.length}
              >
                {languages.length ? (
                  languages.map((l) => (
                    <option key={l.id} value={l.id}>
                      {l.name} ({l.code})
                    </option>
                  ))
                ) : (
                  <option value="">Loading languages…</option>
                )}
              </Select>
            </Field>

            <AnimatePresence initial={false}>
              {error ? (
                <motion.div
                  key="auth-error"
                  className="s7-auth-error"
                  initial={{ opacity: 0, height: 0, marginTop: 0 }}
                  animate={{ opacity: 1, height: 'auto', marginTop: 0 }}
                  exit={{ opacity: 0, height: 0, marginTop: 0 }}
                  transition={{ duration: 0.22 }}
                >
                  <AlertCircle size={15} style={{ flex: 'none', marginTop: 1 }} />
                  <span>{error}</span>
                </motion.div>
              ) : null}
            </AnimatePresence>

            <Button type="submit" loading={busy} disabled={!username.trim() || !password}>
              {busy ? null : <LogIn size={16} />}
              {busy ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>

          <div className="s7-auth-note">
            Press <span className="s7-kbd">⌘ K</span> once inside to jump anywhere.
          </div>
        </motion.div>
      </main>
    </div>
  )
}
