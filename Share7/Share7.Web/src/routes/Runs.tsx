import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { AlertTriangle, CheckCircle2, Coins, Flag, RefreshCw, Rocket, ShieldCheck } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton, SkeletonRows } from '../components/ui/primitives'
import { CopyId, Def, DefList, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field } from '../components/ui/form'
import { api } from '../lib/client'
import { useResource, useResourceList } from '../lib/resource'
import { formatDateTime, formatDuration, formatRelative } from '../lib/time'
import { toast } from '../store/toast'
import { useGames } from '../features/games/data'
import { listVariants } from '../components/ui/motion'
import type { RunAdminDto } from '../types/api'

// ===========================================================================
// Runs
//
// The cheat-review queue. A run is flagged when its reported signals could not
// have been produced in the time it claims to have taken — the per-second
// ceiling on a signal valuation is what decides that — and stays flagged until
// a human rules on it.
//
// The drawer's job is to make that ruling possible, which means showing the
// arithmetic rather than a verdict: what was collected, what survived the caps,
// and what was actually paid. The gap between gross and net IS the evidence.
// ===========================================================================

type Scope = 'open' | 'all'

export function Runs() {
  const [scope, setScope] = useState<Scope>('open')
  const [search, setSearch] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const { games } = useGames()

  const {
    data: runs,
    loading,
    refreshing,
    reload,
    set,
  } = useResourceList<RunAdminDto>(
    `/api/admin/runs/flagged?take=200&includeReviewed=${scope === 'all'}`
  )

  const gameKey = useMemo(() => {
    const map = new Map(games.map((g) => [g.gameId, g.gameKey]))
    return (id: string) => map.get(id) ?? id.slice(0, 8)
  }, [games])

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return runs

    return runs.filter((run) =>
      [run.runId, run.userId, run.flagReason ?? '', run.outcome, gameKey(run.gameId)]
        .join(' ')
        .toLowerCase()
        .includes(term),
    )
  }, [runs, search, gameKey])

  const unreviewed = runs.filter((r) => !r.reviewedAtUtc)
  const capped = runs.filter((r) => r.capReached)

  const columns = useMemo<Column<RunAdminDto>[]>(
    () => [
      {
        key: 'when',
        header: 'Started',
        sort: (r) => r.startedAtUtc,
        numeric: false,
        render: (r) => (
          <div>
            <div>{formatRelative(r.startedAtUtc)}</div>
            <div className="s7-muted" style={{ fontSize: '0.72rem' }}>
              {formatDuration(r.durationMs)} · {r.outcome}
            </div>
          </div>
        ),
      },
      {
        key: 'game',
        header: 'Game',
        sort: (r) => gameKey(r.gameId),
        render: (r) => <code className="s7-key">{gameKey(r.gameId)}</code>,
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
        key: 'paid',
        header: 'Net paid',
        numeric: true,
        sort: (r) => r.payouts.reduce((sum, p) => sum + p.netAmount, 0),
        render: (r) => {
          const net = r.payouts.reduce((sum, p) => sum + p.netAmount, 0)
          const gross = r.payouts.reduce((sum, p) => sum + p.grossAmount, 0)

          return (
            <span>
              {net.toLocaleString()}
              {gross > net ? (
                <span className="s7-muted" style={{ fontSize: '0.72rem' }}>
                  {' '}
                  of {gross.toLocaleString()}
                </span>
              ) : null}
            </span>
          )
        },
      },
      {
        key: 'cap',
        header: 'Capped',
        sort: (r) => r.capReached,
        render: (r) => (r.capReached ? <Badge tone="warning">Capped</Badge> : <span className="s7-muted">—</span>),
      },
      {
        key: 'verdict',
        header: 'Verdict',
        sort: (r) => (r.reviewedAtUtc ? 1 : 0),
        render: (r) =>
          r.reviewedAtUtc ? (
            <Badge tone="success">Reviewed</Badge>
          ) : (
            <Badge tone="danger">Open</Badge>
          ),
      },
      { key: 'user', header: 'Player', render: (r) => <CopyId id={r.userId} label="userId" /> },
    ],
    [gameKey],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Rocket size={22} />}
        title="Runs"
        subtitle="Runs the server could not reconcile with the time they claim to have taken. Each one is waiting on a human verdict."
      />

      <StatRow>
        <Stat
          icon={<Flag size={13} />}
          label="Open flags"
          value={unreviewed.length}
          sub="No verdict yet"
          tone={unreviewed.length ? 'danger' : 'success'}
        />
        <Stat
          icon={<AlertTriangle size={13} />}
          label="Hit a cap"
          value={capped.length}
          sub="Payout trimmed by a ceiling"
          tone="warning"
        />
        <Stat
          icon={<Coins size={13} />}
          label="Net paid"
          value={runs.reduce((sum, r) => sum + r.payouts.reduce((s, p) => s + p.netAmount, 0), 0)}
          sub="Across the runs listed"
          tone="brand"
        />
      </StatRow>

      <Card>
        <CardHeader
          icon={<Flag size={16} />}
          title={`${rows.length} run${rows.length === 1 ? '' : 's'}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search run, player or reason…" />
            <Segmented
              layoutId="runs-scope"
              value={scope}
              onChange={setScope}
              options={[
                { value: 'open', label: 'Awaiting review' },
                { value: 'all', label: 'Include reviewed' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(r) => r.runId}
            loading={loading}
            onRowClick={(r) => setSelectedId(r.runId)}
            selectedId={selectedId}
            initialSort={{ key: 'when', direction: 'desc' }}
            empty={
              scope === 'open'
                ? 'Nothing is awaiting review. Every flagged run has a verdict.'
                : 'No flagged runs.'
            }
          />
        </CardBody>
      </Card>

      <RunDrawer
        runId={selectedId}
        onClose={() => setSelectedId(null)}
        gameKey={gameKey}
        onReviewed={(reviewed) => {
          // Patch the row in place so the verdict shows immediately. In the
          // "awaiting review" scope the row should also leave the list, which a
          // reload would do — but that costs the admin their scroll position
          // mid-queue, so it is left until they refresh or switch scope.
          set((current) => current.map((r) => (r.runId === reviewed.runId ? reviewed : r)))
        }}
      />
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Detail drawer
// ---------------------------------------------------------------------------

function RunDrawer({
  runId,
  onClose,
  gameKey,
  onReviewed,
}: {
  runId: string | null
  onClose: () => void
  gameKey: (id: string) => string
  onReviewed: (run: RunAdminDto) => void
}) {
  // Fetched fresh rather than reused from the list: the list endpoint returns
  // the same DTO, but a run's payouts can be long and the detail view is where
  // the current value matters.
  const { data: run, loading } = useResource<RunAdminDto | null>(
    runId ? `/api/admin/runs/${runId}` : null,
    null,
  )

  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)

  const gross = run?.payouts.reduce((sum, p) => sum + p.grossAmount, 0) ?? 0
  const net = run?.payouts.reduce((sum, p) => sum + p.netAmount, 0) ?? 0
  const withheld = gross - net

  async function review() {
    if (!run) return

    setSaving(true)
    try {
      const updated = await api.post<RunAdminDto>(`/api/admin/runs/${run.runId}/review`, {
        note: note.trim() || null,
      })

      toast.success('Run reviewed', 'The flag is resolved and will not reappear in the queue.')
      onReviewed(updated)
      setNote('')
      onClose()
    } finally {
      setSaving(false)
    }
  }

  return (
    <Drawer
      open={!!runId}
      onClose={onClose}
      title="Run detail"
      subtitle={run ? `${gameKey(run.gameId)} · ${formatDuration(run.durationMs)} · ${run.outcome}` : undefined}
      footer={
        run && !run.reviewedAtUtc ? (
          <>
            <Button loading={saving} onClick={review}>
              <ShieldCheck size={15} /> Mark reviewed
            </Button>
            <Button variant="ghost" onClick={onClose}>
              Close
            </Button>
          </>
        ) : (
          <Button variant="ghost" onClick={onClose}>
            Close
          </Button>
        )
      }
    >
      {loading || !run ? (
        <SkeletonRows rows={6} />
      ) : (
        <div className="s7-stack">
          {run.flagReason ? (
            <Note tone="danger">
              <strong>Flagged:</strong> {run.flagReason}
            </Note>
          ) : null}

          {run.capReached && run.capMessage ? (
            <Note tone="warning">
              <strong>Capped:</strong> {run.capMessage}
            </Note>
          ) : null}

          {run.reviewedAtUtc ? (
            <Note>
              Reviewed {formatRelative(run.reviewedAtUtc)}
              {run.reviewNote ? ` — “${run.reviewNote}”` : '. No note was left.'}
            </Note>
          ) : null}

          <DefList>
            <Def label="Run">
              <CopyId id={run.runId} label="runId" />
            </Def>
            <Def label="Player">
              <CopyId id={run.userId} label="userId" />
            </Def>
            <Def label="Game">{gameKey(run.gameId)}</Def>
            <Def label="State">{run.state}</Def>
            <Def label="Outcome">{run.outcome}</Def>
            <Def label="Started">{formatDateTime(run.startedAtUtc)}</Def>
            <Def label="Ended">{run.endedAtUtc ? formatDateTime(run.endedAtUtc) : 'never settled'}</Def>
            <Def label="Duration">{formatDuration(run.durationMs)}</Def>
            <Def label="Seed">
              <span className="s7-mono">{run.seed}</span>
            </Def>
            <Def label="Layout version">{run.layoutVersion}</Def>
            <Def label="Session">
              {run.sessionId ? <CopyId id={run.sessionId} label="sessionId" /> : 'single player'}
            </Def>
          </DefList>

          <div>
            <h3 className="s7-subhead">What the client reported</h3>
            {!run.collected.length ? (
              <p className="s7-hint">Nothing was collected.</p>
            ) : (
              <div className="s7-dt-wrap">
                <table className="s7-dt">
                  <thead>
                    <tr>
                      <th>Signal</th>
                      <th className="s7-num">Count</th>
                    </tr>
                  </thead>
                  <tbody>
                    {run.collected.map((c) => (
                      <tr key={c.kind}>
                        <td>
                          <code className="s7-key">{c.kind}</code>
                        </td>
                        <td className="s7-num">{c.count.toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div>
            <h3 className="s7-subhead">
              <Coins size={15} /> What was actually paid
            </h3>

            {/* The evidence, laid out as arithmetic. `paid` below `collected`
                is where a rate ceiling shows its work: reporting 900 coins and
                being paid for 60 is the whole story of the flag. */}
            {!run.payouts.length ? (
              <p className="s7-hint">This run paid nothing.</p>
            ) : (
              <>
                <div className="s7-dt-wrap">
                  <table className="s7-dt">
                    <thead>
                      <tr>
                        <th>Source</th>
                        <th>Currency</th>
                        <th className="s7-num">Collected</th>
                        <th className="s7-num">Paid for</th>
                        <th className="s7-num">Unit</th>
                        <th className="s7-num">Gross</th>
                        <th className="s7-num">Net</th>
                      </tr>
                    </thead>
                    <tbody>
                      {run.payouts.map((p, i) => (
                        <tr key={`${p.source}-${p.currency}-${i}`}>
                          <td>
                            <code className="s7-key">{p.source}</code>
                          </td>
                          <td>{p.currency}</td>
                          <td className="s7-num">{p.collectedCount.toLocaleString()}</td>
                          <td className="s7-num">
                            {p.paidCount < p.collectedCount ? (
                              <Badge tone="warning">{p.paidCount.toLocaleString()}</Badge>
                            ) : (
                              p.paidCount.toLocaleString()
                            )}
                          </td>
                          <td className="s7-num">{p.unitValue.toLocaleString()}</td>
                          <td className="s7-num s7-muted">{p.grossAmount.toLocaleString()}</td>
                          <td className="s7-num" style={{ fontWeight: 600 }}>
                            {p.netAmount.toLocaleString()}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {withheld > 0 ? (
                  <Note tone="warning">
                    <strong>{withheld.toLocaleString()}</strong> withheld by ceilings — the player
                    received {net.toLocaleString()} of a possible {gross.toLocaleString()}.
                  </Note>
                ) : (
                  <Note>
                    <CheckCircle2 size={14} style={{ verticalAlign: -2 }} /> Nothing was withheld;
                    every reported signal was paid at full value.
                  </Note>
                )}
              </>
            )}
          </div>

          {!run.reviewedAtUtc ? (
            <Field
              label="Review note"
              hint="Optional, stored with the verdict. Marking a run reviewed resolves the flag; it does not claw back or grant currency."
            >
              <textarea
                className="s7-textarea"
                rows={3}
                value={note}
                onChange={(e) => setNote(e.target.value)}
                placeholder="e.g. plausible for a long session — device clock drift"
              />
            </Field>
          ) : null}
        </div>
      )}
    </Drawer>
  )
}
