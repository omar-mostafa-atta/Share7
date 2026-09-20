// ===========================================================================
// Organizations — data access
//
// Five tables and one scoping column, which is the whole of multi-tenancy in
// this platform. Everything on this surface composes from them:
//
//   Organization      district -> school -> department, recursively
//   Membership        what a person may do inside one org
//   Cohort            a class, a group, a tutoring batch
//   CohortMembership  who is in it, and when they were
//   GuardianLink      a verified relationship and what it consents to
//
// The column is Enrollment.OwnerOrgId. **Adding a learner to a cohort is what
// creates visibility** — it provisions an org-owned enrolment, and org-side
// reads filter on that ownership. A school therefore sees the work done under
// its own enrolment and acquires nothing retroactively from the learner's
// private one. There is no "share my data" step because there is nothing to
// share: the rule falls out of the schema.
//
// See Docs/EducationalArchitecture.md §9, §17.4, §18 and §19.
// ===========================================================================

import { useCallback, useState } from 'react'
import { api } from '../../lib/client'
import { toast } from '../../store/toast'

// ------------------------------------------------------------ organizations

export type OrganizationKind =
  | 'District'
  | 'School'
  | 'Department'
  | 'TutoringCentre'
  | 'Publisher'

export type OrganizationStatus = 'Active' | 'Suspended' | 'Closed'

export type OrgRole = 'OrgAdmin' | 'Teacher' | 'Staff' | 'Learner'

export type MembershipStatus = 'Active' | 'Invited' | 'Revoked'

export type CohortRole = 'Learner' | 'Teacher' | 'Assistant'

export type CohortStatus = 'Active' | 'Archived'

export interface Organization {
  id: string
  orgKey: string
  parentOrgId: string | null
  kind: OrganizationKind
  name: string
  countryCode: string
  status: OrganizationStatus
  itemBankId: string | null
  createdAtUtc: string
  memberCount: number
  cohortCount: number

  /**
   * Learners counted through org-owned enrolments, not through memberships.
   * The enrolment is what the org can actually see; a membership with no
   * enrolment behind it grants visibility of nothing.
   */
  learnerCount: number
}

export interface Membership {
  id: string
  userId: string
  userName: string
  fullName: string | null
  orgId: string
  role: OrgRole
  status: MembershipStatus
  grantedAtUtc: string
  revokedAtUtc: string | null
}

// -------------------------------------------------------------------- cohorts

export interface Cohort {
  id: string
  orgId: string
  orgName: string
  name: string
  academicPeriod: string
  curriculumVersionId: string | null
  placementNodeId: string | null
  placementLabel: string | null
  overlayId: string | null
  status: CohortStatus
  createdAtUtc: string
  learnerCount: number
  teacherCount: number
}

export interface CohortMember {
  id: string
  userId: string
  userName: string
  fullName: string | null
  role: CohortRole
  joinedAtUtc: string
  leftAtUtc: string | null

  /** The org-owned enrolment this membership provisioned. Null for staff. */
  enrollmentId: string | null
}

// ------------------------------------------------------------------ reporting

export type SuppressionReason = 'None' | 'SmallCell' | 'NoEvidence' | 'OutOfScope'

export interface CohortTargetRow {
  targetId: string
  statement: string
  isPlaceholder: boolean
  learnerCount: number
  reportableCount: number
  masteredCount: number
  developingCount: number
  notMetCount: number
  insufficientCount: number

  /** Null whenever suppression is not None. Never zero in that case. */
  masteredShare: number | null
  suppression: SuppressionReason
  lastObservedAtUtc: string | null
}

export interface CohortReport {
  cohortId: string
  cohortName: string
  academicPeriod: string
  orgId: string
  orgName: string
  learnerCount: number
  activeLearnerCount: number
  smallCellThreshold: number
  suppression: SuppressionReason
  targets: CohortTargetRow[]
  strugglingTargets: CohortTargetRow[]
  observationCount: number
}

export interface CohortLearnerRow {
  learnerId: string
  userName: string
  fullName: string | null
  reportableTargetCount: number
  masteredCount: number
  developingCount: number
  notMetCount: number
  observationCount: number

  /**
   * A date, never a timestamp. "Has not worked on this for two weeks" is a
   * teaching fact; "was answering at 23:41" is not the school's business.
   */
  lastActiveOn: string | null
}

// ------------------------------------------------------------------ guardians

export type GuardianRelationship = 'Parent' | 'Guardian' | 'Relative' | 'Carer'

/** Flags, matching GuardianConsentScope on the server. */
export const CONSENT = {
  None: 0,
  ViewProgress: 1,
  ViewAssessments: 2,
  CalibrationUse: 4,
  ManageEnrollment: 8,
} as const

