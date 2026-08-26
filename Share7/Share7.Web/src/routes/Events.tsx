import { motion } from 'motion/react'
import { useState } from 'react'
import { AlertTriangle, Boxes, Database, RefreshCw, Save, Sparkles } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { Note, PageTitle } from '../components/ui/bits'
import { listVariants } from '../components/ui/motion'
import {
  DEFAULT_RANGE,
  compact,
  percent,
  useEventCatalogue,
  useEventDetail,
  useEventSchemas,
} from '../features/analytics/data'
import type { DayRange } from '../features/analytics/data'
import { TrendChart } from '../features/analytics/TrendChart'
import type { EventCatalogueRowDto, TelemetryCategory } from '../types/api'

// ===========================================================================
// Events — the registry, and what each event is actually doing
//
// This page exists because a ten-year-old platform without one accumulates
// four thousand event names, and the person trying to answer a product question
// in year six cannot tell which of six similar names is still being emitted.
//
// The "unregistered" list is the working queue. An unregistered name is STORED
// but never folded into a rollup, so it produces no metric until somebody says
// what it is — which means clearing this list is maintenance, not tidying.
// ===========================================================================

export function Events() {
  const [range] = useState<DayRange>(DEFAULT_RANGE)
  const [selected, setSelected] = useState<string | null>(null)

  const catalogue = useEventCatalogue(range)
  const detail = useEventDetail(selected, range)
  const { save, seed } = useEventSchemas()

  const columns: Column<EventCatalogueRowDto>[] = [
    {
      key: 'name',
      header: 'Event',
      sort: (row) => row.name,
      render: (row) => (
        <div className="s7-cell-stack">
          <strong className="s7-mono">{row.name}</strong>
          <span className="s7-muted">{row.description}</span>
        </div>
      ),
    },
    {
      key: 'group',
      header: 'Group',
      sort: (row) => row.group,
      render: (row) => <Badge tone="muted">{row.group}</Badge>,
    },
    {
      key: 'category',
      header: 'Basis',
      sort: (row) => row.category,
      render: (row) => (
        // Operational vs Behavioural is not a severity label — it is the lawful
        // basis this event is collected under, and it decides both consent and
        // whether a vendor sink may ever see it.
        <Badge tone={row.category === 'Operational' ? 'info' : 'brand'}>{row.category}</Badge>
      ),
    },
    {
      key: 'count',
      header: 'Volume',
      numeric: true,
      sort: (row) => row.count,
      render: (row) =>
        row.count === 0 ? <span className="s7-muted">—</span> : compact(row.count),
    },
    {
      key: 'sampling',
      header: 'Sampling',
      numeric: true,
      sort: (row) => row.sampleRate,
      render: (row) =>
        row.sampleRate >= 1 ? (
          <span className="s7-muted">all</span>
        ) : (
          <Badge tone="warning">{percent(row.sampleRate, 0)}</Badge>
        ),
    },
    {
      key: 'state',
      header: 'State',
      render: (row) => (
        <div className="s7-inline-badges">
          {!row.enabled ? <Badge tone="danger">refused</Badge> : null}
          {!row.rollUpDaily ? <Badge tone="muted">no rollup</Badge> : null}
          {row.retentionDays ? <Badge tone="info">{row.retentionDays}d</Badge> : null}
        </div>
      ),
    },
  ]

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Database size={20} />}
        title="Events"
        subtitle="The vocabulary the platform reports in, and what it costs to keep."
        actions={
          <>
            <Button variant="ghost" onClick={() => void seed().then(() => catalogue.reload())}>
              <Sparkles size={14} /> Seed vocabulary
            </Button>
            <IconButton
              label="Refresh"
              busy={catalogue.refreshing}
              onClick={() => void catalogue.reload()}
            >
              <RefreshCw size={16} />
            </IconButton>
          </>
        }
      />

      {catalogue.data.unregistered.length > 0 ? (
        <Card>
          <CardHeader
            icon={<AlertTriangle size={16} />}
            title={`${catalogue.data.unregistered.length} unrecognised event(s)`}
          />
          <CardBody>
            <Note tone="warning">
              These names arrived from a client before anybody registered them. They are{' '}
              <strong>stored but never rolled up</strong>, so they produce no metric until you say
              what they are. Registering one starts folding it from the next projector pass —
              events already stored stay unfolded, so the series begins on the day you register it.
            </Note>

            <DataTable
              rows={catalogue.data.unregistered}
              columns={columns}
              getId={(row) => row.name}
              onRowClick={(row) => setSelected(row.name)}
              selectedId={selected}
              initialSort={{ key: 'count', direction: 'desc' }}
            />
          </CardBody>
        </Card>
      ) : null}

      <Card>
        <CardHeader icon={<Boxes size={16} />} title="Registered events" />
        <CardBody>
          <DataTable
            rows={catalogue.data.events}
            columns={columns}
            getId={(row) => row.name}
            loading={catalogue.loading}
            onRowClick={(row) => setSelected(row.name)}
            selectedId={selected}
            initialSort={{ key: 'count', direction: 'desc' }}
            empty="No events registered yet — press Seed vocabulary."
          />
        </CardBody>
      </Card>

      <Drawer
        open={selected !== null}
        onClose={() => setSelected(null)}
        title={selected ?? ''}
      >
        {detail.data ? (
          <EventDetail
            detail={detail.data}
            range={range}
            onSave={async (request) => {
              await save(detail.data!.schema.name, request)
              await catalogue.reload()
              await detail.reload()
            }}
          />
        ) : (
          <p className="s7-muted">Loading…</p>
        )}
      </Drawer>
    </motion.div>
  )
}

