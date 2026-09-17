import { useEffect, useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  AlertTriangle,
  Ban,
  CalendarClock,
  Copy,
  Gift,
  Plus,
  RefreshCw,
  Trophy,
  Users2,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import type { TranslationRow } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import { useGames } from '../features/games/data'
import { useLanguages } from '../store/languages'
import {
  blankEvent,
  eventToRequest,
  useEconomyProfiles,
  useGameModes,
  useGameWorlds,
  usePlayEvents,
  usePrizeClaims,
} from '../features/play/data'
import { textFor } from '../lib/format'
import { listVariants } from '../components/ui/motion'
import type {
  PlayEventAdminDto,
  PrizeClaimAdminDto,
  SaveEventPrizeTierRequest,
  SavePlayEventRequest,
} from '../types/api'

// ===========================================================================
// Live events
//
// A competition is one game, one mode, its own ladder, its own rules and its own
// prize table. Creating one writes all of that in a single transaction, because
// each piece is useless without the others.
//
// Two things about this page are deliberate and worth knowing before editing it:
//
//   The window is NOT stored on the event. It lives on the leaderboard cycle the
//   event is bound to, and every "is it running" answer comes from there. Two
//   sources of truth for a window is how an event ends up open on the ladder and
//   finished in the app.
//
//   What may be edited narrows as the event runs. Scheduled: everything. Open:
//   the end date may only move later, and the prize table is frozen. Closed or
//   settled: presentation only. Entrants competed under the rules they were shown.
// ===========================================================================

type Tab = 'events' | 'claims'

const METRICS = [
  { value: 'CORRECT_ANSWERS', label: 'Correct answers — accuracy across entries' },
  { value: 'LESSONS_COMPLETED', label: 'Lessons completed' },
  { value: 'LESSONS_ACED', label: 'Lessons aced' },
  { value: 'TOTAL_LESSON_SCORE', label: 'Total lesson score' },
  { value: 'PICKUPS_COLLECTED', label: 'Pickups collected' },
  { value: 'RUNS_SETTLED', label: 'Runs played' },
  { value: 'RUNS_COMPLETED', label: 'Runs finished' },
  { value: 'BEST_RUN_SECONDS', label: 'Longest single run' },
  { value: 'CURRENCY_EARNED', label: 'Currency earned' },
]

export function LiveEvents() {
  const [tab, setTab] = useState<Tab>('events')

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Trophy size={22} />}
        title="Live events"
        subtitle="Competitions with their own ladder, rules and prizes. The window lives on the bound cycle — an event is a binding onto it, not a second state machine."
        actions={
          <Segmented
            layoutId="events-tab"
            value={tab}
            onChange={setTab}
            options={[
              { value: 'events', label: 'Events' },
              { value: 'claims', label: 'Prize claims' },
            ]}
          />
        }
      />

      {tab === 'events' ? <EventsPanel /> : <ClaimsPanel />}
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

function EventsPanel() {
  const { games } = useGames()
  const [gameId, setGameId] = useState<string | null>(null)

  const { events, loading, refreshing, reload, create, update, cancel, duplicate } = usePlayEvents(gameId)
  const { modes } = useGameModes(gameId)
  const { worlds } = useGameWorlds(gameId)
  const { profiles } = useEconomyProfiles()
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<PlayEventAdminDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [cancelling, setCancelling] = useState<PlayEventAdminDto | null>(null)
  const [cancelReason, setCancelReason] = useState('')

  useEffect(() => {
    if (!gameId && games.length) setGameId(games[0].gameId)
  }, [games, gameId])

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return events

    return events.filter((e) =>
      [e.eventKey, ...e.translations.map((t) => t.name)].join(' ').toLowerCase().includes(term),
    )
  }, [events, search])

  const columns = useMemo<Column<PlayEventAdminDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Event',
        sort: (e) => e.startsAtUtc,
        render: (e) => (
          <div>
            <div style={{ fontWeight: 600 }}>
              {textFor(e.translations.map((t) => ({ langId: t.langId, name: t.name })), selectedLangId) ||
                e.eventKey}
            </div>
            <code className="s7-key">{e.eventKey}</code>
          </div>
        ),
      },
      {
        key: 'mode',
        header: 'Played',
        render: (e) => (
          <span className="s7-inline">
            <code className="s7-key">{e.modeKey}</code>
            {e.worldKey ? <Badge tone="info">{e.worldKey.split('.').pop()}</Badge> : null}
          </span>
        ),
      },
      {
        key: 'window',
        header: 'Window',
        sort: (e) => e.startsAtUtc,
        render: (e) => (
          <div>
            <div>{new Date(e.startsAtUtc).toLocaleString()}</div>
            <span className="s7-muted">→ {new Date(e.endsAtUtc).toLocaleString()}</span>
          </div>
        ),
      },
      {
        key: 'state',
        header: 'State',
        sort: (e) => e.state,
        render: (e) => <StateBadge event={e} />,
      },
      {
        key: 'entrants',
        header: 'Entrants',
        numeric: true,
        sort: (e) => e.participants,
        render: (e) => (
          <span className="s7-inline" style={{ justifyContent: 'flex-end' }}>
            <Users2 size={13} className="s7-muted" />
            {e.participants.toLocaleString()}
          </span>
        ),
      },
      {
        key: 'prizes',
        header: 'Prizes',
        numeric: true,
        sort: (e) => e.prizeTiers.length,
        render: (e) => {
          const real = e.prizeTiers.filter((t) => t.kind === 'real_world').length

          return (
            <span className="s7-inline" style={{ justifyContent: 'flex-end' }}>
              <Badge tone={e.prizeTiers.length ? 'success' : 'danger'}>{e.prizeTiers.length} tier(s)</Badge>
              {real > 0 ? <Badge tone="warning">{real} real-world</Badge> : null}
            </span>
          )
        },
      },
      {
        key: 'awards',
        header: 'Awarded',
        numeric: true,
        sort: (e) => e.awardsIssued,
        render: (e) => <span className="s7-muted">{e.awardsIssued.toLocaleString()}</span>,
      },
      { key: 'id', header: 'Id', render: (e) => <CopyId id={e.eventId} label="eventId" /> },
    ],
    [selectedLangId],
  )

  const usableModes = modes.filter((m) => m.isActive)

  return (
    <>
      <Card>
        <CardHeader
          icon={<CalendarClock size={16} />}
          title={`${rows.length} event${rows.length === 1 ? '' : 's'}`}
          actions={
            <span className="s7-inline">
              <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
                <RefreshCw size={15} />
              </IconButton>
              <Button disabled={!gameId || usableModes.length === 0} onClick={() => setCreating(true)}>
                <Plus size={15} /> New event
              </Button>
            </span>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <Select value={gameId ?? ''} onChange={(e) => setGameId(e.target.value)}>
              {games.map((game) => (
                <option key={game.gameId} value={game.gameId}>
                  {game.displayName || game.gameKey}
                </option>
              ))}
            </Select>
            <SearchBox value={search} onChange={setSearch} placeholder="Search by key or name…" />
          </div>

          {gameId && usableModes.length === 0 ? (
            <Note tone="warning">
              This game has no active mode, and an event is played in exactly one. Add a mode under
              Modes &amp; Worlds first.
            </Note>
          ) : null}

          <DataTable
            rows={rows}
            columns={columns}
            getId={(e) => e.eventId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.eventId ?? null}
            pageResetKey={`${gameId}|${search}`}
            empty={events.length ? 'No event matches that filter.' : 'No events yet for this game.'}
          />
        </CardBody>
      </Card>

      <EventEditor
        key={editing?.eventId ?? (creating ? 'new' : 'closed')}
        event={editing}
        gameId={gameId ?? ''}
        modes={usableModes.map((m) => ({ id: m.modeId, key: m.modeKey }))}
        worlds={worlds.map((w) => ({ key: w.worldKey, name: w.name || w.worldKey }))}
        profiles={profiles.map((p) => ({ id: p.profileId, key: p.profileKey, percent: p.payoutPercent }))}
        open={!!editing || creating}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.eventId, request)
          else await create(request)
          setEditing(null)
          setCreating(false)
        }}
        onCancel={
          editing && !editing.cancelledAtUtc
            ? () => {
                setCancelReason('')
                setCancelling(editing)
              }
            : undefined
        }
        onDuplicate={
          editing
            ? async () => {
                await duplicate(editing.eventId)
                setEditing(null)
              }
            : undefined
        }
      />

      <Modal
        open={!!cancelling}
        onClose={() => setCancelling(null)}
        icon={<Ban size={18} />}
        title={`Cancel "${cancelling?.eventKey}"?`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setCancelling(null)}>
              Keep it running
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                if (!cancelling) return
                await cancel(cancelling.eventId, cancelReason)
                setCancelling(null)
                setEditing(null)
              }}
            >
              Cancel event
            </Button>
          </>
        }
      >
        <div className="s7-stack">
          <Note tone="danger">
            Entries stop immediately and the ladder closes. <b>No prize will ever be awarded</b>, even
            when the cycle settles — including to entrants who already hold a rank. Their standings
            stay readable, because they did play.
          </Note>

          <Field label="Reason" hint="Operator-facing only. Never shown to a child.">
            <Input
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              placeholder="Prize supplier fell through"
            />
          </Field>
        </div>
      </Modal>
    </>
  )
}