export const CONSENT_LABELS: [number, string, string][] = [
  [CONSENT.ViewProgress, 'Progress', 'Mastery verdicts, coverage and next steps'],
  [CONSENT.ViewAssessments, 'Assessments', 'Sittings and their results'],
  [
    CONSENT.CalibrationUse,
    'Calibration',
    'Lets the learner’s reported exam result join the calibration sample — the one scope a learner under 18 cannot grant for themselves',
  ],
  [CONSENT.ManageEnrollment, 'Enrolment', 'Act on the learner’s behalf: curriculum, invitations'],
]

export interface GuardianLink {
  id: string
  guardianUserId: string
  guardianUserName: string
  learnerUserId: string
  learnerUserName: string
  relationship: GuardianRelationship
  consentScope: number
  verifiedAtUtc: string | null
  revokedAtUtc: string | null
  createdAtUtc: string
}

// ----------------------------------------------------------------- assignments

export interface Assignment {
  id: string
  cohortId: string
  title: string
  nodeId: string | null
  nodeTitle: string | null
  assessmentFormId: string | null
  assignedAtUtc: string
  dueAtUtc: string | null

  /**
   * Whether a teacher will be watching. The single input that decides whether
   * this work can support an exam-grade claim — unsupervised homework is
   * honest practice and nothing more, because you cannot know who did it.
   */
  isSupervised: boolean
  withdrawnAtUtc: string | null
  learnerCount: number
  completedCount: number
}

// -------------------------------------------------------------------- overlays

export type OverlayEditKind = 'Hide' | 'Reorder' | 'Insert' | 'Substitute' | 'Repace'

export type OverlayStatus = 'Draft' | 'Published' | 'Withdrawn'

export interface OverlayEdit {
  id: string
  kind: OverlayEditKind
  targetNodeId: string
  targetNodeTitle: string | null
  replacementNodeId: string | null
  replacementNodeTitle: string | null
  newOrder: number | null
  scheduledFromUtc: string | null
  scheduledToUtc: string | null
  applyOrder: number
  note: string | null
}

export interface Overlay {
  id: string
  overlayKey: string
  orgId: string
  curriculumVersionId: string
  name: string
  status: OverlayStatus
  sourceNote: string | null
  createdAtUtc: string
  publishedAtUtc: string | null
  edits: OverlayEdit[]
}

// --------------------------------------------------------------------- copy

export const KIND_BLURB: Record<OrganizationKind, string> = {
  District: 'A group of schools under one administration',
  School: 'The common case, and what every report is designed around',
  Department: 'A faculty or subject department inside a school',
  TutoringCentre: 'Private tuition — usually several unconnected cohorts per teacher',
  Publisher: 'Content-side rather than learner-side: owns a bank, never a roster',
}

export const ROLE_BLURB: Record<OrgRole, string> = {
  OrgAdmin: 'Runs the organization: membership, cohorts, configuration, aggregate reporting',
  Teacher: 'Sees learners through cohort membership and through nothing else',
  Staff: 'Rosters and logistics. No learner evidence',
  Learner: 'A learner the organization has enrolled',
}

/**
 * Why a figure is withheld, in the words an admin needs.
 *
 * Suppression is always a stated reason and never a blank: a report that
 * silently omits small cohorts teaches its reader that blank means zero, and a
 * head teacher acting on "0% mastered" for a class of four is acting on a
 * fabrication.
 */
export function suppressionBlurb(reason: SuppressionReason, threshold: number): string | null {
  switch (reason) {
    case 'SmallCell':
      return `Fewer than ${threshold} learners. A share over a class this small is both statistically meaningless and identifying — "one learner has not met this" names a child to anybody who knows the others.`
    case 'NoEvidence':
      return 'Nobody here has produced admissible evidence on this yet. Not a zero — an absence.'
    case 'OutOfScope':
      return 'None of these learners’ enrolments belong to this organization, so there is nothing it may see.'
    default:
      return null
  }
}

export function consentList(scope: number): string[] {
  return CONSENT_LABELS.filter(([bit]) => (scope & bit) === bit).map(([, label]) => label)
}

// ------------------------------------------------------------------- actions

