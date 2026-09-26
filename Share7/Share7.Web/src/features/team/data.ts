import { api } from '../../lib/client'
import { useResource } from '../../lib/resource'

// ===========================================================================
// Team & Access — wire types and calls
//
// Hand-written like types/api.ts, mirroring Share7.Application/Staff/Models/StaffModels.cs.
// Enums arrive by name.
//
// Two audiences, drawn by the server (Share7/Authorization/Policies.cs):
//   - Adding a member (POST /api/admin/team) and the scope options it is chosen from: Admins and
//     SuperAdmins. Creating an account is all an Admin does to the content team (2026-09-26).
//   - Everything else here — the team, a member's record, the audit log, the security settings:
//     SuperAdmins only.
// ===========================================================================

export type StudioRole = 'Author' | 'Reviewer' | 'Lead'
export type StaffStatus = 'Invited' | 'Active' | 'Suspended' | 'Deactivated'
export type SetupPurpose = 'Activation' | 'Reset'
export type SignInOutcome =
  | 'Succeeded'
  | 'WrongPassword'
  | 'LockedOut'
  | 'NotPermitted'
  | 'Suspended'
  | 'Deactivated'
  | 'TwoStepFailed'
  | 'SucceededWithRecoveryCode'
  | 'Activated'

export interface LocalizedTitle {
  en: string
  ar: string | null
}

export interface ScopeNode {
  id: string
  kind: string
  trail: LocalizedTitle[]
  exists: boolean
}

export interface ScopeLanguage {
  id: string
  code: string
  name: string
}

export interface StaffScope {
  allNodes: boolean
  nodes: ScopeNode[]
  allLanguages: boolean
  languages: ScopeLanguage[]
}

export interface PersonRef {
  userId: string
  name: string
}

export interface TeamCounts {
  active: number
  invited: number
  suspended: number
  deactivated: number
}

export interface TeamMemberListItem {
  userId: string
  username: string
  fullName: string
  jobTitle: string | null
  studioRole: StudioRole
  scope: StaffScope
  status: StaffStatus
  twoStepEnabled: boolean
  lastActiveAtUtc: string | null
  createdAtUtc: string
  activatedAtUtc: string | null
  setupLinkExpiresAtUtc: string | null
}

export interface LegacyContentAccount {
  userId: string
  username: string
  createdAtUtc: string
}

export interface TeamOverview {
  members: TeamMemberListItem[]
  legacyAccounts: LegacyContentAccount[]
  counts: TeamCounts
  requireTwoStep: boolean
  studioAddressConfigured: boolean
  studioAddress: string | null
}

export interface StaffSession {
  id: string
  createdAtUtc: string
  lastSeenAtUtc: string
  expiresAtUtc: string
  ipAddress: string | null
  device: string | null
  twoStepVerified: boolean
}

export interface StaffSignIn {
  occurredAtUtc: string
  outcome: SignInOutcome
  ipAddress: string | null
  device: string | null
}

export interface TeamMemberDetail {
  userId: string
  username: string
  fullName: string
  jobTitle: string | null
  workEmail: string | null
  interfaceLanguage: string
  studioRole: StudioRole
  scope: StaffScope
  status: StaffStatus
  statusReason: string | null
  statusChangedAtUtc: string | null
  statusChangedBy: PersonRef | null
  createdAtUtc: string
  createdBy: PersonRef | null
  activatedAtUtc: string | null
  lastActiveAtUtc: string | null
  notes: string | null
  security: {
    twoStepEnabled: boolean
    recoveryCodesLeft: number
    hasPassword: boolean
    lockedOutUntilUtc: string | null
    failedAttempts: number
  }
  setupLink: { purpose: SetupPurpose; createdAtUtc: string; expiresAtUtc: string } | null
  sessions: StaffSession[]
  recentSignIns: StaffSignIn[]
  rowVersion: string
}

export interface SetupLink {
  url: string
  isAbsolute: boolean
  expiresAtUtc: string
  purpose: SetupPurpose
}

export interface CreatedTeamMember {
  member: TeamMemberDetail

  /** Null for every member the console creates: the admin sets their password (2026-09-26). */
  setupLink: SetupLink | null
}

export interface ScopeTreeNode {
  id: string
  parentId: string | null
  kind: string
  title: LocalizedTitle
  depth: number
  order: number
}

