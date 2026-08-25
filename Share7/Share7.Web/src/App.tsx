import { MotionConfig, useReducedMotion } from 'motion/react'
import { useEffect } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { Toaster } from './components/ui/Toaster'
import { setApiErrorHandler } from './lib/client'
import { useToasts } from './store/toast'
import { Currencies } from './routes/Currencies'
import { Curriculum } from './routes/Curriculum'
import { Login } from './routes/Login'
import { ProtectedRoute } from './routes/ProtectedRoute'

export function App() {
  const reduced = useReducedMotion()

  // Wiring the client's error sink to the toast store here, once, reproduces the old console's
  // behaviour — where api() toasted every failure itself — without the fetch layer importing UI.
  useEffect(() => {
    setApiErrorHandler((error, method, path) => {
      useToasts.getState().push('danger', `${method} ${path} → ${error.status}`, error.message)
    })
    return () => setApiErrorHandler(null)
  }, [])

  return (
    // `reducedMotion="always"` when the OS asks for it: Framer Motion then skips transforms and
    // opacity animations, matching what the CSS media query does for the stylesheet's own.
    <MotionConfig reducedMotion={reduced ? 'always' : 'never'}>
      <Routes>
        <Route path="/login" element={<Login />} />

        <Route
          element={
            <ProtectedRoute>
              <AppShell />
            </ProtectedRoute>
          }
        >
          <Route path="/curriculum" element={<Curriculum />} />
          <Route path="/currencies" element={<Currencies />} />

          {/* Curriculum is the landing page, matching the old console — it redirected to
              pages/curriculum.html straight after sign-in. */}
          <Route path="/" element={<Navigate to="/curriculum" replace />} />
        </Route>

        {/* Anything unrecognised inside /app/ goes to the default page rather than a blank
            screen. Routes for slices still served by the old console are absolute links in the
            sidebar, so they never reach the router. */}
        <Route path="*" element={<Navigate to="/curriculum" replace />} />
      </Routes>

      <Toaster />
    </MotionConfig>
  )
}
