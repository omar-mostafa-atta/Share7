import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  BarChart3,
  CheckCircle2,
  Flag,
  Gavel,
  Plus,
  RefreshCw,
  Ruler,
  Timer,
  XCircle,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import {
  AGGREGATIONS,
  COHORTS,
  PERIODS,
  SORT_DIRECTIONS,
  blankBoard,
  useBoards,
  useCycles,
  useFlaggedResults,
  useMetricBounds,
} from '../features/leaderboards/data'
import { useGames } from '../features/games/data'
import { KEY_PATTERN, textFor } from '../lib/format'
import { formatDateTime, formatRelative, fromLocalInput } from '../lib/time'
import { useLanguages } from '../store/languages'
import { listVariants } from '../components/ui/motion'
import type {
  FlaggedResultDto,
  LeaderboardBoardAdminDto,
  MetricBoundDto,
  SaveLeaderboardBoardRequest,
  SaveMetricBoundRequest,
} from '../types/api'

// ===========================================================================
// Leaderboards
//
// Three tabs, because these are three different jobs done by three different
// people at three different times:
//
//   Boards   authoring — what is ranked and how
//   Bounds   tuning    — what counts as an impossible score
//   Flagged  triage    — ruling on the scores that tripped a bound
//
// The ordering matters and the tabs are ordered by urgency: flagged results
// hold up a cycle's settlement, so that tab carries a count badge.
// ===========================================================================

type Tab = 'boards' | 'bounds' | 'flagged'

export function Leaderboards() {
  const [tab, setTab] = useState<Tab>('boards')
  const { flagged } = useFlaggedResults()

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<BarChart3 size={22} />}
        title="Leaderboards"
        subtitle="What is ranked, over what window, and which scores the server refused to believe."
        actions={
          <Segmented
            layoutId="lb-tab"
            value={tab}
            onChange={setTab}
            options={[
              { value: 'boards', label: 'Boards' },
              { value: 'bounds', label: 'Bounds' },
              {
                value: 'flagged',
                label: (
                  <span className="s7-inline">
                    Flagged
                    {flagged.length ? <Badge tone="danger">{flagged.length}</Badge> : null}
                  </span>
                ),
              },
            ]}
          />
        }
      />

      {tab === 'boards' ? <BoardsTab /> : null}
      {tab === 'bounds' ? <BoundsTab /> : null}
      {tab === 'flagged' ? <FlaggedTab /> : null}
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Boards
// ---------------------------------------------------------------------------

