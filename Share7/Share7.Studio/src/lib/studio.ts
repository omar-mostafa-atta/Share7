import { download, request, upload, type PersonName } from './api'

// ===========================================================================
// The workspace, as the Studio asks for it
//
// One function per thing a member can do, named the way the Studio talks about
// it rather than the way HTTP does. The types mirror what the API sends back
// (Share7.Application/Workspace/Models/WorkspaceModels.cs); enums arrive as
// words, not numbers, so they are written here as unions of those words.
// ===========================================================================

export type DraftKind =
  | 'LessonContent'
  | 'NewNode'
  | 'Rename'
  | 'Move'
  | 'Reorder'
  | 'Retire'
  | 'Restore'
  | 'RecoveryRule'
export type DraftStatus = 'Editing' | 'InReview' | 'ChangesRequested' | 'Approved' | 'Released' | 'Discarded'
export type ReleaseStatus = 'Building' | 'Scheduled' | 'Publishing' | 'Published' | 'Failed' | 'Cancelled'
export type ReviewVerdict = 'Approved' | 'ChangesRequested'
export type AssignmentStatus = 'Open' | 'Done' | 'Cancelled'
export type ItemRole = 'Core' | 'Practice' | 'Recovery' | 'Diagnostic' | 'Placement'
export type NodeKind = 'grade' | 'term' | 'subject' | 'chapter' | 'lesson'

export interface ContentLanguage {
  id: string
  code: string
  name: string
  isContentLanguage: boolean
  requiredToPublish: boolean
  direction: 'ltr' | 'rtl'
  sortOrder: number
}

export interface NodeTitle {
  langId: string
  title: string
}

export interface TrailStep {
  id: string
  kind: NodeKind
  titles: NodeTitle[]
}

export interface StudioNode {
  id: string
  kind: NodeKind
  parentId: string | null
  order: number
  revision: number
  titles: NodeTitle[]
  isRetired: boolean
  childCount: number
  openDrafts: DraftKind[]
  missingLanguages: string[] | null
  questionCounts: Record<string, number> | null
  inScope: boolean
}

export interface StudioNodeDetail {
  node: StudioNode
  trail: TrailStep[]
  children: StudioNode[]
}

// --- a lesson's live content ------------------------------------------------

export interface ContentChoice {
  id: string
  text: string
}

export interface ContentRendering {
  langId: string
  questionId: string
  itemVersionId: string
  text: string
  choices: ContentChoice[]
  correctIndex: number
}

export interface ContentItem {
  itemId: string
  role: ItemRole
  order: number
  renderings: ContentRendering[]
}

export interface ContentSet {
  role: ItemRole
  langId: string
  version: number
  itemCount: number
  updatedAtUtc: string
}

export interface LessonContent {
  lessonId: string
  sets: ContentSet[]
  items: ContentItem[]
}

// --- what a draft proposes --------------------------------------------------

export interface DraftRendering {
  langId: string
  text: string
  choices: string[]
  correctIndex: number
}

export interface DraftItem {
  itemId: string | null
  sourceKeyHint?: string | null
  role: ItemRole
  order: number
  renderings: DraftRendering[]
}

export interface LessonProposal {
  items: DraftItem[]
}

export interface NewNodeProposal {
  titles: NodeTitle[]
  position: number | null
  items: DraftItem[] | null
}

export interface RenameProposal {
  titles: NodeTitle[]
}

export interface MoveProposal {
  newParentId: string
  position: number | null
}

export interface ReorderProposal {
  orderedChildIds: string[]
}

/** One reason something cannot be published, precise enough to sit on its field. */
export interface ContentProblem {
  code: string
  message: string
  role: ItemRole | null
  order: number | null
  langId: string | null
  field: string | null
}

// --- drafts -----------------------------------------------------------------

