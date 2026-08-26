import { motion } from 'motion/react'
import { useEffect, useMemo, useState } from 'react'
import {
  Activity,
  Coins,
  Gauge,
  History,
  Package,
  RefreshCw,
  Rocket,
  Search,
  ShieldAlert,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Def, DefList, Note, PageTitle, SearchBox } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { useResource } from '../lib/resource'
import { asList } from '../lib/resource'
import { formatRelative } from '../lib/time'
import { listVariants } from '../components/ui/motion'
import {
  compact,
  duration,
  sourceTone,
  useUserAnalytics,
  useUserTimeline,
} from '../features/analytics/data'
import type { AdminUserListItemDto, TimelineSourceKind } from '../types/api'

// ===========================================================================
// User 360 — the trace
//
// Everything that ever happened to one child, from every table that recorded
// any of it, in one order.
//
// The critical property is that this is a READ ACROSS THE AUTHORITATIVE TABLES,
// not a copy of them. A grant appears here because it is in the currency
// ledger; a settlement appears because it is in the run table; a screen appears
// because it is in the event stream. Telemetry never re-records a grant, which
// is why the amounts on this page can be trusted against the wallet.
//
// The source filter matters more than it looks: an economy question drowns in
// behavioural noise, and a behaviour question does not want the ledger. One
// click narrows it either way.
// ===========================================================================

const SOURCES: { kind: TimelineSourceKind; label: string }[] = [
  { kind: 'Telemetry', label: 'Behaviour' },
  { kind: 'CurrencyLedger', label: 'Currency' },
  { kind: 'Reward', label: 'Rewards' },
  { kind: 'Purchase', label: 'Purchases' },
  { kind: 'Entitlement', label: 'Items' },
  { kind: 'Run', label: 'Runs' },
  { kind: 'Attempt', label: 'Attempts' },
]

