import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { AnimatedNumber } from './AnimatedNumber'
import { riseVariants } from './motion'

// ===========================================================================
// Stat tiles
//
// The KPI row at the top of a page. Every tile answers one question with one
// number; anything needing a sentence belongs in `sub`, and anything needing
// a table is not a stat.
// ===========================================================================

export type StatTone = 'brand' | 'success' | 'warning' | 'danger' | 'info' | 'cool'

export function StatRow({ children }: { children: ReactNode }) {
  return <div className="s7-stats">{children}</div>
}

export function Stat({
  icon,
  label,
  value,
  sub,
  tone = 'brand',
}: {
  icon?: ReactNode
  label: string

  /**
   * A number counts up on change; a string is rendered as-is. The distinction
   * matters — counting up "12 of 40" character by character is nonsense, and
   * counting a currency total is exactly the signal that it moved.
   */
  value: number | string
  sub?: ReactNode
  tone?: StatTone
}) {
  return (
    <motion.div variants={riseVariants} className={`s7-stat s7-stat-tone-${tone}`}>
      <div className="s7-stat-head">
        {icon}
        {label}
      </div>
      <div className="s7-stat-value">
        {typeof value === 'number' ? <AnimatedNumber value={value} /> : value}
      </div>
      {sub ? <div className="s7-stat-sub">{sub}</div> : null}
    </motion.div>
  )
}
