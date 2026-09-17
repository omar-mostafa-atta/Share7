import { useEffect, useMemo, useRef, useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { CornerDownLeft, Moon, Rows3, Search, Sun, LogOut } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../store/auth'
import { usePrefs } from '../../store/prefs'
import { NAV_ENTRIES } from '../../lib/nav'
import { modalVariants, scrimVariants } from './motion'

// ===========================================================================
// Command palette (Cmd/Ctrl-K)
//
// Fifteen routes is past the point where a sidebar is the fast path. This is
// the fast path: type three letters, hit Enter.
//
// Matching is a subsequence test, not `includes`. "sigval" finds Signal
// Valuations and "lbf" finds nothing useful under `includes` but everything
// under this — which is how anyone who has used a palette before expects it to
// behave.
// ===========================================================================

interface Command {
  id: string
  section: string
  label: string
  blurb: string
  hint?: string
  icon?: React.ReactNode
  run: () => void
}

/** Whether `query`'s characters appear in `text` in order, and how tightly. */
function score(query: string, text: string): number | null {
  if (!query) return 0

  const haystack = text.toLowerCase()
  let index = 0
  let first = -1
  let last = 0

  for (const ch of query.toLowerCase()) {
    const found = haystack.indexOf(ch, index)
    if (found === -1) return null
    if (first === -1) first = found
    last = found
    index = found + 1
  }

  // Lower is better: prefer matches that start early and stay close together,
  // so "cur" ranks Curriculum above "Signal Valuations" (which also contains
  // c-u-r, spread across three words).
  return last - first + first * 0.5
}

export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const setTheme = usePrefs((s) => s.setTheme)
  const theme = usePrefs((s) => s.theme)
  const density = usePrefs((s) => s.density)
  const setDensity = usePrefs((s) => s.setDensity)
  const clear = useAuth((s) => s.clear)

  const [query, setQuery] = useState('')
  const [cursor, setCursor] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  const commands = useMemo<Command[]>(() => {
    const routes: Command[] = NAV_ENTRIES.map((entry) => ({
      id: `go:${entry.to}`,
      section: entry.section,
      label: entry.label,
      blurb: entry.blurb,
      hint: entry.to,
      icon: <entry.icon size={15} />,
      run: () => navigate(entry.to),
    }))

    const actions: Command[] = [
      {
        id: 'act:theme',
        section: 'Actions',
        label: theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme',
        blurb: 'appearance colour scheme dark light',
        icon: theme === 'dark' ? <Sun size={15} /> : <Moon size={15} />,
        run: () => setTheme(theme === 'dark' ? 'light' : 'dark'),
      },
      {
        id: 'act:density',
        section: 'Actions',
        label: density === 'compact' ? 'Comfortable row height' : 'Compact row height',
        blurb: 'density rows spacing table compact',
        icon: <Rows3 size={15} />,
        run: () => setDensity(density === 'compact' ? 'comfortable' : 'compact'),
      },
      {
        id: 'act:signout',
        section: 'Actions',
        label: 'Sign out',
        blurb: 'log out session end',
        icon: <LogOut size={15} />,
        run: () => clear(),
      },
    ]

    return [...routes, ...actions]
  }, [navigate, theme, setTheme, density, setDensity, clear])

  const results = useMemo(() => {
    if (!query.trim()) return commands

    return commands
      .map((command) => {
        // Label matches beat blurb matches, so typing "runs" puts the Runs
        // route above the objectives entry that merely mentions runs.
        const onLabel = score(query, command.label)
        const onBlurb = score(query, `${command.label} ${command.blurb}`)

        const best = onLabel != null ? onLabel : onBlurb != null ? onBlurb + 100 : null
        return best == null ? null : { command, rank: best }
      })
      .filter((x): x is { command: Command; rank: number } => x !== null)
      .sort((a, b) => a.rank - b.rank)
      .map((x) => x.command)
  }, [commands, query])

  // Reset on every open. Reopening onto the previous query is a small thing
  // that feels broken every single time.
  useEffect(() => {
    if (!open) return
    setQuery('')
    setCursor(0)
    // Focus after the entrance animation has begun, or the browser scrolls the
    // still-offscreen panel into view and the whole page jumps.
    const id = window.setTimeout(() => inputRef.current?.focus(), 20)
    return () => window.clearTimeout(id)
  }, [open])

  useEffect(() => setCursor(0), [query])

  // Keep the cursor in view when it moves past the visible window.
  useEffect(() => {
    const node = listRef.current?.querySelector<HTMLElement>('.is-active')
    node?.scrollIntoView({ block: 'nearest' })
  }, [cursor])

  function onKeyDown(event: React.KeyboardEvent) {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setCursor((c) => (results.length ? (c + 1) % results.length : 0))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setCursor((c) => (results.length ? (c - 1 + results.length) % results.length : 0))
    } else if (event.key === 'Enter') {
      event.preventDefault()
      const chosen = results[cursor]
      if (chosen) {
        // Close first: a command that navigates unmounts this component, and
        // calling onClose afterwards would set state on a dead tree.
        onClose()
        chosen.run()
      }
    } else if (event.key === 'Escape') {
      event.preventDefault()
      onClose()
    }
  }

  let lastSection = ''

  return (
    <AnimatePresence>
      {open ? (
        <motion.div
          // Keyed, and it is load-bearing. AnimatePresence correlates children by key; without
          // one the exit never completes, so this scrim — fixed, full-viewport, z-index 90,
          // blurred — stays mounted after the palette closes and covers the console. And because
          // the palette lives in AppShell rather than in a page, it survives every navigation:
          // one ⌘K and the whole app is behind a dark sheet until a hard reload.
          key="palette-scrim"
          className="s7-palette-scrim"
          variants={scrimVariants}
          initial="hidden"
          animate="visible"
          exit="exit"
          onClick={onClose}
        >
          <motion.div
            className="s7-palette"
            role="dialog"
            aria-modal="true"
            aria-label="Command palette"
            variants={modalVariants}
            initial="hidden"
            animate="visible"
            exit="exit"
            onClick={(e) => e.stopPropagation()}
            onKeyDown={onKeyDown}
          >
            <div className="s7-palette-field">
              <Search size={17} color="var(--s7-muted)" />
              <input
                ref={inputRef}
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Jump to a section, or run a command…"
                aria-label="Command"
                autoComplete="off"
                spellCheck={false}
              />
              <span className="s7-kbd">esc</span>
            </div>

            <div className="s7-palette-list" ref={listRef}>
              {!results.length ? (
                <div className="s7-dt-empty">No match for “{query}”.</div>
              ) : (
                results.map((command, index) => {
                  const header = command.section !== lastSection ? command.section : null
                  lastSection = command.section

                  return (
                    <div key={command.id}>
                      {header ? <div className="s7-palette-group">{header}</div> : null}
                      <button
                        type="button"
                        className={`s7-palette-item ${index === cursor ? 'is-active' : ''}`}
                        // Hover moves the keyboard cursor rather than painting a
                        // second highlight, so there is only ever one selection
                        // and Enter always runs what looks chosen.
                        onMouseMove={() => setCursor(index)}
                        onClick={() => {
                          onClose()
                          command.run()
                        }}
                      >
                        {command.icon}
                        <span>{command.label}</span>
                        {command.hint ? <span className="s7-palette-hint">{command.hint}</span> : null}
                      </button>
                    </div>
                  )
                })
              )}
            </div>

            <div className="s7-palette-foot">
              <span className="s7-inline">
                <span className="s7-kbd">↑</span>
                <span className="s7-kbd">↓</span> navigate
              </span>
              <span className="s7-inline">
                <span className="s7-kbd">
                  <CornerDownLeft size={10} />
                </span>{' '}
                open
              </span>
              <span className="s7-inline" style={{ marginLeft: 'auto' }}>
                {results.length} result{results.length === 1 ? '' : 's'}
              </span>
            </div>
          </motion.div>
        </motion.div>
      ) : null}
    </AnimatePresence>
  )
}
