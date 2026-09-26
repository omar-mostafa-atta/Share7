import { MotionConfig, useReducedMotion } from 'motion/react'
import { useEffect } from 'react'
import { useDocumentHasBeenVisible } from './lib/visibility'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { Toaster } from './components/ui/Toaster'
import { setApiErrorHandler } from './lib/client'
import { ADMIN_PORTAL } from './lib/portals'
import { useToasts } from './store/toast'
import { Analytics } from './routes/Analytics'
import { Organizations } from './routes/Organizations'
import { Exams } from './routes/Exams'
import { Currencies } from './routes/Currencies'
import { Events } from './routes/Events'
import { Moved } from './routes/Moved'
import { Games } from './routes/Games'
import { Guidance } from './routes/Guidance'
import { Leaderboards } from './routes/Leaderboards'
import { LiveEvents } from './routes/LiveEvents'
import { Login } from './routes/Login'
import { Multiplayer } from './routes/Multiplayer'
import { Objectives } from './routes/Objectives'
import { Offers } from './routes/Offers'
import { Overview } from './routes/Overview'
import { PlayModes } from './routes/PlayModes'
import { ProductKinds } from './routes/ProductKinds'
import { Progression } from './routes/Progression'
import { ProtectedRoute } from './routes/ProtectedRoute'
import { Retention } from './routes/Retention'
import { Rewards } from './routes/Rewards'
import { Runs } from './routes/Runs'
import { Shop } from './routes/Shop'
import { Signals } from './routes/Signals'
import { Team } from './routes/Team'
import { UserTrace } from './routes/UserTrace'
import { Users } from './routes/Users'

export function App() {
  const reduced = useReducedMotion()

  // Marks the document as having been looked at, which releases the stylesheet's resting-state
  // override. See lib/visibility.ts — without it the console is blank in any tab that loads while
  // hidden, which is every tab opened from a link in the background.
  useDocumentHasBeenVisible()

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

        {/* The Admin Console. It is the only portal now: the content team's own space under
            /content closed at cutover (plan P6) and its people work in the Content Studio, which
            is a separate application on a separate origin. The Portal abstraction in
            lib/portals.ts is kept — it is what a future audience would be added through. */}
        <Route
          element={
            <ProtectedRoute portal={ADMIN_PORTAL}>
              <AppShell portal={ADMIN_PORTAL} />
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

          {/* The authoring surface moved to the Content Studio at cutover (plan P6). These four
              addresses answer rather than disappearing: an admin following an old link is told
              where the work went, which a bounce to the dashboard would not do. They are not in
              the sidebar — routes/Moved.tsx is a landing place, not a page anybody navigates to. */}
          <Route path="/curriculum" element={<Moved />} />
          <Route path="/quality" element={<Moved />} />
          <Route path="/targets" element={<Moved />} />
          <Route path="/content/*" element={<Moved />} />

          <Route path="/organizations" element={<Organizations />} />
          <Route path="/exams" element={<Exams />} />
          <Route path="/games" element={<Games />} />
          <Route path="/modes" element={<PlayModes />} />

          <Route path="/objectives" element={<Objectives />} />
          <Route path="/leaderboards" element={<Leaderboards />} />

          {/* `/events` belongs to the telemetry event registry, which predates this.
              Competitions live at `/live-events` rather than renaming a page admins
              already have bookmarked. */}
          <Route path="/live-events" element={<LiveEvents />} />
          <Route path="/progression" element={<Progression />} />
          <Route path="/guidance" element={<Guidance />} />

          <Route path="/currencies" element={<Currencies />} />
          <Route path="/signals" element={<Signals />} />
          <Route path="/rewards" element={<Rewards />} />
          <Route path="/shop" element={<Shop />} />
          <Route path="/offers" element={<Offers />} />
          <Route path="/catalogue" element={<ProductKinds />} />

          <Route path="/runs" element={<Runs />} />
          <Route path="/multiplayer" element={<Multiplayer />} />
          <Route path="/users" element={<Users />} />

          {/* SuperAdmins only: the page sends anybody else home, and the server refuses them. */}
          <Route path="/team" element={<Team />} />
        </Route>

        {/* Anything unrecognised goes to the dashboard rather than a blank screen — or, for
            someone who cannot open the Admin Console, on to the portal they can. */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      <Toaster />
    </MotionConfig>
  )
}
