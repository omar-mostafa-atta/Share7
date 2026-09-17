import { useEffect, useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  Coins,
  Gamepad2,
  Layers,
  Lock,
  Mountain,
  Plus,
  RefreshCw,
  Star,
  Trash2,
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
  blankMode,
  blankWorld,
  modeToRequest,
  useEconomyProfiles,
  useGameModes,
  useGameWorlds,
  worldToRequest,
} from '../features/play/data'
import { KEY_PATTERN, textFor } from '../lib/format'
import { listVariants } from '../components/ui/motion'
import type {
  EconomyProfileDto,
  GameModeAdminDto,
  GameWorldAdminDto,
  SaveGameModeRequest,
  SaveGameWorldRequest,
} from '../types/api'

// ===========================================================================
// Modes & Worlds
//
// The two axes a game ships content for and the platform owns the policy of.
//
//   A MODE is a rule-set — Classic, Sudden Death, Practice. The client carries
//   the rules a match is actually played by; this catalogue decides whether the
//   mode is offered, to whom, and what a session of it may be worth. That split
//   is the whole point: withdrawing a broken mode is an UPDATE here, not a store
//   release, and a client built three months ago stops offering it on its next
//   catalogue read.
//
//   A WORLD is where a match is set. It changes nothing about difficulty or
//   payout — a world that did would need its own leaderboard — so all this table
//   decides is how a player comes to own one.
//
// Payouts sit beside them because both point at one: the profile is what scales
// whatever a session earned.
// ===========================================================================

type Tab = 'modes' | 'worlds' | 'payouts'

export function PlayModes() {
  const { games, loading: gamesLoading } = useGames()
  const [gameId, setGameId] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('modes')

  // Land on the first game rather than an empty page. The console is nearly
  // always looking at one game, and asking the admin to pick before showing them
  // anything is a click that answers itself.
  useEffect(() => {
    if (!gameId && games.length) setGameId(games[0].gameId)
  }, [games, gameId])

  const selectedGame = games.find((g) => g.gameId === gameId) ?? null

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Layers size={22} />}
        title="Modes & Worlds"
        subtitle="A mode is a rule-set and a world is a setting. The client ships both; this decides which are offered, to whom, and what a session of them is worth."
      />

      <Card>
        <CardHeader
          icon={<Gamepad2 size={16} />}
          title="Game"
          actions={
            <Segmented
              layoutId="play-tab"
              value={tab}
              onChange={setTab}
              options={[
                { value: 'modes', label: 'Modes' },
                { value: 'worlds', label: 'Worlds' },
                { value: 'payouts', label: 'Payouts' },
              ]}
            />
          }
        />
        <CardBody>
          {gamesLoading ? (
            <span className="s7-muted">Loading games…</span>
          ) : games.length === 0 ? (
            <Note tone="warning">
              No games exist yet. A mode and a world both belong to one, so the catalogue starts there.
            </Note>
          ) : (
            <Select value={gameId ?? ''} onChange={(e) => setGameId(e.target.value)}>
              {games.map((game) => (
                <option key={game.gameId} value={game.gameId}>
                  {game.displayName || game.gameKey}
                </option>
              ))}
            </Select>
          )}
        </CardBody>
      </Card>

      {tab === 'modes' && gameId ? <ModesPanel gameId={gameId} gameKey={selectedGame?.gameKey ?? ''} /> : null}
      {tab === 'worlds' && gameId ? <WorldsPanel gameId={gameId} gameKey={selectedGame?.gameKey ?? ''} /> : null}
      {tab === 'payouts' ? <PayoutsPanel /> : null}
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Modes
// ---------------------------------------------------------------------------

