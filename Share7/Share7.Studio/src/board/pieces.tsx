import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { AlertTriangle, Check, X } from 'lucide-react'
import { useI18n } from '../i18n/i18n'
import type { MessageKey } from '../i18n/en'

// ===========================================================================
// The pieces the board is written with
//
// Everything on every screen is made of these, so the Studio looks the same
// from one board to the next. Two rules are enforced here rather than left to
// each screen:
//
//   * a state is a STROKE with a NAME beside it — <Mark> will not render a
//     mark without words, so nothing on the board depends on seeing colour;
//   * an explanation is chalked BESIDE the thing it explains — <Beside> is the
//     only way the Studio explains anything, and there is no help page.
// ===========================================================================

/** Solid means written, dashed means waiting, struck means ended, double means wrong. */
export type Stroke = 'written' | 'writing' | 'waiting' | 'ended' | 'wrong' | 'live'

export function Mark({ stroke, children, title }: { stroke: Stroke; children: ReactNode; title?: string }) {
  // A stroke is drawn when it CHANGES, and never on arrival — a board that
  // animates everything it is holding the moment you open it is telling you
  // nothing. The counter outlives the remount that redraws the line, so the
  // first paint is still and every state after it is written in front of you.
  const seen = useRef(stroke)
  const changes = useRef(0)
  if (seen.current !== stroke) {
    seen.current = stroke
    changes.current += 1
  }
  const drawn = changes.current > 0

  return (
    <span className="mark" data-stroke={stroke} data-draws={drawn ? 'yes' : undefined} title={title}>
      <span key={changes.current} className="mark-line" aria-hidden="true" />
      <span key={`name-${changes.current}`} className="mark-name">
        {children}
      </span>
    </span>
  )
}

/** A short line joining an explanation to the exact field it explains. */
export function Beside({ tone, children }: { tone?: 'wrong' | 'live'; children: ReactNode }) {
  return (
    <p className="beside" data-tone={tone}>
      {children}
    </p>
  )
}

export function Engraved({ children }: { children: ReactNode }) {
  return <span className="engraved">{children}</span>
}

// ---------------------------------------------------------------------------
// The steps of a journey, at one scale, across the top of the board
// ---------------------------------------------------------------------------

export function Steps({ steps, at }: { steps: string[]; at: number }) {
  // Same rule as <Mark>: a step is drawn when the journey ADVANCES, which is a
  // change the member just made, and never when the page arrives carrying it.
  const seen = useRef(at)
  const changes = useRef(0)
  if (seen.current !== at) {
    seen.current = at
    changes.current += 1
  }

  return (
    <ol className="steps" data-draws={changes.current > 0 ? 'yes' : undefined}>
      {steps.map((step, index) => (
        <li
          key={`${step}-${changes.current}`}
          className="step"
          data-state={index < at ? 'done' : index === at ? 'here' : 'ahead'}
        >
          {step}
        </li>
      ))}
    </ol>
  )
}

// ---------------------------------------------------------------------------
// Fields
// ---------------------------------------------------------------------------

interface WriteProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label: string
  hint?: ReactNode
  problem?: ReactNode
  lines?: number
  dir?: 'ltr' | 'rtl'
}

/**
 * One ruled line to write on. The hint and the problem are both chalked beneath
 * it, joined to it by a short line; a problem replaces the hint, because a field
 * that is wrong has nothing else worth saying.
 */
export function Write({ label, hint, problem, lines, ...rest }: WriteProps) {
  const id = useId()
  const describedBy = problem ? `${id}-problem` : hint ? `${id}-hint` : undefined

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {lines && lines > 1 ? (
        <textarea
          id={id}
          className="write"
          rows={lines}
          aria-invalid={problem ? true : undefined}
          aria-describedby={describedBy}
          {...(rest as unknown as React.TextareaHTMLAttributes<HTMLTextAreaElement>)}
        />
      ) : (
        <input id={id} className="write" aria-invalid={problem ? true : undefined} aria-describedby={describedBy} {...rest} />
      )}
      {problem ? (
        <p className="beside" data-tone="wrong" id={`${id}-problem`}>
          {problem}
        </p>
      ) : hint ? (
        <p className="beside" id={`${id}-hint`}>
          {hint}
        </p>
      ) : null}
    </div>
  )
}

