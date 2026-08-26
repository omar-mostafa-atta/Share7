import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { DoorClosed, Radio, RefreshCw, Users2, Wifi } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Dot, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Modal } from '../components/ui/Modal'
import { api } from '../lib/client'
import { useResource } from '../lib/resource'
import { formatDateTime, formatRelative, parseUtc } from '../lib/time'
import { toast } from '../store/toast'
import { useGames } from '../features/games/data'
import { listVariants } from '../components/ui/motion'
import type { MultiplayerAdminSessionsDto, MultiplayerSessionSummaryDto } from '../types/api'

// ===========================================================================
// Multiplayer
//
// Live session inspection, and the ability to close one that has stopped
// responding.
//
// "Live" is the set of states that are still running rather than `!= Closed`:
// Closing and Ending are teardown, and counting them as live overstates load.
// The same list exists on the server in AdminOverviewService — they have to
// agree, and this comment is the only thing tying them together.
// ===========================================================================

const LIVE_STATES = ['Creating', 'Created', 'Starting', 'Running']

/**
 * How long a session may go without a heartbeat before it is probably dead.
 *
 * Not a server rule — nothing enforces it — but a session whose host vanished
 * sits in Running forever, and that is exactly what an operator is looking for
 * when they open this page.
 */
const STALE_AFTER_MS = 2 * 60 * 1000

type Scope = 'live' | 'all'