export interface PasswordRules {
  minimumLength: number
  requireUppercase: boolean
  requireLowercase: boolean
  requireDigit: boolean
}

export interface TeamScopeOptions {
  nodes: ScopeTreeNode[]
  languages: ScopeLanguage[]
  passwordRules: PasswordRules
  studioAddress: string | null
}

export interface StaffSecuritySettings {
  requireTwoStep: boolean
  sessionLifetimeHours: number
  idleTimeoutHours: number
  minimumPasswordLength: number
  setupLinkLifetimeHours: number
  updatedAtUtc: string
  updatedBy: PersonRef | null
  activeMembersWithoutTwoStep: number
}

export interface AuditEvent {
  sequence: number
  occurredAtUtc: string
  actor: PersonRef | null
  actorRoles: string[]
  action: string
  area: string
  targetType: string | null
  targetId: string | null
  summary: string
  dataJson: string | null
  ipAddress: string | null
  device: string | null
  correlationId: string | null
}

export interface AuditPage {
  items: AuditEvent[]
  page: number
  pageSize: number
  total: number
}

export interface AuditFacets {
  areas: string[]
  actions: string[]
  actors: PersonRef[]
}

/** Who a member is, what they do and what they work on — the body of an add or an adoption. */
export interface MemberDraft {
  fullName: string
  username: string

  /** Set by the admin creating them; the member signs in with it straight away. */
  password: string
  workEmail: string
  jobTitle: string
  interfaceLanguage: 'en' | 'ar'
  studioRole: StudioRole
  allNodes: boolean
  nodeIds: string[]
  allLanguages: boolean
  languageIds: string[]
}

// ---------------------------------------------------------------------------
// Words
// ---------------------------------------------------------------------------

export const STUDIO_ROLES: { value: StudioRole; label: string; description: string }[] = [
  {
    value: 'Author',
    label: 'Author',
    description: 'Writes and edits drafts. Somebody else approves them before anything is released.',
  },
  {
    value: 'Reviewer',
    label: 'Reviewer',
    description: 'Everything an author does, and approves or sends back other people’s drafts.',
  },
  {
    value: 'Lead',
    label: 'Lead',
    description: 'Everything a reviewer does, and releases work to the game — their own included, without a second person.',
  },
]

export const STATUS_INFO: Record<StaffStatus, { label: string; tone: 'success' | 'warning' | 'danger' | 'muted' | 'info' }> = {
  Invited: { label: 'No password yet', tone: 'info' },
  Active: { label: 'Active', tone: 'success' },
  Suspended: { label: 'Suspended', tone: 'warning' },
  Deactivated: { label: 'Closed', tone: 'muted' },
}

export const OUTCOME_LABEL: Record<SignInOutcome, string> = {
  Succeeded: 'Signed in',
  WrongPassword: 'Wrong password',
  LockedOut: 'Locked out',
  NotPermitted: 'Not allowed in',
  Suspended: 'Refused — suspended',
  Deactivated: 'Refused — closed',
  TwoStepFailed: 'Wrong 2-step code',
  SucceededWithRecoveryCode: 'Signed in with a recovery code',
  Activated: 'First sign-in, from the setup link',
}

/** A scope in a few words: "Everything", "Primary 4 › Science and 2 more". */
export function describeScope(scope: StaffScope): { parts: string; languages: string } {
  const parts = scope.allNodes
    ? 'All of the curriculum'
    : scope.nodes.length === 0
      ? 'Nothing yet'
      : scope.nodes.length === 1
        ? scope.nodes[0].trail.map((t) => t.en).join(' › ')
        : `${scope.nodes[0].trail.map((t) => t.en).join(' › ')} and ${scope.nodes.length - 1} more`

  const languages = scope.allLanguages ? 'Every language' : scope.languages.map((l) => l.name).join(', ') || 'No language'
  return { parts, languages }
}

// ---------------------------------------------------------------------------
// Calls
// ---------------------------------------------------------------------------

const STAFF_RULES: PasswordRules = { minimumLength: 12, requireUppercase: true, requireLowercase: true, requireDigit: true }

