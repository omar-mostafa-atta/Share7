import { motion } from 'motion/react'
import type { ComponentProps, ReactNode } from 'react'
import { RefreshCw } from 'lucide-react'
import { hoverLift, riseVariants, tapScale } from './motion'

// ---------------------------------------------------------------------------
// Card
// ---------------------------------------------------------------------------

/** A card that rises into place as part of its parent's stagger. */
export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <motion.section variants={riseVariants} className={`s7-card ${className ?? ''}`}>
      {children}
    </motion.section>
  )
}

export function CardHeader({ icon, title, actions }: { icon?: ReactNode; title: ReactNode; actions?: ReactNode }) {
  return (
    <header className="s7-card-header">
      {icon}
      <span>{title}</span>
      {actions ? <span className="s7-spacer">{actions}</span> : null}
    </header>
  )
}

export function CardBody({ children }: { children: ReactNode }) {
  return <div className="s7-card-body">{children}</div>
}

export function Subhead({ icon, children }: { icon?: ReactNode; children: ReactNode }) {
  return (
    <h3 className="s7-subhead">
      {icon}
      {children}
    </h3>
  )
}

export function Divider() {
  return <hr className="s7-divider" />
}

// ---------------------------------------------------------------------------
// Button
// ---------------------------------------------------------------------------

type ButtonVariant = 'primary' | 'ghost' | 'danger'

// `children` is re-declared as plain ReactNode: motion.button widens it to accept MotionValue,
// which nothing here passes and which is not assignable to ReactNode when rendered.
interface ButtonProps extends Omit<ComponentProps<typeof motion.button>, 'variants' | 'children'> {
  variant?: ButtonVariant
  loading?: boolean
  children?: ReactNode
}

export function Button({ variant = 'primary', loading, children, disabled, ...rest }: ButtonProps) {
  return (
    <motion.button
      type="button"
      whileHover={disabled || loading ? undefined : hoverLift}
      whileTap={disabled || loading ? undefined : tapScale}
      disabled={disabled || loading}
      {...rest}
      className={`s7-btn s7-btn-${variant} ${rest.className ?? ''}`}
    >
      {loading ? <RefreshCw size={15} className="s7-spin" aria-hidden /> : null}
      {children}
    </motion.button>
  )
}

/**
 * The square refresh/action button in a card header. Spins its icon while busy, which is the only
 * loading signal on a reload that leaves the existing content on screen.
 */
export function IconButton({
  label,
  busy,
  onClick,
  children,
}: {
  label: string
  busy?: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <motion.button
      type="button"
      title={label}
      aria-label={label}
      onClick={onClick}
      disabled={busy}
      whileHover={busy ? undefined : { rotate: -25 }}
      whileTap={busy ? undefined : tapScale}
      className="s7-btn s7-btn-ghost s7-btn-icon"
    >
      <span className={busy ? 's7-spin' : undefined} style={{ display: 'grid' }}>
        {children}
      </span>
    </motion.button>
  )
}

// ---------------------------------------------------------------------------
// Badge
// ---------------------------------------------------------------------------

type BadgeTone = 'success' | 'muted' | 'warning' | 'danger' | 'info' | 'brand'

export function Badge({ tone = 'muted', children }: { tone?: BadgeTone; children: ReactNode }) {
  return <span className={`s7-badge s7-badge-${tone}`}>{children}</span>
}

// ---------------------------------------------------------------------------
// Empty / loading
// ---------------------------------------------------------------------------

export function EmptyState({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.97 }}
      animate={{ opacity: 1, scale: 1 }}
      transition={{ duration: 0.3 }}
      className="s7-empty"
    >
      {icon}
      <span>{children}</span>
    </motion.div>
  )
}

/**
 * Shimmering placeholder rows.
 *
 * Deliberately not a spinner: the skeleton occupies the height the real content will, so the card
 * does not resize when data lands. The old console showed a centred spinner and every page jumped
 * on first paint.
 */
export function SkeletonRows({ rows = 3 }: { rows?: number }) {
  return (
    <div aria-busy="true" aria-live="polite">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="s7-skeleton s7-skeleton-row" style={{ opacity: 1 - i * 0.15 }} />
      ))}
    </div>
  )
}