function ModesPanel({ gameId, gameKey }: { gameId: string; gameKey: string }) {
  const { modes, loading, refreshing, reload, create, update, remove } = useGameModes(gameId)
  const { profiles } = useEconomyProfiles()
  const selectedLangId = useLangId()

  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<GameModeAdminDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<GameModeAdminDto | null>(null)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return modes

    return modes.filter((m) =>
      [m.modeKey, ...m.translations.map((t) => t.name)].join(' ').toLowerCase().includes(term),
    )
  }, [modes, search])

  const columns = useMemo<Column<GameModeAdminDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Mode',
        sort: (m) => m.modeKey,
        render: (m) => (
          <div>
            <div style={{ fontWeight: 600 }}>
              {textFor(m.translations.map((t) => ({ langId: t.langId, name: t.name })), selectedLangId) ||
                m.modeKey}
              {m.isDefault ? (
                <span style={{ marginInlineStart: '0.4rem' }}>
                  <Badge tone="brand">Default</Badge>
                </span>
              ) : null}
            </div>
            <code className="s7-key">{m.modeKey}</code>
          </div>
        ),
      },
      {
        key: 'topologies',
        header: 'Played',
        render: (m) => (
          <span className="s7-inline">
            {m.topologies.map((t) => (
              <Badge key={t} tone={t === 'solo' ? 'info' : 'brand'}>
                {t}
              </Badge>
            ))}
          </span>
        ),
      },
      {
        key: 'accounting',
        header: 'Counts for',
        render: (m) => (
          <span className="s7-inline">
            {m.countsTowardMastery ? <Badge tone="success">Mastery</Badge> : null}
            {m.settlesEconomy ? <Badge tone="success">Payout</Badge> : null}
            {m.countsTowardRanking ? <Badge tone="success">Ranking</Badge> : null}
            {!m.countsTowardMastery && !m.settlesEconomy && !m.countsTowardRanking ? (
              <Badge tone="muted">Nothing</Badge>
            ) : null}
          </span>
        ),
      },
      {
        key: 'payout',
        header: 'Payout',
        render: (m) => <code className="s7-key">{m.economyProfileKey}</code>,
      },
      {
        key: 'gates',
        header: 'Gates',
        render: (m) => (
          <span className="s7-inline">
            {m.requiresEntitlement ? (
              <Badge tone="warning">
                <Lock size={11} /> {m.entitlementSku ?? 'sold'}
              </Badge>
            ) : null}
            {m.minGradeOrder > 0 ? <Badge tone="muted">Grade ≥ {m.minGradeOrder}</Badge> : null}
            {m.availableFromUtc || m.availableToUtc ? <Badge tone="info">Windowed</Badge> : null}
            {!m.requiresEntitlement && !m.minGradeOrder && !m.availableFromUtc && !m.availableToUtc ? (
              <span className="s7-muted">open</span>
            ) : null}
          </span>
        ),
      },
      {
        key: 'runs',
        header: 'Runs',
        numeric: true,
        sort: (m) => m.runCount,
        render: (m) => <span className="s7-muted">{m.runCount.toLocaleString()}</span>,
      },
      {
        key: 'state',
        header: 'State',
        sort: (m) => m.isActive,
        render: (m) => (m.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="muted">Off</Badge>),
      },
      { key: 'id', header: 'Id', render: (m) => <CopyId id={m.modeId} label="modeId" /> },
    ],
    [selectedLangId],
  )

  const hasDefault = modes.some((m) => m.isDefault)

  return (
    <>
      <Card>
        <CardHeader
          icon={<Layers size={16} />}
          title={`${rows.length} mode${rows.length === 1 ? '' : 's'}`}
          actions={
            <span className="s7-inline">
              <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
                <RefreshCw size={15} />
              </IconButton>
              <Button onClick={() => setCreating(true)}>
                <Plus size={15} /> New mode
              </Button>
            </span>
          }
        />
        <CardBody>
          {!loading && modes.length > 0 && !hasDefault ? (
            <Note tone="danger">
              This game has no default mode. A client that sends no mode key — which is every build
              older than modes — cannot start a session at all. Make one of these the default.
            </Note>
          ) : null}

          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search by key or name…" />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(m) => m.modeId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.modeId ?? null}
            pageResetKey={`${gameId}|${search}`}
            empty={
              modes.length
                ? 'No mode matches that filter.'
                : 'No modes yet. Every game needs at least one, and one of them must be the default.'
            }
          />
        </CardBody>
      </Card>

      <ModeEditor
        key={editing?.modeId ?? (creating ? 'new' : 'closed')}
        mode={editing}
        gameId={gameId}
        gameKey={gameKey}
        profiles={profiles}
        open={!!editing || creating}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.modeId, request)
          else await create(request)
          setEditing(null)
          setCreating(false)
        }}
        onDelete={editing ? () => setConfirmDelete(editing) : undefined}
      />

      <Modal
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        icon={<Trash2 size={18} />}
        title={`Delete "${confirmDelete?.modeKey}"?`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                if (!confirmDelete) return
                await remove(confirmDelete, (confirmDelete.runCount ?? 0) > 0)
                setConfirmDelete(null)
                setEditing(null)
              }}
            >
              Delete mode
            </Button>
          </>
        }
      >
        <Note tone="danger">
          {confirmDelete && confirmDelete.runCount > 0 ? (
            <>
              {confirmDelete.runCount.toLocaleString()} run(s) were played in this mode. Deleting it
              detaches them from their rules — their leaderboard entries and payouts stay, but nothing
              can say what they were played under any more. Withdrawing it with <b>Active off</b>{' '}
              hides it from clients and keeps the history readable.
            </>
          ) : (
            <>
              Nothing has been played in this mode yet, so nothing is lost. If it has been advertised
              to clients, withdrawing it with <b>Active off</b> is still the gentler move.
            </>
          )}
        </Note>
      </Modal>
    </>
  )
}

