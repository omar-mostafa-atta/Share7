import { AnimatePresence, motion } from 'motion/react'
import { Coins, Infinity as InfinityIcon, Pencil } from 'lucide-react'
import { Badge, EmptyState, SkeletonRows } from '../../components/ui/primitives'
import { listVariants, rowVariants, tapScale } from '../../components/ui/motion'
import type { CurrencyDto } from '../../types/api'

export function CurrencyTable({
  currencies,
  loading,
  onEdit,
}: {
  currencies: CurrencyDto[]
  loading: boolean
  onEdit: (currency: CurrencyDto) => void
}) {
  if (loading) return <SkeletonRows rows={4} />

  if (!currencies.length) {
    return <EmptyState icon={<Coins size={26} />}>No currencies defined yet.</EmptyState>
  }

  return (
    <div className="s7-table-wrap">
      <table className="s7-table">
        <thead>
          <tr>
            <th>Key</th>
            <th>Name</th>
            <th>Type</th>
            <th className="s7-num">Daily cap</th>
            <th className="s7-center">Status</th>
            <th aria-label="Actions" />
          </tr>
        </thead>

        <motion.tbody variants={listVariants} initial="hidden" animate="visible">
          <AnimatePresence initial={false}>
            {currencies.map((c) => (
              <motion.tr key={c.currencyId} variants={rowVariants} exit="exit" layout>
                <td>
                  <code className="s7-key">{c.key}</code>
                </td>

                <td>
                  <div>{c.name}</div>
                  {c.description ? (
                    <div className="s7-muted" style={{ fontSize: '0.75rem' }}>
                      {c.description}
                    </div>
                  ) : null}
                </td>

                <td>
                  {c.isHard ? (
                    <Badge tone="warning">hard</Badge>
                  ) : (
                    <Badge tone="info">soft</Badge>
                  )}
                </td>

                {/*
                  Null means two different things depending on isHard — no ceiling for a soft
                  currency, but zero gameplay earning for a hard one — so it cannot render as a
                  single dash for both.
                */}
                <td className="s7-num">
                  {c.dailyEarnCap != null ? (
                    c.dailyEarnCap.toLocaleString()
                  ) : c.isHard ? (
                    <span className="s7-muted" title="No gameplay source: a hard currency with no cap earns nothing">
                      none
                    </span>
                  ) : (
                    <InfinityIcon
                      size={15}
                      className="s7-muted"
                      aria-label="No ceiling"
                      style={{ verticalAlign: 'middle' }}
                    />
                  )}
                </td>

                <td className="s7-center">
                  {c.enabled ? (
                    <Badge tone="success">active</Badge>
                  ) : (
                    <Badge tone="muted">retired</Badge>
                  )}
                </td>

                <td className="s7-center">
                  <motion.button
                    type="button"
                    className="s7-btn s7-btn-ghost s7-btn-icon"
                    title={`Edit ${c.key}`}
                    aria-label={`Edit ${c.key}`}
                    onClick={() => onEdit(c)}
                    whileHover={{ y: -1 }}
                    whileTap={tapScale}
                  >
                    <Pencil size={14} />
                  </motion.button>
                </td>
              </motion.tr>
            ))}
          </AnimatePresence>
        </motion.tbody>
      </table>
    </div>
  )
}
