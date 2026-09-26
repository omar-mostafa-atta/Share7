import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { Clock, History, PauseCircle, RefreshCw, ShieldCheck, UserCheck, UserPlus, Users, UserX } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable, type Column } from '../components/ui/DataTable'
import { Select } from '../components/ui/form'
import { Modal } from '../components/ui/Modal'
import { listVariants } from '../components/ui/motion'
import { useCan } from '../lib/access'
import { ApiError } from '../lib/errors'
import { useResource } from '../lib/resource'
import { formatDateTime, formatRelative } from '../lib/time'
import { toast } from '../store/toast'
import { CreateUserModal } from '../features/users/CreateUserModal'
import { AuditLog } from '../features/team/AuditLog'
import { MemberRecord } from '../features/team/MemberRecord'
import { SecuritySettings } from '../features/team/SecuritySettings'
import { AccessFields, WhoFields, draftProblems, emptyDraft } from '../features/team/MemberForm'
import {
  STATUS_INFO,
  describeScope,
  studioAddress,
  team,
  type LegacyContentAccount,
  type MemberDraft,
  type StaffStatus,
  type TeamMemberListItem,
  type TeamOverview,
} from '../features/team/data'

// ===========================================================================
// Team & Access — SuperAdmins only
//
// The content team as one ledger (the layout chosen on 2026-09-26): a dense,
// searchable table of members, where a row opens in place into that person's
// full record — who they are, what they may do, how they sign in — with the
// acts that change the account beneath. The audit log is the same ledger over
// what everybody did; Security is the handful of rules for signing in.
//
// Adding a member is also on Users, where an Admin can do it: that is the one
// thing about the content team an Admin may do (decided 2026-09-26). Everything
// on this page after the account exists is a SuperAdmin's alone, and the
// server refuses the rest to anybody else whatever this page shows.
// ===========================================================================

type Tab = 'team' | 'audit' | 'security'

const EMPTY: TeamOverview = {
  members: [],
  legacyAccounts: [],
  counts: { active: 0, invited: 0, suspended: 0, deactivated: 0 },
  requireTwoStep: false,
  studioAddressConfigured: true,
  studioAddress: null,
}

export function Team() {
  const allowed = useCan('team.manage')
  const [params, setParams] = useSearchParams()
  const tab = (['team', 'audit', 'security'].includes(params.get('tab') ?? '') ? params.get('tab') : 'team') as Tab

  if (!allowed) return <Navigate to="/" replace />

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<ShieldCheck size={22} />}
        title="Team & Access"
        subtitle="The content team who work in the Studio: who they are, what each may do, and how they sign in. Every change here is recorded."
      />

      <div style={{ marginBottom: '1rem' }}>
        <Segmented
          layoutId="team-tab"
          value={tab}
          onChange={(next) => setParams(next === 'team' ? {} : { tab: next }, { replace: true })}
          options={[
            { value: 'team', label: 'Team' },
            { value: 'audit', label: 'Audit log' },
            { value: 'security', label: 'Security' },
          ]}
        />
      </div>

      {tab === 'team' ? <TeamLedger /> : tab === 'audit' ? <AuditLog /> : <SecuritySettings />}
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// The team
// ---------------------------------------------------------------------------

type StatusFilter = 'current' | StaffStatus | 'all'