export function Choose({
  label,
  hint,
  children,
  ...rest
}: React.SelectHTMLAttributes<HTMLSelectElement> & { label: string; hint?: ReactNode }) {
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <select id={id} className="write" aria-describedby={hint ? `${id}-hint` : undefined} {...rest}>
        {children}
      </select>
      {hint ? (
        <p className="beside" id={`${id}-hint`}>
          {hint}
        </p>
      ) : null}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Nothing there yet — an empty board teaches what it is for
// ---------------------------------------------------------------------------

export function Nothing({ title, children, action }: { title?: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="nothing">
      {/* Inside a band the heading above has already said what is missing, and
          repeating it in a bigger type size just says it twice. */}
      {title ? <h3>{title}</h3> : null}
      {children ? <p className="said">{children}</p> : null}
      {action}
    </div>
  )
}

/** The board being wiped clean while what goes on it is fetched. */
export function Wiping({ rows = 3, tall = false }: { rows?: number; tall?: boolean }) {
  const { t } = useI18n()
  return (
    <div aria-busy="true" aria-live="polite" style={{ display: 'grid', gap: 'var(--s3)', padding: 'var(--s4) 0' }}>
      <span className="sr-only">{t('common.loading')}</span>
      {Array.from({ length: rows }, (_, index) => (
        <div
          key={index}
          className="wiping"
          style={{ height: tall ? 88 : 20, width: index % 3 === 2 ? '62%' : index % 3 === 1 ? '84%' : '100%' }}
        />
      ))}
    </div>
  )
}

// ---------------------------------------------------------------------------
// What the board says back
// ---------------------------------------------------------------------------

interface Told {
  id: number
  text: string
  tone: 'said' | 'wrong'
}

interface Telling {
  say: (text: string) => void
  wrong: (text: string) => void
}

const TellingContext = createContext<Telling | null>(null)

export function TellingProvider({ children }: { children: ReactNode }) {
  const [told, setTold] = useState<Told[]>([])
  const next = useRef(1)

  const add = useCallback((text: string, tone: Told['tone']) => {
    const id = next.current++
    setTold((current) => [...current, { id, text, tone }])
    window.setTimeout(() => setTold((current) => current.filter((one) => one.id !== id)), tone === 'wrong' ? 7000 : 4000)
  }, [])

  const value = useMemo<Telling>(
    () => ({ say: (text) => add(text, 'said'), wrong: (text) => add(text, 'wrong') }),
    [add],
  )

  return (
    <TellingContext.Provider value={value}>
      {children}
      <div className="telling" role="status" aria-live="polite">
        {told.map((one) => (
          <div key={one.id} className="told" data-tone={one.tone === 'wrong' ? 'wrong' : undefined}>
            {one.tone === 'wrong' ? <AlertTriangle size={15} strokeWidth={1.5} aria-hidden /> : <Check size={15} strokeWidth={1.5} aria-hidden />}
            <span>{one.text}</span>
          </div>
        ))}
      </div>
    </TellingContext.Provider>
  )
}

export function useTelling(): Telling {
  const telling = useContext(TellingContext)
  if (!telling) throw new Error('useTelling() outside <TellingProvider>')
  return telling
}

/**
 * Turns anything thrown by the API client into one sentence in the member's
 * language. A refusal the server explained keeps its own words; anything else
 * falls back to "something went wrong".
 */
export function useSaying() {
  const { tError } = useI18n()
  const { wrong } = useTelling()

  return useCallback(
    (error: unknown) => {
      const messageKey = (error as { messageKey?: string } | null)?.messageKey
      wrong(messageKey ? tError(messageKey) : tError('errors.unexpected'))
    },
    [tError, wrong],
  )
}

// ---------------------------------------------------------------------------
// A sheet pulled over the board, for the few tasks that need protecting
// ---------------------------------------------------------------------------

export function Sheet({
  open,
  onClose,
  title,
  children,
  actions,
}: {
  open: boolean
  onClose: () => void
  title: string
  children: ReactNode
  actions?: ReactNode
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const { t } = useI18n()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog ref={ref} onCancel={(event) => { event.preventDefault(); onClose() }} aria-label={title}>
      <div style={{ display: 'grid', gap: 'var(--s4)' }}>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 'var(--s4)' }}>
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{title}</h2>
          <button type="button" className="act icon" style={{ marginInlineStart: 'auto' }} onClick={onClose} aria-label={t('common.close')}>
            <X size={16} strokeWidth={1.5} aria-hidden />
          </button>
        </div>
        {children}
        {actions ? <div className="acts" style={{ justifyContent: 'flex-end', marginTop: 'var(--s2)' }}>{actions}</div> : null}
      </div>
    </dialog>
  )
}

// ---------------------------------------------------------------------------
// Small shared readings
// ---------------------------------------------------------------------------

/**
 * "Grade 5 · Term 1 · Science" — the way down to a node, in the interface language. The last step
 * is where you are and is not a link — unless `linkAll`, for a trail that stops above the board
 * it sits on, whose own name is the heading beneath it.
 */
export function Trail({
  steps,
  onGo,
  linkAll = false,
}: {
  steps: { id: string; label: string }[]
  onGo?: (id: string) => void
  linkAll?: boolean
}) {
  return (
    <nav className="trail" aria-label="trail">
      {steps.map((step, index) => (
        <span key={step.id} style={{ display: 'contents' }}>
          {index > 0 ? <span className="sep" aria-hidden>·</span> : null}
          {onGo && (linkAll || index < steps.length - 1) ? (
            <a href={`#/curriculum/${step.id}`} onClick={(event) => { event.preventDefault(); onGo(step.id) }}>
              {step.label}
            </a>
          ) : (
            <span>{step.label}</span>
          )}
        </span>
      ))}
    </nav>
  )
}

/** A count with its name, so "3" is never alone on the board. */
export function Counted({ count, name }: { count: number; name: MessageKey }) {
  const { t } = useI18n()
  return (
    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
      <span className="num">{count}</span> {t(name)}
    </span>
  )
}