export interface DraftSummary {
  id: string
  kind: DraftKind
  status: DraftStatus
  isPractice: boolean
  nodeId: string | null
  parentNodeId: string | null
  nodeKind: NodeKind | null
  title: string
  trail: TrailStep[]
  createdBy: PersonName
  createdAtUtc: string
  updatedBy: PersonName
  updatedAtUtc: string
  submittedAtUtc: string | null
  revision: number
  isOutOfDate: boolean
  openComments: number
  releaseId: string | null
}

export interface DraftPermissions {
  edit: boolean
  submit: boolean
  review: boolean
  discard: boolean
  release: boolean
}

export interface ReviewDecision {
  id: string
  reviewer: PersonName
  verdict: ReviewVerdict
  note: string | null
  draftRevision: number
  isCurrent: boolean
  createdAtUtc: string
}

export interface DraftComment {
  id: string
  parentCommentId: string | null
  author: PersonName
  body: string
  anchor: CommentAnchor | null
  createdAtUtc: string
  editedAtUtc: string | null
  resolvedAtUtc: string | null
  resolvedBy: PersonName | null
}

/** Where a comment is pinned: a question, a language, a field — each optional. */
export interface CommentAnchor {
  itemId?: string | null
  role?: ItemRole | null
  order?: number | null
  langId?: string | null
  field?: string | null
}

export interface Draft {
  summary: DraftSummary
  proposal: unknown
  base: unknown
  problems: ContentProblem[]
  languagesTouched: string[]
  contributors: PersonName[]
  reviews: ReviewDecision[]
  alsoHere: PersonName[]
  can: DraftPermissions
}

export interface DiffLine {
  change: 'added' | 'changed' | 'removed' | 'moved'
  subject: 'question' | 'title' | 'parent' | 'position' | 'order' | 'state'
  itemId: string | null
  role: ItemRole | null
  order: number | null
  langId: string | null
  before: string | null
  after: string | null
}

export interface DraftPage {
  drafts: DraftSummary[]
  total: number
  page: number
  pageSize: number
}

export interface ReviewQueueItem {
  draft: DraftSummary
  waitingSince: string
  canReview: boolean
  cannotReviewBecause: string | null
}

// --- releases ---------------------------------------------------------------

export interface ReleaseSummary {
  id: string
  title: string
  notes: string | null
  status: ReleaseStatus
  draftCount: number
  createdBy: PersonName
  createdAtUtc: string
  scheduledForUtc: string | null
  publishedAtUtc: string | null
  publishedBy: PersonName | null
  rollbackOfReleaseId: string | null
  rolledBackByReleaseId: string | null
  reason: string | null
  failureMessage: string | null
}

export interface ReleaseItem {
  draftId: string | null
  kind: DraftKind
  nodeId: string
  title: string
  trail: TrailStep[]
  diff: DiffLine[]
  outcome: unknown
}

export interface ReleaseBlocker {
  draftId: string
  reason: string
  relatedDraftId: string | null
}

export interface ImpactLine {
  kind: 'questionsChanged' | 'orderChanged' | 'retired' | 'moved'
  nodeId: string
  trail: TrailStep[]
  students: number
}

export interface Release {
  summary: ReleaseSummary
  items: ReleaseItem[]
  blockers: ReleaseBlocker[]
  impact: { lines: ImpactLine[] }
}

// --- Excel ------------------------------------------------------------------

// ===========================================================================
// Skills, quality and second chances (plan Phase 5)
// ===========================================================================

export type SkillReviewState = 'Unreviewed' | 'Reviewed' | 'Deprecated'
export type SkillKind = 'skill' | 'concept' | 'procedure' | 'lesson_placeholder'

export interface Framework {
  id: string
  frameworkKey: string
  name: string
  versionLabel: string
  publishedAtUtc: string | null
  skills: number
  reviewed: number
  isPlaceholders: boolean
}

export interface Skill {
  id: string
  frameworkId: string
  targetKey: string
  /** The claim in each language, keyed by language id. */
  statements: Record<string, string>
  targetKindKey: SkillKind
  reviewState: SkillReviewState
  difficultyBand: number | null
  questions: number
  replaced: number
  /** True once somebody has edited it here, so a later import leaves it alone. */
  isEdited: boolean
}