function TeamLedger() {
  const { data, loading, refreshing, reload } = useResource<TeamOverview>('/api/admin/team', EMPTY)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<StatusFilter>('current')
  const [open, setOpen] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [adopting, setAdopting] = useState<LegacyContentAccount | null>(null)

  const rows = useMemo(() => {
    const query = search.trim().toLowerCase()
    return data.members
      .filter((m) => (status === 'all' ? true : status === 'current' ? m.status !== 'Deactivated' : m.status === status))
      .filter(
        (m) =>
          !query ||
          m.fullName.toLowerCase().includes(query) ||
          m.username.toLowerCase().includes(query) ||
          (m.jobTitle ?? '').toLowerCase().includes(query),
      )
  }, [data.members, search, status])

  const columns = useMemo<Column<TeamMemberListItem>[]>(
    () => [
      {
        key: 'member',
        header: 'Member',
        sort: (m) => m.fullName,
        render: (m) => (
          <div className="s7-inline" style={{ flexWrap: 'nowrap' }}>
            <span className="s7-avatar" aria-hidden>
              {(m.fullName || m.username).charAt(0).toUpperCase()}
            </span>
            <span style={{ minWidth: 0 }}>
              {/* A real button, so a keyboard can open the record the row opens on a click. */}
              <button type="button" className="s7-linklike s7-row-open">
                {m.fullName}
              </button>
              <div className="s7-muted" style={{ fontSize: '0.72rem' }}>
                {m.username}
                {m.jobTitle ? ` · ${m.jobTitle}` : ''}
              </div>
            </span>
          </div>
        ),
      },
      {
        key: 'role',
        header: 'Studio role',
        sort: (m) => ['Author', 'Reviewer', 'Lead'].indexOf(m.studioRole),
        render: (m) => <Badge tone={m.studioRole === 'Lead' ? 'brand' : m.studioRole === 'Reviewer' ? 'info' : 'muted'}>{m.studioRole}</Badge>,
      },
      {
        key: 'scope',
        header: 'Works on',
        render: (m) => {
          const scope = describeScope(m.scope)
          return (
            <span className="s7-stack-tight" style={{ maxWidth: '22rem' }}>
              <span>{scope.parts}</span>
              <span className="s7-hint">{scope.languages}</span>
            </span>
          )
        },
      },
      {
        key: 'status',
        header: 'Status',
        sort: (m) => m.status,
        render: (m) => (
          <span className="s7-stack-tight">
            <Badge tone={STATUS_INFO[m.status].tone}>{STATUS_INFO[m.status].label}</Badge>

          </span>
        ),
      },
      {
        key: 'twoStep',
        header: '2-step',
        sort: (m) => m.twoStepEnabled,
        render: (m) => (m.twoStepEnabled ? <Badge tone="success">On</Badge> : <span className="s7-muted">Off</span>),
      },
      {
        key: 'active',
        header: 'Last active',
        sort: (m) => m.lastActiveAtUtc,
        render: (m) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
            {m.lastActiveAtUtc ? formatRelative(m.lastActiveAtUtc) : 'never'}
          </span>
        ),
      },
    ],
    [],
  )

  return (
    <>
      <StatRow>
        <Stat icon={<UserCheck size={13} />} label="Active" value={data.counts.active} sub="Can sign in to the Studio" tone="success" />
        <Stat icon={<Clock size={13} />} label="No password yet" value={data.counts.invited} sub="Set one from their record" tone="info" />
        <Stat icon={<PauseCircle size={13} />} label="Suspended" value={data.counts.suspended} sub="Signed out until let back in" tone={data.counts.suspended ? 'warning' : 'brand'} />
        <Stat icon={<UserX size={13} />} label="Closed" value={data.counts.deactivated} sub="Names kept on their work" tone="cool" />
      </StatRow>

      {loading ? null : <StudioAddress address={studioAddress(data.studioAddress)} />}

      {data.legacyAccounts.length ? (
        <Card>
          <CardHeader icon={<History size={16} />} title="Accounts from before the Studio" />
          <CardBody>
            <p className="s7-muted" style={{ marginTop: 0 }}>
              These content-team accounts were made on the Users page before Team & Access existed. They cannot sign in to the Studio
              until they have a Studio role and a part of the curriculum.
            </p>
            <ul className="s7-plain-list">
              {data.legacyAccounts.map((account) => (
                <li key={account.userId} className="s7-session">
                  <span>
                    <code className="s7-key">{account.username}</code>
                    <span className="s7-hint"> · made {formatDateTime(account.createdAtUtc)}</span>
                  </span>
                  <Button variant="ghost" onClick={() => setAdopting(account)}>
                    Set them up in the Studio
                  </Button>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      ) : null}

      <Card>
        <CardHeader
          icon={<Users size={16} />}
          title="The content team"
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search a name, username or job title…" />
            <Select value={status} onChange={(e) => setStatus(e.target.value as StatusFilter)} aria-label="Status" style={{ maxWidth: '13rem' }}>
              <option value="current">Everyone but closed</option>
              <option value="Active">Active</option>
              <option value="Invited">No password yet</option>
              <option value="Suspended">Suspended</option>
              <option value="Deactivated">Closed</option>
              <option value="all">Everyone</option>
            </Select>
            <Button onClick={() => setAdding(true)} style={{ marginInlineStart: 'auto' }}>
              <UserPlus size={15} /> Add member
            </Button>
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(m) => m.userId}
            loading={loading}
            selectedId={open}
            onRowClick={(m) => setOpen(open === m.userId ? null : m.userId)}
            expanded={(m) => <MemberRecord userId={m.userId} onChanged={() => void reload()} address={data.studioAddress} />}
            initialSort={{ key: 'member' }}
            pageResetKey={`${search}|${status}`}
            empty={
              data.members.length === 0
                ? 'Nobody is in the content team yet. Add the first member: you set their username and password, and they sign in at the Studio’s address.'
                : 'Nobody matches that.'
            }
          />
        </CardBody>
      </Card>

      <CreateUserModal open={adding} onClose={() => setAdding(false)} onCreated={() => void reload()} contentTeamOnly />

      <AdoptModal account={adopting} onClose={() => setAdopting(null)} onDone={() => void reload()} />
    </>
  )
}

// ---------------------------------------------------------------------------
// The Studio's address
// ---------------------------------------------------------------------------

/** The one address the whole content team signs in at, whatever their role — to pass on. */
function StudioAddress({ address }: { address: string }) {
  const [copied, setCopied] = useState(false)

  return (
    <Note>
      <span className="s7-inline" style={{ justifyContent: 'space-between', width: '100%' }}>
        <span>
          The content team signs in at <code className="s7-key">{address}</code> — the same address for every member, whatever their role.
        </span>
        <button
          type="button"
          className="s7-linklike"
          onClick={async () => {
            try {
              await navigator.clipboard.writeText(address)
              setCopied(true)
              window.setTimeout(() => setCopied(false), 1500)
            } catch {
              // Refused clipboard: the address is on screen.
            }
          }}
        >
          {copied ? 'Copied' : 'Copy'}
        </button>
      </span>
    </Note>
  )
}

// ---------------------------------------------------------------------------
// A pre-Studio account, given its profile
// ---------------------------------------------------------------------------

function AdoptModal({ account, onClose, onDone }: { account: LegacyContentAccount | null; onClose: () => void; onDone: () => void }) {
  const [draft, setDraft] = useState<MemberDraft>(emptyDraft)
  const [touched, setTouched] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [openFor, setOpenFor] = useState<string | null>(null)

  // A fresh form for every account opened.
  if (account && openFor !== account.userId) {
    setOpenFor(account.userId)
    setDraft(emptyDraft())
    setTouched(false)
    setError(null)
  }

  const problems = draftProblems(draft, { who: true, username: false, access: true })
  const ready = Object.keys(problems).length === 0

  async function submit() {
    setTouched(true)
    if (!account || !ready) return
    setBusy(true)
    setError(null)
    try {
      const member = await team.adopt(account.userId, draft)
      toast.success(
        'Set up in the Studio',
        member.security.hasPassword
          ? `${account.username} signs in to the Studio with the password they already have.`
          : `${account.username} has no password yet: open their record and reset their access to get them a link.`,
      )
      onDone()
      onClose()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not go through.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={!!account}
      onClose={onClose}
      wide
      icon={<UserPlus size={18} />}
      title={account ? `Set up ${account.username} in the Studio` : ''}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button loading={busy} disabled={touched && !ready} onClick={() => void submit()} style={{ marginInlineStart: 'auto' }}>
            Set them up
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <WhoFields draft={draft} onChange={setDraft} withUsername={false} touched={touched} />
        <AccessFields draft={draft} onChange={setDraft} touched={touched} idPrefix="adopt" />
        {error ? <Note tone="danger">{error}</Note> : null}
      </div>
    </Modal>
  )
}
