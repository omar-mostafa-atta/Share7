import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Gauge, Plus, RefreshCw, ShieldAlert, Sparkles } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import {
  SIGNAL_KINDS,
  blankValuation,
  surfaceTone,
  useSignalValuations,
} from '../features/signals/data'
import { useCurrencies } from '../features/currencies/data'
import { useGames } from '../features/games/data'
import { formatDateTime } from '../lib/time'
import { listVariants } from '../components/ui/motion'
import type {
  CreateSignalValuationRequest,
  SignalValuationDto,
  UpdateSignalValuationRequest,
} from '../types/api'

// ===========================================================================
// Signal Valuations
//
// What one collected thing is worth, and how much of it the server will pay for.
//
// Four numbers do the work:
//   unitValue     what one unit pays
//   maxPerRun     how many units one run can be paid for
//   maxPerDay     how many across all runs in a UTC day
//   maxPerSecond  a rate ceiling — the anti-cheat one
//
// maxPerSecond is the interesting one and the easiest to get wrong. It bounds
// units against the run's own duration, so a client reporting 900 coins in a
// 30-second run gets paid for the plausible fraction and the run is flagged.
// Leaving it empty means an impossible report is paid in full.
// ===========================================================================

type Filter = 'all' | 'enabled' | 'disabled'

