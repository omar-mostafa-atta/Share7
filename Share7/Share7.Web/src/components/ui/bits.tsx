import { useState } from 'react'
import { motion } from 'motion/react'
import { Check, Copy, Search, X } from 'lucide-react'
import type { ReactNode } from 'react'
import { riseVariants } from './motion'

// ===========================================================================
// Small shared parts
//
// Each of these existed three or four times across the vanilla console's page
// scripts, in slightly different form. They are here so a change to how the
// console shows an id, a proportion or a search box happens once.
// ===========================================================================

// ---------------------------------------------------------------------------
// Page title
// ---------------------------------------------------------------------------

export function PageTitle({
  icon,
  title,
  subtitle,
  actions,
}: {
  icon: ReactNode
  title: string
  subtitle?: ReactNode
  actions?: ReactNode
}) {
  return (
    <motion.div variants={riseVariants} className="s7-page-title">
      <span className="s7-page-glyph" aria-hidden>
        {icon}
      </span>
      <div style={{ minWidth: 0 }}>
        <h1>{title}</h1>
        {subtitle ? <p>{subtitle}</p> : null}
      </div>
      {actions ? <div className="s7-page-actions">{actions}</div> : null}
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Search box
// ---------------------------------------------------------------------------

export function SearchBox({
  value,
  onChange,
  placeholder = 'Search…',
}: {
  value: string
  onChange: (value: string) => void
  placeholder?: string
}) {
  return (
    <div className="s7-search">
      <Search size={15} />
      <input
        type="search"
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        // Escape clears rather than closing anything. Inside a drawer the
        // browser's own search-input clear would otherwise race the drawer's
        // Escape handler and both would fire.
        onKeyDown={(e) => {
          if (e.key === 'Escape' && value) {
            e.stopPropagation()
            onChange('')
          }
        }}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Segmented control
// ---------------------------------------------------------------------------

export function Segmented<T extends string>({
  value,
  options,
  onChange,
  layoutId,
}: {
  value: T
  options: { value: T; label: ReactNode }[]
  onChange: (value: T) => void

  /**
   * Required, and must be unique per rendered control. The sliding pill is a
   * shared-layout animation; two controls on one page with the same id make
   * the pill fly between them when either changes.
   */
  layoutId: string
}) {
  return (
    <div className="s7-seg" role="tablist">
      {options.map((option) => {
        const active = option.value === value
        return (
          <button
            key={option.value}
            type="button"
            role="tab"
            aria-selected={active}
            className={active ? 'is-active' : undefined}
            onClick={() => onChange(option.value)}
          >
            {active ? <motion.span layoutId={layoutId} className="s7-seg-pill" /> : null}
            <span>{option.label}</span>
          </button>
        )
      })}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Copyable id
// ---------------------------------------------------------------------------

/**
 * A GUID shown so it can be *recognised and copied*, not read.
 *
 * Middle truncation rather than a trailing ellipsis: consecutive rows of
 * sequential GUIDs share a prefix, so `a3f2…` alone distinguishes nothing. The
 * last four characters are what differ.
 */
export function CopyId({ id, label }: { id: string; label?: string }) {
  const [copied, setCopied] = useState(false)

  if (!id) return <span className="s7-muted">—</span>

  const short = id.length > 12 ? `${id.slice(0, 6)}…${id.slice(-4)}` : id

  async function copy(e: React.MouseEvent) {
    // Ids frequently sit inside a clickable table row. Without this the copy
    // also opens the row's drawer.
    e.stopPropagation()

    try {
      await navigator.clipboard.writeText(id)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1200)
    } catch {
      // Clipboard access is refused in some embedded contexts. The id is still
      // visible and selectable, so there is nothing to recover from.
    }
  }

  return (
    <button type="button" className="s7-id" onClick={copy} title={label ? `${label}: ${id}` : id}>
      {copied ? <Check size={11} /> : <Copy size={11} />}
      {short}
    </button>
  )
}

// ---------------------------------------------------------------------------
// Proportion meter
// ---------------------------------------------------------------------------

export function Meter({ value, max, tone }: { value: number; max: number | null | undefined; tone?: 'warning' | 'danger' }) {
  // No ceiling means nothing to be a proportion *of*, so the bar would be
  // meaningless. Say so instead of drawing an empty or full track.
  if (max == null || max <= 0) return <span className="s7-muted">no cap</span>

  const ratio = Math.max(0, Math.min(1, value / max))
  const resolved = tone ?? (ratio >= 1 ? 'danger' : ratio >= 0.8 ? 'warning' : undefined)

  return (
    <span className="s7-inline" style={{ gap: '0.45rem' }}>
      <span className={`s7-meter ${resolved ? `s7-meter-${resolved}` : ''}`}>
        <motion.span
          initial={{ width: 0 }}
          animate={{ width: `${ratio * 100}%` }}
          transition={{ duration: 0.5, ease: [0.16, 1, 0.3, 1] }}
        />
      </span>
      <span className="s7-muted s7-num" style={{ fontSize: '0.75rem' }}>
        {value.toLocaleString()} / {max.toLocaleString()}
      </span>
    </span>
  )
}

// ---------------------------------------------------------------------------
// Status dot
// ---------------------------------------------------------------------------

export function Dot({ live, title }: { live?: boolean; title?: string }) {
  return <span className={`s7-dot ${live ? 's7-dot-live' : ''}`} title={title} aria-label={title} />
}

// ---------------------------------------------------------------------------
// Definition list
// ---------------------------------------------------------------------------

export function DefList({ children }: { children: ReactNode }) {
  return <dl className="s7-dl">{children}</dl>
}

export function Def({ label, children }: { label: ReactNode; children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children ?? <span className="s7-muted">—</span>}</dd>
    </>
  )
}

// ---------------------------------------------------------------------------
// Note
// ---------------------------------------------------------------------------

export function Note({ tone, children }: { tone?: 'warning' | 'danger'; children: ReactNode }) {
  return <div className={`s7-note ${tone ? `s7-note-${tone}` : ''}`}>{children}</div>
}

// ---------------------------------------------------------------------------
// Removable filter chip
// ---------------------------------------------------------------------------

export function FilterChip({ label, onClear }: { label: ReactNode; onClear: () => void }) {
  return (
    <span className="s7-badge s7-badge-brand" style={{ gap: '0.3rem' }}>
      {label}
      <button
        type="button"
        onClick={onClear}
        aria-label="Clear filter"
        style={{ display: 'grid', background: 'none', border: 0, padding: 0, cursor: 'pointer', color: 'inherit' }}
      >
        <X size={11} />
      </button>
    </span>
  )
}