export interface SkillImportProblem {
  row: number
  code: string
  message: string
}

export interface SkillImport {
  rowsRead: number
  added: number
  updated: number
  leftAlone: number
  problems: SkillImportProblem[]
  wroteNothing: boolean
}

export interface ItemSkill {
  targetId: string
  statement: string
  emphasis: number
  isPrimary: boolean
  isPlaceholder: boolean
}

export interface ItemSkills {
  itemId: string
  text: string
  mapped: ItemSkill[]
}

export interface ItemSkillLine {
  targetId: string
  emphasis: number
  isPrimary: boolean
}

export interface SubjectMapping {
  subjectNodeId: string
  subjectTitle: string
  lessons: number
  questions: number
  mappedToSkills: number
  onPlaceholders: number
  unmapped: number
  isComplete: boolean
}

/** One question on the mapping board, and what it says it measures now. */
export interface QuestionToMap {
  itemId: string
  text: string
  lessonId: string
  lessonTitle: string
  role: ItemRole
  order: number
  primaryTargetId: string | null
  primaryStatement: string | null
  /** True when what it is mapped to is the stand-in its lesson was minted with. */
  onStandIn: boolean
  alsoTouches: number
}

/** A stand-in, and what replacing it would move. */
export interface StandIn {
  targetId: string
  statement: string
  nodeId: string | null
  nodeTitle: string | null
  questions: number
  answers: number
  isReplaced: boolean
}

export interface PromotionReport {
  targetId: string
  targetKey: string
  standInsReplaced: number
  questionsMoved: number
  lessonsMoved: number
  /** Answers re-read against the real claim they were always about. */
  answersRebuilt: number
  exclusionsKept: number
}

/** One line of a paper: a skill it asks about, and whether the bank can serve it. */
export interface BlueprintLine {
  lineId: string
  targetId: string
  statement: string
  /** True when the line names a lesson stand-in rather than a real skill. */
  isPlaceholder: boolean
  weight: number
  itemCount: number
  difficultyBandLow: number | null
  difficultyBandHigh: number | null
  /** Questions in the bank mapped to it. Zero means the line cannot be served. */
  availableItems: number
}

export interface BlueprintArea {
  areaId: string
  areaKey: string
  label: string
  weight: number
  normalisedWeight: number
  order: number
  lines: BlueprintLine[]
}

export interface Blueprint {
  blueprintId: string
  blueprintKey: string
  versionNumber: number
  name: string
  frameworkId: string
  frameworkName: string
  /** Where the paper came from, in its own words. Shown on the board, never hidden. */
  sourceNote: string | null
  isPublished: boolean
  minCoverageRatio: number
  minAreaCoverageRatio: number
  minObservationsOverall: number
  minObservationsPerArea: number
  maxMedianEvidenceAgeDays: number
  areas: BlueprintArea[]
  /** While this is non-zero the paper measures attendance rather than proficiency. */
  placeholderLines: number
  /** Lines no question in the bank can satisfy. */
  unservableLines: number
}

export interface BenchmarkReport {
  blueprintId: string
  blueprintKey: string
  areas: number
  lines: number
  linesOnStandIns: number
  warning: string | null
}

export interface ChoiceShare {
  choiceId: string
  text: string | null
  isCorrect: boolean
  count: number
  share: number
}

export interface ItemQuality {
  itemId: string
  itemVersionId: string
  versionNumber: number
  sourceKey: string
  isAnchor: boolean
  stem: string | null
  nodeTitle: string | null
  nodeId: string | null
  nTotal: number
  nFirstEncounter: number
  facility: number | null
  meanElapsedMs: number | null
  choices: ChoiceShare[]
  flags: string[]
}

export interface FlaggedQuestion {
  quality: ItemQuality
  trail: TrailStep[]
  openDraftId: string | null
  canFix: boolean
}