export function Signals() {
  const { valuations, loading, refreshing, reload, create, update } = useSignalValuations()
  const { currencies } = useCurrencies()
  const { games } = useGames()

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [editing, setEditing] = useState<SignalValuationDto | null>(null)
  const [creating, setCreating] = useState(false)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()

    return valuations.filter((v) => {
      if (filter === 'enabled' && !v.enabled) return false
      if (filter === 'disabled' && v.enabled) return false
      if (!term) return true

      return [v.signalKind, v.currency, v.gameKey ?? '', v.surface]
        .join(' ')
        .toLowerCase()
        .includes(term)
    })
  }, [valuations, search, filter])

  // A valuation paying a disabled currency is inert but looks configured, which
  // is exactly the sort of thing that goes unnoticed for a month.
  const brokenCurrency = valuations.filter((v) => v.enabled && !v.currencyEnabled)
  const unbounded = valuations.filter((v) => v.enabled && v.maxPerSecond == null)

  const columns = useMemo<Column<SignalValuationDto>[]>(
    () => [
      {
        key: 'kind',
        header: 'Signal',
        sort: (v) => v.signalKind,
        render: (v) => (
          <div>
            <code className="s7-key">{v.signalKind}</code>
            <div className="s7-muted" style={{ fontSize: '0.72rem' }}>
              {v.gameKey ?? 'all games'}
            </div>
          </div>
        ),
      },
      {
        key: 'surface',
        header: 'Reported by',
        sort: (v) => v.surface,
        render: (v) => <Badge tone={surfaceTone(v.surface)}>{v.surface}</Badge>,
      },
      {
        key: 'pays',
        header: 'Pays',
        numeric: true,
        sort: (v) => v.unitValue,
        render: (v) => (
          <span>
            {v.unitValue.toLocaleString()}{' '}
            <span className="s7-muted" style={{ fontSize: '0.75rem' }}>
              {v.currency}
            </span>
            {!v.currencyEnabled ? (
              <Badge tone="danger">currency off</Badge>
            ) : v.currencyIsHard ? (
              <Badge tone="warning">hard</Badge>
            ) : null}
          </span>
        ),
      },
      {
        key: 'perRun',
        header: 'Max / run',
        numeric: true,
        sort: (v) => v.maxPerRun,
        render: (v) => v.maxPerRun.toLocaleString(),
      },
      {
        key: 'perDay',
        header: 'Max / day',
        numeric: true,
        sort: (v) => v.maxPerDay,
        render: (v) =>
          v.maxPerDay == null ? <span className="s7-muted">none</span> : v.maxPerDay.toLocaleString(),
      },
      {
        key: 'perSecond',
        header: 'Max / sec',
        numeric: true,
        sort: (v) => v.maxPerSecond,
        render: (v) =>
          v.maxPerSecond == null ? (
            <Badge tone="warning">unbounded</Badge>
          ) : (
            <span>{v.maxPerSecond}</span>
          ),
      },
      {
        key: 'enabled',
        header: 'State',
        sort: (v) => v.enabled,
        render: (v) => (v.enabled ? <Badge tone="success">On</Badge> : <Badge tone="muted">Off</Badge>),
      },
      {
        key: 'updated',
        header: 'Updated',
        sort: (v) => v.updatedAtUtc,
        render: (v) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {formatDateTime(v.updatedAtUtc)}
          </span>
        ),
      },
      { key: 'id', header: 'Id', render: (v) => <CopyId id={v.id} label="valuationId" /> },
    ],
    [],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Sparkles size={22} />}
        title="Signal Valuations"
        subtitle="What a collected thing is worth, and how much of it the server is willing to pay for. These rows are the whole of the run economy — nothing else decides a payout."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New valuation
          </Button>
        }
      />

      {brokenCurrency.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="danger">
            <strong>{brokenCurrency.length}</strong> enabled valuation
            {brokenCurrency.length === 1 ? '' : 's'} pay{brokenCurrency.length === 1 ? 's' : ''} a
            retired currency ({[...new Set(brokenCurrency.map((v) => v.currency))].join(', ')}). They
            look configured and pay nothing.
          </Note>
        </motion.div>
      ) : null}

      {unbounded.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="warning">
            <strong>{unbounded.length}</strong> enabled valuation
            {unbounded.length === 1 ? ' has' : 's have'} no per-second ceiling. A client reporting an
            impossible count for the run's duration will be paid in full rather than flagged.
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<Gauge size={16} />}
          title={`${rows.length} of ${valuations.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search kind, currency or game…" />
            <Segmented
              layoutId="signals-filter"
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: 'All' },
                { value: 'enabled', label: 'On' },
                { value: 'disabled', label: 'Off' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(v) => v.id}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.id ?? null}
            empty={
              valuations.length
                ? 'No valuation matches that filter.'
                : 'Nothing is valued yet, so no run pays out.'
            }
          />
        </CardBody>
      </Card>

      <ValuationEditor
        key={editing?.id ?? (creating ? 'new' : 'closed')}
        valuation={editing}
        open={!!editing || creating}
        currencies={currencies.map((c) => ({ id: c.currencyId, key: c.key, enabled: c.enabled, isHard: c.isHard }))}
        games={games.map((g) => ({ id: g.gameId, key: g.gameKey }))}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onCreate={async (request) => {
          await create(request)
          setCreating(false)
        }}
        onUpdate={async (request) => {
          if (!editing) return
          await update(editing.id, request)
          setEditing(null)
        }}
      />
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------

function ValuationEditor({
  valuation,
  open,
  currencies,
  games,
  onClose,
  onCreate,
  onUpdate,
}: {
  valuation: SignalValuationDto | null
  open: boolean
  currencies: { id: string; key: string; enabled: boolean; isHard: boolean }[]
  games: { id: string; key: string }[]
  onClose: () => void
  onCreate: (request: CreateSignalValuationRequest) => Promise<void>
  onUpdate: (request: UpdateSignalValuationRequest) => Promise<void>
}) {
  const isNew = !valuation

  const [form, setForm] = useState<CreateSignalValuationRequest>(() =>
    valuation
      ? {
          gameId: valuation.gameId,
          signalKind: valuation.signalKind,
          currencyId: valuation.currencyId,
          unitValue: valuation.unitValue,
          maxPerRun: valuation.maxPerRun,
          maxPerDay: valuation.maxPerDay,
          maxPerSecond: valuation.maxPerSecond,
          enabled: valuation.enabled,
        }
      : blankValuation(),
  )

  const [saving, setSaving] = useState(false)

  const chosenCurrency = currencies.find((c) => c.id === form.currencyId)

  const blocked =
    !form.signalKind.trim() || !form.currencyId || form.unitValue < 0 || form.maxPerRun < 0

  function patch(next: Partial<CreateSignalValuationRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  async function save() {
    setSaving(true)
    try {
      if (isNew) {
        await onCreate(form)
      } else {
        // The identity of a valuation — which signal, for which game, paid in
        // which currency — is fixed. Only the numbers move.
        await onUpdate({
          unitValue: form.unitValue,
          maxPerRun: form.maxPerRun,
          maxPerDay: form.maxPerDay,
          maxPerSecond: form.maxPerSecond,
          enabled: form.enabled,
        })
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New valuation' : form.signalKind}
      subtitle={
        isNew
          ? 'Signal, game and currency are permanent — the ceilings are not.'
          : `${valuation?.surface} signal · paid in ${valuation?.currency}`
      }
      footer={
        <>
          <Button loading={saving} disabled={blocked} onClick={save}>
            {isNew ? 'Create valuation' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <Field
          label="Signal kind"
          hint="What the client (or the grader) reports. Free text — a new game may emit a kind not listed."
        >
          <Input
            mono
            list="s7-signal-kinds"
            value={form.signalKind}
            disabled={!isNew}
            onChange={(e) => patch({ signalKind: e.target.value.trim() })}
            placeholder="coin"
          />
          <datalist id="s7-signal-kinds">
            {SIGNAL_KINDS.map((k) => (
              <option key={k} value={k} />
            ))}
          </datalist>
        </Field>

        <Field label="Game" hint="Leave unset to value this signal in every game.">
          <Select
            value={form.gameId ?? ''}
            disabled={!isNew}
            onChange={(e) => patch({ gameId: e.target.value || null })}
          >
            <option value="">All games</option>
            {games.map((g) => (
              <option key={g.id} value={g.id}>
                {g.key}
              </option>
            ))}
          </Select>
        </Field>

        <Field
          label="Currency"
          hint={
            chosenCurrency && !chosenCurrency.enabled
              ? 'This currency is retired — the valuation would pay nothing.'
              : chosenCurrency?.isHard
                ? 'A hard currency. Players normally pay real money for this.'
                : 'What the payout is denominated in.'
          }
          error={
            chosenCurrency && !chosenCurrency.enabled ? 'Retired currency accepts no credits.' : null
          }
        >
          <Select
            value={form.currencyId}
            disabled={!isNew}
            onChange={(e) => patch({ currencyId: e.target.value })}
          >
            <option value="">Choose a currency…</option>
            {currencies.map((c) => (
              <option key={c.id} value={c.id}>
                {c.key}
                {c.isHard ? ' (hard)' : ''}
                {!c.enabled ? ' — retired' : ''}
              </option>
            ))}
          </Select>
        </Field>

        <Field label="Unit value" hint="What one of these is worth.">
          <Input
            type="number"
            min={0}
            value={form.unitValue}
            onChange={(e) => patch({ unitValue: Number(e.target.value) || 0 })}
          />
        </Field>

        <h3 className="s7-subhead">
          <ShieldAlert size={15} /> Ceilings
        </h3>

        <Field label="Max per run" hint="Units beyond this in a single run are collected but not paid.">
          <Input
            type="number"
            min={0}
            value={form.maxPerRun}
            onChange={(e) => patch({ maxPerRun: Number(e.target.value) || 0 })}
          />
        </Field>

        <Field label="Max per day" hint="Across every run in a UTC day. Empty means no daily ceiling.">
          <Input
            type="number"
            min={0}
            value={form.maxPerDay ?? ''}
            onChange={(e) =>
              patch({ maxPerDay: e.target.value === '' ? null : Number(e.target.value) })
            }
            placeholder="no limit"
          />
        </Field>

        <Field
          label="Max per second"
          hint="Rate ceiling, measured against the run's own duration. This is the anti-cheat bound — an impossible count is trimmed to the plausible one and the run is flagged."
        >
          <Input
            type="number"
            min={0}
            step="0.1"
            value={form.maxPerSecond ?? ''}
            onChange={(e) =>
              patch({ maxPerSecond: e.target.value === '' ? null : Number(e.target.value) })
            }
            placeholder="unbounded"
          />
        </Field>

        {form.maxPerSecond == null ? (
          <Note tone="warning">
            With no per-second ceiling a client claiming any number of these in a one-second run is
            paid in full and nothing is flagged.
          </Note>
        ) : null}

        <Field label="Enabled">
          <Switch
            checked={form.enabled}
            onChange={(v) => patch({ enabled: v })}
            label={form.enabled ? 'On — this signal pays out' : 'Off — collected but never paid'}
          />
        </Field>
      </div>
    </Drawer>
  )
}