export function useScopeOptions(enabled: boolean) {
  return useResource<TeamScopeOptions>(enabled ? '/api/admin/team/scope-options' : null, {
    nodes: [],
    languages: [],
    passwordRules: STAFF_RULES,
    studioAddress: null,
  })
}

/**
 * The one address every content-team member signs in at: the server's `Studio:PublicUrl` when it
 * is set, otherwise `/studio` on this site, which is where the API serves the Studio.
 */
export function studioAddress(configured: string | null | undefined): string {
  return configured || `${window.location.origin}/studio`
}

function body(draft: MemberDraft) {
  const blank = (value: string) => (value.trim() ? value.trim() : null)
  return {
    fullName: draft.fullName.trim(),
    workEmail: blank(draft.workEmail),
    jobTitle: blank(draft.jobTitle),
    studioRole: draft.studioRole,
    allNodes: draft.allNodes,
    nodeIds: draft.allNodes ? null : draft.nodeIds,
    allLanguages: draft.allLanguages,
    languageIds: draft.allLanguages ? null : draft.languageIds,
    interfaceLanguage: draft.interfaceLanguage,
  }
}

/** Silent: the form shows its own failure beside the fields. */
export const team = {
  create: (draft: MemberDraft) =>
    api.post<CreatedTeamMember>(
      '/api/admin/team',
      { ...body(draft), username: draft.username.trim(), password: draft.password },
      { silent: true },
    ),

  setPassword: (userId: string, password: string, clearTwoStep: boolean) =>
    api.post<TeamMemberDetail>(`/api/admin/team/${userId}/password`, { password, clearTwoStep }, { silent: true }),

  adopt: (userId: string, draft: MemberDraft) =>
    api.post<TeamMemberDetail>(`/api/admin/team/legacy/${userId}/adopt`, body(draft), { silent: true }),

  member: (userId: string) => api.get<TeamMemberDetail>(`/api/admin/team/${userId}`),

  updateProfile: (userId: string, profile: { fullName: string; jobTitle: string | null; workEmail: string | null; interfaceLanguage: string; rowVersion: string }) =>
    api.put<TeamMemberDetail>(`/api/admin/team/${userId}/profile`, profile, { silent: true }),

  updateAccess: (userId: string, draft: MemberDraft, rowVersion: string) => {
    const b = body(draft)
    return api.put<TeamMemberDetail>(
      `/api/admin/team/${userId}/access`,
      { studioRole: b.studioRole, allNodes: b.allNodes, nodeIds: b.nodeIds, allLanguages: b.allLanguages, languageIds: b.languageIds, rowVersion },
      { silent: true },
    )
  },

  updateNotes: (userId: string, notes: string) =>
    api.put<TeamMemberDetail>(`/api/admin/team/${userId}/notes`, { notes: notes.trim() || null }, { silent: true }),

  suspend: (userId: string, reason: string) =>
    api.post<TeamMemberDetail>(`/api/admin/team/${userId}/suspend`, { reason: reason.trim() || null }, { silent: true }),

  reactivate: (userId: string) => api.post<TeamMemberDetail>(`/api/admin/team/${userId}/reactivate`, undefined, { silent: true }),

  deactivate: (userId: string, reason: string, confirmUsername: string) =>
    api.post<TeamMemberDetail>(`/api/admin/team/${userId}/deactivate`, { reason: reason.trim(), confirmUsername }, { silent: true }),

  resetAccess: (userId: string, clearTwoStep: boolean) =>
    api.post<SetupLink>(`/api/admin/team/${userId}/reset-access`, { clearTwoStep }, { silent: true }),

  revokeSetupLink: (userId: string) => api.del<void>(`/api/admin/team/${userId}/setup-link`, { silent: true }),

  signOutEverywhere: (userId: string) =>
    api.post<TeamMemberDetail>(`/api/admin/team/${userId}/sign-out-everywhere`, undefined, { silent: true }),

  revokeSession: (userId: string, sessionId: string) =>
    api.del<void>(`/api/admin/team/${userId}/sessions/${sessionId}`, { silent: true }),

  updateSecurity: (settings: Omit<StaffSecuritySettings, 'updatedAtUtc' | 'updatedBy' | 'activeMembersWithoutTwoStep'>) =>
    api.put<StaffSecuritySettings>('/api/admin/team/security', settings, { silent: true }),
}


