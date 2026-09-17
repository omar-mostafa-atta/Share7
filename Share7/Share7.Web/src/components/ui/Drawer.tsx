import { useEffect, useRef } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { X } from 'lucide-react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { springSoft } from './motion'

// ===========================================================================
// Drawer — right-hand detail sheet
//
// The console's answer to "show me everything about this row". A modal would
// work, but a run with 30 payout lines or a board with 40 cycles is a reading
// task, not a decision, and a drawer keeps the table it came from visible
// behind it so the admin does not lose their place in a long list.
//
// Two structural details, both of which were bugs before they were rules:
//
//  1. ONE keyed child inside AnimatePresence. The panel is nested inside the
//     scrim rather than sitting beside it in a Fragment. AnimatePresence
//     correlates children by key; a Fragment is a single unkeyed child, so the
//     exit callback never fires, the scrim stays mounted at opacity 0, and it
//     goes on covering the page — a dark sheet over content that cannot be
//     clicked. Same trap Modal documents.
//
//  2. Rendered through a portal to document.body. The page wrapper in AppShell
//     animates `y`, which leaves a transform on it, and a transformed ancestor
//     becomes the containing block for `position: fixed` descendants. Inside
//     the tree this drawer would be positioned against the page column rather
//     than the viewport.
// ===========================================================================

export function Drawer({
  open,
  onClose,
  title,
  subtitle,
  footer,
  children,
}: {
  open: boolean
  onClose: () => void
  title: ReactNode
  subtitle?: ReactNode
  footer?: ReactNode
  children: ReactNode
}) {
  // Callers pass an inline arrow for onClose, so its identity changes every render of the parent.
  // Depending on it directly would tear down and re-run the effects below each time — which
  // re-reads `previous` from a body that is already hidden, then restores *that* on close.
  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose

  // Escape closes, and the listener only exists while open — a permanently mounted keydown
  // handler on a page with several drawers closes all of them at once.
  useEffect(() => {
    if (!open) return

    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape') onCloseRef.current()
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  // The page behind must not scroll while a drawer is over it: on a trackpad the wheel otherwise
  // falls through to the table once the drawer's own content reaches its end.
  useEffect(() => {
    if (!open) return

    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.body.style.overflow = previous
    }
  }, [open])

  return createPortal(
    <AnimatePresence>
      {open ? (
        <motion.div
          key="drawer-scrim"
          className="s7-drawer-scrim"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          // pointerEvents: none on the way out so a scrim still finishing its fade cannot
          // swallow the click that follows closing it.
          exit={{ opacity: 0, pointerEvents: 'none' }}
          transition={{ duration: 0.18 }}
          onClick={() => onCloseRef.current()}
        >
          <motion.aside
            className="s7-drawer"
            role="dialog"
            aria-modal="true"
            initial={{ x: '100%' }}
            animate={{ x: 0 }}
            exit={{ x: '100%' }}
            transition={springSoft}
            // Without this a click that starts on the panel bubbles to the scrim and closes the
            // drawer the admin is typing in.
            onClick={(e) => e.stopPropagation()}
          >
            <header className="s7-drawer-head">
              <div style={{ minWidth: 0, flex: '1 1 auto' }}>
                <h2>{title}</h2>
                {subtitle ? <p>{subtitle}</p> : null}
              </div>
              <motion.button
                type="button"
                className="s7-btn s7-btn-ghost s7-btn-icon"
                aria-label="Close"
                whileHover={{ rotate: 90 }}
                whileTap={{ scale: 0.94 }}
                onClick={() => onCloseRef.current()}
              >
                <X size={16} />
              </motion.button>
            </header>

            <div className="s7-drawer-body">{children}</div>

            {footer ? <footer className="s7-drawer-foot">{footer}</footer> : null}
          </motion.aside>
        </motion.div>
      ) : null}
    </AnimatePresence>,
    document.body,
  )
}