function BoardsTab() {
  const { boards, loading, refreshing, reload, create, update } = useBoards()
  const { games } = useGames()
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<LeaderboardBoardAdminDto | null>(null)
  const [creating, setCreating] = useState(false)

  const gameKey = useMemo(() => {
    const map = new Map(games.map((g) => [g.gameId, g.gameKey]))
    return (id: string | null) => (id ? (map.get(id) ?? 'unknown') : 'all games')
  }, [games])

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return boards

    return boards.filter((b) =>
      [b.boardKey, b.metric, b.period, ...b.translations.map((t) => t.name)]
        .join(' ')
        .toLowerCase()
        .includes(term),
    )
  }, [boards, search])

  const columns = useMemo<Column<LeaderboardBoardAdminDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Board',
        sort: (b) => textFor(b.translations, selectedLangId) || b.boardKey,
        render: (b) => (
          <div>
            <div style={{ fontWeight: 600 }}>{textFor(b.translations, selectedLangId) || b.boardKey}</div>
            <code className="s7-key">{b.boardKey}</code>
          </div>
        ),
      },
      {
        key: 'metric',
        header: 'Ranks by',
        sort: (b) => b.metric,
        render: (b) => (
          <span>
            <code className="s7-key">{b.metric}</code>
            <span className="s7-muted" style={{ fontSize: '0.72rem' }}>
              {' '}
              {b.aggregation} · {b.sortDirection === 'Desc' ? 'highest first' : 'lowest first'}
            </span>
          </span>
        ),
      },
      { key: 'period', header: 'Period', sort: (b) => b.period, render: (b) => <Badge tone="info">{b.period}</Badge> },
      {
        key: 'scope',
        header: 'Scope',
        render: (b) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {gameKey(b.gameId)} · {b.supportedCohorts}
          </span>
        ),
      },
      {
        key: 'cycles',
        header: 'Cycles',
        numeric: true,
        sort: (b) => b.cycleCount,
        render: (b) =>
          b.cycleCount ? b.cycleCount.toLocaleString() : <Badge tone="warning">none</Badge>,
      },
      {
        key: 'active',
        header: 'State',
        sort: (b) => b.isActive,
        render: (b) => (b.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="muted">Off</Badge>),
      },
      { key: 'id', header: 'Id', render: (b) => <CopyId id={b.boardId} label="boardId" /> },
    ],
    [selectedLangId, gameKey],
  )

  // A board with no cycles ranks nothing — there is no window for results to
  // land in, so it is defined but inert.
  const cycleless = boards.filter((b) => b.isActive && !b.cycleCount)

  return (
    <>
      <StatRow>
        <Stat icon={<BarChart3 size={13} />} label="Boards" value={boards.length} sub={`${boards.filter((b) => b.isActive).length} active`} tone="brand" />
        <Stat icon={<Timer size={13} />} label="Total cycles" value={boards.reduce((s, b) => s + b.cycleCount, 0)} sub="Across every board" tone="info" />
        <Stat icon={<Flag size={13} />} label="Inert boards" value={cycleless.length} sub="Active but with no cycle" tone={cycleless.length ? 'warning' : 'success'} />
      </StatRow>

      {cycleless.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="warning">
            <strong>{cycleless.length}</strong> active board
            {cycleless.length === 1 ? ' has' : 's have'} no cycle, so nothing is being ranked. Open a
            board and schedule one.
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<BarChart3 size={16} />}
          title={`${rows.length} of ${boards.length}`}
          actions={
            <>
              <Button variant="ghost" onClick={() => setCreating(true)}>
                <Plus size={15} /> New board
              </Button>
              <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
                <RefreshCw size={15} />
              </IconButton>
            </>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search board or metric…" />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(b) => b.boardId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.boardId ?? null}
            empty={boards.length ? 'No board matches that search.' : 'No boards defined yet.'}
          />
        </CardBody>
      </Card>

      <BoardEditor
        key={editing?.boardId ?? (creating ? 'new' : 'closed')}
        board={editing}
        open={!!editing || creating}
        games={games.map((g) => ({ id: g.gameId, key: g.gameKey }))}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.boardId, request)
          else await create(request)
          setEditing(null)
          setCreating(false)
        }}
      />
    </>
  )
}

