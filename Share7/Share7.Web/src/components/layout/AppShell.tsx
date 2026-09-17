import { motion } from 'motion/react'
import { Menu, Monitor, Moon, Rows3, Search, Sun } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { RouteErrorBoundary } from './ErrorBoundary'
import { CommandPalette } from '../ui/CommandPalette'
import { PageTitle } from '../ui/bits'
import { entryForPath } from '../../lib/nav'
import { usePrefs } from '../../store/prefs'
import type { ThemeChoice } from '../../store/prefs'
import { pageVariants } from '../ui/motion'

export function AppShell() {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [paletteOpen, setPaletteOpen] = useState(false)
  const location = useLocation()

  const theme = usePrefs((s) => s.theme)
  const setTheme = usePrefs((s) => s.setTheme)
  const density = usePrefs((s) => s.density)
  const setDensity = usePrefs((s) => s.setDensity)

  const entry = entryForPath(location.pathname)

  // Close the mobile drawer whenever the route changes, including on browser back — otherwise it
  // stays open over the page just navigated to.
  useEffect(() => {
    setDrawerOpen(false)
  }, [location.pathname])

  // Cmd-K / Ctrl-K anywhere. Bound on the window rather than the shell so it
  // fires while focus is inside a table, a drawer or a form field.
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setPaletteOpen((open) => !open)
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  // Cycles system → light → dark. Three states rather than two because
  // "follow the OS" is a real preference and a two-way toggle cannot express it.
  const nextTheme: Record<ThemeChoice, ThemeChoice> = {
    system: 'light',
    light: 'dark',
    dark: 'system',
  }

  const themeIcon =
    theme === 'system' ? <Monitor size={15} /> : theme === 'light' ? <Sun size={15} /> : <Moon size={15} />

  return (
    <>
      <div className="s7-aurora" aria-hidden />

      <div className="s7-shell">
        <Sidebar
          open={drawerOpen}
          onNavigate={() => setDrawerOpen(false)}
          onClose={() => setDrawerOpen(false)}
        />

        <button
          type="button"
          className="s7-sidebar-toggle"
          aria-label="Open navigation"
          onClick={() => setDrawerOpen((v) => !v)}
        >
          <Menu size={19} />
        </button>

        <main className="s7-main">
          <div className="s7-topbar">
            <div className="s7-topbar-title">
              <strong>{entry?.label ?? 'Share7'}</strong>
              <span>{entry?.section ?? 'Admin Console'}</span>
            </div>

            <button type="button" className="s7-omni" onClick={() => setPaletteOpen(true)}>
              <Search size={14} />
              <span style={{ flex: '1 1 auto', textAlign: 'left' }}>Search or jump to…</span>
              <span className="s7-kbd">{navigator.platform.includes('Mac') ? '⌘' : 'Ctrl'} K</span>
            </button>

            <button
              type="button"
              className="s7-btn s7-btn-ghost s7-btn-icon"
              title={`Row height: ${density}`}
              aria-label={`Row height: ${density}. Switch.`}
              onClick={() => setDensity(density === 'compact' ? 'comfortable' : 'compact')}
            >
              <Rows3 size={15} />
            </button>

            <button
              type="button"
              className="s7-btn s7-btn-ghost s7-btn-icon"
              title={`Theme: ${theme}`}
              aria-label={`Theme: ${theme}. Switch.`}
              onClick={() => setTheme(nextTheme[theme])}
            >
              {themeIcon}
            </button>
          </div>

          {/*
            No AnimatePresence here, deliberately.

            It used to wrap this in `mode="wait"`, which unmounts the outgoing page and holds an
            EMPTY content column for the length of its exit (160ms) before mounting the next one.
            Against the dark canvas that gap is a black rectangle on every single navigation —
            the flicker. `mode="popLayout"` and the default both trade it for the two pages
            briefly stacking, which is the layout jump the original comment was avoiding.

            Keying a plain motion.div on the pathname sidesteps the choice: React swaps the
            subtree in one commit, so there is never a frame with neither page in it, and the
            incoming page still animates in. Nothing animates out — which is correct, because
            nobody watches content leave.
          */}
          <motion.div
            key={location.pathname}
            variants={pageVariants}
            initial="hidden"
            animate="visible"
          >
            {/* Keeps a render-time throw inside the content column. Without it the
                error unmounts the whole tree, sidebar included, and the console
                becomes a blank canvas with no way out but a reload. */}
            <RouteErrorBoundary resetKey={location.pathname}>
              <Outlet />
            </RouteErrorBoundary>
          </motion.div>
        </main>
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
    </>
  )
}

/**
 * Title block at the top of a page.
 *
 * Kept as a re-export shape rather than deleted: Currencies and Curriculum
 * already call it with `children` as the subtitle, and changing those two call
 * sites to the new component buys nothing. New pages use `PageTitle` directly,
 * which additionally takes `actions`.
 */
export function PageHeader({
  icon,
  title,
  children,
}: {
  icon: ReactNode
  title: string
  children?: ReactNode
}) {
  return <PageTitle icon={icon} title={title} subtitle={children} />
}
