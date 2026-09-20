import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  Building2,
  CalendarClock,
  ClipboardList,
  EyeOff,
  GitBranch,
  Heart,
  Layers,
  Plus,
  ShieldCheck,
  Trash2,
  UserPlus,
  Users2,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, IconButton } from '../components/ui/primitives'
import { Note, PageTitle, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable, type Column } from '../components/ui/DataTable'
import { Modal } from '../components/ui/Modal'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { useResource } from '../lib/resource'
import { listVariants } from '../components/ui/motion'
import {
  CONSENT_LABELS,
  KIND_BLURB,
  ROLE_BLURB,
  consentList,
  suppressionBlurb,
  useOrganizationActions,
  type Assignment,
  type Cohort,
  type CohortLearnerRow,
  type CohortMember,
  type CohortReport,
  type CohortRole,
  type GuardianLink,
  type Membership,
  type Organization,
  type OrganizationKind,
  type OrgRole,
  type Overlay,
} from '../features/organizations/data'

// ===========================================================================
// Organizations
//
// Five tables and one scoping column. The console exists because somebody has
// to provision the first organization and the first admin, and because support
// needs one authoritative answer to "why can't this teacher see that class".
//
// **The thing this page has to make visible is what an organization CANNOT
// see.** Adding a learner to a cohort provisions an org-owned enrolment, and
// that enrolment — not the membership, not the account — is what org-side reads
// filter on. So a school sees the work done under its own enrolment and
// acquires nothing retroactively from the learner's private one. The roster
// says so where somebody is about to press the button.
//
// Every aggregate carries its N and suppresses small cells with the reason
// stated rather than blanked: a reader who learns that blank means zero will
// act on a zero eventually.
//
// Docs/EducationalArchitecture.md §9, §17.4, §18, §19.
// ===========================================================================

type Tab = 'cohorts' | 'members' | 'overlays'

export function Organizations() {
  const orgs = useResource<Organization[]>('/api/admin/organizations', [])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  const selected = orgs.data.find((o) => o.id === selectedId) ?? null

  const reload = () => {
    void orgs.reload()
  }

  const actions = useOrganizationActions(reload)

  const learners = orgs.data.reduce((total, o) => total + o.learnerCount, 0)
  const cohorts = orgs.data.reduce((total, o) => total + o.cohortCount, 0)
  const schools = orgs.data.filter((o) => o.kind === 'School').length

  const columns: Column<Organization>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'Organization',
        sort: (o) => o.name,
        render: (o) => (
          <div className="s7-stack-tight">
            <strong>{o.name}</strong>
            <span className="s7-muted s7-small s7-mono">{o.orgKey}</span>
          </div>
        ),
      },
      {
        key: 'kind',
        header: 'Kind',
        sort: (o) => o.kind,
        render: (o) => (
          <span title={KIND_BLURB[o.kind]}>
            <Badge tone="muted">{o.kind}</Badge>
          </span>
        ),
      },
      {
        key: 'parent',
        header: 'Under',
        render: (o) =>
          o.parentOrgId ? (
            <span className="s7-muted s7-small">
              {orgs.data.find((p) => p.id === o.parentOrgId)?.name ?? '—'}
            </span>
          ) : (
            <span className="s7-muted s7-small">—</span>
          ),
      },
      {
        key: 'learners',
        header: 'Learners',
        sort: (o) => o.learnerCount,
        render: (o) => (
          <span
            className="s7-mono"
            title="Counted through org-owned enrolments, not memberships — the enrolment is what the organization can actually see."
          >
            {o.learnerCount}
          </span>
        ),
      },
      {
        key: 'cohorts',
        header: 'Cohorts',
        sort: (o) => o.cohortCount,
        render: (o) => <span className="s7-mono">{o.cohortCount}</span>,
      },
      {
        key: 'status',
        header: 'Status',
        render: (o) => (
          <Badge tone={o.status === 'Active' ? 'success' : o.status === 'Suspended' ? 'warning' : 'muted'}>
            {o.status}
          </Badge>
        ),
      },
    ],
    [orgs.data],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible" className="s7-page">
      <PageTitle
        icon={<Building2 size={18} />}
        title="Organizations"
        subtitle="Schools, districts and tutoring centres — their people, their classes, and exactly how much of a learner they can see"
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={14} /> New organization
          </Button>
        }
      />

      <StatRow>
        <Stat icon={<Building2 size={15} />} label="Organizations" value={orgs.data.length} sub={`${schools} school(s)`} />
        <Stat icon={<Users2 size={15} />} label="Cohorts" value={cohorts} sub="Classes, groups and tutoring batches" />
        <Stat
          icon={<GitBranch size={15} />}
          label="Enrolled learners"
          value={learners}
          sub="Counted through org-owned enrolments — the only thing an organization can see through"
          tone={learners > 0 ? 'brand' : 'cool'}
        />
      </StatRow>

      <Card>
        <CardHeader icon={<Building2 size={16} />} title="Organizations" />
        <CardBody>
          <DataTable
            rows={orgs.data}
            columns={columns}
            getId={(o) => o.id}
            loading={orgs.loading}
            initialSort={{ key: 'name' }}
            onRowClick={(o) => setSelectedId(o.id === selectedId ? null : o.id)}
            empty={
              <EmptyState icon={<Building2 size={28} />}>
                <strong>No organization yet</strong>
                <span className="s7-empty-hint">
                  Nothing is seeded, and the platform works without any of this — every learner today is B2C, with
                  enrolments they own themselves. An organization is what makes a class, a teacher’s view and a
                  school report possible.
                </span>
              </EmptyState>
            }
          />
        </CardBody>
      </Card>

      {selected ? <OrgDetail org={selected} actions={actions} onChanged={reload} /> : null}

      <GuardianSection actions={actions} />

      <NewOrganizationModal
        open={creating}
        orgs={orgs.data}
        busy={actions.busyId === 'new'}
        onClose={() => setCreating(false)}
        onCreate={async (body) => {
          await actions.createOrganization(body)
          setCreating(false)
        }}
      />
    </motion.div>
  )
}

