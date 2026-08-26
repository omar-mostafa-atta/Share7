import { useMemo } from 'react'
import type { RetentionReportDto } from '../../types/api'
import { percent } from './data'

// ===========================================================================
// Retention triangle
//
// One row per install cohort, one cell per day since install. The shape is a
// triangle rather than a rectangle because a cohort from yesterday cannot have
// a D7 yet — and that is the single most important thing this component gets
// right.
//
// A MISSING CELL IS NOT ZERO. The server returns `cells` shortened to the day
// index a cohort has actually aged to, and anything past that renders as blank
// rather than as 0%. Filling the gap with zeros draws a cliff at the right-hand
// edge of every recent cohort, which reads as a catastrophic drop-off and is
// really just today's date.
//
// Colour is a single-hue ramp on opacity rather than a rainbow. A sequential
// quantity gets a sequential scale; a red-to-green ramp implies a midpoint that
// retention does not have, and it fails for the ~8% of readers with red-green
// colour vision deficiency — who would be reading the most important chart in
// the console through the one encoding that does not work for them. The number
// is printed in every cell for the same reason: colour is the summary, the text
// is the answer.
// ===========================================================================

export function RetentionGrid({
  report,
  loading,
}: {
  report: RetentionReportDto
  loading?: boolean
}) {
  const columns = useMemo(() => {
    const max = Math.min(report.maxDayIndex, 30)
    return Array.from({ length: max + 1 }, (_, i) => i)
  }, [report.maxDayIndex])

  if (loading) {
    return <div className="s7-chart-empty">Reading cohorts…</div>
  }

  if (report.cohorts.length === 0) {
    return (
      <div className="s7-chart-empty">
        No cohorts in this range yet. Cohorts appear once the nightly pass has run over a day with
        activity in it.
      </div>
    )
  }

  return (
    <div className="s7-retention">
      <table>
        <thead>
          <tr>
            <th className="s7-retention-cohort">Cohort</th>
            <th className="s7-retention-size">Users</th>
            {columns.map((day) => (
              <th key={day} className="s7-retention-day">
                {day === 0 ? 'D0' : `D${day}`}
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {report.cohorts.map((cohort) => (
            <tr key={cohort.cohortDayUtc}>
              <th className="s7-retention-cohort">{cohort.cohortDayUtc.slice(0, 10)}</th>
              <td className="s7-retention-size">{cohort.cohortSize.toLocaleString()}</td>

              {columns.map((day) => {
                // Past what this cohort has aged to: unknown, and drawn as
                // nothing at all. See the header note.
                if (day >= cohort.cells.length) {
                  return <td key={day} className="s7-retention-cell s7-retention-unknown" />
                }

                const retained = cohort.cells[day]
                const rate = cohort.cohortSize > 0 ? retained / cohort.cohortSize : 0

                return (
                  <td
                    key={day}
                    className="s7-retention-cell"
                    title={`${retained.toLocaleString()} of ${cohort.cohortSize.toLocaleString()} returned on day ${day}`}
                    style={{
                      // Floored so a non-zero cell is never invisible — a cohort
                      // where 1 in 5,000 came back is still a fact, and a cell
                      // washed to white claims it did not happen.
                      background: rate === 0
                        ? 'transparent'
                        : `color-mix(in srgb, var(--s7-brand) ${Math.max(6, rate * 100)}%, transparent)`,
                    }}
                  >
                    {percent(rate, 0)}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * The curve under the triangle — retention per day index, weighted by cohort
 * size across every cohort in range.
 *
 * Weighted, not a mean of percentages: an unweighted mean lets a forty-user
 * Tuesday count for as much as a forty-thousand-user launch day, which is how a
 * retention chart ends up disagreeing with the totals printed beside it.
 */
export function RetentionCurve({ report }: { report: RetentionReportDto }) {
  if (report.curve.length === 0) return null

  const day0 = report.curve.find((p) => p.dayIndex === 0)
  const base = day0?.userCount ?? 0

  return (
    <div className="s7-curve">
      {report.curve.slice(0, 31).map((point) => (
        <div key={point.dayIndex} className="s7-curve-col" title={`${point.cohortCount} cohort(s)`}>
          <div
            className="s7-curve-bar"
            style={{ height: `${Math.max(2, point.retention * 100)}%` }}
          />
          <span className="s7-curve-label">
            {point.dayIndex === 0 ? 'D0' : point.dayIndex}
          </span>
          <span className="s7-curve-value">{percent(point.retention, 0)}</span>
        </div>
      ))}

      {base > 0 ? (
        <div className="s7-curve-note">
          {base.toLocaleString()} users across {day0?.cohortCount ?? 0} cohort(s) at D0.
        </div>
      ) : null}
    </div>
  )
}