/**
 * What can be said about the team's own questions. Deliberately not the
 * platform-wide measurement summary the Admin Console reads: that one counts
 * every answer in the database and takes half a minute.
 */
export interface QualitySummary {
  questions: number
  answered: number
  enoughToSay: number
  unmapped: number
  anchors: number
  reportingFloor: number
}

export type ExclusionReason =
  | 'MisKeyedItem'
  | 'InvalidatedContract'
  | 'IntegrityFlag'
  | 'Misattributed'
  | 'WithdrawnConsent'
  | 'Remapped'

export interface ExclusionReport {
  itemVersionId: string
  observationsExcluded: number
  reason: ExclusionReason
}

export interface RecoveryAtNode {
  nodeId: string
  nodeKind: NodeKind
  title: string
  trail: TrailStep[]
  afterWrongAnswers: number
  questionsToServe: number
  allowRepeats: boolean
  isOwn: boolean
  fromNodeId: string | null
  fromNodeTitle: string | null
  isDefault: boolean
  lessons: number
  lessonsWithOwnRule: number
  lessonsWithNoRecoveryQuestions: number
  openDraftId: string | null
  canPropose: boolean
}

export interface RecoveryRuleRow {
  ruleId: string
  nodeId: string
  nodeKind: NodeKind
  title: string
  afterWrongAnswers: number
  questionsToServe: number
  allowRepeats: boolean
  writtenAtUtc: string
  releaseId: string | null
}

/** What a recovery-rule draft proposes. `clear` takes the rule away rather than changing it. */
export interface RecoveryRuleProposal {
  afterWrongAnswers: number
  questionsToServe: number
  allowRepeats: boolean
  clear: boolean
}

/** What was in force when the draft was started, and where it was written. */
export interface RecoveryRuleBase {
  afterWrongAnswers: number
  questionsToServe: number
  allowRepeats: boolean
  isOwn: boolean
  fromNodeId: string | null
  fromNodeTitle: string | null
  revision: number
}

export interface ImportProblem {
  row: number
  column: string | null
  code: string
  message: string
}

export interface ImportTrial {
  languages: string[]
  items: DraftItem[]
  problems: ImportProblem[]
  mainCount: number
  recoveryCount: number
}

// --- the inbox --------------------------------------------------------------

export interface Notification {
  id: string
  kind: string
  actor: PersonName | null
  draftId: string | null
  releaseId: string | null
  assignmentId: string | null
  nodeId: string | null
  title: string | null
  createdAtUtc: string
  isRead: boolean
}

export interface Assignment {
  id: string
  nodeId: string
  trail: TrailStep[]
  assignee: PersonName
  assignedBy: PersonName
  note: string | null
  dueOn: string | null
  status: AssignmentStatus
  createdAtUtc: string
  closedAtUtc: string | null
}

export interface ActivityItem {
  sequence: number
  occurredAtUtc: string
  actor: PersonName | null
  action: string
  summary: string
  targetType: string | null
  targetId: string | null
}

export interface Teammate {
  userId: string
  name: string
  role: 'Author' | 'Reviewer' | 'Lead'
}

export interface QuestionHit {
  lessonId: string
  trail: TrailStep[]
  itemId: string
  role: ItemRole
  order: number
  langId: string
  questionId: string
  text: string
}

// ===========================================================================
// Asking
// ===========================================================================