// ---------------------------------------------------------------- org detail

function OrgDetail({
  org,
  actions,
  onChanged,
}: {
  org: Organization
  actions: ReturnType<typeof useOrganizationActions>
  onChanged: () => void
}) {
  const [tab, setTab] = useState<Tab>('cohorts')

  return (
    <Card>
      <CardHeader
        icon={<Layers size={16} />}
        title={
          <span>
            {org.name} <span className="s7-muted s7-small s7-mono">{org.orgKey}</span>
          </span>
        }
        actions={
          <Segmented
            value={tab}
            onChange={setTab}
            layoutId="org-detail-tab"
            options={[
              { value: 'cohorts', label: 'Cohorts' },
              { value: 'members', label: 'People' },
              { value: 'overlays', label: 'Curriculum overlays' },
            ]}
          />
        }
      />
      <CardBody>
        {tab === 'cohorts' ? <CohortsTab org={org} actions={actions} onChanged={onChanged} /> : null}
        {tab === 'members' ? <MembersTab org={org} actions={actions} /> : null}
        {tab === 'overlays' ? <OverlaysTab org={org} /> : null}
      </CardBody>
    </Card>
  )
}

// ------------------------------------------------------------------ cohorts

function CohortsTab({
  org,
  actions,
  onChanged,
}: {
  org: Organization
  actions: ReturnType<typeof useOrganizationActions>
  onChanged: () => void
}) {
  const cohorts = useResource<Cohort[]>(`/api/admin/organizations/${org.id}/cohorts`, [])
  const [openId, setOpenId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  const reload = () => {
    void cohorts.reload()
    onChanged()
  }

  return (
    <div className="s7-stack">
      <div className="s7-row-end">
        <Button variant="ghost" onClick={() => setCreating(true)}>
          <Plus size={14} /> New cohort
        </Button>
      </div>

      {cohorts.data.length === 0 ? (
        <EmptyState icon={<Users2 size={28} />}>
          <strong>No cohorts</strong>
          <span className="s7-empty-hint">
            A cohort is the unit a teacher actually teaches, and the relation an assignment resolves against.
            Without one, the assignment play context has nothing to name.
          </span>
        </EmptyState>
      ) : (
        <div className="s7-org-cohorts">
          {cohorts.data.map((c) => (
            <CohortCard
              key={c.id}
              cohort={c}
              open={openId === c.id}
              actions={actions}
              onToggle={() => setOpenId(openId === c.id ? null : c.id)}
              onChanged={reload}
            />
          ))}
        </div>
      )}

      <NewCohortModal
        open={creating}
        orgId={org.id}
        busy={actions.busyId === 'new-cohort'}
        onClose={() => setCreating(false)}
        onCreate={async (body) => {
          await actions.createCohort(body)
          setCreating(false)
          reload()
        }}
      />
    </div>
  )
}

function CohortCard({
  cohort,
  open,
  actions,
  onToggle,
  onChanged,
}: {
  cohort: Cohort
  open: boolean
  actions: ReturnType<typeof useOrganizationActions>
  onToggle: () => void
  onChanged: () => void
}) {
  return (
    <div className={`s7-org-cohort ${open ? 'is-open' : ''}`}>
      <button type="button" className="s7-org-cohort-head" onClick={onToggle}>
        <div className="s7-stack-tight">
          <strong>{cohort.name}</strong>
          <span className="s7-muted s7-small">
            {cohort.academicPeriod || 'no period named'}
            {cohort.placementLabel ? ` · ${cohort.placementLabel}` : ''}
          </span>
        </div>
        <div className="s7-org-cohort-counts">
          <Badge tone="muted">{cohort.learnerCount} learners</Badge>
          <Badge tone="muted">{cohort.teacherCount} staff</Badge>
          {cohort.overlayId ? <Badge tone="info">overlaid</Badge> : null}
          {cohort.status === 'Archived' ? <Badge tone="muted">archived</Badge> : null}
        </div>
      </button>

      {open ? <CohortDetail cohort={cohort} actions={actions} onChanged={onChanged} /> : null}
    </div>
  )
}

function CohortDetail({
  cohort,
  actions,
  onChanged,
}: {
  cohort: Cohort
  actions: ReturnType<typeof useOrganizationActions>
  onChanged: () => void
}) {
  const members = useResource<CohortMember[]>(
    `/api/admin/organizations/cohorts/${cohort.id}/members`,
    [],
  )
  const report = useResource<CohortReport | null>(
    `/api/admin/organizations/cohorts/${cohort.id}/report`,
    null,
  )
  const learners = useResource<CohortLearnerRow[]>(
    `/api/admin/organizations/cohorts/${cohort.id}/learners`,
    [],
  )
  const assignments = useResource<Assignment[]>(
    `/api/admin/organizations/cohorts/${cohort.id}/assignments`,
    [],
  )

  const [adding, setAdding] = useState(false)
  const [assigning, setAssigning] = useState(false)

  const reload = () => {
    void members.reload()
    void report.reload()
    void learners.reload()
    void assignments.reload()
    onChanged()
  }

  const suppression = report.data
    ? suppressionBlurb(report.data.suppression, report.data.smallCellThreshold)
    : null

  return (
    <div className="s7-org-cohort-body">
      {/* ---- roster ---- */}
      <section className="s7-stack">
        <div className="s7-row-end">
          <h4 className="s7-subhead">Roster</h4>
          <Button variant="ghost" onClick={() => setAdding(true)}>
            <UserPlus size={14} /> Add
          </Button>
        </div>

        <Note>
          Adding a learner creates an <strong>org-owned enrolment</strong>, and that enrolment is the only thing
          this organization can see through. It sees the work done under it from this moment — and nothing the
          learner did before, including work on the very same lessons.
        </Note>

        {members.data.length === 0 ? (
          <EmptyState icon={<Users2 size={24} />}>
            <strong>Nobody on the roster</strong>
            <span className="s7-empty-hint">A cohort with no teacher cannot be reported on by anybody.</span>
          </EmptyState>
        ) : (
          <div className="s7-kv">
            {members.data.map((m) => (
              <div className="s7-kv-row" key={m.id}>
                <span className="s7-kv-label">
                  {m.fullName || m.userName}
                  <span className="s7-muted s7-small"> · {m.userName}</span>
                </span>
                <span className="s7-kv-value">
                  <Badge tone={m.role === 'Learner' ? 'muted' : 'info'}>{m.role}</Badge>
                  {m.role === 'Learner' && !m.enrollmentId ? (
                    <span
                      title="No enrolment was provisioned, because the cohort names no curriculum version. This organization sees none of their work."
                    >
                      <Badge tone="warning">no enrolment</Badge>
                    </span>
                  ) : null}
                  <IconButton
                    label="Remove from cohort"
                    onClick={() => void actions.removeCohortMember(m.id).then(reload)}
                    busy={actions.busyId === m.id}
                  >
                    <Trash2 size={14} />
                  </IconButton>
                </span>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* ---- report ---- */}
      <section className="s7-stack">
        <h4 className="s7-subhead">How the cohort stands</h4>

        {suppression ? <Note tone="warning">{suppression}</Note> : null}

        {report.data && report.data.strugglingTargets.length > 0 ? (
          <>
            <Note>
              A whole cohort failing one claim together is far more often a <strong>teaching</strong> signal than
              twenty-eight simultaneous misconceptions. That is what this list is for.
            </Note>
            <div className="s7-kv">
              {report.data.strugglingTargets.map((t) => (
                <div className="s7-kv-row" key={t.targetId}>
                  <span className="s7-kv-label">
                    {t.statement}
                    {t.isPlaceholder ? (
                      <span
                        className="s7-muted s7-small"
                        title="A lesson standing in for a competency. It can honestly support 'on the questions in this lesson' and nothing wider."
                      >
                        {' '}
                        · placeholder
                      </span>
                    ) : null}
                  </span>
                  <span className="s7-kv-value">
                    <span className="s7-mono">
                      {t.masteredCount}/{t.reportableCount} mastered
                    </span>
                    <Badge tone={t.notMetCount > t.masteredCount ? 'warning' : 'muted'}>
                      {t.notMetCount} not met
                    </Badge>
                  </span>
                </div>
              ))}
            </div>
          </>
        ) : report.data && report.data.suppression === 'None' ? (
          <EmptyState icon={<ShieldCheck size={24} />}>
            <strong>Nothing stands out</strong>
            <span className="s7-empty-hint">
              No claim has enough evidence behind it to be called a cohort-wide problem.
            </span>
          </EmptyState>
        ) : null}

        <div className="s7-org-learners">
          {learners.data.map((l) => (
            <div className="s7-org-learner" key={l.learnerId}>
              <div className="s7-stack-tight">
                <strong>{l.fullName || l.userName}</strong>
                <span className="s7-muted s7-small">
                  {l.observationCount} answers this organization can see
                  {l.lastActiveOn ? ` · last worked ${l.lastActiveOn}` : ' · nothing yet'}
                </span>
              </div>
              <div className="s7-org-learner-verdicts">
                <Badge tone="success">{l.masteredCount} met</Badge>
                <Badge tone="muted">{l.developingCount} developing</Badge>
                <Badge tone={l.notMetCount > 0 ? 'warning' : 'muted'}>{l.notMetCount} not met</Badge>
              </div>
            </div>
          ))}
        </div>

        {learners.data.length > 0 ? (
          <span className="s7-muted s7-small">
            <EyeOff size={12} /> No play times, session lengths or device data appear here, and there is no field
            on this response that could carry them. A teacher seeing error patterns is pedagogy; a teacher seeing
            that a child was answering at eleven at night is not.
          </span>
        ) : null}
      </section>

      {/* ---- assignments ---- */}
      <section className="s7-stack">
        <div className="s7-row-end">
          <h4 className="s7-subhead">Assignments</h4>
          <Button variant="ghost" onClick={() => setAssigning(true)}>
            <ClipboardList size={14} /> Set work
          </Button>
        </div>

        {assignments.data.length === 0 ? (
          <EmptyState icon={<ClipboardList size={24} />}>
            <strong>No work set</strong>
            <span className="s7-empty-hint">
              An assignment is a cohort, a piece of content and a declared set of conditions. Its evidence follows
              those conditions like everything else.
            </span>
          </EmptyState>
        ) : (
          <div className="s7-kv">
            {assignments.data.map((a) => (
              <div className="s7-kv-row" key={a.id}>
                <span className="s7-kv-label">
                  {a.title}
                  {a.nodeTitle ? <span className="s7-muted s7-small"> · {a.nodeTitle}</span> : null}
                </span>
                <span className="s7-kv-value">
                  {a.isSupervised ? (
                    <span title="Supervised, so its evidence can carry the conditions an exam claim rests on.">
                      <Badge tone="success">supervised</Badge>
                    </span>
                  ) : (
                    <span title="Unsupervised. Practice-class evidence at best, because you cannot know who did it.">
                      <Badge tone="muted">homework</Badge>
                    </span>
                  )}
                  {a.dueAtUtc ? (
                    <span className="s7-muted s7-small">
                      <CalendarClock size={12} /> {a.dueAtUtc.slice(0, 10)}
                    </span>
                  ) : null}
                  {a.withdrawnAtUtc ? (
                    <Badge tone="muted">withdrawn</Badge>
                  ) : (
                    <IconButton
                      label="Withdraw"
                      onClick={() => void actions.withdrawAssignment(a.id).then(reload)}
                      busy={actions.busyId === a.id}
                    >
                      <Trash2 size={14} />
                    </IconButton>
                  )}
                </span>
              </div>
            ))}
          </div>
        )}
      </section>

      <AddMemberModal
        open={adding}
        busy={actions.busyId === cohort.id}
        onClose={() => setAdding(false)}
        onAdd={async (userId, role) => {
          await actions.addCohortMember(cohort.id, userId, role)
          setAdding(false)
          reload()
        }}
      />

      <NewAssignmentModal
        open={assigning}
        cohortId={cohort.id}
        busy={actions.busyId === 'new-assignment'}
        onClose={() => setAssigning(false)}
        onCreate={async (body) => {
          await actions.createAssignment(body)
          setAssigning(false)
          reload()
        }}
      />
    </div>
  )
}

// ------------------------------------------------------------------ members

function MembersTab({
  org,
  actions,
}: {
  org: Organization
  actions: ReturnType<typeof useOrganizationActions>
}) {
  const members = useResource<Membership[]>(`/api/admin/organizations/${org.id}/members`, [])
  const [granting, setGranting] = useState(false)

  const reload = () => {
    void members.reload()
  }

  return (
    <div className="s7-stack">
      <Note>
        An organization role is not the platform’s flat <span className="s7-mono">Teacher</span> claim, and does
        not replace it. A global role says nothing about <em>whose</em> children a person may look at, which is
        the only question that matters here.
      </Note>

      <div className="s7-row-end">
        <Button variant="ghost" onClick={() => setGranting(true)}>
          <UserPlus size={14} /> Grant a role
        </Button>
      </div>

      {members.data.length === 0 ? (
        <EmptyState icon={<Users2 size={28} />}>
          <strong>Nobody yet</strong>
          <span className="s7-empty-hint">An organization with no admin cannot be run by anyone but the platform.</span>
        </EmptyState>
      ) : (
        <div className="s7-kv">
          {members.data.map((m) => (
            <div className="s7-kv-row" key={m.id}>
              <span className="s7-kv-label">
                {m.fullName || m.userName}
                <span className="s7-muted s7-small"> · {m.userName}</span>
              </span>
              <span className="s7-kv-value">
                <span title={ROLE_BLURB[m.role]}>
                  <Badge tone={m.role === 'OrgAdmin' ? 'info' : 'muted'}>{m.role}</Badge>
                </span>
                {m.revokedAtUtc ? (
                  <span title="Kept rather than deleted — a report about last term has to name who taught it.">
                    <Badge tone="muted">revoked</Badge>
                  </span>
                ) : (
                  <IconButton
                    label="Revoke"
                    onClick={() => void actions.revokeMembership(m.id).then(reload)}
                    busy={actions.busyId === m.id}
                  >
                    <Trash2 size={14} />
                  </IconButton>
                )}
              </span>
            </div>
          ))}
        </div>
      )}

      <GrantRoleModal
        open={granting}
        busy={actions.busyId === org.id}
        onClose={() => setGranting(false)}
        onGrant={async (userId, role) => {
          await actions.grantMembership(org.id, userId, role)
          setGranting(false)
          reload()
        }}
      />
    </div>
  )
}

// ----------------------------------------------------------------- overlays

function OverlaysTab({ org }: { org: Organization }) {
  const overlays = useResource<Overlay[]>(`/api/admin/organizations/${org.id}/overlays`, [])

  return (
    <div className="s7-stack">
      <Note>
        An overlay is a <strong>diff, resolved at read time</strong> — never a change to the official curriculum.
        A school can reorder the national syllabus, hide a chapter it covers elsewhere and insert two of its own
        lessons, and the ministry’s version is byte-for-byte the same afterwards. Only this org’s cohorts read it.
      </Note>

      {overlays.data.length === 0 ? (
        <EmptyState icon={<GitBranch size={28} />}>
          <strong>No overlay</strong>
          <span className="s7-empty-hint">
            Cohorts here read the published curriculum exactly as it was authored.
          </span>
        </EmptyState>
      ) : (
        <div className="s7-kv">
          {overlays.data.map((o) => (
            <div className="s7-kv-row" key={o.id}>
              <span className="s7-kv-label">
                {o.name}
                <span className="s7-muted s7-small"> · {o.edits.length} edit(s)</span>
                {o.sourceNote ? <span className="s7-muted s7-small"> · {o.sourceNote}</span> : null}
              </span>
              <span className="s7-kv-value">
                <Badge tone={o.status === 'Published' ? 'success' : 'muted'}>{o.status}</Badge>
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------- guardians

function GuardianSection({ actions }: { actions: ReturnType<typeof useOrganizationActions> }) {
  const [learnerId, setLearnerId] = useState('')
  const [query, setQuery] = useState<string | null>(null)

  const links = useResource<GuardianLink[]>(
    query ? `/api/admin/organizations/guardians/learner/${query}` : null,
    [],
  )

  return (
    <Card>
      <CardHeader icon={<Heart size={16} />} title="Guardian links" />
      <CardBody>
        <Note>
          Consent is a row with a scope and a revocation date, not a boolean set at signup. A boolean cannot
          answer “did they agree to <em>this</em>” — and on a platform whose audience is children, every new
          feature asks that question again. An <strong>unverified link grants nothing</strong>: anybody can type a
          child’s user name.
        </Note>

        <div className="s7-filters">
          <Input
            mono
            placeholder="Learner user id"
            value={learnerId}
            onChange={(e) => setLearnerId(e.currentTarget.value)}
          />
          <Button variant="ghost" onClick={() => setQuery(learnerId.trim() || null)}>
            Look up
          </Button>
        </div>

        {query === null ? (
          <EmptyState icon={<Heart size={28} />}>
            <strong>Look up a learner</strong>
            <span className="s7-empty-hint">
              Guardian links are found by the child they are about, because that is the question support is
              always answering.
            </span>
          </EmptyState>
        ) : links.data.length === 0 ? (
          <EmptyState icon={<Heart size={28} />}>
            <strong>No guardian is linked</strong>
            <span className="s7-empty-hint">
              Nobody can read this learner’s reports but the learner. Note that a learner under 18 cannot consent
              to their own exam result being used for calibration either — a verified guardian is what makes that
              obtainable.
            </span>
          </EmptyState>
        ) : (
          <div className="s7-kv">
            {links.data.map((l) => (
              <div className="s7-kv-row" key={l.id}>
                <span className="s7-kv-label">
                  {l.guardianUserName}
                  <span className="s7-muted s7-small"> · {l.relationship}</span>
                  <span className="s7-muted s7-small">
                    {' '}
                    · {consentList(l.consentScope).join(', ') || 'consents to nothing'}
                  </span>
                </span>
                <span className="s7-kv-value">
                  {l.revokedAtUtc ? (
                    <Badge tone="muted">revoked</Badge>
                  ) : l.verifiedAtUtc ? (
                    <Badge tone="success">verified</Badge>
                  ) : (
                    <>
                      <Badge tone="warning">unverified</Badge>
                      <Button
                        variant="ghost"
                        loading={actions.busyId === l.id}
                        onClick={() => void actions.verifyGuardianLink(l.id).then(() => links.reload())}
                      >
                        Verify
                      </Button>
                    </>
                  )}
                  {!l.revokedAtUtc ? (
                    <IconButton
                      label="Revoke"
                      onClick={() => void actions.revokeGuardianLink(l.id).then(() => links.reload())}
                      busy={actions.busyId === l.id}
                    >
                      <Trash2 size={14} />
                    </IconButton>
                  ) : null}
                </span>
              </div>
            ))}
          </div>
        )}
      </CardBody>
    </Card>
  )
}

// ------------------------------------------------------------------- modals

function NewOrganizationModal({
  open,
  orgs,
  busy,
  onClose,
  onCreate,
}: {
  open: boolean
  orgs: Organization[]
  busy: boolean
  onClose: () => void
  onCreate: (body: {
    orgKey: string
    name: string
    kind: OrganizationKind
    countryCode: string
    parentOrgId: string | null
  }) => Promise<void>
}) {
  const [name, setName] = useState('')
  const [orgKey, setOrgKey] = useState('')
  const [kind, setKind] = useState<OrganizationKind>('School')
  const [country, setCountry] = useState('EG')
  const [parent, setParent] = useState('')

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<Building2 size={16} />}
      title="New organization"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button
            loading={busy}
            disabled={!name.trim() || !orgKey.trim()}
            onClick={() =>
              void onCreate({
                orgKey: orgKey.trim(),
                name: name.trim(),
                kind,
                countryCode: country.trim(),
                parentOrgId: parent || null,
              })
            }
          >
            Create
          </Button>
        </>
      }
    >
      <Field label="Name">
        <Input value={name} onChange={(e) => setName(e.currentTarget.value)} placeholder="Nile International School" />
      </Field>

      <Field label="Key" hint="Stable and human-readable. What an integration quotes and support says out loud.">
        <Input mono value={orgKey} onChange={(e) => setOrgKey(e.currentTarget.value)} placeholder="nile-international" />
      </Field>

      <Field label="Kind" hint={KIND_BLURB[kind]}>
        <Select value={kind} onChange={(e) => setKind(e.currentTarget.value as OrganizationKind)}>
          {(Object.keys(KIND_BLURB) as OrganizationKind[]).map((k) => (
            <option key={k} value={k}>
              {k}
            </option>
          ))}
        </Select>
      </Field>

      <Field
        label="Country"
        hint="Not decoration — retention and consent rules are national, and a report crossing a border is a different legal object."
      >
        <Input mono value={country} onChange={(e) => setCountry(e.currentTarget.value)} placeholder="EG" />
      </Field>

      <Field label="Under" hint="Leave empty for a root organization. An admin of a parent administers everything beneath it.">
        <Select value={parent} onChange={(e) => setParent(e.currentTarget.value)}>
          <option value="">— none —</option>
          {orgs.map((o) => (
            <option key={o.id} value={o.id}>
              {o.name}
            </option>
          ))}
        </Select>
      </Field>
    </Modal>
  )
}

function NewCohortModal({
  open,
  orgId,
  busy,
  onClose,
  onCreate,
}: {
  open: boolean
  orgId: string
  busy: boolean
  onClose: () => void
  onCreate: (body: {
    orgId: string
    name: string
    academicPeriod: string
    curriculumVersionId: string | null
    placementNodeId: string | null
  }) => Promise<void>
}) {
  const versions = useResource<{ id: string; name: string }[]>(
    '/api/admin/organizations/curriculum-versions',
    [],
  )

  const [name, setName] = useState('')
  const [period, setPeriod] = useState('')
  const [versionId, setVersionId] = useState('')

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<Users2 size={16} />}
      title="New cohort"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button
            loading={busy}
            disabled={!name.trim()}
            onClick={() =>
              void onCreate({
                orgId,
                name: name.trim(),
                academicPeriod: period.trim(),
                curriculumVersionId: versionId || null,
                placementNodeId: null,
              })
            }
          >
            Create
          </Button>
        </>
      }
    >
      <Field label="Name">
        <Input value={name} onChange={(e) => setName(e.currentTarget.value)} placeholder="6B" />
      </Field>

      <Field
        label="Academic period"
        hint="As the organization writes it. A string rather than a date range, because academic periods are named institutionally and no two customers agree where one ends."
      >
        <Input value={period} onChange={(e) => setPeriod(e.currentTarget.value)} placeholder="2026/2027 Term 1" />
      </Field>

      <Field
        label="Curriculum"
        hint="Required for the cohort to enrol learners at all — the enrolment is what gives this organization sight of their work. Leave empty for a group that is people rather than a course."
      >
        <Select value={versionId} onChange={(e) => setVersionId(e.currentTarget.value)}>
          <option value="">— none —</option>
          {versions.data.map((v) => (
            <option key={v.id} value={v.id}>
              {v.name}
            </option>
          ))}
        </Select>
      </Field>
    </Modal>
  )
}

function GrantRoleModal({
  open,
  busy,
  onClose,
  onGrant,
}: {
  open: boolean
  busy: boolean
  onClose: () => void
  onGrant: (userId: string, role: OrgRole) => Promise<void>
}) {
  const [userId, setUserId] = useState('')
  const [role, setRole] = useState<OrgRole>('Teacher')

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<UserPlus size={16} />}
      title="Grant a role"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={busy} disabled={!userId.trim()} onClick={() => void onGrant(userId.trim(), role)}>
            Grant
          </Button>
        </>
      }
    >
      <Field label="User id">
        <Input mono value={userId} onChange={(e) => setUserId(e.currentTarget.value)} />
      </Field>

      <Field label="Role" hint={ROLE_BLURB[role]}>
        <Select value={role} onChange={(e) => setRole(e.currentTarget.value as OrgRole)}>
          {(Object.keys(ROLE_BLURB) as OrgRole[]).map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </Select>
      </Field>
    </Modal>
  )
}

function AddMemberModal({
  open,
  busy,
  onClose,
  onAdd,
}: {
  open: boolean
  busy: boolean
  onClose: () => void
  onAdd: (userId: string, role: CohortRole) => Promise<void>
}) {
  const [userId, setUserId] = useState('')
  const [role, setRole] = useState<CohortRole>('Learner')

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<UserPlus size={16} />}
      title="Add to the cohort"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button loading={busy} disabled={!userId.trim()} onClick={() => void onAdd(userId.trim(), role)}>
            Add
          </Button>
        </>
      }
    >
      <Field label="User id">
        <Input mono value={userId} onChange={(e) => setUserId(e.currentTarget.value)} />
      </Field>

      <Field
        label="Role"
        hint={
          role === 'Learner'
            ? 'Creates an org-owned enrolment. This organization will see the work done under it from now on — and nothing done before.'
            : 'Teaching staff see the learners in this cohort and no others.'
        }
      >
        <Select value={role} onChange={(e) => setRole(e.currentTarget.value as CohortRole)}>
          <option value="Learner">Learner</option>
          <option value="Teacher">Teacher</option>
          <option value="Assistant">Assistant</option>
        </Select>
      </Field>
    </Modal>
  )
}

function NewAssignmentModal({
  open,
  cohortId,
  busy,
  onClose,
  onCreate,
}: {
  open: boolean
  cohortId: string
  busy: boolean
  onClose: () => void
  onCreate: (body: {
    cohortId: string
    title: string
    nodeId: string | null
    assessmentFormId: string | null
    dueAtUtc: string | null
    isSupervised: boolean
  }) => Promise<void>
}) {
  const [title, setTitle] = useState('')
  const [nodeId, setNodeId] = useState('')
  const [due, setDue] = useState('')
  const [supervised, setSupervised] = useState(false)

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<ClipboardList size={16} />}
      title="Set work"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button
            loading={busy}
            disabled={!title.trim() || !nodeId.trim()}
            onClick={() =>
              void onCreate({
                cohortId,
                title: title.trim(),
                nodeId: nodeId.trim(),
                assessmentFormId: null,
                dueAtUtc: due ? new Date(due).toISOString() : null,
                isSupervised: supervised,
              })
            }
          >
            Set
          </Button>
        </>
      }
    >
      <Field label="Title">
        <Input value={title} onChange={(e) => setTitle(e.currentTarget.value)} placeholder="Fractions, exercises 1–12" />
      </Field>

      <Field label="Lesson id" hint="Exactly one of a lesson or a form. Both would leave the client choosing.">
        <Input mono value={nodeId} onChange={(e) => setNodeId(e.currentTarget.value)} />
      </Field>

      <Field label="Due">
        <Input type="date" value={due} onChange={(e) => setDue(e.currentTarget.value)} />
      </Field>

      <Field
        label="Supervised"
        hint={
          supervised
            ? 'A teacher will be watching, so this work can carry the conditions an exam-grade claim rests on.'
            : 'Homework. Its evidence stays practice-class however well it goes, because you cannot know who did it — and that is not a setting somebody can override.'
        }
      >
        <Switch checked={supervised} onChange={setSupervised} label="A teacher will be watching" />
      </Field>
    </Modal>
  )
}

export { CONSENT_LABELS }