function StateBadge({ event }: { event: PlayEventAdminDto }) {
  if (event.cancelledAtUtc) return <Badge tone="danger">Cancelled</Badge>
  if (!event.isActive) return <Badge tone="muted">Unpublished</Badge>

  switch (event.state) {
    case 'OPEN':
      return <Badge tone="success">Running</Badge>
    case 'SCHEDULED':
      return <Badge tone="info">Scheduled</Badge>
    case 'CLOSED':
      return <Badge tone="warning">Closed</Badge>
    case 'SETTLED':
      return <Badge tone="muted">Settled</Badge>
    default:
      return <Badge tone="muted">{event.state}</Badge>
  }
}

// ---------------------------------------------------------------------------
// Event editor
// ---------------------------------------------------------------------------

function EventEditor({
  event,
  gameId,
  modes,
  worlds,
  profiles,
  open,
  onClose,
  onSave,
  onCancel,
  onDuplicate,
}: {
  event: PlayEventAdminDto | null
  gameId: string
  modes: { id: string; key: string }[]
  worlds: { key: string; name: string }[]
  profiles: { id: string; key: string; percent: number }[]
  open: boolean
  onClose: () => void
  onSave: (request: SavePlayEventRequest) => Promise<void>
  onCancel?: () => void
  onDuplicate?: () => Promise<void>
}) {
  const [form, setForm] = useState<SavePlayEventRequest>(() =>
    event ? eventToRequest(event) : blankEvent(gameId, modes[0]?.id ?? ''),
  )
  const [saving, setSaving] = useState(false)

  const isNew = !event
  const started = !isNew && event.state !== 'SCHEDULED'
  const finished = !isNew && (event.state === 'CLOSED' || event.state === 'SETTLED')

  function patch(next: Partial<SavePlayEventRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  const hasRealWorldPrize = form.prizeTiers.some((t) => t.kind === 'real_world')

  const keyError =
    isNew && form.eventKey && !/^[a-z0-9]+(?:[._-][a-z0-9]+)*$/.test(form.eventKey)
      ? 'Lowercase letters, digits, dots — e.g. runner.weekly.2026w37.'
      : null

  const windowError =
    new Date(form.endsAtUtc) <= new Date(form.startsAtUtc)
      ? 'The end has to come after the start.'
      : null

  // A prize of real value plus a price of entry is a paid competition — a different legal object in
  // most of the world, and not one this platform offers to children.
  const entryError =
    hasRealWorldPrize && form.entryProductId
      ? 'An event with a real-world prize has to be free to enter.'
      : null

  const gradeError =
    form.maxGradeOrder > 0 && form.minGradeOrder > form.maxGradeOrder
      ? 'The lowest grade cannot be above the highest.'
      : null

  const overlapError = overlappingTiers(form.prizeTiers)
    ? 'Two prize tiers cover the same ranks. Each placing collects exactly one.'
    : null

  const blocked =
    !!keyError ||
    !!windowError ||
    !!entryError ||
    !!gradeError ||
    !!overlapError ||
    !form.eventKey.trim() ||
    !form.modeId

  const translationRows: TranslationRow[] = form.translations.map((t) => ({
    langId: t.langId,
    name: t.name,
    description: t.description,
  }))

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New event' : form.eventKey}
      subtitle={
        isNew
          ? 'Creates the event, its board and its cycle together. It stays unpublished until you switch it on.'
          : finished
            ? 'Finished. Only presentation can change now.'
            : started
              ? 'Running. The end can be extended; the prize table is frozen.'
              : 'Scheduled. Everything can still change.'
      }
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
            {isNew ? 'Create event' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          {onDuplicate ? (
            <Button variant="ghost" onClick={() => void onDuplicate()}>
              <Copy size={15} /> Next window
            </Button>
          ) : null}
          {onCancel ? (
            <Button variant="danger" onClick={onCancel} style={{ marginInlineStart: 'auto' }}>
              <Ban size={15} /> Cancel event
            </Button>
          ) : null}
        </>
      }
    >
      <div className="s7-stack">
        <Field
          label="Event key"
          error={keyError}
          hint={isNew ? 'Permanent. Awards and analytics name it long after the event is over.' : 'Cannot be changed.'}
        >
          <Input
            mono
            value={form.eventKey}
            disabled={!isNew}
            onChange={(e) => patch({ eventKey: e.target.value.toLowerCase().replace(/[^a-z0-9._-]/g, '') })}
            placeholder="runner.weekly.2026w37"
          />
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Mode" hint={started ? 'Fixed once entries began.' : 'One mode per event.'}>
            <Select
              value={form.modeId}
              disabled={started}
              onChange={(e) => patch({ modeId: e.target.value })}
            >
              {modes.map((mode) => (
                <option key={mode.id} value={mode.id}>
                  {mode.key}
                </option>
              ))}
            </Select>
          </Field>

          <Field label="World" hint="Blank leaves the mode's own world selection alone.">
            <Select
              value={form.worldKey ?? ''}
              disabled={started}
              onChange={(e) => patch({ worldKey: e.target.value || null })}
            >
              <option value="">No pinned world</option>
              {worlds.map((world) => (
                <option key={world.key} value={world.key}>
                  {world.name}
                </option>
              ))}
            </Select>
          </Field>
        </div>

        {form.worldKey ? (
          <Field
            label="Lend the world"
            hint="An event must never be unplayable because a child has not bought its world."
          >
            <Switch
              checked={form.grantsWorldForDuration}
              onChange={(v) => patch({ grantsWorldForDuration: v })}
              label={
                form.grantsWorldForDuration
                  ? 'Entrants may play it for the duration'
                  : 'Only entrants who already own it can take part'
              }
            />
          </Field>
        ) : null}

        <div className="s7-form-grid-2">
          <Field label="Starts (UTC)" error={windowError}>
            <Input
              type="datetime-local"
              disabled={started}
              value={toLocalInput(form.startsAtUtc)}
              onChange={(e) => patch({ startsAtUtc: fromLocalInput(e.target.value) ?? form.startsAtUtc })}
            />
          </Field>
          <Field label="Ends (UTC)" hint={started ? 'May only be extended.' : undefined}>
            <Input
              type="datetime-local"
              value={toLocalInput(form.endsAtUtc)}
              onChange={(e) => patch({ endsAtUtc: fromLocalInput(e.target.value) ?? form.endsAtUtc })}
            />
          </Field>
        </div>

        <div className="s7-form-grid-2">
          <Field label="Ranked on" hint={isNew ? 'Fixed after creation — the board starts collecting it.' : 'Fixed.'}>
            <Select value={form.metric} disabled={!isNew} onChange={(e) => patch({ metric: e.target.value })}>
              {METRICS.map((metric) => (
                <option key={metric.value} value={metric.value}>
                  {metric.label}
                </option>
              ))}
            </Select>
          </Field>

          <Field label="How entries add up" hint={isNew ? 'Total across entries, or the single best one.' : 'Fixed.'}>
            <Select
              value={form.aggregation}
              disabled={!isNew}
              onChange={(e) => patch({ aggregation: e.target.value })}
            >
              <option value="sum">Total of every entry</option>
              <option value="best">Best single entry</option>
            </Select>
          </Field>
        </div>

        <Field
          label="Ranked within"
          hint="Grade cohorts keep a six-year-old from competing with a twelve-year-old for the same prize."
        >
          <Select
            value={form.prizeCohort}
            disabled={started}
            onChange={(e) => patch({ prizeCohort: e.target.value })}
          >
            <option value="all">Everybody together</option>
            <option value="grade">Each school year separately</option>
          </Select>
        </Field>

        <h3 className="s7-subhead">Who may enter</h3>

        <div className="s7-form-grid-2">
          <Field label="Entries per day" hint="Blank means no limit.">
            <Input
              type="number"
              min={1}
              value={form.maxEntriesPerDay ?? ''}
              onChange={(e) => patch({ maxEntriesPerDay: e.target.value ? Number(e.target.value) : null })}
            />
          </Field>
          <Field label="Entries in total" hint="Blank means no limit.">
            <Input
              type="number"
              min={1}
              value={form.maxEntriesTotal ?? ''}
              onChange={(e) => patch({ maxEntriesTotal: e.target.value ? Number(e.target.value) : null })}
            />
          </Field>
        </div>

        <div className="s7-form-grid-2">
          <Field label="Lowest grade order" error={gradeError} hint="0 is ungated.">
            <Input
              type="number"
              min={0}
              value={form.minGradeOrder}
              onChange={(e) => patch({ minGradeOrder: Number(e.target.value) || 0 })}
            />
          </Field>
          <Field label="Highest grade order" hint="0 is ungated.">
            <Input
              type="number"
              min={0}
              value={form.maxGradeOrder}
              onChange={(e) => patch({ maxGradeOrder: Number(e.target.value) || 0 })}
            />
          </Field>
        </div>

        <div className="s7-form-grid-2">
          <Field label="Minimum level" hint="0 is ungated.">
            <Input
              type="number"
              min={0}
              value={form.minLevel}
              onChange={(e) => patch({ minLevel: Number(e.target.value) || 0 })}
            />
          </Field>
          <Field label="Members only" error={entryError} hint="A product an entrant must own. Leave blank for an open event.">
            <Input
              mono
              value={form.entryProductId ?? ''}
              onChange={(e) => patch({ entryProductId: e.target.value || null })}
              placeholder="product id"
            />
          </Field>
        </div>

        <Field label="Payout profile" hint="How entries settle. Blank uses the platform default.">
          <Select
            value={form.economyProfileId ?? ''}
            onChange={(e) => patch({ economyProfileId: e.target.value || null })}
          >
            <option value="">Platform default</option>
            {profiles.map((p) => (
              <option key={p.id} value={p.id}>
                {p.key} — {p.percent}%
              </option>
            ))}
          </Select>
        </Field>

        <h3 className="s7-subhead">Prize table</h3>
        <p className="s7-hint" style={{ marginBottom: '0.6rem' }}>
          Each placing collects exactly one tier — the narrowest that covers its rank. Ranges cannot
          overlap, because a prize table is a promise rather than a stack of bonuses.
        </p>

        {started ? (
          <Note tone="warning">
            This event has started, so its prize table is frozen. Entrants competed for what they were
            shown.
          </Note>
        ) : null}

        <PrizeTierEditor
          tiers={form.prizeTiers}
          disabled={started}
          error={overlapError}
          onChange={(tiers) => patch({ prizeTiers: tiers })}
        />

        {hasRealWorldPrize ? (
          <Note tone="warning">
            <AlertTriangle size={13} /> A real-world prize opens a claim for a person to fulfil — the
            platform never delivers one itself and stores no address, phone number or payment detail.
            Entry must stay free, and running prizes of value for children needs a legal review in
            every market you run it in.
          </Note>
        ) : null}

        {hasRealWorldPrize ? (
          <Field label="Claim window (days)" hint="How long a winner has before an unanswered claim lapses.">
            <Input
              type="number"
              min={1}
              max={365}
              value={form.claimWindowDays}
              onChange={(e) => patch({ claimWindowDays: Number(e.target.value) || 30 })}
            />
          </Field>
        ) : null}

        <h3 className="s7-subhead">How it looks</h3>

        <div className="s7-form-grid-2">
          <Field label="Banner address" hint="Addressables address the client resolves. Optional.">
            <Input
              mono
              value={form.bannerAddress ?? ''}
              onChange={(e) => patch({ bannerAddress: e.target.value || null })}
              placeholder="events/weekly-banner"
            />
          </Field>
          <Field label="Accent colour" hint="#RRGGBB. Themes the event card without a release.">
            <Input
              mono
              value={form.accentColor ?? ''}
              onChange={(e) => patch({ accentColor: e.target.value || null })}
              placeholder="#4F46E5"
            />
          </Field>
        </div>

        <div className="s7-form-grid-2">
          <Field label="Sort order" hint="Lower is higher up the list.">
            <Input
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(e) => patch({ sortOrder: Number(e.target.value) || 0 })}
            />
          </Field>
          <Field label="Published">
            <Switch
              checked={form.isActive}
              onChange={(v) => patch({ isActive: v })}
              label={form.isActive ? 'Visible to players' : 'Hidden — nobody can see or enter it'}
            />
          </Field>
        </div>

        <div>
          <h3 className="s7-subhead">Name, blurb and rules</h3>
          <p className="s7-hint" style={{ marginBottom: '0.6rem' }}>
            The rules are shown in full before a child's first entry — a competition whose rules they
            cannot read is not one they agreed to.
          </p>
          <TranslationsEditor
            withDescription
            value={translationRows}
            onChange={(rows) =>
              patch({
                translations: rows.map((r) => {
                  const existing = form.translations.find((t) => t.langId === r.langId)

                  return {
                    langId: r.langId,
                    name: r.name,
                    description: r.description ?? '',
                    rules: existing?.rules ?? '',
                  }
                }),
              })
            }
          />

          {form.translations.map((translation) => (
            <Field key={translation.langId} label={`Rules (${shortLang(translation.langId)})`}>
              <Input
                value={translation.rules}
                onChange={(e) =>
                  patch({
                    translations: form.translations.map((t) =>
                      t.langId === translation.langId ? { ...t, rules: e.target.value } : t,
                    ),
                  })
                }
                placeholder="Answer as many questions as you can before Sunday."
              />
            </Field>
          ))}
        </div>
      </div>
    </Drawer>
  )
}

