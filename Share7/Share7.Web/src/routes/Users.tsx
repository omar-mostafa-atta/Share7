import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  BookOpen,
  ChevronLeft,
  ChevronRight,
  Coins,
  Flame,
  Gauge,
  Gift,
  Package,
  RefreshCw,
  Rocket,
  ShieldAlert,
  Trash2,
  Trophy,
  UserRound,
  Users as UsersIcon,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton, SkeletonRows } from '../components/ui/primitives'
import { CopyId, Def, DefList, Meter, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select } from '../components/ui/form'
import { Modal } from '../components/ui/Modal'
import { api } from '../lib/client'
import { useResource, useResourceList } from '../lib/resource'
import { formatDateTime, formatDuration, formatRelative } from '../lib/time'
import { toast } from '../store/toast'
import { useAuth } from '../store/auth'
import { useProducts } from '../features/shop/data'
import { listVariants } from '../components/ui/motion'
import type {
  AdminUserDetailDto,
  AdminUserEntitlementDto,
  AdminUserListItemDto,
  AdminUserPageDto,
  AdminUserProgressionDto,
  AdminUserRunDto,
  AdminUserWalletDto,
} from '../types/api'

/** Which panel of the detail drawer is open. Each fetches only when selected. */
type DrawerTab = 'profile' | 'wallet' | 'progression' | 'items' | 'runs'

// ===========================================================================
// Users
//
// Backed by GET /api/admin/users, which this work added — the API previously
// exposed only delete-by-id, so the console had no way to answer "who is on
// this platform" without already knowing the answer.
//
// Two operations are genuinely destructive and are gated accordingly:
//   - Delete removes the account and every row it owns. Irreversible, and only
//     a SuperAdmin may delete a privileged account.
//   - Granting an entitlement is not destructive but is not free either — it
//     hands over a product without a purchase record.
//
// The detail drawer reads five admin-scoped endpoints added alongside it:
// detail, wallet, progression, entitlements and runs. They exist because every
// equivalent player-facing route is `/me`-scoped at the controller — the
// services beneath already took a userId, so those endpoints compose what was
// there rather than computing anything new.
//
// Each panel fetches only when its tab is opened. Loading all five on every row
// click would issue five requests to show a name and an email.
// ===========================================================================

const PAGE_SIZE = 50