export function useOrganizationActions(onChanged: () => void) {
  const [busyId, setBusyId] = useState<string | null>(null)

  const run = useCallback(
    async (id: string, work: () => Promise<void>) => {
      setBusyId(id)
      try {
        await work()
        onChanged()
      } finally {
        setBusyId(null)
      }
    },
    [onChanged],
  )

  const createOrganization = useCallback(
    (body: {
      orgKey: string
      name: string
      kind: OrganizationKind
      countryCode: string
      parentOrgId: string | null
    }) =>
      run('new', async () => {
        await api.post('/api/admin/organizations', body)
        toast.success(
          'Organization created',
          'Its own item bank was created with it, capped at practice-class evidence. Nothing the organization authors can move a national exam projection until somebody qualified reviews it.',
        )
      }),
    [run],
  )

  const grantMembership = useCallback(
    (orgId: string, userId: string, role: OrgRole) =>
      run(orgId, async () => {
        await api.post(`/api/admin/organizations/${orgId}/members`, { userId, role })
        toast.success('Membership granted', ROLE_BLURB[role])
      }),
    [run],
  )

  const revokeMembership = useCallback(
    (membershipId: string) =>
      run(membershipId, async () => {
        await api.del(`/api/admin/organizations/members/${membershipId}`)
        toast.success(
          'Membership revoked',
          'Kept rather than deleted — a report about last term has to name who taught it, and an access audit has to resolve against the grant that permitted it.',
        )
      }),
    [run],
  )

  const createCohort = useCallback(
    (body: {
      orgId: string
      name: string
      academicPeriod: string
      curriculumVersionId: string | null
      placementNodeId: string | null
    }) =>
      run('new-cohort', async () => {
        await api.post('/api/admin/organizations/cohorts', body)
        toast.success('Cohort created')
      }),
    [run],
  )

  const addCohortMember = useCallback(
    (cohortId: string, userId: string, role: CohortRole) =>
      run(cohortId, async () => {
        await api.post(`/api/admin/organizations/cohorts/${cohortId}/members`, { userId, role })
        toast.success(
          role === 'Learner' ? 'Learner enrolled' : 'Added to the cohort',
          role === 'Learner'
            ? 'An org-owned enrolment was created. From now on this organization sees the work done under it — and nothing the learner did before.'
            : undefined,
        )
      }),
    [run],
  )

  const removeCohortMember = useCallback(
    (cohortMembershipId: string) =>
      run(cohortMembershipId, async () => {
        await api.del(`/api/admin/organizations/cohorts/members/${cohortMembershipId}`)
        toast.success(
          'Removed from the cohort',
          'The enrolment was ended, not deleted. Evidence collected while they were in the class is real and stays attached to it.',
        )
      }),
    [run],
  )

  const createGuardianLink = useCallback(
    (body: {
      guardianUserId: string
      learnerUserId: string
      relationship: GuardianRelationship
      consentScope: number
    }) =>
      run('new-guardian', async () => {
        await api.post('/api/admin/organizations/guardians', body)
        toast.success(
          'Guardian link created — unverified',
          'It grants nothing until somebody who can actually check the relationship verifies it. Anybody can type a child’s user name.',
        )
      }),
    [run],
  )

  const verifyGuardianLink = useCallback(
    (linkId: string) =>
      run(linkId, async () => {
        await api.post(`/api/admin/organizations/guardians/${linkId}/verify`)
        toast.success('Guardian link verified')
      }),
    [run],
  )

  const setGuardianConsent = useCallback(
    (linkId: string, scope: number) =>
      run(linkId, async () => {
        await api.post(`/api/admin/organizations/guardians/${linkId}/consent`, { scope })
        toast.success('Consent updated')
      }),
    [run],
  )

  const revokeGuardianLink = useCallback(
    (linkId: string) =>
      run(linkId, async () => {
        await api.del(`/api/admin/organizations/guardians/${linkId}`)
        toast.success(
          'Guardian link revoked',
          'A timestamp, not a deletion — the period during which data was legitimately shared is what an audit has to reconstruct.',
        )
      }),
    [run],
  )

  const createAssignment = useCallback(
    (body: {
      cohortId: string
      title: string
      nodeId: string | null
      assessmentFormId: string | null
      dueAtUtc: string | null
      isSupervised: boolean
    }) =>
      run('new-assignment', async () => {
        await api.post('/api/admin/organizations/assignments', body)
        toast.success(
          'Assignment set',
          body.isSupervised
            ? 'Supervised, so its evidence can carry the conditions an exam claim rests on.'
            : 'Unsupervised. Its evidence stays practice-class, because you cannot know who did it.',
        )
      }),
    [run],
  )

  const withdrawAssignment = useCallback(
    (assignmentId: string) =>
      run(assignmentId, async () => {
        await api.del(`/api/admin/organizations/assignments/${assignmentId}`)
        toast.success('Assignment withdrawn')
      }),
    [run],
  )

  return {
    busyId,
    createOrganization,
    grantMembership,
    revokeMembership,
    createCohort,
    addCohortMember,
    removeCohortMember,
    createGuardianLink,
    verifyGuardianLink,
    setGuardianConsent,
    revokeGuardianLink,
    createAssignment,
    withdrawAssignment,
  }
}