function ModeEditor({
  mode,
  gameId,
  gameKey,
  profiles,
  open,
  onClose,
  onSave,
  onDelete,
}: {
  mode: GameModeAdminDto | null
  gameId: string
  gameKey: string
  profiles: EconomyProfileDto[]
  open: boolean
  onClose: () => void
  onSave: (request: SaveGameModeRequest) => Promise<void>
  onDelete?: () => void
}) {
  const [form, setForm] = useState<SaveGameModeRequest>(() =>
    mode ? modeToRequest(mode) : blankMode(gameId),
  )
  const [saving, setSaving] = useState(false)

  const isNew = !mode

  function patch(next: Partial<SaveGameModeRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  function toggleTopology(token: string, on: boolean) {
    const next = on
      ? [...new Set([...form.topologies, token])]
      : form.topologies.filter((t) => t !== token)

    patch({ topologies: next })
  }

  const keyError =
    isNew && form.modeKey && !/^[a-z0-9]+(?:[._-][a-z0-9]+)*$/.test(form.modeKey)
      ? 'Lowercase letters, digits and dots — e.g. runner.mode.classic.'
      : null

  const topologyError = form.topologies.length === 0 ? 'A mode has to be playable somewhere.' : null

  const playersError =
    form.minPlayers > form.maxPlayers ? 'Minimum players cannot exceed the maximum.' : null

  const entitlementError =
    form.requiresEntitlement && !form.entitlementProductId
      ? 'A sold mode needs the product it is sold under.'
      : null

  // The compatibility path has to stay unconditional: a client that predates modes sends no key and
  // resolves to the default, so a default behind a window or a price refuses exactly the builds it
  // exists to serve.
  const defaultError = form.isDefault
    ? !form.isActive
      ? 'A default mode must be active.'
      : form.availableFromUtc || form.availableToUtc
        ? 'A default mode cannot have an availability window.'
        : form.requiresEntitlement
          ? 'A default mode cannot require an entitlement.'
          : form.minGradeOrder > 0
            ? 'A default mode cannot be grade gated.'
            : null
    : null

  const blocked =
    !!keyError ||
    !!topologyError ||
    !!playersError ||
    !!entitlementError ||
    !!defaultError ||
    !form.modeKey.trim()

  const translationRows: TranslationRow[] = form.translations.map((t) => ({
    langId: t.langId,
    name: t.name,
    description: t.description,
  }))

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New mode' : form.modeKey}
      subtitle={
        isNew
          ? `The key is permanent and must match the Unity GameModeDefinition exactly.`
          : 'Key and game are fixed. Policy, windows, prices and text all move freely.'
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
            {isNew ? 'Create mode' : 'Save changes'}
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
        <Field
          label="Mode key"
          error={keyError}
          hint={
            isNew
              ? `Must equal the Unity definition's modeId — that equality is the whole join between the two catalogues.`
              : 'Cannot be changed. Runs, results and events all point at it.'
          }
        >
          <Input
            mono
            value={form.modeKey}
            disabled={!isNew}
            onChange={(e) => patch({ modeKey: e.target.value.toLowerCase().replace(/[^a-z0-9._-]/g, '') })}
            placeholder={`${gameKey.replace(/^game\./, '')}.mode.classic`}
          />
        </Field>

        <Field label="Played as" error={topologyError} hint="Co-op is reserved and refused until a game defines it.">
          <div className="s7-stack" style={{ gap: '0.5rem' }}>
            <Switch
              checked={form.topologies.includes('solo')}
              onChange={(v) => toggleTopology('solo', v)}
              label="Solo"
            />
            <Switch
              checked={form.topologies.includes('versus')}
              onChange={(v) => toggleTopology('versus', v)}
              label="Versus"
            />
          </div>
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Minimum players" error={playersError}>
            <Input
              type="number"
              min={1}
              value={form.minPlayers}
              onChange={(e) => patch({ minPlayers: Number(e.target.value) || 1 })}
            />
          </Field>
          <Field label="Maximum players">
            <Input
              type="number"
              min={1}
              value={form.maxPlayers}
              onChange={(e) => patch({ maxPlayers: Number(e.target.value) || 1 })}
            />
          </Field>
        </div>

        <Field
          label="Counts toward"
          hint="A ceiling, not a grant: the context of each session narrows it further. Practice is always worth nothing."
        >
          <div className="s7-stack" style={{ gap: '0.5rem' }}>
            <Switch
              checked={form.countsTowardMastery}
              onChange={(v) => patch({ countsTowardMastery: v })}
              label="Mastery — completion, unlocks, the progress a parent sees"
            />
            <Switch
              checked={form.settlesEconomy}
              onChange={(v) => patch({ settlesEconomy: v })}
              label="Payout — coins and XP"
            />
            <Switch
              checked={form.countsTowardRanking}
              onChange={(v) => patch({ countsTowardRanking: v })}
              label="Ranking — results reach leaderboards"
            />
          </div>
        </Field>

        <Field
          label="Payout profile"
          hint="How much of what a session earns is actually paid. Blank uses the platform default."
        >
          <Select
            value={form.economyProfileId ?? ''}
            onChange={(e) => patch({ economyProfileId: e.target.value || null })}
          >
            <option value="">Platform default</option>
            {profiles.map((p) => (
              <option key={p.profileId} value={p.profileId}>
                {p.profileKey} — {p.payoutPercent}%{p.paysRuleRewards ? '' : ', no bonuses'}
              </option>
            ))}
          </Select>
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Opens (UTC)" hint="Blank means already open.">
            <Input
              type="datetime-local"
              value={toLocalInput(form.availableFromUtc)}
              onChange={(e) => patch({ availableFromUtc: fromLocalInput(e.target.value) })}
            />
          </Field>
          <Field label="Closes (UTC)" hint="Blank means never closes.">
            <Input
              type="datetime-local"
              value={toLocalInput(form.availableToUtc)}
              onChange={(e) => patch({ availableToUtc: fromLocalInput(e.target.value) })}
            />
          </Field>
        </div>

        <Field label="Sold" error={entitlementError}>
          <div className="s7-stack" style={{ gap: '0.5rem' }}>
            <Switch
              checked={form.requiresEntitlement}
              onChange={(v) => patch({ requiresEntitlement: v, entitlementProductId: v ? form.entitlementProductId : null })}
              label="Requires an entitlement"
            />
            {form.requiresEntitlement ? (
              <Input
                mono
                value={form.entitlementProductId ?? ''}
                onChange={(e) => patch({ entitlementProductId: e.target.value || null })}
                placeholder="product id (from Shop)"
              />
            ) : null}
          </div>
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Minimum grade order" hint="0 is ungated.">
            <Input
              type="number"
              min={0}
              value={form.minGradeOrder}
              onChange={(e) => patch({ minGradeOrder: Number(e.target.value) || 0 })}
            />
          </Field>
          <Field label="Sort order" hint="Lower appears first in the picker.">
            <Input
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(e) => patch({ sortOrder: Number(e.target.value) || 0 })}
            />
          </Field>
        </div>

        <Field label="Availability">
          <Switch
            checked={form.isActive}
            onChange={(v) => patch({ isActive: v })}
            label={form.isActive ? 'Active — offered to clients' : 'Withdrawn — hidden, and refuses new sessions'}
          />
        </Field>

        <Field
          label="Default mode"
          error={defaultError}
          hint="What a client that sends no mode key plays. Exactly one per game."
        >
          <Switch
            checked={form.isDefault}
            onChange={(v) => patch({ isDefault: v })}
            label={form.isDefault ? 'This is the default' : 'Not the default'}
          />
        </Field>

        <div>
          <h3 className="s7-subhead">Names and descriptions</h3>
          <p className="s7-hint" style={{ marginBottom: '0.6rem' }}>
            Shown in the mode picker, in the player's own language. There is no fallback, so a
            language left empty shows nothing to those players.
          </p>
          <TranslationsEditor
            withDescription
            value={translationRows}
            onChange={(rows) =>
              patch({
                translations: rows.map((r) => ({
                  langId: r.langId,
                  name: r.name,
                  description: r.description ?? '',
                })),
              })
            }
          />
        </div>
      </div>
    </Drawer>
  )
}

