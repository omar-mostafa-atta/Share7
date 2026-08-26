import { motion } from 'motion/react'
import { useState } from 'react'
import { CalendarRange, RefreshCw, Users } from 'lucide-react'
import { Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { PageTitle, Note } from '../components/ui/bits'
import { Field, Input, Select } from '../components/ui/form'
import { listVariants } from '../components/ui/motion'
import { DEFAULT_RANGE, useRetention } from '../features/analytics/data'
import type { DayRange } from '../features/analytics/data'
import { RetentionCurve, RetentionGrid } from '../features/analytics/RetentionGrid'
import { formatDateTime } from '../lib/time'

// ===========================================================================
// Retention
//
// The one page in the console that answers "is this a business". Everything
// else measures activity; this measures whether the activity comes back.
//
// The whole view reads TelemetryRetentionCohorts, which the nightly pass
// pre-aggregates — so the triangle is a scan of a table with tens of thousands
// of rows rather than a self-join over the raw event stream. That is the entire
// reason `DayIndex` is stored on TelemetryUserDay instead of derived.
// ===========================================================================

export function Retention() {
  const [range, setRange] = useState<DayRange>({ ...DEFAULT_RANGE, from: daysBack(59) })
  const [maxDayIndex, setMaxDayIndex] = useState(30)

  const { data, loading, refreshing, reload } = useRetention(range, maxDayIndex)

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<CalendarRange size={20} />}
        title="Retention"
        subtitle="Of everyone who arrived on a given day, how many came back."
        actions={
          <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
            <RefreshCw size={16} />
          </IconButton>
        }
      />

      <Card>
        <CardHeader icon={<Users size={16} />} title="Range" />
        <CardBody>
          <div className="s7-form-row">
            <Field label="Cohorts from" hint="Install day, UTC.">
              <Input
                type="date"
                value={range.from}
                onChange={(e) => setRange((r) => ({ ...r, from: e.target.value }))}
              />
            </Field>

            <Field label="Cohorts to">
              <Input
                type="date"
                value={range.to}
                onChange={(e) => setRange((r) => ({ ...r, to: e.target.value }))}
              />
            </Field>

            <Field
              label="Observe to"
              hint="Furthest day index shown. Wider costs nothing to read — the cells are precomputed."
            >
              <Select
                value={String(maxDayIndex)}
                onChange={(e) => setMaxDayIndex(Number(e.target.value))}
              >
                <option value="7">D7</option>
                <option value="14">D14</option>
                <option value="30">D30</option>
                <option value="60">D60</option>
                <option value="90">D90</option>
              </Select>
            </Field>
          </div>

          {data.computedAtUtc ? (
            <Note>
              Cohorts last recomputed {formatDateTime(data.computedAtUtc)}. Cells are blank past the
              day a cohort has actually aged to — <strong>blank means not yet known, not zero</strong>.
              A recent cohort's empty right-hand side is today's date, not a drop-off.
            </Note>
          ) : null}
        </CardBody>
      </Card>

      <Card>
        <CardHeader icon={<CalendarRange size={16} />} title="Cohort triangle" />
        <CardBody>
          <RetentionGrid report={data} loading={loading} />
        </CardBody>
      </Card>

      <Card>
        <CardHeader icon={<Users size={16} />} title="Curve" />
        <CardBody>
          <RetentionCurve report={data} />
          <p className="s7-funnel-note">
            Weighted by cohort size across every cohort in range — an unweighted mean would let a
            forty-user Tuesday count for as much as a forty-thousand-user launch day.
          </p>
        </CardBody>
      </Card>
    </motion.div>
  )
}

function daysBack(days: number): string {
  const date = new Date()
  date.setUTCDate(date.getUTCDate() - days)
  return date.toISOString().slice(0, 10)
}