export function Users() {
  const [search, setSearch] = useState('')
  const [role, setRole] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<AdminUserListItemDto | null>(null)

  // Debouncing is deliberately absent: the search box submits on change and the
  // roster query is server-side and indexed enough for an admin-only screen.
  // What IS handled is the page resetting — searching from page 7 and staying
  // on page 7 shows an empty table and looks broken.
  const query = useMemo(() => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (search.trim()) params.set('search', search.trim())
    if (role) params.set('role', role)
    return `/api/admin/users?${params}`
  }, [search, role, page])

  const { data, loading, refreshing, reload } = useResource<AdminUserPageDto>(query, {
    users: [],
    total: 0,
    page: 1,
    pageSize: PAGE_SIZE,
  })

  const pageCount = Math.max(1, Math.ceil(data.total / PAGE_SIZE))

  const columns = useMemo<Column<AdminUserListItemDto>[]>(
    () => [
      {
        key: 'user',
        header: 'Account',
        sort: (u) => u.fullName || u.userName,
        render: (u) => (
          <div className="s7-inline">
            <span className="s7-avatar" aria-hidden>
              {(u.fullName || u.userName || '?').charAt(0).toUpperCase()}
            </span>
            <span style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 600 }}>{u.fullName || u.userName}</div>
              <div className="s7-muted" style={{ fontSize: '0.72rem' }}>
                {u.email || u.userName}
              </div>
            </span>
          </div>
        ),
      },
      {
        key: 'roles',
        header: 'Roles',
        sort: (u) => u.roles.join(','),
        render: (u) =>
          !u.roles.length ? (
            <span className="s7-muted">—</span>
          ) : (
            <span className="s7-inline">
              {u.roles.map((r) => (
                <Badge key={r} tone={r === 'SuperAdmin' ? 'danger' : r === 'Admin' ? 'brand' : 'muted'}>
                  {r}
                </Badge>
              ))}
            </span>
          ),
      },
      {
        key: 'age',
        header: 'Age',
        numeric: true,
        sort: (u) => u.age,
        render: (u) => (u.age == null ? <span className="s7-muted">—</span> : u.age),
      },
      {
        key: 'profile',
        header: 'Profile',
        sort: (u) => u.isProfileComplete,
        render: (u) =>
          u.isProfileComplete ? (
            <Badge tone="success">Complete</Badge>
          ) : (
            <Badge tone="warning">Incomplete</Badge>
          ),
      },
      {
        key: 'seen',
        header: 'Last played',
        sort: (u) => u.lastSeenAtUtc,
        render: (u) =>
          u.lastSeenAtUtc ? (
            <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
              {formatRelative(u.lastSeenAtUtc)}
            </span>
          ) : (
            <span className="s7-muted">never</span>
          ),
      },
      {
        key: 'joined',
        header: 'Joined',
        sort: (u) => u.createdAtUtc,
        render: (u) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {formatDateTime(u.createdAtUtc)}
          </span>
        ),
      },
      { key: 'id', header: 'Id', render: (u) => <CopyId id={u.userId} label="userId" /> },
    ],
    [],
  )

  const incomplete = data.users.filter((u) => !u.isProfileComplete).length

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<UsersIcon size={22} />}
        title="Users"
        subtitle="Every account on the platform. Open one to see its profile, grant a product, or delete it."
      />

      <StatRow>
        <Stat icon={<UsersIcon size={13} />} label="Matching accounts" value={data.total} sub={search || role ? 'For the current filter' : 'Everyone'} tone="brand" />
        <Stat icon={<UserRound size={13} />} label="On this page" value={data.users.length} sub={`Page ${data.page} of ${pageCount}`} tone="info" />
        <Stat
          icon={<ShieldAlert size={13} />}
          label="Incomplete profiles"
          value={incomplete}
          sub="Signed up but never finished onboarding"
          tone={incomplete ? 'warning' : 'success'}
        />
      </StatRow>

      <Card>
        <CardHeader
          icon={<UsersIcon size={16} />}
          title="Roster"
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox
              value={search}
              onChange={(v) => {
                setSearch(v)
                setPage(1)
              }}
              placeholder="Search username, name or email…"
            />
            <Select
              value={role}
              onChange={(e) => {
                setRole(e.target.value)
                setPage(1)
              }}
              style={{ maxWidth: '11rem' }}
            >
              <option value="">Any role</option>
              <option value="Student">Student</option>
              <option value="Teacher">Teacher</option>
              <option value="Admin">Admin</option>
              <option value="SuperAdmin">SuperAdmin</option>
            </Select>
          </div>

          <DataTable
            rows={data.users}
            columns={columns}
            getId={(u) => u.userId}
            loading={loading}
            onRowClick={setSelected}
            selectedId={selected?.userId ?? null}
            empty={search || role ? 'No account matches that filter.' : 'No accounts yet.'}
          />

          {pageCount > 1 ? (
            <div className="s7-bar" style={{ marginTop: '0.9rem', marginBottom: 0 }}>
              <Button variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                <ChevronLeft size={15} /> Previous
              </Button>
              <span className="s7-hint">
                Page {data.page} of {pageCount} — {data.total.toLocaleString()} accounts
              </span>
              <Button
                variant="ghost"
                disabled={page >= pageCount}
                onClick={() => setPage((p) => p + 1)}
                style={{ marginInlineStart: 'auto' }}
              >
                Next <ChevronRight size={15} />
              </Button>
            </div>
          ) : null}
        </CardBody>
      </Card>

      <UserDrawer user={selected} onClose={() => setSelected(null)} onDeleted={() => void reload()} />
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Detail drawer
// ---------------------------------------------------------------------------