// ---------------------------------------------------------------------------
// Prize tiers
// ---------------------------------------------------------------------------

function PrizeTierEditor({
  tiers,
  disabled,
  error,
  onChange,
}: {
  tiers: SaveEventPrizeTierRequest[]
  disabled?: boolean
  error?: string | null
  onChange: (tiers: SaveEventPrizeTierRequest[]) => void
}) {
  const languages = useLanguages((s) => s.languages)

  function patchTier(index: number, next: Partial<SaveEventPrizeTierRequest>) {
    onChange(tiers.map((tier, i) => (i === index ? { ...tier, ...next } : tier)))
  }

  function addTier() {
    const nextRank = tiers.reduce((max, tier) => Math.max(max, tier.toRank), 0) + 1

    onChange([
      ...tiers,
      {
        fromRank: nextRank,
        toRank: nextRank,
        kind: 'in_game',
        grants: [{ currency: 'coins', amount: 100, productId: null }],
        declaredValueMinor: null,
        valueCurrencyCode: null,
        quantity: null,
        sortOrder: tiers.length,
        translations: languages.map((l) => ({ langId: l.id, title: '', description: '' })),
      },
    ])
  }

  return (
    <div className="s7-stack">
      {error ? <Note tone="danger">{error}</Note> : null}

      {tiers.length === 0 ? (
        <Note tone="warning">
          No prizes yet. An event can run without them, but nothing will be awarded when it settles.
        </Note>
      ) : null}

      {tiers.map((tier, index) => (
        <Card key={tier.tierId ?? index}>
          <CardHeader
            icon={<Gift size={15} />}
            title={
              tier.fromRank === tier.toRank ? `Rank ${tier.fromRank}` : `Ranks ${tier.fromRank}–${tier.toRank}`
            }
            actions={
              disabled ? null : (
                <IconButton
                  label="Remove tier"
                  onClick={() => onChange(tiers.filter((_, i) => i !== index))}
                >
                  <Ban size={14} />
                </IconButton>
              )
            }
          />
          <CardBody>
            <div className="s7-stack">
              <div className="s7-form-grid-2">
                <Field label="From rank">
                  <Input
                    type="number"
                    min={1}
                    disabled={disabled}
                    value={tier.fromRank}
                    onChange={(e) => patchTier(index, { fromRank: Number(e.target.value) || 1 })}
                  />
                </Field>
                <Field label="To rank">
                  <Input
                    type="number"
                    min={1}
                    disabled={disabled}
                    value={tier.toRank}
                    onChange={(e) => patchTier(index, { toRank: Number(e.target.value) || 1 })}
                  />
                </Field>
              </div>

              <Field label="Prize kind">
                <Select
                  value={tier.kind}
                  disabled={disabled}
                  onChange={(e) =>
                    patchTier(index, {
                      kind: e.target.value,
                      // Swapping kinds clears the half that no longer applies, so a real-world tier
                      // cannot quietly keep currency grants the API would refuse.
                      grants: e.target.value === 'in_game' ? tier.grants : [],
                      declaredValueMinor: e.target.value === 'real_world' ? tier.declaredValueMinor : null,
                      valueCurrencyCode: e.target.value === 'real_world' ? tier.valueCurrencyCode : null,
                    })
                  }
                >
                  <option value="in_game">In-game — currency or products</option>
                  <option value="real_world">Real-world — a person fulfils it</option>
                </Select>
              </Field>

              {tier.kind === 'in_game' ? (
                <GrantsEditor
                  grants={tier.grants}
                  disabled={disabled}
                  onChange={(grants) => patchTier(index, { grants })}
                />
              ) : (
                <div className="s7-form-grid-2">
                  <Field label="Declared value (minor units)" hint="For your own reporting. Never shown as a price.">
                    <Input
                      type="number"
                      min={0}
                      disabled={disabled}
                      value={tier.declaredValueMinor ?? ''}
                      onChange={(e) =>
                        patchTier(index, {
                          declaredValueMinor: e.target.value ? Number(e.target.value) : null,
                        })
                      }
                    />
                  </Field>
                  <Field label="Currency code">
                    <Input
                      mono
                      disabled={disabled}
                      value={tier.valueCurrencyCode ?? ''}
                      onChange={(e) => patchTier(index, { valueCurrencyCode: e.target.value || null })}
                      placeholder="EGP"
                    />
                  </Field>
                </div>
              )}

              <Field
                label="How many exist"
                hint="Blank means unlimited. A tier spanning ranks 1–10 with three prizes awards three."
              >
                <Input
                  type="number"
                  min={1}
                  disabled={disabled}
                  value={tier.quantity ?? ''}
                  onChange={(e) => patchTier(index, { quantity: e.target.value ? Number(e.target.value) : null })}
                />
              </Field>

              <div>
                <h4 className="s7-subhead">What the winner is told they won</h4>
                <TranslationsEditor
                  withDescription
                  nameLabel="Prize"
                  value={tier.translations.map((t) => ({
                    langId: t.langId,
                    name: t.title,
                    description: t.description,
                  }))}
                  onChange={(rows) =>
                    patchTier(index, {
                      translations: rows.map((r) => ({
                        langId: r.langId,
                        title: r.name,
                        description: r.description ?? '',
                      })),
                    })
                  }
                />
              </div>
            </div>
          </CardBody>
        </Card>
      ))}

      {disabled ? null : (
        <Button variant="ghost" onClick={addTier}>
          <Plus size={15} /> Add a prize tier
        </Button>
      )}
    </div>
  )
}