const query = (params: Record<string, string | number | boolean | null | undefined>) => {
  const search = new URLSearchParams()
  for (const [name, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') search.set(name, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const studio = {
  // --- the curriculum ------------------------------------------------------
  languages: () => request<ContentLanguage[]>('GET', '/api/studio/curriculum/languages'),

  children: (parentId?: string | null, includeRetired = false) =>
    request<StudioNode[]>('GET', `/api/studio/curriculum/nodes${query({ parentId, includeRetired })}`),

  node: (nodeId: string) => request<StudioNodeDetail>('GET', `/api/studio/curriculum/nodes/${nodeId}`),

  lesson: (lessonId: string) =>
    request<{ lesson: StudioNode; trail: TrailStep[]; live: LessonContent; openDraft: DraftSummary | null }>(
      'GET',
      `/api/studio/curriculum/lessons/${lessonId}/workspace`,
    ),

  search: (q: string, under?: string | null, langId?: string | null, take = 50) =>
    request<QuestionHit[]>('GET', `/api/studio/curriculum/search${query({ q, under, langId, take })}`),

  // --- drafts --------------------------------------------------------------
  drafts: (params: {
    mine?: boolean
    status?: DraftStatus
    nodeId?: string
    underNodeId?: string
    includeClosed?: boolean
    includePractice?: boolean
    page?: number
    pageSize?: number
  }) => request<DraftPage>('GET', `/api/studio/drafts${query(params)}`),

  startDraft: (body: {
    kind: DraftKind
    nodeId?: string | null
    parentNodeId?: string | null
    nodeKind?: NodeKind | null
    isPractice?: boolean
    proposal?: unknown
  }) => request<Draft>('POST', '/api/studio/drafts', body),

  draft: (draftId: string) => request<Draft>('GET', `/api/studio/drafts/${draftId}`),

  saveDraft: (draftId: string, revision: number, proposal: unknown) =>
    request<Draft>('PUT', `/api/studio/drafts/${draftId}`, { revision, proposal }),

  check: (draftId: string) => request<Draft>('POST', `/api/studio/drafts/${draftId}/check`),

  diff: (draftId: string) => request<{ lines: DiffLine[] }>('GET', `/api/studio/drafts/${draftId}/diff`),

  submit: (draftId: string, revision: number, note?: string) =>
    request<Draft>('POST', `/api/studio/drafts/${draftId}/submit`, { revision, note: note ?? null }),

  withdraw: (draftId: string, revision: number) =>
    request<Draft>('POST', `/api/studio/drafts/${draftId}/withdraw`, { revision, note: null }),

  discard: (draftId: string) => request<DraftSummary>('POST', `/api/studio/drafts/${draftId}/discard`),

  liveNow: (draftId: string) => request<unknown>('GET', `/api/studio/drafts/${draftId}/live`),

  bringUpToDate: (draftId: string, revision: number, proposal?: unknown) =>
    request<Draft>('POST', `/api/studio/drafts/${draftId}/rebase`, { revision, proposal: proposal ?? null }),

  presence: (draftId: string) => request<PersonName[]>('POST', `/api/studio/drafts/${draftId}/presence`),

  approve: (draftId: string, revision: number, note?: string) =>
    request<Draft>('POST', `/api/studio/drafts/${draftId}/approve`, { revision, note: note ?? null }),

  requestChanges: (draftId: string, revision: number, note: string) =>
    request<Draft>('POST', `/api/studio/drafts/${draftId}/request-changes`, { revision, note }),

  comments: (draftId: string) => request<DraftComment[]>('GET', `/api/studio/drafts/${draftId}/comments`),

  comment: (draftId: string, body: string, anchor: CommentAnchor | null, parentCommentId?: string | null) =>
    request<DraftComment>('POST', `/api/studio/drafts/${draftId}/comments`, {
      body,
      anchor,
      parentCommentId: parentCommentId ?? null,
    }),

  importIntoDraft: (draftId: string, revision: number, file: File) => {
    const form = new FormData()
    form.set('revision', String(revision))
    form.set('file', file)
    return upload<Draft>(`/api/studio/drafts/${draftId}/import`, form)
  },

  // --- reviewing -----------------------------------------------------------
  queue: () => request<ReviewQueueItem[]>('GET', '/api/studio/reviews/queue'),

  editComment: (commentId: string, body: string) =>
    request<DraftComment>('PUT', `/api/studio/reviews/comments/${commentId}`, { body, anchor: null, parentCommentId: null }),

  resolveComment: (commentId: string) => request<DraftComment>('POST', `/api/studio/reviews/comments/${commentId}/resolve`),

  // --- releases ------------------------------------------------------------
  releases: () => request<ReleaseSummary[]>('GET', '/api/studio/releases'),

  startRelease: (title: string, notes: string | null, draftIds: string[]) =>
    request<Release>('POST', '/api/studio/releases', { title, notes, draftIds }),

  release: (releaseId: string) => request<Release>('GET', `/api/studio/releases/${releaseId}`),

  changeRelease: (releaseId: string, add: string[], remove: string[]) =>
    request<Release>('PUT', `/api/studio/releases/${releaseId}/drafts`, { add, remove }),

  publish: (releaseId: string) => request<Release>('POST', `/api/studio/releases/${releaseId}/publish`),

  schedule: (releaseId: string, publishAtUtc: string) =>
    request<Release>('POST', `/api/studio/releases/${releaseId}/schedule`, { publishAtUtc }),

  cancelRelease: (releaseId: string) => request<Release>('POST', `/api/studio/releases/${releaseId}/cancel`),

  rollback: (releaseId: string, reason: string) =>
    request<Release>('POST', `/api/studio/releases/${releaseId}/rollback`, { reason }),

  // --- Excel ---------------------------------------------------------------
  trialSheet: (file: File) => {
    const form = new FormData()
    form.set('file', file)
    return upload<ImportTrial>('/api/studio/imports/trial', form)
  },

  blankSheet: (codes?: string[]) =>
    download(`/api/studio/imports/template${query({ languages: codes?.join(',') })}`, 'share7-questions-template.xlsx'),

  lessonSheet: (lessonId: string, fromDraft: boolean) =>
    download(
      `/api/studio/imports/lessons/${lessonId}/export${query({ source: fromDraft ? 'draft' : null })}`,
      `lesson${fromDraft ? '-draft' : ''}.xlsx`,
    ),

  // --- the inbox -----------------------------------------------------------
  notifications: (unread = false, take = 50) =>
    request<Notification[]>('GET', `/api/studio/notifications${query({ unread, take })}`),

  markRead: (ids: string[] | null) => request<void>('POST', '/api/studio/notifications/read', { ids }),

  assignments: (mine = true, includeClosed = false) =>
    request<Assignment[]>('GET', `/api/studio/assignments${query({ mine, includeClosed })}`),

  assign: (nodeId: string, assigneeUserId: string, note: string | null, dueOn: string | null) =>
    request<Assignment>('POST', '/api/studio/assignments', { nodeId, assigneeUserId, note, dueOn }),

  closeAssignment: (assignmentId: string, status: AssignmentStatus) =>
    request<Assignment>('POST', `/api/studio/assignments/${assignmentId}/close`, { status }),

  activity: (params: { nodeId?: string; before?: number; take?: number }) =>
    request<ActivityItem[]>('GET', `/api/studio/activity${query(params)}`),

  team: () => request<Teammate[]>('GET', '/api/studio/team'),

  // --- skills --------------------------------------------------------------
  frameworks: () => request<Framework[]>('GET', '/api/studio/skills/frameworks'),

  startFramework: (frameworkKey: string, name: string, versionLabel: string) =>
    request<Framework>('POST', '/api/studio/skills/frameworks', { frameworkKey, name, versionLabel }),

  outcomesTemplate: () =>
    download('/api/studio/skills/template', 'learning-outcomes-template.xlsx'),

  importOutcomes: (frameworkId: string, file: File, dryRun: boolean) => {
    const form = new FormData()
    form.set('file', file)
    return upload<SkillImport>(`/api/studio/skills/frameworks/${frameworkId}/import${query({ dryRun })}`, form)
  },

  skills: (frameworkId: string, params: { langId?: string; search?: string; reviewState?: string; take?: number }) =>
    request<Skill[]>('GET', `/api/studio/skills/frameworks/${frameworkId}/skills${query(params)}`),

  skill: (targetId: string, langId?: string) =>
    request<Skill>('GET', `/api/studio/skills/${targetId}${query({ langId })}`),

  editSkill: (targetId: string, body: { statements?: Record<string, string>; targetKindKey?: string; difficultyBand?: number | null }) =>
    request<Skill>('PUT', `/api/studio/skills/${targetId}`, body),

  setSkillState: (targetId: string, state: SkillReviewState) =>
    request<Skill>('POST', `/api/studio/skills/${targetId}/review-state`, { state }),

  questionSkills: (itemId: string, langId?: string) =>
    request<ItemSkills>('GET', `/api/studio/skills/questions/${itemId}${query({ langId })}`),

  mapQuestion: (itemId: string, skills: ItemSkillLine[]) =>
    request<ItemSkills>('PUT', `/api/studio/skills/questions/${itemId}`, { skills }),

  subjectProgress: (subjectNodeId: string, langId?: string) =>
    request<SubjectMapping>('GET', `/api/studio/skills/subjects/${subjectNodeId}/progress${query({ langId })}`),

  questionsToMap: (
    subjectNodeId: string,
    params: { langId?: string; state?: string; take?: number; skip?: number },
  ) => request<QuestionToMap[]>('GET', `/api/studio/skills/subjects/${subjectNodeId}/questions${query(params)}`),

  standIns: (nodeId: string, langId?: string) =>
    request<StandIn[]>('GET', `/api/studio/skills/nodes/${nodeId}/stand-ins${query({ langId })}`),

  // --- papers --------------------------------------------------------------
  blueprints: (langId?: string) =>
    request<Blueprint[]>('GET', `/api/studio/exams/blueprints${query({ langId })}`),

  blueprint: (blueprintId: string, langId?: string) =>
    request<Blueprint>('GET', `/api/studio/exams/blueprints/${blueprintId}${query({ langId })}`),

  buildBenchmark: (subjectNodeId: string, versionLabel: string, langId?: string) =>
    request<BenchmarkReport>('POST', '/api/studio/exams/benchmark', { subjectNodeId, versionLabel, langId }),

  publishBlueprint: (blueprintId: string, langId?: string) =>
    request<Blueprint>('POST', `/api/studio/exams/blueprints/${blueprintId}/publish${query({ langId })}`),

  promote: (body: {
    targetKey: string
    statements: Record<string, string>
    targetKindKey?: SkillKind
    difficultyBand?: number | null
    replacesTargetIds: string[]
    markReviewed?: boolean
    useExistingTargetId?: string | null
  }) => request<PromotionReport>('POST', '/api/studio/skills/promote', body),

  // --- what the answers say about the questions ----------------------------
  qualitySummary: () => request<QualitySummary>('GET', '/api/studio/quality/summary'),

  flagged: (params: { langId?: string; flag?: string; nodeId?: string; take?: number; skip?: number }) =>
    request<FlaggedQuestion[]>('GET', `/api/studio/quality/questions${query(params)}`),

  flaggedQuestion: (itemId: string, langId?: string) =>
    request<FlaggedQuestion>('GET', `/api/studio/quality/questions/${itemId}${query({ langId })}`),

  setAnchor: (itemId: string, isAnchor: boolean, reason: string) =>
    request<FlaggedQuestion>('POST', `/api/studio/quality/questions/${itemId}/anchor`, { isAnchor, reason }),

  excludeAnswers: (itemVersionId: string, reason: ExclusionReason, note: string) =>
    request<ExclusionReport>('POST', `/api/studio/quality/versions/${itemVersionId}/exclude`, { reason, note }),

  // --- second chances ------------------------------------------------------
  recoveryAt: (nodeId: string) =>
    request<RecoveryAtNode>('GET', `/api/studio/recovery/nodes/${nodeId}`),

  recoveryWritten: (langId?: string) =>
    request<RecoveryRuleRow[]>('GET', `/api/studio/recovery/written${query({ langId })}`),
}