function UserDrawer({
  user,
  onClose,
  onDeleted,
}: {
  user: AdminUserListItemDto | null
  onClose: () => void
  onDeleted: () => void
}) {
  const roles = useAuth((s) => s.roles)
  const isSuperAdmin = roles.includes('SuperAdmin')

  const { products } = useProducts()

  const [tab, setTab] = useState<DrawerTab>('profile')

  // The full detail, which the roster row deliberately does not carry.
  const { data: detail, loading } = useResource<AdminUserDetailDto | null>(
    user ? `/api/admin/users/${user.userId}` : null,
    null,
  )

  // The three heavy panels are fetched only when their tab is open. A drawer that
  // pulled a wallet, a progression snapshot and a run history on every row click
  // would issue four requests to show a name and an email.
  const wallet = useResource<AdminUserWalletDto | null>(
    user && tab === 'wallet' ? `/api/admin/users/${user.userId}/wallet?take=50` : null,
    null,
  )

  const progression = useResource<AdminUserProgressionDto | null>(
    user && tab === 'progression' ? `/api/admin/users/${user.userId}/progression` : null,
    null,
  )

  const entitlements = useResourceList<AdminUserEntitlementDto>(
    user && tab === 'items' ? `/api/admin/users/${user.userId}/entitlements` : null
  )

  const runs = useResourceList<AdminUserRunDto>(
    user && tab === 'runs' ? `/api/admin/users/${user.userId}/runs?take=50` : null
  )

  const [productId, setProductId] = useState('')
  const [granting, setGranting] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [confirmText, setConfirmText] = useState('')

  // Only a SuperAdmin may delete a privileged account — the API enforces it, and
  // the button reflects it rather than letting the click fail.
  const targetIsPrivileged = !!user?.roles.some((r) => r === 'Admin' || r === 'SuperAdmin')
  const mayDelete = !targetIsPrivileged || isSuperAdmin

  async function grant() {
    if (!user || !productId) return

    setGranting(true)
    try {
      await api.post('/api/admin/entitlements', { userId: user.userId, productId })
      const product = products.find((p) => p.productId === productId)
      toast.success('Entitlement granted', `${user.userName} now owns "${product?.key ?? productId}".`)
      setProductId('')
    } finally {
      setGranting(false)
    }
  }

  return (
    <>
      <Drawer
        open={!!user}
        onClose={onClose}
        title={user?.fullName || user?.userName || 'Account'}
        subtitle={user?.email ?? user?.userName}
        footer={
          <>
            <Button variant="ghost" onClick={onClose}>
              Close
            </Button>
            <Button
              variant="danger"
              disabled={!mayDelete}
              title={mayDelete ? undefined : 'Only a SuperAdmin can delete a privileged account.'}
              onClick={() => setConfirmDelete(true)}
              style={{ marginInlineStart: 'auto' }}
            >
              <Trash2 size={15} /> Delete account
            </Button>
          </>
        }
      >
        {!user ? null : (
          <div className="s7-stack">
            <Segmented
              layoutId="user-tab"
              value={tab}
              onChange={setTab}
              options={[
                { value: 'profile', label: 'Profile' },
                { value: 'wallet', label: 'Wallet' },
                { value: 'progression', label: 'Progression' },
                { value: 'items', label: 'Items' },
                { value: 'runs', label: 'Runs' },
              ]}
            />

            {tab === 'profile' ? (
              loading ? (
                <SkeletonRows rows={6} />
              ) : (
                <>
                  <StatRow>
                    <Stat icon={<Rocket size={13} />} label="Runs" value={detail?.runCount ?? 0} sub={`${detail?.flaggedRunCount ?? 0} flagged`} tone={detail?.flaggedRunCount ? 'warning' : 'brand'} />
                    <Stat icon={<BookOpen size={13} />} label="Lessons passed" value={detail?.lessonsCompleted ?? 0} sub="At or above the pass mark" tone="success" />
                    <Stat icon={<Package size={13} />} label="Items owned" value={detail?.entitlementCount ?? 0} sub={`${detail?.purchaseCount ?? 0} purchases`} tone="info" />
                  </StatRow>

                  <DefList>
                    <Def label="User id">
                      <CopyId id={user.userId} label="userId" />
                    </Def>
                    <Def label="Username">{detail?.userName ?? user.userName}</Def>
                    <Def label="Full name">{detail?.fullName ?? user.fullName}</Def>
                    <Def label="Email">{detail?.email ?? user.email}</Def>
                    <Def label="Phone">{detail?.phoneNumber}</Def>
                    <Def label="Age">{detail?.age ?? user.age}</Def>
                    <Def label="Grade">
                      {detail?.gradeName ??
                        (detail?.gradeId ? <CopyId id={detail.gradeId} label="gradeId" /> : null)}
                    </Def>
                    <Def label="Language">{detail?.preferredLanguageCode?.toUpperCase()}</Def>
                    <Def label="Roles">
                      {user.roles.length ? (
                        <span className="s7-inline">
                          {user.roles.map((r) => (
                            <Badge key={r} tone={r === 'SuperAdmin' ? 'danger' : r === 'Admin' ? 'brand' : 'muted'}>
                              {r}
                            </Badge>
                          ))}
                        </span>
                      ) : null}
                    </Def>
                    <Def label="Profile">
                      {detail?.isProfileComplete ? 'Complete' : 'Never finished onboarding'}
                    </Def>
                    <Def label="Joined">{formatDateTime(detail?.createdAtUtc ?? user.createdAtUtc)}</Def>
                    <Def label="Last played">
                      {detail?.lastSeenAtUtc ? formatDateTime(detail.lastSeenAtUtc) : 'never'}
                    </Def>
                  </DefList>

                  <div>
                    <h3 className="s7-subhead">
                      <Gift size={15} /> Grant a product
                    </h3>
                    <p className="s7-hint" style={{ marginBottom: '0.6rem' }}>
                      Hands the product over directly. No purchase is recorded and no currency is
                      spent, so the entitlement's source reads as an admin grant.
                    </p>

                    <div className="s7-bar">
                      <Select
                        value={productId}
                        onChange={(e) => setProductId(e.target.value)}
                        style={{ flex: '1 1 14rem' }}
                      >
                        <option value="">Choose a product…</option>
                        {products.map((p) => (
                          <option key={p.productId} value={p.productId}>
                            {p.key} — {p.kindName}
                            {!p.active ? ' (inactive)' : ''}
                          </option>
                        ))}
                      </Select>
                      <Button loading={granting} disabled={!productId} onClick={grant}>
                        Grant
                      </Button>
                    </div>
                  </div>
                </>
              )
            ) : null}

            {tab === 'wallet' ? (
              wallet.loading ? (
                <SkeletonRows rows={6} />
              ) : (
                <>
                  {!wallet.data?.balances.length ? (
                    <Note>This account holds no currency.</Note>
                  ) : (
                    <StatRow>
                      {wallet.data.balances.map((b) => (
                        <Stat key={b.currency} icon={<Coins size={13} />} label={b.currency} value={b.amount} tone="brand" />
                      ))}
                    </StatRow>
                  )}

                  <div>
                    <h3 className="s7-subhead">Ledger</h3>
                    <p className="s7-hint" style={{ marginBottom: '0.6rem' }}>
                      Every movement, newest first. The running balance is what settles a dispute —
                      read down the column rather than re-adding the amounts.
                      {wallet.data && wallet.data.ledgerCount > wallet.data.recent.length
                        ? ` Showing ${wallet.data.recent.length} of ${wallet.data.ledgerCount.toLocaleString()}.`
                        : ''}
                    </p>

                    {!wallet.data?.recent.length ? (
                      <Note>No currency has ever moved on this account.</Note>
                    ) : (
                      <div className="s7-dt-wrap" style={{ maxHeight: '26rem' }}>
                        <table className="s7-dt">
                          <thead>
                            <tr>
                              <th>When</th>
                              <th>Currency</th>
                              <th className="s7-num">Amount</th>
                              <th className="s7-num">Balance after</th>
                              <th>Reason</th>
                            </tr>
                          </thead>
                          <tbody>
                            {wallet.data.recent.map((entry) => (
                              <tr key={entry.id}>
                                <td style={{ fontSize: '0.78rem' }}>{formatRelative(entry.createdAtUtc)}</td>
                                <td>{entry.currency}</td>
                                <td className="s7-num">
                                  <span
                                    style={{
                                      fontWeight: 600,
                                      color:
                                        entry.amount < 0 ? 'var(--s7-danger)' : 'var(--s7-success)',
                                    }}
                                  >
                                    {entry.amount > 0 ? '+' : ''}
                                    {entry.amount.toLocaleString()}
                                  </span>
                                </td>
                                <td className="s7-num">{entry.balanceAfter.toLocaleString()}</td>
                                <td>
                                  <Badge tone="muted">{entry.transactionType}</Badge>
                                  <span className="s7-muted" style={{ fontSize: '0.72rem' }}>
                                    {' '}
                                    {entry.sourceType}
                                  </span>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>

                  <Note>
                    Read-only. There is no route that credits another account's wallet — the grant
                    endpoint is restricted to the caller's own balance on purpose.
                  </Note>
                </>
              )
            ) : null}

            {tab === 'progression' ? (
              progression.loading ? (
                <SkeletonRows rows={6} />
              ) : !progression.data ? (
                <Note>No progression recorded.</Note>
              ) : (
                <>
                  <StatRow>
                    <Stat icon={<Gauge size={13} />} label="Level" value={progression.data.level.level} sub={`${progression.data.level.xp.toLocaleString()} XP total`} tone="brand" />
                    <Stat icon={<Flame size={13} />} label="Streak" value={progression.data.streak.current} sub={`best ${progression.data.streak.best}`} tone="warning" />
                    <Stat icon={<Trophy size={13} />} label="Objectives" value={progression.data.objectives.length} sub={`${progression.data.objectives.filter((o) => o.canClaim).length} unclaimed`} tone={progression.data.objectives.some((o) => o.canClaim) ? 'success' : 'info'} />
                  </StatRow>

                  {!progression.data.level.isMaxLevel ? (
                    <div>
                      <span className="s7-label">
                        Progress to level {progression.data.level.level + 1}
                      </span>
                      <Meter
                        value={progression.data.level.xpIntoLevel}
                        max={progression.data.level.xpForNextLevel}
                      />
                    </div>
                  ) : (
                    <Note>This account is at the top of the level curve.</Note>
                  )}

                  <div>
                    <h3 className="s7-subhead">Objectives</h3>
                    {!progression.data.objectives.length ? (
                      <Note>No objectives are active for this account.</Note>
                    ) : (
                      <div className="s7-dt-wrap" style={{ maxHeight: '24rem' }}>
                        <table className="s7-dt">
                          <thead>
                            <tr>
                              <th>Objective</th>
                              <th>Resets</th>
                              <th>Progress</th>
                              <th>State</th>
                            </tr>
                          </thead>
                          <tbody>
                            {progression.data.objectives.map((o) => (
                              <tr key={o.key}>
                                <td>
                                  <div style={{ fontWeight: 600 }}>{o.name}</div>
                                  <code className="s7-key">{o.key}</code>
                                </td>
                                <td>
                                  <Badge tone="muted">{o.kind}</Badge>
                                </td>
                                <td style={{ minWidth: '10rem' }}>
                                  <Meter value={o.value} max={o.target} />
                                </td>
                                <td>
                                  {o.canClaim ? (
                                    <Badge tone="success">Ready to claim</Badge>
                                  ) : (
                                    <Badge tone="muted">{o.state}</Badge>
                                  )}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    )}
                  </div>
                </>
              )
            ) : null}

            {tab === 'items' ? (
              entitlements.loading ? (
                <SkeletonRows rows={5} />
              ) : !entitlements.data.length ? (
                <Note>This account owns nothing yet.</Note>
              ) : (
                <div className="s7-dt-wrap">
                  <table className="s7-dt">
                    <thead>
                      <tr>
                        <th>Product</th>
                        <th>Kind</th>
                        <th>How</th>
                        <th>Granted</th>
                      </tr>
                    </thead>
                    <tbody>
                      {entitlements.data.map((e) => (
                        <tr key={e.entitlementId}>
                          <td>
                            <code className="s7-key">{e.productKey}</code>
                            {!e.productActive ? <Badge tone="muted">retired</Badge> : null}
                          </td>
                          <td>
                            <Badge tone="muted">{e.kindName}</Badge>
                          </td>
                          <td>
                            <Badge tone={e.source === 'AdminGrant' ? 'warning' : 'info'}>{e.source}</Badge>
                          </td>
                          <td style={{ fontSize: '0.78rem' }}>{formatRelative(e.grantedAtUtc)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )
            ) : null}

            {tab === 'runs' ? (
              runs.loading ? (
                <SkeletonRows rows={5} />
              ) : !runs.data.length ? (
                <Note>This account has never started a run.</Note>
              ) : (
                <div className="s7-dt-wrap" style={{ maxHeight: '28rem' }}>
                  <table className="s7-dt">
                    <thead>
                      <tr>
                        <th>Started</th>
                        <th>Outcome</th>
                        <th className="s7-num">Duration</th>
                        <th className="s7-num">Net paid</th>
                        <th>Flag</th>
                      </tr>
                    </thead>
                    <tbody>
                      {runs.data.map((run) => (
                        <tr key={run.runId}>
                          <td style={{ fontSize: '0.78rem' }}>{formatRelative(run.startedAtUtc)}</td>
                          <td>
                            <Badge tone={run.outcome === 'Completed' ? 'success' : 'muted'}>
                              {run.outcome}
                            </Badge>
                          </td>
                          <td className="s7-num">{formatDuration(run.durationMs)}</td>
                          <td className="s7-num">{run.netPaid.toLocaleString()}</td>
                          <td>
                            {!run.isFlagged ? (
                              <span className="s7-muted">—</span>
                            ) : run.reviewed ? (
                              <Badge tone="success">reviewed</Badge>
                            ) : (
                              <Badge tone="danger" >{run.flagReason ?? 'flagged'}</Badge>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )
            ) : null}
          </div>
        )}
      </Drawer>

      <Modal
        open={confirmDelete}
        onClose={() => {
          setConfirmDelete(false)
          setConfirmText('')
        }}
        icon={<Trash2 size={18} />}
        title={`Delete ${user?.userName}?`}
        footer={
          <>
            <Button
              variant="ghost"
              onClick={() => {
                setConfirmDelete(false)
                setConfirmText('')
              }}
            >
              Cancel
            </Button>
            <Button
              variant="danger"
              // Typing the username is the gate. This is the only irreversible
              // action in the console and it destroys a child's entire history.
              disabled={confirmText !== user?.userName}
              onClick={async () => {
                if (!user) return

                await api.del(`/api/admin/users/${user.userId}`)
                toast.success('Account deleted', `${user.userName} and all of their data are gone.`)

                setConfirmDelete(false)
                setConfirmText('')
                onClose()
                onDeleted()
              }}
            >
              Delete permanently
            </Button>
          </>
        }
      >
        <div className="s7-stack">
          <Note tone="danger">
            This deletes the account and every row it owns — progress, wallet balances and ledger,
            entitlements, purchases, run history, leaderboard entries and objective progress. It
            cannot be undone.
          </Note>

          <Field label={`Type "${user?.userName}" to confirm`}>
            <Input
              value={confirmText}
              onChange={(e) => setConfirmText(e.target.value)}
              placeholder={user?.userName}
              autoComplete="off"
            />
          </Field>
        </div>
      </Modal>
    </>
  )
}