// ---------------------------------------------------------------------------
// Worlds
// ---------------------------------------------------------------------------

const UNLOCK_KINDS = [
  { value: 'free', label: 'Free — everybody has it' },
  { value: 'purchase', label: 'Bought in the shop' },
  { value: 'level', label: 'Opens at a player level' },
  { value: 'grade', label: 'Opens at a school year' },
  { value: 'reward', label: 'Won — a prize or a reward rule' },
]

function WorldsPanel({ gameId, gameKey }: { gameId: string; gameKey: string }) {
  const { worlds, loading, refreshing, reload, create, update, remove } = useGameWorlds(gameId)
  const selectedLangId = useLangId()

  const [editing, setEditing] = useState<GameWorldAdminDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<GameWorldAdminDto | null>(null)

  const columns = useMemo<Column<GameWorldAdminDto>[]>(
    () => [
      {
        key: 'name',
        header: 'World',
        sort: (w) => w.sortOrder,
        render: (w) => (
          <div>
            <div style={{ fontWeight: 600 }}>
              {textFor(w.translations.map((t) => ({ langId: t.langId, name: t.name })), selectedLangId) ||
                w.worldKey}
              {w.isDefault ? (
                <span style={{ marginInlineStart: '0.4rem' }}>
                  <Badge tone="brand">Fallback</Badge>
                </span>
              ) : null}
            </div>
            <code className="s7-key">{w.worldKey}</code>
          </div>
        ),
      },
      {
        key: 'unlock',
        header: 'How it is owned',
        render: (w) => {
          if (w.unlockKind === 'free') return <Badge tone="success">Free</Badge>
          if (w.unlockKind === 'level') return <Badge tone="info">Level {w.minLevel}</Badge>
          if (w.unlockKind === 'grade') return <Badge tone="info">Grade ≥ {w.minGradeOrder}</Badge>
          return (
            <span className="s7-inline">
              <Badge tone={w.unlockKind === 'purchase' ? 'warning' : 'brand'}>
                {w.unlockKind === 'purchase' ? 'Bought' : 'Won'}
              </Badge>
              {w.sku ? <code className="s7-key">{w.sku}</code> : <Badge tone="danger">no product</Badge>}
            </span>
          )
        },
      },
      {
        key: 'state',
        header: 'State',
        sort: (w) => w.isActive,
        render: (w) => (w.isActive ? <Badge tone="success">Offered</Badge> : <Badge tone="muted">Off</Badge>),
      },
      { key: 'id', header: 'Id', render: (w) => <CopyId id={w.worldId} label="worldId" /> },
    ],
    [selectedLangId],
  )

  return (
    <>
      <Card>
        <CardHeader
          icon={<Mountain size={16} />}
          title={`${worlds.length} world${worlds.length === 1 ? '' : 's'}`}
          actions={
            <span className="s7-inline">
              <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
                <RefreshCw size={15} />
              </IconButton>
              <Button onClick={() => setCreating(true)}>
                <Plus size={15} /> New world
              </Button>
            </span>
          }
        />
        <CardBody>
          <Note>
            A world changes where a match is set and nothing else — not its difficulty, not what it
            pays, not which board it reaches. The key here is the client's own environment id.
          </Note>

          <DataTable
            rows={worlds}
            columns={columns}
            getId={(w) => w.worldId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.worldId ?? null}
            pageResetKey={gameId}
            empty="No worlds yet. Until one exists, every session runs whatever the client picks."
          />
        </CardBody>
      </Card>

      <WorldEditor
        key={editing?.worldId ?? (creating ? 'new' : 'closed')}
        world={editing}
        gameId={gameId}
        gameKey={gameKey}
        open={!!editing || creating}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.worldId, request)
          else await create(request)
          setEditing(null)
          setCreating(false)
        }}
        onDelete={editing ? () => setConfirmDelete(editing) : undefined}
      />

      <Modal
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        icon={<Trash2 size={18} />}
        title={`Remove "${confirmDelete?.worldKey}"?`}
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
              Remove world
            </Button>
          </>
        }
      >
        <Note tone="warning">
          This removes the policy row, not the content and not anybody's ownership — entitlements
          point at products, so a child who bought this world keeps it. Turning it off instead is
          usually what is meant: the world stops being offered and everything else stays intact.
        </Note>
      </Modal>
    </>
  )
}