function GrantsEditor({
  grants,
  disabled,
  onChange,
}: {
  grants: { currency: string | null; amount: number; productId: string | null }[]
  disabled?: boolean
  onChange: (grants: { currency: string | null; amount: number; productId: string | null }[]) => void
}) {
  return (
    <div className="s7-stack" style={{ gap: '0.5rem' }}>
      {grants.map((grant, index) => (
        <div key={index} className="s7-form-grid-2">
          {grant.productId === null ? (
            <>
              <Field label="Currency">
                <Input
                  mono
                  disabled={disabled}
                  value={grant.currency ?? ''}
                  onChange={(e) =>
                    onChange(grants.map((g, i) => (i === index ? { ...g, currency: e.target.value } : g)))
                  }
                  placeholder="coins"
                />
              </Field>
              <Field label="Amount">
                <Input
                  type="number"
                  min={0}
                  disabled={disabled}
                  value={grant.amount}
                  onChange={(e) =>
                    onChange(
                      grants.map((g, i) => (i === index ? { ...g, amount: Number(e.target.value) || 0 } : g)),
                    )
                  }
                />
              </Field>
            </>
          ) : (
            <Field label="Product">
              <Input
                mono
                disabled={disabled}
                value={grant.productId ?? ''}
                onChange={(e) =>
                  onChange(grants.map((g, i) => (i === index ? { ...g, productId: e.target.value } : g)))
                }
                placeholder="product id"
              />
            </Field>
          )}
        </div>
      ))}

      {disabled ? null : (
        <span className="s7-inline">
          <Button
            variant="ghost"
            onClick={() => onChange([...grants, { currency: 'coins', amount: 100, productId: null }])}
          >
            <Plus size={14} /> Currency
          </Button>
          <Button
            variant="ghost"
            onClick={() => onChange([...grants, { currency: null, amount: 0, productId: '' }])}
          >
            <Plus size={14} /> Product
          </Button>
        </span>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Prize claims
// ---------------------------------------------------------------------------

const CLAIM_STATES = [
  { value: '', label: 'All' },
  { value: 'pending_review', label: 'Waiting for review' },
  { value: 'awaiting_guardian', label: 'With a guardian' },
  { value: 'fulfilled', label: 'Delivered' },
  { value: 'forfeited', label: 'Forfeited' },
  { value: 'rejected', label: 'Rejected' },
]

function ClaimsPanel() {
  const [state, setState] = useState('pending_review')
  const { claims, loading, refreshing, reload, update } = usePrizeClaims(state || null)

  const [working, setWorking] = useState<PrizeClaimAdminDto | null>(null)
  const [note, setNote] = useState('')

  const columns = useMemo<Column<PrizeClaimAdminDto>[]>(
    () => [
      {
        key: 'prize',
        header: 'Prize',
        render: (c) => (
          <div>
            <div style={{ fontWeight: 600 }}>{c.prizeTitle}</div>
            <span className="s7-muted">
              {c.eventName || c.eventKey} · rank {c.finalRank}
            </span>
          </div>
        ),
      },
      {
        key: 'winner',
        header: 'Winner',
        render: (c) => (
          <div>
            <div>{c.displayName || '—'}</div>
            <CopyId id={c.userId} label="userId" />
          </div>
        ),
      },
      {
        key: 'value',
        header: 'Declared value',
        numeric: true,
        sort: (c) => c.declaredValueMinor ?? 0,
        render: (c) =>
          c.declaredValueMinor ? (
            <span className="s7-muted">
              {(c.declaredValueMinor / 100).toLocaleString()} {c.valueCurrencyCode}
            </span>
          ) : (
            <span className="s7-muted">—</span>
          ),
      },
      {
        key: 'state',
        header: 'State',
        sort: (c) => c.state,
        render: (c) => <ClaimStateBadge state={c.state} />,
      },
      {
        key: 'expires',
        header: 'Lapses',
        sort: (c) => c.expiresAtUtc,
        render: (c) => {
          const days = Math.round((new Date(c.expiresAtUtc).getTime() - Date.now()) / 86_400_000)
          const terminal = c.state !== 'PENDING_REVIEW' && c.state !== 'AWAITING_GUARDIAN'

          if (terminal) return <span className="s7-muted">—</span>

          return days < 0 ? (
            <Badge tone="danger">lapsed</Badge>
          ) : (
            <Badge tone={days <= 3 ? 'warning' : 'muted'}>{days} day(s)</Badge>
          )
        },
      },
    ],
    [],
  )

  const next = working ? allowedTransitions(working.state) : []

  return (
    <>
      <Card>
        <CardHeader
          icon={<Gift size={16} />}
          title={`${claims.length} claim${claims.length === 1 ? '' : 's'}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <Note>
            Real-world prizes only, and every step is a person's decision. This queue holds no address,
            phone number or payment detail — fulfilment happens through whatever channel you already
            have a lawful basis for.
          </Note>

          <div className="s7-bar">
            <Select value={state} onChange={(e) => setState(e.target.value)}>
              {CLAIM_STATES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </Select>
          </div>

          <DataTable
            rows={claims}
            columns={columns}
            getId={(c) => c.claimId}
            loading={loading}
            onRowClick={(claim) => {
              setNote(claim.reviewNote ?? '')
              setWorking(claim)
            }}
            selectedId={working?.claimId ?? null}
            pageResetKey={state}
            empty="Nothing in this queue."
          />
        </CardBody>
      </Card>

      <Modal
        open={!!working}
        onClose={() => setWorking(null)}
        icon={<Gift size={18} />}
        title={working?.prizeTitle ?? ''}
        footer={
          <>
            <Button variant="ghost" onClick={() => setWorking(null)}>
              Close
            </Button>
            {next.map((target) => (
              <Button
                key={target.value}
                variant={target.danger ? 'danger' : 'primary'}
                onClick={async () => {
                  if (!working) return
                  await update(working.claimId, { state: target.value, note: note || null })
                  setWorking(null)
                }}
              >
                {target.label}
              </Button>
            ))}
          </>
        }
      >
        <div className="s7-stack">
          <div className="s7-muted">
            {working?.eventName || working?.eventKey} · rank {working?.finalRank} ·{' '}
            {working?.displayName}
          </div>

          {next.length === 0 ? (
            <Note>This claim has finished. Its state is part of the record and does not move again.</Note>
          ) : null}

          <Field label="Note" hint="For whoever reads this next. Never shown to the winner.">
            <Input
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Guardian contacted by phone, delivery arranged"
            />
          </Field>
        </div>
      </Modal>
    </>
  )
}

function ClaimStateBadge({ state }: { state: string }) {
  switch (state) {
    case 'PENDING_REVIEW':
      return <Badge tone="warning">Waiting</Badge>
    case 'AWAITING_GUARDIAN':
      return <Badge tone="info">With guardian</Badge>
    case 'FULFILLED':
      return <Badge tone="success">Delivered</Badge>
    case 'FORFEITED':
      return <Badge tone="muted">Forfeited</Badge>
    case 'REJECTED':
      return <Badge tone="danger">Rejected</Badge>
    default:
      return <Badge tone="muted">{state}</Badge>
  }
}

/** The moves a claim can make from where it is. Mirrors PrizeClaim.CanTransition on the server. */
function allowedTransitions(state: string): { value: string; label: string; danger?: boolean }[] {
  switch (state) {
    case 'PENDING_REVIEW':
      return [
        { value: 'awaiting_guardian', label: 'Approve — contact guardian' },
        { value: 'rejected', label: 'Reject', danger: true },
        { value: 'forfeited', label: 'Forfeit', danger: true },
      ]
    case 'AWAITING_GUARDIAN':
      return [
        { value: 'fulfilled', label: 'Mark delivered' },
        { value: 'forfeited', label: 'Forfeit', danger: true },
        { value: 'rejected', label: 'Reject', danger: true },
      ]
    default:
      return []
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Whether any two tiers cover the same rank — refused by the API, caught here in place. */
function overlappingTiers(tiers: SaveEventPrizeTierRequest[]): boolean {
  for (let i = 0; i < tiers.length; i++) {
    for (let j = i + 1; j < tiers.length; j++) {
      if (tiers[i].fromRank <= tiers[j].toRank && tiers[j].fromRank <= tiers[i].toRank) return true
    }
  }

  return false
}

function shortLang(langId: string): string {
  return langId.slice(0, 4)
}

function toLocalInput(iso: string): string {
  if (!iso) return ''

  const date = new Date(iso)
  const offset = date.getTimezoneOffset() * 60_000

  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

function fromLocalInput(value: string): string | null {
  if (!value) return null

  return new Date(value).toISOString()
}
