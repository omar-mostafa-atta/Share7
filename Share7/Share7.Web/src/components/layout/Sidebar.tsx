import { AnimatePresence, motion } from 'motion/react'
import {
  BarChart3,
  Coins,
  Gamepad2,
  LogOut,
  Network,
  ShoppingBag,
  Tag,
  Trophy,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../../store/auth'
import { BrandBadge } from '../ui/Logo'
import { scrimVariants, springSoft } from '../ui/motion'

interface NavEntry {
  to: string
  label: string
  icon: LucideIcon
  /** Not yet ported from the vanilla console — rendered but inert. */
  pending?: boolean
}

interface NavGroup {
  section: string
  items: NavEntry[]
}

// Mirrors NAV_ITEMS in wwwroot/js/nav.js, in the same order and grouping, so the two consoles
// read the same while both are live. `pending` marks the slices still served by the old console.
const NAV: NavGroup[] = [
  {
    section: 'Content',
    items: [{ to: '/curriculum', label: 'Curriculum', icon: Network }],
  },
  {
    section: 'Engagement',
    items: [
      { to: '/objectives', label: 'Objectives', icon: Trophy, pending: true },
      { to: '/leaderboards', label: 'Leaderboards', icon: BarChart3, pending: true },
    ],
  },
  {
    section: 'Commerce',
    items: [
      { to: '/games', label: 'Games', icon: Gamepad2, pending: true },
      { to: '/shop', label: 'Shop', icon: ShoppingBag, pending: true },
      { to: '/offers', label: 'Offers', icon: Tag, pending: true },
      { to: '/currencies', label: 'Currencies', icon: Coins },
    ],
  },
]

export function Sidebar({
  open,
  onNavigate,
  onClose,
}: {
  open: boolean
  onNavigate: () => void
  onClose: () => void
}) {
  const username = useAuth((s) => s.username)
  const roles = useAuth((s) => s.roles)
  const clear = useAuth((s) => s.clear)

  const initial = (username || 'A').charAt(0).toUpperCase()

  return (
    <>
      {/* Scrim only exists on mobile, where the sidebar is a drawer over the content. */}
      <AnimatePresence>
        {open ? (
          <motion.div
            key="sidebar-scrim"
            className="s7-scrim"
            variants={scrimVariants}
            initial="hidden"
            animate="visible"
            exit="exit"
            onClick={onClose}
          />
        ) : null}
      </AnimatePresence>

      <aside className={`s7-sidebar ${open ? 'is-open' : ''}`}>
        <div className="s7-brand">
          {/* The badge carries its own colour, so it replaces the gradient tile rather than
              sitting inside it — a brand mark inside a second brand-coloured chip reads as two
              logos stacked. */}
          <BrandBadge size={38} />
          <div>
            <div className="s7-brand-text">شارع العلوم</div>
            <span className="s7-brand-sub">Admin Console</span>
          </div>
        </div>

        <nav className="s7-nav">
          {NAV.map((group) => (
            <div key={group.section}>
              <div className="s7-nav-section">{group.section}</div>
              {group.items.map(({ to, label, icon: Icon, pending }) =>
                pending ? (
                  // Points at the old console rather than a dead link. A full reload is correct
                  // here: it is a different application on the same origin.
                  <a
                    key={to}
                    href={`/pages${to}.html`}
                    className="s7-nav-link"
                    title={`${label} — still served by the previous console`}
                  >
                    <Icon size={17} />
                    <span>{label}</span>
                    <span className="s7-spacer s7-badge s7-badge-muted">old</span>
                  </a>
                ) : (
                  <NavLink
                    key={to}
                    to={to}
                    onClick={onNavigate}
                    className={({ isActive }) => `s7-nav-link ${isActive ? 'is-active' : ''}`}
                  >
                    {({ isActive }) => (
                      <>
                        {/* A shared layoutId makes the gradient pill slide from the previously
                            active link to this one, rather than disappearing and reappearing. */}
                        {isActive ? (
                          <motion.span
                            layoutId="s7-nav-pill"
                            className="s7-nav-pill"
                            transition={springSoft}
                          />
                        ) : null}
                        <Icon size={17} />
                        <span>{label}</span>
                      </>
                    )}
                  </NavLink>
                ),
              )}
            </div>
          ))}
        </nav>

        <div className="s7-sidebar-footer">
          <div className="s7-user">
            <div className="s7-avatar">{initial}</div>
            <div style={{ minWidth: 0 }}>
              <div className="s7-user-name">{username || 'Admin'}</div>
              <div className="s7-user-role" title={roles.join(', ')}>
                {roles.length ? roles.join(', ') : 'No role'}
              </div>
            </div>
            <motion.button
              type="button"
              className="s7-logout"
              title="Sign out"
              aria-label="Sign out"
              whileHover={{ scale: 1.08 }}
              whileTap={{ scale: 0.94 }}
              onClick={() => clear()}
            >
              <LogOut size={15} />
            </motion.button>
          </div>
        </div>
      </aside>
    </>
  )
}