function BoardEditor({
  board,
  open,
  games,
  onClose,
  onSave,
}: {
  board: LeaderboardBoardAdminDto | null
  open: boolean
  games: { id: string; key: string }[]
  onClose: () => void
  onSave: (request: SaveLeaderboardBoardRequest) => Promise<void>
}) {
  const isNew = !board

  const [form, setForm] = useState<SaveLeaderboardBoardRequest>(() =>
    board
      ? {
          boardKey: board.boardKey,
          metric: board.metric,
          sortDirection: board.sortDirection,
          aggregation: board.aggregation,
          period: board.period,
          supportedCohorts: board.supportedCohorts,
          gameId: board.gameId,
          gradeId: board.gradeId,
          langId: board.langId,
          visibleRankLimit: board.visibleRankLimit,
          graceSeconds: board.graceSeconds,
          isActive: board.isActive,
          translations: board.translations.map((t) => ({ ...t })),
        }
      : blankBoard(),
  )

  const [saving, setSaving] = useState(false)
  const { cycles, create: addCycle, rebuild, settle } = useCycles(board?.boardId ?? null)

  const [newStart, setNewStart] = useState('')
  const [newEnd, setNewEnd] = useState('')
  const [confirmSettle, setConfirmSettle] = useState<string | null>(null)

  const keyError =
    isNew && form.boardKey && !KEY_PATTERN.test(form.boardKey.replace(/[.-]/g, '_'))
      ? 'Lowercase letters, digits, dots, dashes and underscores.'
      : null

  const blocked = !!keyError || !form.boardKey.trim() || !form.metric.trim()

  function patch(next: Partial<SaveLeaderboardBoardRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  return (
    <>
      <Drawer
        open={open}
        onClose={onClose}
        title={isNew ? 'New board' : form.boardKey}
        subtitle={isNew ? undefined : `${board.cycleCount} cycle${board.cycleCount === 1 ? '' : 's'}`}
        footer={
          <>
            <Button
              loading={saving}
              disabled={blocked}
              onClick={async () => {
                setSaving(true)
                try {
                  await onSave(form)
                } finally {
                  setSaving(false)
                }
              }}
            >
              {isNew ? 'Create board' : 'Save changes'}
            </Button>
            <Button variant="ghost" onClick={onClose}>
              Cancel
            </Button>
          </>
        }
      >
        <div className="s7-stack">
          <Field label="Board key" error={keyError} hint={isNew ? 'Permanent.' : 'Cannot be changed.'}>
            <Input
              mono
              value={form.boardKey}
              disabled={!isNew}
              onChange={(e) => patch({ boardKey: e.target.value.toLowerCase().replace(/\s+/g, '_') })}
              placeholder="runner.distance.weekly"
            />
          </Field>

          <Field label="Metric" hint="The value being ranked. Must match what the game reports.">
            <Input
              mono
              value={form.metric}
              onChange={(e) => patch({ metric: e.target.value.trim() })}
              placeholder="distance_m"
            />
          </Field>

          <div className="s7-form-grid-2">
            <Field label="Sort direction" hint="Desc ranks the highest value first.">
              <Select value={form.sortDirection} onChange={(e) => patch({ sortDirection: e.target.value })}>
                {SORT_DIRECTIONS.map((d) => (
                  <option key={d} value={d}>
                    {d === 'Desc' ? 'Desc — highest first' : 'Asc — lowest first'}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Aggregation" hint="How several results from one player combine.">
              <Select value={form.aggregation} onChange={(e) => patch({ aggregation: e.target.value })}>
                {AGGREGATIONS.map((a) => (
                  <option key={a} value={a}>
                    {a}
                  </option>
                ))}
              </Select>
            </Field>
          </div>

          <div className="s7-form-grid-2">
            <Field label="Period" hint="How long one cycle lasts.">
              <Select value={form.period} onChange={(e) => patch({ period: e.target.value })}>
                {PERIODS.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Cohorts" hint="Which slices players can be ranked within.">
              <Select
                value={form.supportedCohorts}
                onChange={(e) => patch({ supportedCohorts: e.target.value })}
              >
                {COHORTS.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </Select>
            </Field>
          </div>

          <Field label="Game" hint="Leave unset to rank across every game.">
            <Select value={form.gameId ?? ''} onChange={(e) => patch({ gameId: e.target.value || null })}>
              <option value="">All games</option>
              {games.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.key}
                </option>
              ))}
            </Select>
          </Field>

          <div className="s7-form-grid-2">
            <Field label="Visible rank limit" hint="Ranks beyond this are hidden. Empty shows all.">
              <Input
                type="number"
                min={1}
                value={form.visibleRankLimit ?? ''}
                onChange={(e) =>
                  patch({ visibleRankLimit: e.target.value === '' ? null : Number(e.target.value) })
                }
                placeholder="all"
              />
            </Field>

            <Field
              label="Grace seconds"
              hint="How long after a cycle closes a late result is still accepted."
            >
              <Input
                type="number"
                min={0}
                value={form.graceSeconds}
                onChange={(e) => patch({ graceSeconds: Number(e.target.value) || 0 })}
              />
            </Field>
          </div>

          <Field label="Active">
            <Switch
              checked={form.isActive}
              onChange={(v) => patch({ isActive: v })}
              label={form.isActive ? 'Active — visible to players' : 'Off — hidden from clients'}
            />
          </Field>

          <div>
            <h3 className="s7-subhead">Display names</h3>
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

          {!isNew ? (
            <div>
              <h3 className="s7-subhead">
                <Timer size={15} /> Cycles
              </h3>

              {!cycles.length ? (
                <Note tone="warning">
                  This board has no cycles, so no result can land in it. Schedule one below.
                </Note>
              ) : (
                <div className="s7-dt-wrap" style={{ marginBottom: '0.7rem' }}>
                  <table className="s7-dt">
                    <thead>
                      <tr>
                        <th>State</th>
                        <th>Window</th>
                        <th className="s7-num">Ranked</th>
                        <th />
                      </tr>
                    </thead>
                    <tbody>
                      {cycles.map((cycle) => (
                        <tr key={cycle.cycleId}>
                          <td>
                            <Badge
                              tone={
                                cycle.state === 'Open'
                                  ? 'success'
                                  : cycle.state === 'Scheduled'
                                    ? 'info'
                                    : 'muted'
                              }
                            >
                              {cycle.state}
                            </Badge>
                          </td>
                          <td style={{ fontSize: '0.78rem' }}>
                            {formatDateTime(cycle.startsAtUtc)} →{' '}
                            {cycle.endsAtUtc ? formatDateTime(cycle.endsAtUtc) : 'open-ended'}
                          </td>
                          <td className="s7-num">{cycle.totalRanked.toLocaleString()}</td>
                          <td>
                            <span className="s7-inline">
                              <Button variant="ghost" onClick={() => void rebuild(cycle.cycleId)}>
                                <RefreshCw size={13} /> Rebuild
                              </Button>
                              {cycle.state !== 'Settled' ? (
                                <Button variant="ghost" onClick={() => setConfirmSettle(cycle.cycleId)}>
                                  <Gavel size={13} /> Settle
                                </Button>
                              ) : null}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <div className="s7-form-grid-2">
                <Field label="Cycle starts">
                  <Input type="datetime-local" value={newStart} onChange={(e) => setNewStart(e.target.value)} />
                </Field>
                <Field label="Cycle ends">
                  <Input type="datetime-local" value={newEnd} onChange={(e) => setNewEnd(e.target.value)} />
                </Field>
              </div>

              <Button
                variant="ghost"
                disabled={!newStart || !newEnd || newStart >= newEnd}
                onClick={async () => {
                  const startsAtUtc = fromLocalInput(newStart)
                  const endsAtUtc = fromLocalInput(newEnd)
                  if (!startsAtUtc || !endsAtUtc) return

                  await addCycle({ startsAtUtc, endsAtUtc })
                  setNewStart('')
                  setNewEnd('')
                }}
              >
                <Plus size={15} /> Schedule cycle
              </Button>
            </div>
          ) : null}
        </div>
      </Drawer>

      <Modal
        open={!!confirmSettle}
        onClose={() => setConfirmSettle(null)}
        icon={<Gavel size={18} />}
        title="Settle this cycle?"
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmSettle(null)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                if (!confirmSettle) return
                await settle(confirmSettle)
                setConfirmSettle(null)
              }}
            >
              Settle and pay out
            </Button>
          </>
        }
      >
        <Note tone="danger">
          Settlement freezes the final ranks and writes reward transactions against them. There is no
          way to unsettle a cycle. Resolve any flagged results and rebuild first — a score still
          under review is settled exactly as it currently stands.
        </Note>
      </Modal>
    </>
  )
}

// ---------------------------------------------------------------------------
// Bounds
// ---------------------------------------------------------------------------

function BoundsTab() {
  const { bounds, loading, refreshing, reload, save } = useMetricBounds()
  const { games } = useGames()
  const [editing, setEditing] = useState<MetricBoundDto | null>(null)
  const [creating, setCreating] = useState(false)

  const gameKey = useMemo(() => {
    const map = new Map(games.map((g) => [g.gameId, g.gameKey]))
    return (id: string | null) => (id ? (map.get(id) ?? 'unknown') : 'all games')
  }, [games])

  const columns = useMemo<Column<MetricBoundDto>[]>(
    () => [
      { key: 'metric', header: 'Metric', sort: (b) => b.metric, render: (b) => <code className="s7-key">{b.metric}</code> },
      { key: 'game', header: 'Game', render: (b) => <span className="s7-muted">{gameKey(b.gameId)}</span> },
      {
        key: 'max',
        header: 'Max single value',
        numeric: true,
        sort: (b) => b.maxValue,
        render: (b) => (b.maxValue == null ? <Badge tone="warning">none</Badge> : b.maxValue.toLocaleString()),
      },
      {
        key: 'perDay',
        header: 'Max results / day',
        numeric: true,
        sort: (b) => b.maxResultsPerDay,
        render: (b) => (b.maxResultsPerDay == null ? <span className="s7-muted">none</span> : b.maxResultsPerDay.toLocaleString()),
      },
      {
        key: 'valuePerDay',
        header: 'Max value / day',
        numeric: true,
        sort: (b) => b.maxValuePerDay,
        render: (b) => (b.maxValuePerDay == null ? <span className="s7-muted">none</span> : b.maxValuePerDay.toLocaleString()),
      },
      {
        key: 'enabled',
        header: 'State',
        sort: (b) => b.enabled,
        render: (b) => (b.enabled ? <Badge tone="success">On</Badge> : <Badge tone="muted">Off</Badge>),
      },
    ],
    [gameKey],
  )

  return (
    <>
      <Note>
        A bound is what makes a result implausible. Any score exceeding one is stored but flagged,
        kept out of the ranks, and waits for a human verdict on the Flagged tab.
      </Note>

      {/* Card takes className, not style — the gap below the note is a layout
          concern of this tab rather than of the card component. */}
      <div style={{ marginTop: '1rem' }} />

      <Card>
        <CardHeader
          icon={<Ruler size={16} />}
          title={`${bounds.length} bound${bounds.length === 1 ? '' : 's'}`}
          actions={
            <>
              <Button variant="ghost" onClick={() => setCreating(true)}>
                <Plus size={15} /> New bound
              </Button>
              <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
                <RefreshCw size={15} />
              </IconButton>
            </>
          }
        />
        <CardBody>
          <DataTable
            rows={bounds}
            columns={columns}
            getId={(b) => b.id}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.id ?? null}
            empty="No bounds set — every reported score is believed."
          />
        </CardBody>
      </Card>

      <BoundEditor
        key={editing?.id ?? (creating ? 'new' : 'closed')}
        bound={editing}
        open={!!editing || creating}
        games={games.map((g) => ({ id: g.gameId, key: g.gameKey }))}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          await save(request)
          setEditing(null)
          setCreating(false)
        }}
      />
    </>
  )
}

function BoundEditor({
  bound,
  open,
  games,
  onClose,
  onSave,
}: {
  bound: MetricBoundDto | null
  open: boolean
  games: { id: string; key: string }[]
  onClose: () => void
  onSave: (request: SaveMetricBoundRequest) => Promise<void>
}) {
  const [form, setForm] = useState<SaveMetricBoundRequest>(() => ({
    gameId: bound?.gameId ?? null,
    metric: bound?.metric ?? '',
    maxValue: bound?.maxValue ?? null,
    maxResultsPerDay: bound?.maxResultsPerDay ?? null,
    maxValuePerDay: bound?.maxValuePerDay ?? null,
    enabled: bound?.enabled ?? true,
  }))

  const [saving, setSaving] = useState(false)

  const noLimits =
    form.maxValue == null && form.maxResultsPerDay == null && form.maxValuePerDay == null

  function patch(next: Partial<SaveMetricBoundRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={bound ? `Bound: ${bound.metric}` : 'New bound'}
      subtitle="Saved by (game, metric) — saving over an existing pair replaces it."
      footer={
        <>
          <Button
            loading={saving}
            disabled={!form.metric.trim() || noLimits}
            onClick={async () => {
              setSaving(true)
              try {
                await onSave(form)
              } finally {
                setSaving(false)
              }
            }}
          >
            Save bound
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <Field label="Metric">
          <Input
            mono
            value={form.metric}
            onChange={(e) => patch({ metric: e.target.value.trim() })}
            placeholder="distance_m"
          />
        </Field>

        <Field label="Game" hint="Leave unset to bound this metric in every game.">
          <Select value={form.gameId ?? ''} onChange={(e) => patch({ gameId: e.target.value || null })}>
            <option value="">All games</option>
            {games.map((g) => (
              <option key={g.id} value={g.id}>
                {g.key}
              </option>
            ))}
          </Select>
        </Field>

        <Field label="Max single value" hint="One result above this is implausible on its face.">
          <Input
            type="number"
            min={0}
            value={form.maxValue ?? ''}
            onChange={(e) => patch({ maxValue: e.target.value === '' ? null : Number(e.target.value) })}
            placeholder="none"
          />
        </Field>

        <Field label="Max results per day" hint="How many results one player may submit per UTC day.">
          <Input
            type="number"
            min={1}
            value={form.maxResultsPerDay ?? ''}
            onChange={(e) =>
              patch({ maxResultsPerDay: e.target.value === '' ? null : Number(e.target.value) })
            }
            placeholder="none"
          />
        </Field>

        <Field label="Max value per day" hint="Total across all of a player's results in a day.">
          <Input
            type="number"
            min={0}
            value={form.maxValuePerDay ?? ''}
            onChange={(e) =>
              patch({ maxValuePerDay: e.target.value === '' ? null : Number(e.target.value) })
            }
            placeholder="none"
          />
        </Field>

        {noLimits ? (
          <Note tone="warning">
            A bound with no limits set does nothing. Fill in at least one ceiling.
          </Note>
        ) : null}

        <Field label="Enabled">
          <Switch
            checked={form.enabled}
            onChange={(v) => patch({ enabled: v })}
            label={form.enabled ? 'On — scores are checked against this' : 'Off — not enforced'}
          />
        </Field>
      </div>
    </Drawer>
  )
}

// ---------------------------------------------------------------------------
// Flagged
// ---------------------------------------------------------------------------

function FlaggedTab() {
  const { flagged, loading, refreshing, reload, resolve } = useFlaggedResults()
  const { games } = useGames()
  const [search, setSearch] = useState('')
  const [ruling, setRuling] = useState<{ result: FlaggedResultDto; legitimate: boolean } | null>(null)

  const gameKey = useMemo(() => {
    const map = new Map(games.map((g) => [g.gameId, g.gameKey]))
    return (id: string) => map.get(id) ?? id.slice(0, 8)
  }, [games])

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return flagged

    return flagged.filter((r) =>
      [r.displayName, r.metric, r.flagReason ?? '', gameKey(r.gameId)]
        .join(' ')
        .toLowerCase()
        .includes(term),
    )
  }, [flagged, search, gameKey])

  const columns = useMemo<Column<FlaggedResultDto>[]>(
    () => [
      {
        key: 'player',
        header: 'Player',
        sort: (r) => r.displayName,
        render: (r) => (
          <div>
            <div style={{ fontWeight: 600 }}>{r.displayName}</div>
            <CopyId id={r.userId} label="userId" />
          </div>
        ),
      },
      { key: 'game', header: 'Game', sort: (r) => gameKey(r.gameId), render: (r) => <code className="s7-key">{gameKey(r.gameId)}</code> },
      { key: 'metric', header: 'Metric', sort: (r) => r.metric, render: (r) => <code className="s7-key">{r.metric}</code> },
      {
        key: 'value',
        header: 'Claimed',
        numeric: true,
        sort: (r) => r.value,
        render: (r) => <strong>{r.value.toLocaleString()}</strong>,
      },
      {
        key: 'reason',
        header: 'Why flagged',
        sort: (r) => r.flagReason ?? '',
        render: (r) => (
          <span style={{ fontSize: '0.8rem' }}>
            {r.flagReason ?? <span className="s7-muted">no reason recorded</span>}
          </span>
        ),
      },
      {
        key: 'when',
        header: 'Occurred',
        sort: (r) => r.occurredAtUtc,
        render: (r) => <span className="s7-muted" style={{ fontSize: '0.78rem' }}>{formatRelative(r.occurredAtUtc)}</span>,
      },
      {
        key: 'verdict',
        header: 'Verdict',
        render: (r) => (
          <span className="s7-inline">
            <Button
              variant="ghost"
              onClick={(e) => {
                e.stopPropagation()
                setRuling({ result: r, legitimate: true })
              }}
            >
              <CheckCircle2 size={14} /> Accept
            </Button>
            <Button
              variant="ghost"
              onClick={(e) => {
                e.stopPropagation()
                setRuling({ result: r, legitimate: false })
              }}
            >
              <XCircle size={14} /> Reject
            </Button>
          </span>
        ),
      },
    ],
    [gameKey],
  )

  return (
    <>
      <StatRow>
        <Stat
          icon={<Flag size={13} />}
          label="Awaiting a verdict"
          value={flagged.length}
          sub="Held out of the ranks until ruled on"
          tone={flagged.length ? 'danger' : 'success'}
        />
      </StatRow>

      <Card>
        <CardHeader
          icon={<Flag size={16} />}
          title={`${rows.length} flagged result${rows.length === 1 ? '' : 's'}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search player, metric or reason…" />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(r) => r.resultId}
            loading={loading}
            initialSort={{ key: 'value', direction: 'desc' }}
            empty="Nothing is flagged. Every reported score was within its bounds."
          />
        </CardBody>
      </Card>

      <Modal
        open={!!ruling}
        onClose={() => setRuling(null)}
        icon={ruling?.legitimate ? <CheckCircle2 size={18} /> : <XCircle size={18} />}
        title={ruling?.legitimate ? 'Accept this score?' : 'Reject this score?'}
        footer={
          <>
            <Button variant="ghost" onClick={() => setRuling(null)}>
              Cancel
            </Button>
            <Button
              variant={ruling?.legitimate ? 'primary' : 'danger'}
              onClick={async () => {
                if (!ruling) return
                await resolve(ruling.result, ruling.legitimate)
                setRuling(null)
              }}
            >
              {ruling?.legitimate ? 'Accept — it counts' : 'Reject — keep it out'}
            </Button>
          </>
        }
      >
        {ruling?.legitimate ? (
          <Note tone="warning">
            Accepting clears the flag, but it does <strong>not</strong> insert the score into the
            standings on its own — the projection that built the current ranks ran while this result
            was excluded. Rebuild the cycle afterwards for it to appear.
          </Note>
        ) : (
          <Note tone="danger">
            The result stays on record and permanently out of the boards. The player is not
            otherwise penalised.
          </Note>
        )}
      </Modal>
    </>
  )
}
