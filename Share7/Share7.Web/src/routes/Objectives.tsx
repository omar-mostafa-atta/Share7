import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { CalendarClock, Plus, RefreshCw, Target, Trash2, Trophy } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import {
  AGGREGATIONS,
  COMMON_METRICS,
  KINDS,
  blankObjective,
  isLive,
  useObjectives,
} from '../features/objectives/data'
import { useGames } from '../features/games/data'
import { KEY_PATTERN, slugify, textFor } from '../lib/format'
import { formatDateTime, fromLocalInput, toLocalInput } from '../lib/time'
import { useLanguages } from '../store/languages'
import { listVariants } from '../components/ui/motion'
import type {
  CreateObjectiveRequest,
  ObjectiveAdminDto,
  UpdateObjectiveRequest,
} from '../types/api'

// ===========================================================================
// Objectives
//
// A goal counted against a metric. Three things decide what one means, and all
// three are permanent once created:
//
//   metric       what is counted        ("runs_completed")
//   aggregation  how reports combine    (SUM / MAX / LAST)
//   kind         how often it resets    (DAILY / WEEKLY / ACHIEVEMENT)
//
// The API enforces that by omitting them from the update payload. The editor
// mirrors it by disabling those fields rather than hiding them — an admin
// should be able to see how an existing objective is counted.
// ===========================================================================

type Filter = 'all' | 'live' | 'scheduled' | 'off'

