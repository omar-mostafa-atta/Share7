import { useId, useMemo, useState } from 'react'
import type { TimeseriesPointDto } from '../../types/api'
import { compact } from './data'

// ===========================================================================
// Trend chart
//
// Inline SVG rather than a charting library. Three reasons, in order:
//
//   1. Every series here is one number per day over at most 400 days. A library
//      that can draw a candlestick is several hundred kilobytes of the console's
//      bundle spent on a polyline.
//   2. It inherits the console's tokens, so it is correct in whatever theme the
//      panel grows into without a second palette to keep in sync.
//   3. Nothing about the shape of this data is going to change. The moment a
//      chart here needs zoom, brushing or a second axis, the honest answer is a
//      library — not this file growing one.
//
// The one thing it does carefully is the EMPTY-DAY problem: a series with no
// row for a Tuesday must show a gap at zero, not a straight line from Monday to
// Wednesday. `fill` below inserts the missing days, because a chart that
// interpolates over an outage reads as a dip rather than as a hole.
// ===========================================================================

const WIDTH = 720
const HEIGHT = 180
const PAD_X = 8
const PAD_Y = 12

export interface TrendSeries {
  key: string
  label: string
  points: TimeseriesPointDto[]
  tone?: 'brand' | 'success' | 'warning' | 'danger' | 'info' | 'cool'
}

const TONE_COLOURS: Record<string, string> = {
  brand: 'var(--s7-brand)',
  success: 'var(--s7-success)',
  warning: 'var(--s7-warning)',
  danger: 'var(--s7-danger)',
  info: 'var(--s7-info)',
  cool: 'var(--s7-brand-3)',
}

/** Inserts a zero for every calendar day the series has no row for. */
function fill(points: TimeseriesPointDto[], from: string, to: string): TimeseriesPointDto[] {
  if (points.length === 0) return []

  const byDay = new Map(points.map((p) => [p.dayUtc.slice(0, 10), p]))
  const out: TimeseriesPointDto[] = []

  const cursor = new Date(`${from}T00:00:00Z`)
  const end = new Date(`${to}T00:00:00Z`)

  // Bounded so a bad range cannot spin here. The server refuses anything wider
  // than its configured maximum, but the client should not depend on that to
  // avoid a hang.
  for (let guard = 0; cursor <= end && guard < 1000; guard++) {
    const day = cursor.toISOString().slice(0, 10)
    out.push(byDay.get(day) ?? { dayUtc: day, count: 0, uniqueUsers: null })
    cursor.setUTCDate(cursor.getUTCDate() + 1)
  }

  return out
}

export function TrendChart({
  series,
  from,
  to,
  height = HEIGHT,
}: {
  series: TrendSeries[]
  from: string
  to: string
  height?: number
}) {
  const gradientId = useId()
  const [hover, setHover] = useState<number | null>(null)

  const filled = useMemo(
    () => series.map((s) => ({ ...s, points: fill(s.points, from, to) })),
    [series, from, to],
  )

  const length = filled[0]?.points.length ?? 0

  // A shared maximum across every series, so two lines on one chart are actually
  // comparable. Per-series scaling makes a series of 3 look identical to one of
  // 3,000, which is the single most common way a dashboard misleads.
  const max = useMemo(() => {
    let peak = 0
    for (const s of filled) for (const p of s.points) if (p.count > peak) peak = p.count
    return peak === 0 ? 1 : peak
  }, [filled])

  if (length === 0) {
    return (
      <div className="s7-chart-empty">
        Nothing recorded in this range.
      </div>
    )
  }

  const stepX = length > 1 ? (WIDTH - PAD_X * 2) / (length - 1) : 0
  const scaleY = (value: number) => height - PAD_Y - (value / max) * (height - PAD_Y * 2)

  const path = (points: TimeseriesPointDto[]) =>
    points
      .map((p, i) => `${i === 0 ? 'M' : 'L'} ${PAD_X + i * stepX} ${scaleY(p.count)}`)
      .join(' ')

  const area = (points: TimeseriesPointDto[]) =>
    `${path(points)} L ${PAD_X + (length - 1) * stepX} ${height - PAD_Y} L ${PAD_X} ${height - PAD_Y} Z`

  const primary = filled[0]
  const hoverPoint = hover !== null ? primary?.points[hover] : null

  return (
    <div className="s7-chart">
      <svg
        viewBox={`0 0 ${WIDTH} ${height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`${series.map((s) => s.label).join(', ')} from ${from} to ${to}`}
        onMouseLeave={() => setHover(null)}
        onMouseMove={(event) => {
          const box = event.currentTarget.getBoundingClientRect()
          const ratio = (event.clientX - box.left) / box.width
          setHover(Math.min(length - 1, Math.max(0, Math.round(ratio * (length - 1)))))
        }}
      >
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={TONE_COLOURS[primary?.tone ?? 'brand']} stopOpacity="0.22" />
            <stop offset="100%" stopColor={TONE_COLOURS[primary?.tone ?? 'brand']} stopOpacity="0" />
          </linearGradient>
        </defs>

        {/* Only the first series gets a fill. Stacked translucent areas stop
            being readable at two and are actively misleading at three. */}
        {primary ? <path d={area(primary.points)} fill={`url(#${gradientId})`} /> : null}

        {filled.map((s) => (
          <path
            key={s.key}
            d={path(s.points)}
            fill="none"
            stroke={TONE_COLOURS[s.tone ?? 'brand']}
            strokeWidth={2}
            vectorEffect="non-scaling-stroke"
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        ))}

        {hover !== null ? (
          <line
            x1={PAD_X + hover * stepX}
            x2={PAD_X + hover * stepX}
            y1={PAD_Y}
            y2={height - PAD_Y}
            stroke="var(--s7-line)"
            strokeWidth={1}
            vectorEffect="non-scaling-stroke"
          />
        ) : null}
      </svg>

      <div className="s7-chart-foot">
        <span>{from}</span>

        {hoverPoint ? (
          <strong>
            {hoverPoint.dayUtc.slice(0, 10)} · {compact(hoverPoint.count)}
            {/* Null and zero are different answers. "pending" means the nightly
                pass has not run for that day; a zero would claim nobody did it. */}
            {hoverPoint.uniqueUsers === null
              ? ' · uniques pending'
              : ` · ${compact(hoverPoint.uniqueUsers)} users`}
          </strong>
        ) : (
          <strong>peak {compact(max)}</strong>
        )}

        <span>{to}</span>
      </div>

      {series.length > 1 ? (
        <div className="s7-chart-legend">
          {series.map((s) => (
            <span key={s.key}>
              <i style={{ background: TONE_COLOURS[s.tone ?? 'brand'] }} />
              {s.label}
            </span>
          ))}
        </div>
      ) : null}
    </div>
  )
}
