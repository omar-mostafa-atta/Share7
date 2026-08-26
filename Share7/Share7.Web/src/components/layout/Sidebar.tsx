import { AnimatePresence, motion } from 'motion/react'
import { LogOut } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../../store/auth'
import { BrandBadge } from '../ui/Logo'
import { NAV } from '../../lib/nav'
import { scrimVariants, springSoft } from '../ui/motion'

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

  // SuperAdmin is the role that can delete an account, so it is worth showing
  // as its own mark rather than as one entry in a comma-joined list.
  const isSuper = roles.includes('SuperAdmin')

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
              {group.items.map(({ to, label, icon: Icon, blurb }) => (
                <NavLink
                  key={to}
                  to={to}
                  // `end` only on the root, or "/" would stay highlighted on every route.
                  end={to === '/'}
                  onClick={onNavigate}
                  title={blurb}
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
              ))}
            </div>
          ))}
        </nav>

        <div className="s7-sidebar-footer">
          <div className="s7-user">
            <div className="s7-avatar">{initial}</div>
            <div style={{ minWidth: 0 }}>
              <div className="s7-user-name">{username || 'Admin'}</div>
              <div className="s7-user-role" title={roles.join(', ')}>
                {isSuper ? 'SuperAdmin' : roles.length ? roles.join(', ') : 'No role'}
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
