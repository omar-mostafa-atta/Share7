import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Gamepad2, Plus, RefreshCw, Trash2, Users2 } from 'lucide-react'
import { Card, CardBody, CardHeader, Badge, Button, IconButton } from '../components/ui/primitives'
import { PageTitle, CopyId, SearchBox, Segmented, Note } from '../components/ui/bits'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Switch } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import type { TranslationRow } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import { blankGame, toRequest, useGames } from '../features/games/data'
import { KEY_PATTERN, slugify, textFor } from '../lib/format'
import { useLanguages } from '../store/languages'
import { listVariants } from '../components/ui/motion'
import type { GameAdminDto, SaveGameRequest } from '../types/api'

// ===========================================================================
// Games
//
// The mini-game catalogue. A game row is what the platform hands Unity when it
// asks what it may run, so `gameKey` is an identity other systems reference —
// objectives, boards, signal valuations and runs all point at a gameId — and
// the editor never lets it change after creation.
// ===========================================================================

type Filter = 'all' | 'active' | 'inactive'

export function Games() {
  const { games, loading, refreshing, reload, create, update, remove } = useGames()
  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [editing, setEditing] = useState<GameAdminDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<GameAdminDto | null>(null)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()

    return games.filter((game) => {
      if (filter === 'active' && !game.isActive) return false
      if (filter === 'inactive' && game.isActive) return false
      if (!term) return true

      // Searches the key and every translated name, so an Arabic title is
      // findable while the console is in English.
      const haystack = [game.gameKey, ...game.translations.map((t) => t.displayName)]
        .join(' ')
        .toLowerCase()

      return haystack.includes(term)
    })
  }, [games, search, filter])

  const columns = useMemo<Column<GameAdminDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Game',
        sort: (g) => displayName(g, selectedLangId),
        render: (g) => (
          <div>
            <div style={{ fontWeight: 600 }}>{displayName(g, selectedLangId)}</div>
            <code className="s7-key">{g.gameKey}</code>
          </div>
        ),
      },
      {
        key: 'modes',
        header: 'Modes',
        render: (g) => (
          <span className="s7-inline">
            {g.supportsSinglePlayer ? <Badge tone="info">Solo</Badge> : null}
            {g.supportsMultiplayer ? <Badge tone="brand">Multi</Badge> : null}
            {!g.supportsSinglePlayer && !g.supportsMultiplayer ? (
              // Neither flag set means no client can ever launch it. Worth
              // calling out rather than rendering an empty cell.
              <Badge tone="danger">Unplayable</Badge>
            ) : null}
          </span>
        ),
      },
      {
        key: 'players',
        header: 'Players',
        numeric: true,
        sort: (g) => g.maxPlayers,
        render: (g) => (
          <span className="s7-inline" style={{ justifyContent: 'flex-end' }}>
            <Users2 size={13} className="s7-muted" />
            {g.minPlayers === g.maxPlayers ? g.minPlayers : `${g.minPlayers}–${g.maxPlayers}`}
          </span>
        ),
      },
      {
        key: 'flow',
        header: 'Flow',
        render: (g) => (
          <span className="s7-inline">
            {g.useLobby ? <Badge tone="muted">Lobby</Badge> : null}
            {g.useMatchmaking ? <Badge tone="muted">Matchmaking</Badge> : null}
            {!g.useLobby && !g.useMatchmaking ? <span className="s7-muted">direct</span> : null}
          </span>
        ),
      },
      {
        key: 'ready',
        header: 'Ready timeout',
        numeric: true,
        sort: (g) => g.readyTimeoutSeconds,
        render: (g) => <span className="s7-muted">{g.readyTimeoutSeconds}s</span>,
      },
      {
        key: 'langs',
        header: 'Translations',
        numeric: true,
        sort: (g) => g.translations.filter((t) => t.displayName?.trim()).length,
        render: (g) => {
          const filled = g.translations.filter((t) => t.displayName?.trim()).length
          const total = languages.length || filled

          return filled >= total ? (
            <Badge tone="success">{filled}/{total}</Badge>
          ) : (
            <Badge tone="warning">{filled}/{total}</Badge>
          )
        },
      },
      {
        key: 'state',
        header: 'State',
        sort: (g) => g.isActive,
        render: (g) => (g.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="muted">Off</Badge>),
      },
      {
        key: 'id',
        header: 'Id',
        render: (g) => <CopyId id={g.gameId} label="gameId" />,
      },
    ],
    [selectedLangId, languages.length],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Gamepad2 size={22} />}
        title="Games"
        subtitle="The mini-game catalogue. A game's key is referenced by objectives, leaderboards, signal valuations and every run, so it cannot change once created."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New game
          </Button>
        }
      />

      <Card>
        <CardHeader
          icon={<Gamepad2 size={16} />}
          title={`${rows.length} of ${games.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search by key or title…" />
            <Segmented
              layoutId="games-filter"
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: 'All' },
                { value: 'active', label: 'Active' },
                { value: 'inactive', label: 'Off' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(g) => g.gameId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.gameId ?? null}
            empty={games.length ? 'No game matches that filter.' : 'No games yet. Create the first one.'}
          />
        </CardBody>
      </Card>

      <GameEditor
        key={editing?.gameId ?? (creating ? 'new' : 'closed')}
        game={editing}
        open={!!editing || creating}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.gameId, request)
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
        title={`Delete "${confirmDelete?.gameKey}"?`}
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
              Delete game
            </Button>
          </>
        }
      >
        <Note tone="danger">
          Objectives, leaderboard boards, signal valuations and runs all reference a game by id. The
          API refuses a delete that would orphan them — if this game has been played, deactivate it
          instead, which hides it from clients while leaving its history intact.
        </Note>
      </Modal>
    </motion.div>
  )
}

function displayName(game: GameAdminDto, langId: string): string {
  const rows = game.translations.map((t) => ({ langId: t.langId, name: t.displayName }))
  return textFor(rows, langId) || game.gameKey
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------

function GameEditor({
  game,
  open,
  onClose,
  onSave,
  onDelete,
}: {
  game: GameAdminDto | null
  open: boolean
  onClose: () => void
  onSave: (request: SaveGameRequest) => Promise<void>
  onDelete?: () => void
}) {
  const [form, setForm] = useState<SaveGameRequest>(() => (game ? toRequest(game) : blankGame()))
  const [saving, setSaving] = useState(false)

  const isNew = !game
  const keyError =
    isNew && form.gameKey && !KEY_PATTERN.test(form.gameKey)
      ? 'Lowercase letters, digits and underscores, starting with a letter.'
      : null

  // min > max is rejected by the API, but catching it here explains itself in
  // place rather than as a toast after a round-trip.
  const playersError =
    form.minPlayers > form.maxPlayers ? 'Minimum players cannot exceed the maximum.' : null

  const modesError =
    !form.supportsSinglePlayer && !form.supportsMultiplayer
      ? 'A game with neither mode enabled can never be launched.'
      : null

  const blocked = !!keyError || !!playersError || !!modesError || !form.gameKey.trim()

  function patch(next: Partial<SaveGameRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  // The editor stores game translations as {langId, displayName, description};
  // TranslationsEditor speaks {langId, name, description}. Mapped at the boundary
  // rather than changing either shape — the wire format is the API's, and the
  // component is shared with five other features.
  const translationRows: TranslationRow[] = form.translations.map((t) => ({
    langId: t.langId,
    name: t.displayName,
    description: t.description,
  }))

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New game' : form.gameKey}
      subtitle={
        isNew
          ? 'The key is permanent — every other system references it.'
          : 'Key is fixed. Everything else can change.'
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
            {isNew ? 'Create game' : 'Save changes'}
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
          label="Game key"
          error={keyError}
          hint={isNew ? 'Permanent. Referenced by runs, objectives and boards.' : 'Cannot be changed.'}
        >
          <Input
            mono
            value={form.gameKey}
            disabled={!isNew}
            onChange={(e) => patch({ gameKey: slugify(e.target.value) })}
            placeholder="runner"
          />
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
          label="Ready timeout (seconds)"
          hint="How long the lobby waits for everyone to confirm before giving up."
        >
          <Input
            type="number"
            min={1}
            step="0.5"
            value={form.readyTimeoutSeconds}
            onChange={(e) => patch({ readyTimeoutSeconds: Number(e.target.value) || 1 })}
          />
        </Field>

        <Field label="Modes" error={modesError}>
          <div className="s7-stack" style={{ gap: '0.5rem' }}>
            <Switch
              checked={form.supportsSinglePlayer}
              onChange={(v) => patch({ supportsSinglePlayer: v })}
              label="Single player"
            />
            <Switch
              checked={form.supportsMultiplayer}
              onChange={(v) => patch({ supportsMultiplayer: v })}
              label="Multiplayer"
            />
          </div>
        </Field>

        <Field label="Session flow" hint="Only meaningful when multiplayer is enabled.">
          <div className="s7-stack" style={{ gap: '0.5rem' }}>
            <Switch
              checked={form.useLobby}
              disabled={!form.supportsMultiplayer}
              onChange={(v) => patch({ useLobby: v })}
              label="Use a lobby"
            />
            <Switch
              checked={form.useMatchmaking}
              disabled={!form.supportsMultiplayer}
              onChange={(v) => patch({ useMatchmaking: v })}
              label="Use matchmaking"
            />
          </div>
        </Field>

        <Field label="Availability">
          <Switch
            checked={form.isActive}
            onChange={(v) => patch({ isActive: v })}
            label={form.isActive ? 'Active — offered to clients' : 'Inactive — hidden from clients'}
          />
        </Field>

        <div>
          <h3 className="s7-subhead">Titles and descriptions</h3>
          <p className="s7-hint" style={{ marginBottom: '0.6rem' }}>
            The backend serves the title in the player's own language and does not fall back, so a
            language left empty shows nothing to those players.
          </p>
          <TranslationsEditor
            withDescription
            nameLabel="Title"
            value={translationRows}
            onChange={(rows) =>
              patch({
                translations: rows.map((r) => ({
                  langId: r.langId,
                  displayName: r.name,
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
