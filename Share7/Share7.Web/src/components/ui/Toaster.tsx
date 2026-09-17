import { AnimatePresence, motion } from 'motion/react'
import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react'
import { useToasts, type ToastKind } from '../../store/toast'
import { toastVariants } from './motion'

const ICONS: Record<ToastKind, typeof Info> = {
  danger: XCircle,
  success: CheckCircle2,
  warning: AlertTriangle,
  info: Info,
}

/**
 * Bottom-right toast stack. Mounted once at the app root.
 *
 * `layout` on each toast makes the remaining ones slide up when one above is dismissed, instead
 * of the stack jumping. `popLayout` keeps the exiting toast out of the flow while it animates
 * away, so the slide-up starts immediately rather than after it disappears.
 */
export function Toaster() {
  const toasts = useToasts((s) => s.toasts)
  const dismiss = useToasts((s) => s.dismiss)

  return (
    <div className="s7-toaster" role="region" aria-label="Notifications">
      <AnimatePresence mode="popLayout" initial={false}>
        {toasts.map((t) => {
          const Icon = ICONS[t.kind]
          return (
            <motion.output
              key={t.id}
              layout
              variants={toastVariants}
              initial="hidden"
              animate="visible"
              exit="exit"
              className={`s7-toast s7-toast-${t.kind}`}
            >
              <Icon size={17} aria-hidden />
              <div className="s7-toast-body">
                <div className="s7-toast-title">{t.title}</div>
                {t.detail ? <div className="s7-toast-detail">{t.detail}</div> : null}
              </div>
              <button
                type="button"
                className="s7-toast-close"
                onClick={() => dismiss(t.id)}
                aria-label="Dismiss"
              >
                <X size={15} />
              </button>
            </motion.output>
          )
        })}
      </AnimatePresence>
    </div>
  )
}
