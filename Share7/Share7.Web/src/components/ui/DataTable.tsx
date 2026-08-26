import { useMemo, useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import type { ReactNode } from 'react'
import { SkeletonRows } from './primitives'
import { rowVariants } from './motion'

// ===========================================================================
// DataTable
//
// One table for the whole console. Sorting, the sticky header and the empty
// and loading states live here rather than being re-solved per page, which is
// what the vanilla console did — seven pages, seven slightly different tables,
// three of them sortable.
//
// Filtering is deliberately NOT here. Every page filters on different axes
// (a board by cohort, a run by flag state, an offer by expiry) and pushing
// that behind a generic predicate API produced more configuration than the
// three lines it replaced. Pages filter `rows` before passing them in.
// ===========================================================================

export interface Column<T> {
  key: string

  header: ReactNode

  render: (row: T) => ReactNode

  /**
   * Present makes the column sortable. Returns the value to order by, which is
   * usually not what `render` displays — a date column renders "3 days ago"
   * and sorts on the timestamp, and sorting the rendered string would order it
   * alphabetically.
   */
  sort?: (row: T) => string | number | boolean | null | undefined

  /** Right-aligns and applies tabular numerals. */
  numeric?: boolean

  width?: string
}

type Direction = 'asc' | 'desc'

export function DataTable<T>({
  rows,
  columns,
  getId,
  loading,
  empty,
  onRowClick,
  selectedId,
  maxHeight,
  initialSort,
}: {
  rows: T[]
  columns: Column<T>[]
  getId: (row: T) => string
  loading?: boolean
  empty?: ReactNode
  onRowClick?: (row: T) => void
  selectedId?: string | null
  maxHeight?: string
  initialSort?: { key: string; direction?: Direction }
}) {
  const [sortKey, setSortKey] = useState<string | null>(initialSort?.key ?? null)
  const [direction, setDirection] = useState<Direction>(initialSort?.direction ?? 'asc')

  const sorted = useMemo(() => {
    const column = columns.find((c) => c.key === sortKey)
    if (!column?.sort) return rows

    // Copy before sorting: Array.prototype.sort mutates, and `rows` is state
    // owned by the calling page.
    const factor = direction === 'asc' ? 1 : -1

    return [...rows].sort((a, b) => {
      const left = column.sort!(a)
      const right = column.sort!(b)

      // Nulls sort last in both directions rather than being treated as the
      // smallest value. A board with no cycles belongs at the bottom whether
      // the admin is looking for the most or the fewest.
      if (left == null && right == null) return 0
      if (left == null) return 1
      if (right == null) return -1

      if (typeof left === 'number' && typeof right === 'number') return (left - right) * factor
      if (typeof left === 'boolean' && typeof right === 'boolean') {
        return (Number(left) - Number(right)) * factor
      }

      return String(left).localeCompare(String(right), undefined, { numeric: true }) * factor
    })
  }, [rows, columns, sortKey, direction])

  function toggle(column: Column<T>) {
    if (!column.sort) return

    if (sortKey === column.key) {
      setDirection((d) => (d === 'asc' ? 'desc' : 'asc'))
      return
    }

    setSortKey(column.key)

    // A new numeric column opens descending: for counts, values and dates the
    // interesting end is almost always the top one. Text opens ascending.
    setDirection(column.numeric ? 'desc' : 'asc')
  }

  if (loading) return <SkeletonRows rows={5} />

  if (!sorted.length) {
    return <div className="s7-dt-empty">{empty ?? 'Nothing here yet.'}</div>
  }

  return (
    <div className="s7-dt-wrap" style={maxHeight ? { ['--s7-dt-max' as string]: maxHeight } : undefined}>
      <table className={`s7-dt ${onRowClick ? 's7-dt-clickable' : ''}`}>
        <thead>
          <tr>
            {columns.map((column) => {
              const isSorted = sortKey === column.key
              return (
                <th
                  key={column.key}
                  style={column.width ? { width: column.width } : undefined}
                  className={[
                    column.numeric ? 's7-num' : '',
                    column.sort ? 's7-dt-sortable' : '',
                    isSorted ? 'is-sorted' : '',
                  ]
                    .filter(Boolean)
                    .join(' ')}
                  onClick={() => toggle(column)}
                  aria-sort={isSorted ? (direction === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  {column.header}
                  {column.sort ? (
                    <span className="s7-dt-sort-glyph">
                      {!isSorted ? (
                        <ArrowUpDown size={12} />
                      ) : direction === 'asc' ? (
                        <ArrowUp size={12} />
                      ) : (
                        <ArrowDown size={12} />
                      )}
                    </span>
                  ) : null}
                </th>
              )
            })}
          </tr>
        </thead>

        <tbody>
          <AnimatePresence initial={false}>
            {sorted.map((row) => {
              const id = getId(row)
              return (
                <motion.tr
                  key={id}
                  layout="position"
                  variants={rowVariants}
                  initial="hidden"
                  animate="visible"
                  exit="exit"
                  className={selectedId === id ? 'is-selected' : undefined}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                >
                  {columns.map((column) => (
                    <td key={column.key} className={column.numeric ? 's7-num' : undefined}>
                      {column.render(row)}
                    </td>
                  ))}
                </motion.tr>
              )
            })}
          </AnimatePresence>
        </tbody>
      </table>
    </div>
  )
}