function WorldEditor({
  world,
  gameId,
  gameKey,
  open,
  onClose,
  onSave,
  onDelete,
}: {
  world: GameWorldAdminDto | null
  gameId: string
  gameKey: string
  open: boolean
  onClose: () => void
  onSave: (request: SaveGameWorldRequest) => Promise<void>
  onDelete?: () => void
}) {
  const [form, setForm] = useState<SaveGameWorldRequest>(() =>
    world ? worldToRequest(world) : blankWorld(gameId),
  )
  const [saving, setSaving] = useState(false)

  const isNew = !world

  function patch(next: Partial<SaveGameWorldRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  const needsProduct = form.unlockKind === 'purchase' || form.unlockKind === 'reward'

  const productError = needsProduct && !form.productId ? 'This world needs the product it is granted as.' : null
  const levelError = form.unlockKind === 'level' && form.minLevel <= 0 ? 'Set the level it opens at.' : null
  const gradeError =
    form.unlockKind === 'grade' && form.minGradeOrder <= 0 ? 'Set the grade order it opens at.' : null

  const defaultError =
    form.isDefault && form.unlockKind !== 'free'
      ? 'The fallback world must be free — it is what a session runs in when nothing was chosen.'
      : null

  const blocked =
    !!productError || !!levelError || !!gradeError || !!defaultError || !form.worldKey.trim()

  const translationRows: TranslationRow[] = form.translations.map((t) => ({
    langId: t.langId,
    name: t.name,
    description: t.description,
  }))

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New world' : form.worldKey}
      subtitle={
        isNew
          ? "The key is the client's own environment id, and permanent."
          : 'Key and game are fixed. How it is owned can change.'
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
            {isNew ? 'Add world' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          {onDelete ? (
            <Button variant="danger" onClick={onDelete} style={{ marginInlineStart: 'auto' }}>
              <Trash2 size={15} /> Remove
            </Button>
          ) : null}
        </>
      }
    >
      <div className="s7-stack">
        <Field
          label="World key"
          hint={isNew ? "Must equal the Unity EnvironmentDefinition's id." : 'Cannot be changed.'}
        >
          <Input
            mono
            value={form.worldKey}
            disabled={!isNew}
            onChange={(e) => patch({ worldKey: e.target.value.toLowerCase().replace(/[^a-z0-9._-]/g, '') })}
            placeholder={`${gameKey.replace(/^game\./, '')}.env.desert`}
          />
        </Field>

        <Field label="How it is owned">
          <Select value={form.unlockKind} onChange={(e) => patch({ unlockKind: e.target.value })}>
            {UNLOCK_KINDS.map((kind) => (
              <option key={kind.value} value={kind.value}>
                {kind.label}
              </option>
            ))}
          </Select>
        </Field>

        {needsProduct ? (
          <Field
            label="Product"
            error={productError}
            hint="The entitlement a purchase or a prize grants. Create it under Shop first."
          >
            <Input
              mono
              value={form.productId ?? ''}
              onChange={(e) => patch({ productId: e.target.value || null })}
              placeholder="product id"
            />
          </Field>
        ) : null}

        {form.unlockKind === 'level' ? (
          <Field label="Opens at level" error={levelError}>
            <Input
              type="number"
              min={1}
              value={form.minLevel}
              onChange={(e) => patch({ minLevel: Number(e.target.value) || 0 })}
            />
          </Field>
        ) : null}

        {form.unlockKind === 'grade' ? (
          <Field label="Opens at grade order" error={gradeError}>
            <Input
              type="number"
              min={1}
              value={form.minGradeOrder}
              onChange={(e) => patch({ minGradeOrder: Number(e.target.value) || 0 })}
            />
          </Field>
        ) : null}

        <div className="s7-form-grid-2">
          <Field label="Sort order" hint="Lower appears first in the picker.">
            <Input
              type="number"
              min={0}
              value={form.sortOrder}
              onChange={(e) => patch({ sortOrder: Number(e.target.value) || 0 })}
            />
          </Field>
          <Field label="Offered">
            <Switch
              checked={form.isActive}
              onChange={(v) => patch({ isActive: v })}
              label={form.isActive ? 'Offered' : 'Hidden'}
            />
          </Field>
        </div>

        <Field
          label="Fallback world"
          error={defaultError}
          hint="What a session runs in when no world was chosen or owned. Exactly one per game, and always free."
        >
          <Switch
            checked={form.isDefault}
            onChange={(v) => patch({ isDefault: v })}
            label={form.isDefault ? 'This is the fallback' : 'Not the fallback'}
          />
        </Field>

        <div>
          <h3 className="s7-subhead">Names and descriptions</h3>
          <TranslationsEditor
            withDescription
            value={translationRows}
            onChange={(rows) =>
              patch({
                translations: rows.map((r) => ({
                  langId: r.langId,
                  name: r.name,
                  description: r.description ?? '',
                })),
              })
            }
          />
        </div>
      </div>
    </Drawer>
  )
}

// ---------------------------------------------------------------------------
// Payout profiles
// ---------------------------------------------------------------------------

function PayoutsPanel() {
  const { profiles, loading, refreshing, reload, create, update, remove } = useEconomyProfiles()

  const [editing, setEditing] = useState<EconomyProfileDto | null>(null)
  const [creating, setCreating] = useState(false)

  const columns = useMemo<Column<EconomyProfileDto>[]>(
    () => [
      {
        key: 'key',
        header: 'Profile',
        sort: (p) => p.profileKey,
        render: (p) => (
          <div>
            <div style={{ fontWeight: 600 }}>
              {p.name}
              {p.isDefault ? (
                <span style={{ marginInlineStart: '0.4rem' }}>
                  <Badge tone="brand">
                    <Star size={11} /> Default
                  </Badge>
                </span>
              ) : null}
            </div>
            <code className="s7-key">{p.profileKey}</code>
          </div>
        ),
      },
      {
        key: 'percent',
        header: 'Pays',
        numeric: true,
        sort: (p) => p.payoutPercent,
        render: (p) => (
          <Badge tone={p.payoutPercent === 0 ? 'muted' : p.payoutPercent < 100 ? 'warning' : 'success'}>
            {p.payoutPercent}%
          </Badge>
        ),
      },
      {
        key: 'rules',
        header: 'Bonuses',
        render: (p) =>
          p.paysRuleRewards ? <Badge tone="success">Paid</Badge> : <Badge tone="muted">Skipped</Badge>,
      },
      {
        key: 'usage',
        header: 'Used by',
        numeric: true,
        sort: (p) => p.usedByModes + p.usedByEvents,
        render: (p) => (
          <span className="s7-muted">
            {p.usedByModes} mode{p.usedByModes === 1 ? '' : 's'}, {p.usedByEvents} event
            {p.usedByEvents === 1 ? '' : 's'}
          </span>
        ),
      },
    ],
    [],
  )

  return (
    <>
      <Card>
        <CardHeader
          icon={<Coins size={16} />}
          title={`${profiles.length} payout profile${profiles.length === 1 ? '' : 's'}`}
          actions={
            <span className="s7-inline">
              <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
                <RefreshCw size={15} />
              </IconButton>
              <Button onClick={() => setCreating(true)}>
                <Plus size={15} /> New profile
              </Button>
            </span>
          }
        />
        <CardBody>
          <Note>
            A profile scales what a session earned; it never prices anything. What a coin is worth
            stays in Signal Valuations, and the percentage is applied after every cap — so it cannot
            be used to buy back the daily ceiling.
          </Note>

          <DataTable
            rows={profiles}
            columns={columns}
            getId={(p) => p.profileId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.profileId ?? null}
            empty="No profiles yet. Seeding writes four: default, event, reduced and none."
          />
        </CardBody>
      </Card>

      <ProfileEditor
        key={editing?.profileId ?? (creating ? 'new' : 'closed')}
        profile={editing}
        open={!!editing || creating}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.profileId, request)
          else await create(request)
          setEditing(null)
          setCreating(false)
        }}
        onDelete={
          editing && !editing.isDefault && editing.usedByModes + editing.usedByEvents === 0
            ? async () => {
                await remove(editing)
                setEditing(null)
              }
            : undefined
        }
      />
    </>
  )
}

