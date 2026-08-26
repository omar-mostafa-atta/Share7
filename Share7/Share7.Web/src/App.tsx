import { MotionConfig, useReducedMotion } from 'motion/react'
import { useEffect } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { Toaster } from './components/ui/Toaster'
import { setApiErrorHandler } from './lib/client'
import { useToasts } from './store/toast'
import { Analytics } from './routes/Analytics'
import { Currencies } from './routes/Currencies'
import { Curriculum } from './routes/Curriculum'
import { Events } from './routes/Events'
import { Games } from './routes/Games'
import { Leaderboards } from './routes/Leaderboards'
import { Login } from './routes/Login'
import { Multiplayer } from './routes/Multiplayer'
import { Objectives } from './routes/Objectives'
import { Offers } from './routes/Offers'
import { Overview } from './routes/Overview'
import { ProductKinds } from './routes/ProductKinds'
import { Progression } from './routes/Progression'
import { ProtectedRoute } from './routes/ProtectedRoute'
import { Retention } from './routes/Retention'
import { Rewards } from './routes/Rewards'
import { Runs } from './routes/Runs'
import { Shop } from './routes/Shop'
import { Signals } from './routes/Signals'
import { UserTrace } from './routes/UserTrace'
import { Users } from './routes/Users'

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
          {/* The landing page is now a dashboard rather than a redirect to Curriculum.
              The old console had nowhere to land, so signing in dropped the admin into
              whichever page happened to be first in the nav. */}
          <Route path="/" element={<Overview />} />

          {/* Analytics. Four pages rather than one, because the questions have
              genuinely different shapes: a dashboard, a cohort matrix, a
              registry, and a single-account history. Cramming them into tabs
              on one route makes three of the four unlinkable. */}
          <Route path="/analytics" element={<Analytics />} />
          <Route path="/retention" element={<Retention />} />
          <Route path="/events" element={<Events />} />
          <Route path="/trace" element={<UserTrace />} />

          <Route path="/curriculum" element={<Curriculum />} />
          <Route path="/games" element={<Games />} />

          <Route path="/objectives" element={<Objectives />} />
          <Route path="/leaderboards" element={<Leaderboards />} />
          <Route path="/progression" element={<Progression />} />

          <Route path="/currencies" element={<Currencies />} />
          <Route path="/signals" element={<Signals />} />
          <Route path="/rewards" element={<Rewards />} />
          <Route path="/shop" element={<Shop />} />
          <Route path="/offers" element={<Offers />} />
          <Route path="/catalogue" element={<ProductKinds />} />

          <Route path="/runs" element={<Runs />} />
          <Route path="/multiplayer" element={<Multiplayer />} />
          <Route path="/users" element={<Users />} />
        </Route>

        {/* Anything unrecognised goes to the dashboard rather than a blank screen. */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      <Toaster />
    </MotionConfig>
  )
}
