import { AnimatePresence, motion } from 'motion/react'
import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { useId } from 'react'

// ---------------------------------------------------------------------------
// Field wrapper
// ---------------------------------------------------------------------------

/**
 * Label + control + inline error. The error animates its own height so the form does not jump
 * when a message appears under a control the admin is still typing in.
 */
export function Field({
  label,
  error,
  hint,
  children,
}: {
  label: string
  error?: string | null
  hint?: ReactNode
  children: ReactNode
}) {
  return (
    <div className="s7-field">
      <label className="s7-label">{label}</label>
      {children}
      <AnimatePresence initial={false}>
        {error ? (
          <motion.span
            // Keyed for the same reason as the modal scrim: an AnimatePresence child without a
            // key never completes its exit and stays in the layout permanently.
            key="error"
            className="s7-error"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.18 }}
          >
            {error}
          </motion.span>
        ) : null}
      </AnimatePresence>
      {hint ? <span className="s7-hint">{hint}</span> : null}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Controls
// ---------------------------------------------------------------------------

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean
  mono?: boolean
}

export function Input({ invalid, mono, className, ...rest }: InputProps) {
  return (
    <input
      {...rest}
      className={[
        's7-input',
        invalid ? 'is-invalid' : '',
        mono ? 's7-input-mono' : '',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
    />
  )
}

export function Select({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select {...rest} className={`s7-select ${className ?? ''}`}>
      {children}
    </select>
  )
}

/**
 * A checkbox styled as a switch. The visible track is a sibling of a real hidden input, so
 * keyboard focus, labels and form semantics all still work.
 */
export function Switch({
  checked,
  onChange,
  label,
  disabled,
}: {
  checked: boolean
  onChange: (checked: boolean) => void
  label: ReactNode
  disabled?: boolean
}) {
  const id = useId()
  return (
    <label className="s7-switch" htmlFor={id}>
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span className="s7-switch-track" aria-hidden />
      <span className="s7-switch-label">{label}</span>
    </label>
  )
}
