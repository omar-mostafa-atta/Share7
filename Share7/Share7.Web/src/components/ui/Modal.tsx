import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { modalVariants, scrimVariants } from './motion'

/**
 * Dialog rendered into a portal at document.body.
 *
 * A portal rather than inline markup because the modal must escape the card's `overflow` and
 * stacking context — inline, a dialog opened from inside a scrollable card gets clipped by it.
 */
export function Modal({
  open,
  onClose,
  icon,
  title,
  children,
  footer,
}: {
  open: boolean
  onClose: () => void
  icon?: ReactNode
  title: ReactNode
  children: ReactNode
  footer?: ReactNode
}) {
  // Callers pass an inline arrow for onClose, so its identity changes on every render of the
  // parent. Depending on it directly would tear down and re-run the effect below each time —
  // which re-reads `previous` from a body that is already `hidden`, and then restores *that* on
  // close. A ref keeps the handler current while the effect depends only on `open`.
  const onCloseRef = useRef(onClose)
  onCloseRef.current = onClose

  // Escape closes, and the page behind is locked so a trackpad scroll does not move it under the
  // dialog. Both are undone on unmount, including when the component leaves while still open.
  useEffect(() => {
    if (!open) return

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onCloseRef.current()
    }

    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    window.addEventListener('keydown', onKey)

    return () => {
      document.body.style.overflow = previous
      window.removeEventListener('keydown', onKey)
    }
  }, [open])

  return createPortal(
    <AnimatePresence>
      {open ? (
        <motion.div
          // AnimatePresence tracks its children by key, and without one it cannot correlate the
          // removed child with the node it is animating out — so the exit callback never fires,
          // the element stays mounted forever, and a full-screen scrim at opacity 0 goes on
          // swallowing every click on the page. A single conditional child still needs this.
          key="scrim"
          className="s7-modal-scrim"
          variants={scrimVariants}
          initial="hidden"
          animate="visible"
          exit="exit"
          onClick={onClose}
        >
          <motion.div
            className="s7-modal"
            variants={modalVariants}
            role="dialog"
            aria-modal="true"
            // Without this a click that starts on the panel would bubble to the scrim and close
            // the dialog the admin is typing in.
            onClick={(e) => e.stopPropagation()}
          >
            <header className="s7-modal-header">
              {icon}
              <span>{title}</span>
            </header>
            <div className="s7-modal-body">{children}</div>
            {footer ? <footer className="s7-modal-footer">{footer}</footer> : null}
          </motion.div>
        </motion.div>
      ) : null}
    </AnimatePresence>,
    document.body,
  )
}