export function Multiplayer() {
  const [scope, setScope] = useState<Scope>('live')
  const [search, setSearch] = useState('')
  const [closing, setClosing] = useState<MultiplayerSessionSummaryDto | null>(null)

  const { games } = useGames()

  const { data, loading, refreshing, reload, set } = useResource<MultiplayerAdminSessionsDto>(
    '/api/admin/multiplayer/sessions?limit=200',
    { sessions: [], totalMatching: 0, serverTimeUtc: '' },
  )

  const gameKey = useMemo(() => {
    const map = new Map(games.map((g) => [g.gameId, g.gameKey]))
    return (id: string) => map.get(id) ?? id.slice(0, 8)
  }, [games])

  const sessions = data.sessions ?? []
  const live = sessions.filter((s) => LIVE_STATES.includes(s.state))

  // Measured against the SERVER's clock, not the browser's. The two disagree
  // often enough that a locally-computed staleness marks healthy sessions dead
  // on a machine whose time is off by a few minutes.
  const serverNow = parseUtc(data.serverTimeUtc)?.getTime() ?? Date.now()

  const stale = live.filter((s) => {
    const beat = parseUtc(s.lastHeartbeatAtUtc)?.getTime()
    return beat != null && serverNow - beat > STALE_AFTER_MS
  })

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()

    return sessions.filter((s) => {
      if (scope === 'live' && !LIVE_STATES.includes(s.state)) return false
      if (!term) return true

      return [s.transportSessionName, s.joinCode ?? '', s.state, gameKey(s.gameId), s.transportRegion ?? '']
        .join(' ')
        .toLowerCase()
        .includes(term)
    })
  }, [sessions, scope, search, gameKey])

  const columns = useMemo<Column<MultiplayerSessionSummaryDto>[]>(
    () => [
      {
        key: 'state',
        header: 'State',
        sort: (s) => s.state,
        render: (s) => {
          const isLive = LIVE_STATES.includes(s.state)
          const beat = parseUtc(s.lastHeartbeatAtUtc)?.getTime()
          const isStale = isLive && beat != null && serverNow - beat > STALE_AFTER_MS

          return (
            <span className="s7-inline">
              <Dot live={isLive && !isStale} title={s.state} />
              <span>{s.state}</span>
              {isStale ? <Badge tone="danger">stale</Badge> : null}
            </span>
          )
        },
      },
      {
        key: 'game',
        header: 'Game',
        sort: (s) => gameKey(s.gameId),
        render: (s) => (
          <div>
            <code className="s7-key">{gameKey(s.gameId)}</code>
            {s.isRanked ? <Badge tone="brand">ranked</Badge> : null}
          </div>
        ),
      },
      {
        key: 'players',
        header: 'Players',
        numeric: true,
        sort: (s) => s.currentPlayerCount,
        render: (s) => (
          <span className="s7-inline" style={{ justifyContent: 'flex-end' }}>
            <Users2 size={13} className="s7-muted" />
            {s.currentPlayerCount} / {s.maxPlayers}
          </span>
        ),
      },
      {
        key: 'room',
        header: 'Room',
        sort: (s) => s.transportSessionName,
        render: (s) => (
          <div>
            <span className="s7-mono">{s.transportSessionName}</span>
            <div className="s7-muted" style={{ fontSize: '0.72rem' }}>
              {s.transportRegion ?? 'no region'}
              {s.joinCode ? ` · code ${s.joinCode}` : ''}
            </div>
          </div>
        ),
      },
      {
        key: 'visibility',
        header: 'Visibility',
        sort: (s) => s.visibility,
        render: (s) => <Badge tone="muted">{s.visibility}</Badge>,
      },
      {
        key: 'heartbeat',
        header: 'Last heartbeat',
        sort: (s) => s.lastHeartbeatAtUtc,
        render: (s) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {formatRelative(s.lastHeartbeatAtUtc)}
          </span>
        ),
      },
      {
        key: 'created',
        header: 'Created',
        sort: (s) => s.createdAtUtc,
        render: (s) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {formatDateTime(s.createdAtUtc)}
          </span>
        ),
      },
      { key: 'id', header: 'Session', render: (s) => <CopyId id={s.id} label="sessionId" /> },
      {
        key: 'actions',
        header: '',
        render: (s) =>
          LIVE_STATES.includes(s.state) ? (
            <Button
              variant="ghost"
              onClick={(e) => {
                e.stopPropagation()
                setClosing(s)
              }}
            >
              <DoorClosed size={14} /> Close
            </Button>
          ) : (
            <span className="s7-muted" style={{ fontSize: '0.75rem' }}>
              {s.closedReason ?? '—'}
            </span>
          ),
      },
    ],
    [gameKey, serverNow],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Radio size={22} />}
        title="Multiplayer"
        subtitle="Sessions the platform believes are running. Closing one ends it for everyone still connected."
      />

      <StatRow>
        <Stat icon={<Wifi size={13} />} label="Live now" value={live.length} sub="Creating, created, starting or running" tone="cool" />
        <Stat
          icon={<Users2 size={13} />}
          label="Players in session"
          value={live.reduce((sum, s) => sum + s.currentPlayerCount, 0)}
          sub="Across live sessions"
          tone="brand"
        />
        <Stat
          icon={<Radio size={13} />}
          label="Stale"
          value={stale.length}
          sub={`No heartbeat for ${STALE_AFTER_MS / 60000} minutes`}
          tone={stale.length ? 'danger' : 'success'}
        />
      </StatRow>

      {stale.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="warning">
            <strong>{stale.length}</strong> session{stale.length === 1 ? '' : 's'} still marked live
            but silent for over {STALE_AFTER_MS / 60000} minutes. The host has most likely
            disappeared; closing them frees the room.
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<Radio size={16} />}
          title={`${rows.length} of ${data.totalMatching || sessions.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search room, code or game…" />
            <Segmented
              layoutId="mp-scope"
              value={scope}
              onChange={setScope}
              options={[
                { value: 'live', label: 'Live' },
                { value: 'all', label: 'All' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(s) => s.id}
            loading={loading}
            initialSort={{ key: 'created', direction: 'desc' }}
            empty={scope === 'live' ? 'No sessions are running right now.' : 'No sessions recorded.'}
          />
        </CardBody>
      </Card>

      <Modal
        open={!!closing}
        onClose={() => setClosing(null)}
        icon={<DoorClosed size={18} />}
        title="Close this session?"
        footer={
          <>
            <Button variant="ghost" onClick={() => setClosing(null)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                if (!closing) return

                await api.post(`/api/admin/multiplayer/sessions/${closing.id}/close`)
                toast.success('Session closed', `${closing.transportSessionName} was ended.`)

                // Patch rather than reload: the list is long and a reload would
                // scroll the operator back to the top mid-triage.
                set((current) => ({
                  ...current,
                  sessions: current.sessions.map((s) =>
                    s.id === closing.id ? { ...s, state: 'Closed', closedReason: 'AdminClosed' } : s,
                  ),
                }))

                setClosing(null)
              }}
            >
              Close session
            </Button>
          </>
        }
      >
        <Note tone="danger">
          {closing?.currentPlayerCount
            ? `${closing.currentPlayerCount} player${closing.currentPlayerCount === 1 ? ' is' : 's are'} still connected and will be dropped.`
            : 'No players are connected.'}{' '}
          Any run in progress inside this session settles as abandoned.
        </Note>
      </Modal>
    </motion.div>
  )
}
