import { motion } from 'motion/react'
import { AlertCircle, LogIn } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/primitives'
import { BrandBadge, BrandCharacter } from '../components/ui/Logo'
import { Field, Input, Select } from '../components/ui/form'
import { api } from '../lib/client'
import { ApiError } from '../lib/errors'
import { useAuth } from '../store/auth'
import { useLanguages } from '../store/languages'
import type { AuthResult, LoginRequest } from '../types/api'

export function Login() {
  const accessToken = useAuth((s) => s.accessToken)
  const setSession = useAuth((s) => s.setSession)

  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)
  const loadLanguages = useLanguages((s) => s.load)
  const selectLanguage = useLanguages((s) => s.select)

  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
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
    return <Navigate to={location.state?.from ?? '/curriculum'} replace />
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

      setSession(auth)
      navigate(location.state?.from ?? '/curriculum', { replace: true })
    } catch (err) {
      const message =
        err instanceof ApiError
          ? err.message
          : 'Sign-in failed. Check credentials and try again.'
      setError(message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="s7-login">
      {/* The character sits beside the card on wide screens and is hidden on narrow ones, where
          it would push the form below the fold. It removes itself if the file is absent. */}
      <motion.div
        className="s7-login-mascot"
        initial={{ opacity: 0, x: -24 }}
        animate={{ opacity: 1, x: 0 }}
        transition={{ duration: 0.6, delay: 0.2, ease: [0.16, 1, 0.3, 1] }}
      >
        <BrandCharacter height={320} />
      </motion.div>

      <motion.div
        className="s7-login-card"
        initial={{ opacity: 0, y: 24, scale: 0.97 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.5, ease: [0.16, 1, 0.3, 1] }}
      >
        <div className="s7-login-brand">
          <motion.div
            style={{ display: 'grid', placeItems: 'center', marginBottom: '0.85rem' }}
            initial={{ scale: 0.7, rotate: -12 }}
            animate={{ scale: 1, rotate: 0 }}
            transition={{ type: 'spring', stiffness: 320, damping: 18, delay: 0.12 }}
          >
            <BrandBadge size={62} />
          </motion.div>
          <h1>شارع العلوم</h1>
          <p>
            Sign in with an <code>Admin</code> or <code>SuperAdmin</code> account.
          </p>
        </div>

        <form onSubmit={submit} className="s7-login-fields">
          <Field label="Username">
            <Input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoComplete="username"
              placeholder="Enter your username"
              autoFocus
              required
            />
          </Field>

          <Field label="Password">
            <Input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              placeholder="Enter your password"
              required
            />
          </Field>

          <Field label="Content language">
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

          {error ? (
            <motion.div
              className="s7-login-error"
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              transition={{ duration: 0.22 }}
            >
              <AlertCircle size={16} />
              <span>{error}</span>
            </motion.div>
          ) : null}

          <Button type="submit" loading={busy}>
            {busy ? null : <LogIn size={16} />}
            {busy ? 'Signing in…' : 'Sign In'}
          </Button>
        </form>

        <div className="s7-login-foot">Served by the API itself — every call is same-origin.</div>
      </motion.div>
    </div>
  )
}