export function Objectives() {
  const { objectives, loading, refreshing, reload, create, update, remove } = useObjectives()
  const { games } = useGames()
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [editing, setEditing] = useState<ObjectiveAdminDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<ObjectiveAdminDto | null>(null)

  const gameName = useMemo(() => {
    const map = new Map(games.map((g) => [g.gameId, g.gameKey]))
    return (id: string | null) => (id ? (map.get(id) ?? 'unknown game') : 'all games')
  }, [games])

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    const now = Date.now()

    return objectives.filter((objective) => {
      const live = isLive(objective, now)

      if (filter === 'live' && !live) return false
      if (filter === 'off' && objective.isActive) return false
      // "Scheduled" is active-but-not-yet-live: the flag is on and the window
      // opens in the future. Without this the two states are indistinguishable
      // in the table and a mis-dated launch looks like a broken objective.
      if (filter === 'scheduled') {
        if (!objective.isActive || live) return false
        const from = objective.availableFromUtc ? Date.parse(`${objective.availableFromUtc}Z`) : null
        if (!from || from <= now) return false
      }

      if (!term) return true

      return [objective.key, objective.metric, objective.kind, ...objective.translations.map((t) => t.name)]
        .join(' ')
        .toLowerCase()
        .includes(term)
    })
  }, [objectives, search, filter])

  const columns = useMemo<Column<ObjectiveAdminDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Objective',
        sort: (o) => textFor(o.translations, selectedLangId) || o.key,
        render: (o) => (
          <div>
            <div style={{ fontWeight: 600 }}>{textFor(o.translations, selectedLangId) || o.key}</div>
            <code className="s7-key">{o.key}</code>
          </div>
        ),
      },
      {
        key: 'kind',
        header: 'Resets',
        sort: (o) => o.kind,
        render: (o) => (
          <Badge tone={o.kind === 'ACHIEVEMENT' ? 'brand' : o.kind === 'WEEKLY' ? 'info' : 'muted'}>
            {o.kind}
          </Badge>
        ),
      },
      {
        key: 'metric',
        header: 'Counts',
        sort: (o) => o.metric,
        render: (o) => (
          <span>
            <code className="s7-key">{o.metric}</code>
            <span className="s7-muted" style={{ marginInlineStart: 6, fontSize: '0.75rem' }}>
              {o.aggregation}
            </span>
          </span>
        ),
      },
      {
        key: 'target',
        header: 'Target',
        numeric: true,
        sort: (o) => o.target,
        render: (o) => o.target.toLocaleString(),
      },
      {
        key: 'scope',
        header: 'Scope',
        render: (o) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {gameName(o.gameId)}
            {o.scope ? ` · ${o.scope}` : ''}
          </span>
        ),
      },
      {
        key: 'window',
        header: 'Window',
        sort: (o) => o.availableFromUtc ?? '',
        render: (o) =>
          o.availableFromUtc || o.availableToUtc ? (
            <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
              {o.availableFromUtc ? formatDateTime(o.availableFromUtc) : 'always'} →{' '}
              {o.availableToUtc ? formatDateTime(o.availableToUtc) : 'open'}
            </span>
          ) : (
            <span className="s7-muted">always</span>
          ),
      },
      {
        key: 'state',
        header: 'State',
        sort: (o) => (isLive(o) ? 2 : o.isActive ? 1 : 0),
        render: (o) =>
          isLive(o) ? (
            <Badge tone="success">Live</Badge>
          ) : o.isActive ? (
            <Badge tone="warning">Scheduled</Badge>
          ) : (
            <Badge tone="muted">Off</Badge>
          ),
      },
      { key: 'id', header: 'Id', render: (o) => <CopyId id={o.objectiveId} label="objectiveId" /> },
    ],
    [selectedLangId, gameName],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Trophy size={22} />}
        title="Objectives"
        subtitle="Goals counted against a metric. What an objective counts, how reports combine and how often it resets are fixed at creation — players already hold progress measured that way."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New objective
          </Button>
        }
      />

      <Card>
        <CardHeader
          icon={<Target size={16} />}
          title={`${rows.length} of ${objectives.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search key, metric or title…" />
            <Segmented
              layoutId="objectives-filter"
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: 'All' },
                { value: 'live', label: 'Live' },
                { value: 'scheduled', label: 'Scheduled' },
                { value: 'off', label: 'Off' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(o) => o.objectiveId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.objectiveId ?? null}
            empty={objectives.length ? 'No objective matches that filter.' : 'No objectives yet.'}
          />
        </CardBody>
      </Card>

      <ObjectiveEditor
        key={editing?.objectiveId ?? (creating ? 'new' : 'closed')}
        objective={editing}
        open={!!editing || creating}
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
          await update(editing.objectiveId, request)
          setEditing(null)
        }}
        onDelete={editing ? () => setConfirmDelete(editing) : undefined}
      />

      <Modal
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        icon={<Trash2 size={18} />}
        title={`Delete "${confirmDelete?.key}"?`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                if (!confirmDelete) return
                await remove(confirmDelete)
                setConfirmDelete(null)
                setEditing(null)
              }}
            >
              Delete objective
            </Button>
          </>
        }
      >
        <Note tone="danger">
          Players holding progress against this objective lose it. If it has ever been live, turning
          it off preserves the history and stops it appearing — deletion does not.
        </Note>
      </Modal>
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------

function ObjectiveEditor({
  objective,
  open,
  games,
  onClose,
  onCreate,
  onUpdate,
  onDelete,
}: {
  objective: ObjectiveAdminDto | null
  open: boolean
  games: { id: string; key: string }[]
  onClose: () => void
  onCreate: (request: CreateObjectiveRequest) => Promise<void>
  onUpdate: (request: UpdateObjectiveRequest) => Promise<void>
  onDelete?: () => void
}) {
  const isNew = !objective

  const [form, setForm] = useState<CreateObjectiveRequest>(() =>
    objective
      ? {
          key: objective.key,
          kind: objective.kind,
          metric: objective.metric,
          scope: objective.scope,
          target: objective.target,
          aggregation: objective.aggregation,
          gameId: objective.gameId,
          gradeId: objective.gradeId,
          langId: objective.langId,
          availableFromUtc: objective.availableFromUtc,
          availableToUtc: objective.availableToUtc,
          iconKey: objective.iconKey,
          sortOrder: objective.sortOrder,
          isActive: objective.isActive,
          translations: objective.translations.map((t) => ({ ...t })),
        }
      : blankObjective(),
  )

  const [saving, setSaving] = useState(false)

  const keyError =
    isNew && form.key && !KEY_PATTERN.test(form.key.replace(/\./g, '_'))
      ? 'Lowercase letters, digits, underscores and dots, starting with a letter.'
      : null

  const windowError =
    form.availableFromUtc && form.availableToUtc && form.availableFromUtc >= form.availableToUtc
      ? 'The window closes before it opens.'
      : null

  const blocked =
    !!keyError || !!windowError || !form.key.trim() || !form.metric.trim() || form.target < 1

  function patch(next: Partial<CreateObjectiveRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  async function save() {
    setSaving(true)
    try {
      if (isNew) {
        await onCreate(form)
      } else {
        // Only the mutable subset goes up. Sending the full object would be
        // rejected — and if it were not, it would let the metric change.
        await onUpdate({
          target: form.target,
          availableFromUtc: form.availableFromUtc,
          availableToUtc: form.availableToUtc,
          iconKey: form.iconKey,
          sortOrder: form.sortOrder,
          isActive: form.isActive,
          translations: form.translations,
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
      title={isNew ? 'New objective' : form.key}
      subtitle={
        isNew
          ? 'Metric, aggregation and reset cadence are permanent once saved.'
          : 'Only the target, schedule and wording can change.'
      }
      footer={
        <>
          <Button loading={saving} disabled={blocked} onClick={save}>
            {isNew ? 'Create objective' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          {onDelete ? (
            <Button variant="danger" onClick={onDelete} style={{ marginInlineStart: 'auto' }}>
              <Trash2 size={15} /> Delete
            </Button>
          ) : null}
        </>
      }
    >
      <div className="s7-stack">
        {!isNew ? (
          <Note>
            Key, metric, aggregation, scope and cadence are shown for reference and cannot be
            edited. Players hold progress counted under them.
          </Note>
        ) : null}

        <Field label="Key" error={keyError} hint="Permanent. Used by the client to claim rewards.">
          <Input
            mono
            value={form.key}
            disabled={!isNew}
            onChange={(e) => patch({ key: slugify(e.target.value) })}
            placeholder="daily_runs"
          />
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Resets" hint="How often progress goes back to zero.">
            <Select
              value={form.kind}
              disabled={!isNew}
              onChange={(e) => patch({ kind: e.target.value })}
            >
              {KINDS.map((kind) => (
                <option key={kind} value={kind}>
                  {kind}
                </option>
              ))}
            </Select>
          </Field>

          <Field label="Aggregation" hint="SUM adds reports · MAX keeps the best · LAST overwrites.">
            <Select
              value={form.aggregation}
              disabled={!isNew}
              onChange={(e) => patch({ aggregation: e.target.value })}
            >
              {AGGREGATIONS.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </Select>
          </Field>
        </div>

        <Field
          label="Metric"
          hint="What is counted. Free text — a game may report a metric not listed here."
        >
          <Input
            mono
            list="s7-metrics"
            value={form.metric}
            disabled={!isNew}
            onChange={(e) => patch({ metric: e.target.value.trim() })}
            placeholder="runs_completed"
          />
          <datalist id="s7-metrics">
            {COMMON_METRICS.map((m) => (
              <option key={m} value={m} />
            ))}
          </datalist>
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Target" hint="Reports must reach this to complete.">
            <Input
              type="number"
              min={1}
              value={form.target}
              onChange={(e) => patch({ target: Number(e.target.value) || 1 })}
            />
          </Field>

          <Field label="Sort order" hint="Lower sorts first in the player's list.">
            <Input
              type="number"
              value={form.sortOrder}
              onChange={(e) => patch({ sortOrder: Number(e.target.value) || 0 })}
            />
          </Field>
        </div>

        <Field label="Game" hint="Leave unset to count across every game.">
          <Select
            value={form.gameId ?? ''}
            disabled={!isNew}
            onChange={(e) => patch({ gameId: e.target.value || null })}
          >
            <option value="">All games</option>
            {games.map((game) => (
              <option key={game.id} value={game.id}>
                {game.key}
              </option>
            ))}
          </Select>
        </Field>

        <Field label="Scope" hint="Optional free-text narrowing, interpreted by the metric's emitter.">
          <Input
            mono
            value={form.scope ?? ''}
            disabled={!isNew}
            onChange={(e) => patch({ scope: e.target.value || null })}
            placeholder="(none)"
          />
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Available from" error={windowError} hint="Local time. Sent as UTC.">
            <Input
              type="datetime-local"
              value={toLocalInput(form.availableFromUtc)}
              onChange={(e) => patch({ availableFromUtc: fromLocalInput(e.target.value) })}
            />
          </Field>
          <Field label="Available to" hint="Leave empty for no end.">
            <Input
              type="datetime-local"
              value={toLocalInput(form.availableToUtc)}
              onChange={(e) => patch({ availableToUtc: fromLocalInput(e.target.value) })}
            />
          </Field>
        </div>

        <Field label="Icon key" hint="Resolved by the client against its own art. Optional.">
          <Input
            mono
            value={form.iconKey ?? ''}
            onChange={(e) => patch({ iconKey: e.target.value || null })}
            placeholder="(none)"
          />
        </Field>

        <Field label="Active">
          <Switch
            checked={form.isActive}
            onChange={(v) => patch({ isActive: v })}
            label={
              form.isActive
                ? 'On — subject to the availability window above'
                : 'Off — hidden regardless of the window'
            }
          />
        </Field>

        <div>
          <h3 className="s7-subhead">
            <CalendarClock size={15} /> Wording
          </h3>
          <TranslationsEditor
            withDescription
            value={form.translations}
            onChange={(rows) =>
              patch({
                translations: rows.map((r) => ({
                  langId: r.langId,
                  name: r.name,
                  description: r.description ?? null,
                })),
              })
            }
          />
        </div>
      </div>
    </Drawer>
  )
}