function EventDetail({
  detail,
  range,
  onSave,
}: {
  detail: NonNullable<ReturnType<typeof useEventDetail>['data']>
  range: DayRange
  onSave: (request: {
    group: string
    description: string
    category: TelemetryCategory
    sampleRate: number
    retentionDays: number | null
    enabled: boolean
    rollUpDaily: boolean
    dimensions: string
  }) => Promise<void>
}) {
  const schema = detail.schema

  const [form, setForm] = useState({
    group: schema.group,
    description: schema.description,
    category: schema.category,
    sampleRate: schema.sampleRate,
    retentionDays: schema.retentionDays,
    enabled: schema.enabled,
    rollUpDaily: schema.rollUpDaily,
    dimensions: schema.dimensions,
  })

  const [saving, setSaving] = useState(false)

  return (
    <div className="s7-drawer-body">
      <TrendChart
        from={range.from}
        to={range.to}
        height={140}
        series={[{ key: schema.name, label: schema.name, tone: 'brand', points: detail.daily }]}
      />

      <Field label="Group" hint="Grouping for this console only — not the lawful basis.">
        <Input value={form.group} onChange={(e) => setForm({ ...form, group: e.target.value })} />
      </Field>

      <Field label="Description" hint="What question this event answers, for whoever reads it in year six.">
        <Input
          value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })}
        />
      </Field>

      <Field
        label="Lawful basis"
        hint="Operational is collected without consent and never leaves for a third party. Behavioural is consent-gated."
      >
        <Select
          value={form.category}
          onChange={(e) => setForm({ ...form, category: e.target.value as TelemetryCategory })}
        >
          <option value="Operational">Operational</option>
          <option value="Behavioural">Behavioural</option>
        </Select>
      </Field>

      <Field
        label="Sample rate"
        hint="Returned to clients on their next batch. Counts are scaled back up by the rate each event was actually sampled at."
      >
        <Input
          type="number"
          step="0.05"
          min="0.01"
          max="1"
          value={form.sampleRate}
          onChange={(e) => setForm({ ...form, sampleRate: Number(e.target.value) })}
        />
      </Field>

      <Field
        label="Retention (days)"
        hint="Raw rows only — rollups are never swept. Blank uses the category default."
      >
        <Input
          type="number"
          min="1"
          value={form.retentionDays ?? ''}
          onChange={(e) =>
            setForm({ ...form, retentionDays: e.target.value ? Number(e.target.value) : null })
          }
        />
      </Field>

      <Field
        label="Dimensions"
        hint="Comma-separated: platform, app_version, game_id, locale. Each one multiplies the daily rows."
      >
        <Input
          value={form.dimensions}
          onChange={(e) => setForm({ ...form, dimensions: e.target.value })}
        />
      </Field>

      <Switch
        checked={form.enabled}
        onChange={(enabled) => setForm({ ...form, enabled })}
        label="Accept from clients"
      />

      <Switch
        checked={form.rollUpDaily}
        onChange={(rollUpDaily) => setForm({ ...form, rollUpDaily })}
        label="Roll up daily"
      />

      <Button
        loading={saving}
        onClick={async () => {
          setSaving(true)
          try {
            await onSave(form)
          } finally {
            setSaving(false)
          }
        }}
      >
        <Save size={14} /> Save
      </Button>

      {detail.parameters.length > 0 ? (
        <>
          <h4 className="s7-drawer-subhead">Parameters</h4>
          <p className="s7-muted">
            From a sample of {detail.sampleSize.toLocaleString()} recent rows — a shape question,
            not a metric.
          </p>

          {detail.parameters.map((parameter) => (
            <div key={parameter.key} className="s7-param">
              <div className="s7-param-head">
                <strong className="s7-mono">{parameter.key}</strong>
                <span className="s7-muted">{parameter.distinctValues} distinct</span>
              </div>

              {parameter.topValues.slice(0, 5).map((value) => (
                <div key={value.key} className="s7-economy-line">
                  <span className="s7-mono">{value.key || '(empty)'}</span>
                  <em>{percent(value.share, 0)}</em>
                </div>
              ))}
            </div>
          ))}
        </>
      ) : null}
    </div>
  )
}