function ProfileEditor({
  profile,
  open,
  onClose,
  onSave,
  onDelete,
}: {
  profile: EconomyProfileDto | null
  open: boolean
  onClose: () => void
  onSave: (request: {
    profileKey: string
    name: string
    payoutPercent: number
    paysRuleRewards: boolean
    isDefault: boolean
  }) => Promise<void>
  onDelete?: () => Promise<void>
}) {
  const [form, setForm] = useState(() => ({
    profileKey: profile?.profileKey ?? '',
    name: profile?.name ?? '',
    payoutPercent: profile?.payoutPercent ?? 100,
    paysRuleRewards: profile?.paysRuleRewards ?? true,
    isDefault: profile?.isDefault ?? false,
  }))
  const [saving, setSaving] = useState(false)

  const isNew = !profile
  const keyError =
    isNew && form.profileKey && !KEY_PATTERN.test(form.profileKey)
      ? 'Lowercase letters, digits and underscores.'
      : null

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New payout profile' : form.profileKey}
      subtitle="Scales what a session earned, after every cap."
      footer={
        <>
          <Button
            loading={saving}
            disabled={!!keyError || !form.profileKey.trim() || !form.name.trim()}
            onClick={async () => {
              setSaving(true)
              try {
                await onSave(form)
              } finally {
                setSaving(false)
              }
            }}
          >
            {isNew ? 'Create profile' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          {onDelete ? (
            <Button variant="danger" onClick={() => void onDelete()} style={{ marginInlineStart: 'auto' }}>
              <Trash2 size={15} /> Delete
            </Button>
          ) : null}
        </>
      }
    >
      <div className="s7-stack">
        <Field label="Key" error={keyError} hint={isNew ? 'Permanent.' : 'Cannot be changed.'}>
          <Input
            mono
            value={form.profileKey}
            disabled={!isNew}
            onChange={(e) =>
              setForm((f) => ({ ...f, profileKey: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '') }))
            }
            placeholder="reduced"
          />
        </Field>

        <Field label="Name" hint="Operator-facing only. No child ever sees it.">
          <Input
            value={form.name}
            onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
            placeholder="Half payout"
          />
        </Field>

        <Field
          label="Pays"
          hint="100 pays exactly what the valuations say. 0 pays nothing at all."
        >
          <Input
            type="number"
            min={0}
            max={500}
            value={form.payoutPercent}
            onChange={(e) => setForm((f) => ({ ...f, payoutPercent: Number(e.target.value) || 0 }))}
          />
        </Field>

        <Field label="Completion bonuses" hint="Fixed reward rules — separate from the percentage, because a fraction of a fixed bonus is not what anyone means.">
          <Switch
            checked={form.paysRuleRewards}
            onChange={(v) => setForm((f) => ({ ...f, paysRuleRewards: v }))}
            label={form.paysRuleRewards ? 'Rules fire' : 'Rules are skipped'}
          />
        </Field>

        <Field label="Platform default" hint="What every mode and event that names no profile settles under.">
          <Switch
            checked={form.isDefault}
            onChange={(v) => setForm((f) => ({ ...f, isDefault: v }))}
            label={form.isDefault ? 'This is the default' : 'Not the default'}
          />
        </Field>
      </div>
    </Drawer>
  )
}

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

/** The admin's chosen content language, for picking which translation a row prints. */
function useLangId(): string {
  return useLanguages((s) => s.selectedLangId)
}

/** An ISO instant as the value a `datetime-local` input wants, in the admin's own timezone. */
function toLocalInput(iso: string | null): string {
  if (!iso) return ''

  const date = new Date(iso)
  const offset = date.getTimezoneOffset() * 60_000

  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

/** Back the other way: a local wall-clock value as the instant the API stores. */
function fromLocalInput(value: string): string | null {
  if (!value) return null

  return new Date(value).toISOString()
}