export function UserTrace() {
  const [search, setSearch] = useState('')
  const [userId, setUserId] = useState<string | null>(null)
  const [sources, setSources] = useState<TimelineSourceKind[]>([])

  // Only searches once there is something to search for. A blank query would
  // fetch the first page of every account on the platform to populate a
  // dropdown nobody opened.
  const results = useResource<AdminUserListItemDto[]>(
    search.trim().length >= 2 ? `/api/admin/users?search=${encodeURIComponent(search)}&take=10` : null,
    [],
    (raw) => asList<AdminUserListItemDto>(raw),
  )

  const profile = useUserAnalytics(userId)
  const timeline = useUserTimeline(userId, sources)

  useEffect(() => {
    timeline.reset()
    if (userId) void timeline.load()
    // `timeline` is rebuilt every render; depending on it would loop forever.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId, sources])

  const data = profile.data

  const toggleSource = (kind: TimelineSourceKind) =>
    setSources((current) =>
      current.includes(kind) ? current.filter((s) => s !== kind) : [...current, kind],
    )

  const grouped = useMemo(() => groupByDay(timeline.entries), [timeline.entries])

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<History size={20} />}
        title="User trace"
        subtitle="Every event, grant, reward, purchase, run and attempt for one account."
        actions={
          userId ? (
            <IconButton
              label="Refresh"
              busy={timeline.loading}
              onClick={() => {
                timeline.reset()
                void timeline.load()
                void profile.reload()
              }}
            >
              <RefreshCw size={16} />
            </IconButton>
          ) : null
        }
      />

      <Card>
        <CardHeader icon={<Search size={16} />} title="Find an account" />
        <CardBody>
          <SearchBox
            value={search}
            onChange={setSearch}
            placeholder="Username or email — at least two characters"
          />

          {results.data.length > 0 ? (
            <div className="s7-trace-results">
              {results.data.map((user) => (
                <button
                  key={user.userId}
                  type="button"
                  className={`s7-chip ${userId === user.userId ? 's7-chip-on' : ''}`}
                  onClick={() => setUserId(user.userId)}
                >
                  {user.userName ?? user.userId.slice(0, 8)}
                </button>
              ))}
            </div>
          ) : null}
        </CardBody>
      </Card>

      {!userId ? (
        <Note>Pick an account to see its history.</Note>
      ) : !data ? (
        <Note>Loading the account…</Note>
      ) : (
        <>
          <StatRow>
            <Stat
              icon={<Activity size={15} />}
              label="Active days"
              value={data.activeDays}
              sub={
                data.cohortDayUtc
                  ? `joined ${data.cohortDayUtc.slice(0, 10)} · day ${data.dayIndex ?? 0}`
                  : 'never reported telemetry'
              }
              tone="brand"
            />
            <Stat
              icon={<Gauge size={15} />}
              label="Play time"
              value={duration(data.totalPlaySeconds)}
              sub={`${data.totalSessions.toLocaleString()} session(s)`}
              tone="cool"
            />
            <Stat
              icon={<Rocket size={15} />}
              label="Runs"
              value={data.runCount}
              sub={
                data.flaggedRunCount > 0
                  ? `${data.flaggedRunCount} flagged for review`
                  : 'none flagged'
              }
              tone={data.flaggedRunCount > 0 ? 'warning' : 'success'}
            />
            <Stat
              icon={<Package size={15} />}
              label="Owned"
              value={data.entitlementCount}
              sub={`${data.purchaseCount} purchase attempt(s)`}
              tone="info"
            />
          </StatRow>

          <Card>
            <CardHeader
              icon={<Coins size={16} />}
              title="Wallet"
              actions={<CopyId id={data.userId} label="User id" />}
            />
            <CardBody>
              <DefList>
                {data.balances.map((balance) => {
                  const flow = data.currencyFlow.find((f) => f.currencyId === balance.currencyId)

                  return (
                    <Def key={balance.currencyId} label={balance.code}>
                      <strong>{balance.balance.toLocaleString()}</strong>
                      {flow ? (
                        <span className="s7-muted">
                          {' '}
                          · earned {compact(flow.earned)}, spent {compact(flow.spent)}
                        </span>
                      ) : null}
                    </Def>
                  )
                })}

                <Def label="Install">
                  {data.installAppVersion || '—'} on {data.installPlatform || '—'}
                </Def>
                <Def label="Last seen">
                  {data.lastSeenAtUtc ? formatRelative(data.lastSeenAtUtc) : '—'}{' '}
                  <span className="s7-muted">
                    ({data.lastAppVersion || '—'} on {data.lastPlatform || '—'})
                  </span>
                </Def>
              </DefList>

              <p className="s7-funnel-note">
                Balances come from the wallet and the flow from the currency ledger — never from
                telemetry, which records the context around a grant and deliberately not the grant.
              </p>
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              icon={<History size={16} />}
              title="Timeline"
              actions={
                <div className="s7-inline-badges">
                  {SOURCES.map((source) => (
                    <button
                      key={source.kind}
                      type="button"
                      className={`s7-chip ${sources.includes(source.kind) ? 's7-chip-on' : ''}`}
                      onClick={() => toggleSource(source.kind)}
                    >
                      {source.label}
                    </button>
                  ))}
                </div>
              }
            />
            <CardBody>
              {timeline.entries.length === 0 ? (
                <div className="s7-chart-empty">
                  {timeline.loading ? 'Reading…' : 'Nothing recorded for this account yet.'}
                </div>
              ) : (
                <div className="s7-timeline">
                  {grouped.map(([day, entries]) => (
                    <div key={day} className="s7-timeline-day">
                      <h4>{day}</h4>

                      {entries.map((entry) => (
                        <div
                          key={`${entry.source}-${entry.refId}-${entry.atUtc}`}
                          className={`s7-timeline-row s7-tone-${sourceTone(entry.source)}`}
                        >
                          <time>{entry.atUtc.slice(11, 19)}</time>

                          <Badge tone="muted">{entry.source}</Badge>

                          <div className="s7-timeline-body">
                            <strong>{entry.summary}</strong>

                            {Object.keys(entry.data).length > 0 ? (
                              <details>
                                <summary>details</summary>
                                <dl>
                                  {Object.entries(entry.data)
                                    .filter(([, value]) => value !== '')
                                    .map(([key, value]) => (
                                      <div key={key}>
                                        <dt>{key}</dt>
                                        <dd className="s7-mono">{value}</dd>
                                      </div>
                                    ))}
                                </dl>
                              </details>
                            ) : null}
                          </div>

                          {entry.amount !== null ? (
                            <span
                              className={`s7-timeline-amount ${
                                entry.amount >= 0 ? 's7-pos' : 's7-neg'
                              }`}
                            >
                              {entry.amount >= 0 ? '+' : ''}
                              {entry.amount.toLocaleString()} {entry.currencyCode}
                              {entry.balanceAfter !== null ? (
                                <em> → {entry.balanceAfter.toLocaleString()}</em>
                              ) : null}
                            </span>
                          ) : null}
                        </div>
                      ))}
                    </div>
                  ))}

                  {timeline.nextBefore ? (
                    <Button
                      variant="ghost"
                      loading={timeline.loading}
                      onClick={() => void timeline.load(timeline.nextBefore)}
                    >
                      Load more
                    </Button>
                  ) : (
                    <p className="s7-funnel-note">
                      Reached the beginning of the recorded history. Raw events past their retention
                      window are swept — the rollups above them are kept for the life of the
                      platform.
                    </p>
                  )}
                </div>
              )}
            </CardBody>
          </Card>

          {data.flaggedRunCount > 0 ? (
            <Note tone="warning">
              <ShieldAlert size={14} /> {data.flaggedRunCount} of this account's runs were flagged
              and capped rather than refused. A flag is not proof of cheating — a bad device clock
              or a dropped session trips the same bounds — so the run was paid and recorded for a
              human to look at.
            </Note>
          ) : null}
        </>
      )}
    </motion.div>
  )
}

/**
 * Groups entries by UTC day, preserving the newest-first order.
 *
 * By day rather than a flat list because a trace is read by scrolling to
 * "the Tuesday it went wrong", and a thousand undifferentiated rows makes that
 * a hunt.
 */
function groupByDay<T extends { atUtc: string }>(entries: T[]): [string, T[]][] {
  const groups = new Map<string, T[]>()

  for (const entry of entries) {
    const day = entry.atUtc.slice(0, 10)
    const bucket = groups.get(day)

    if (bucket) bucket.push(entry)
    else groups.set(day, [entry])
  }

  return [...groups.entries()]
}
