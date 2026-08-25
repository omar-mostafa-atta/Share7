import { AnimatePresence, motion } from 'motion/react'
import { Menu } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { pageVariants } from '../ui/motion'

export function AppShell() {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const location = useLocation()

  // Close the mobile drawer whenever the route changes, including on browser back — otherwise it
  // stays open over the page just navigated to.
  useEffect(() => {
    setDrawerOpen(false)
  }, [location.pathname])

  return (
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
        {/*
          mode="wait" so the outgoing page finishes leaving before the next arrives. With the
          default both are mounted at once and, since each page owns the full column width, the
          incoming one renders below the outgoing one and the layout jumps.
        */}
        <AnimatePresence mode="wait">
          <motion.div
            key={location.pathname}
            variants={pageVariants}
            initial="hidden"
            animate="visible"
            exit="exit"
          >
            <Outlet />
          </motion.div>
        </AnimatePresence>
      </main>
    </div>
  )
}

/** Title block at the top of a page. A stagger child, so it leads the cards in. */
export function PageHeader({
  icon,
  title,
  children,
}: {
  icon: ReactNode
  title: string
  children?: ReactNode
}) {
  return (
    <motion.header
      className="s7-page-header"
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35 }}
    >
      <h1>
        {icon}
        {title}
      </h1>
      {children ? <p>{children}</p> : null}
    </motion.header>
  )
}
