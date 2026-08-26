// ===========================================================================
// Wire types
//
// Hand-written rather than generated. The API returns Task<IActionResult> from
// all 139 actions with no [ProducesResponseType] attributes, so its OpenAPI
// document declares no response schemas — a generated client would type every
// response as `any`. These mirror the C# DTOs directly; ASP.NET serialises
// property names as camelCase and enums as strings (JsonStringEnumConverter),
// so the shapes below are what actually arrives.
// ===========================================================================

// ---- auth (Share7.Application.Auth.Models.AuthResult) ---------------------

export interface AuthResult {
  succeeded: boolean
  errors: string[]
  userId: string
  username: string | null
  email: string | null
  roles: string[]
  isProfileComplete: boolean
  accessToken: string | null
  accessTokenExpiresAt: string | null
  refreshToken: string | null
  refreshTokenExpiresAt: string | null
}

export interface LoginRequest {
  username: string
  password: string
}

// ---- languages -----------------------------------------------------------

export interface Language {
  id: string
  code: string
  name: string
}

// ---- economy (Share7.Application.Economy.Models) -------------------------

export interface CurrencyDto {
  currencyId: string
  key: string
  name: string
  description: string | null
  enabled: boolean

  /** Whether this is a currency people pay real money for. Immutable once created. */
  isHard: boolean

  /** Most one account may earn from gameplay per UTC day, or null for no ceiling. */
  dailyEarnCap: number | null
}

export interface CurrenciesResponse {
  currencies: CurrencyDto[]
}

export interface CreateCurrencyRequest {
  key: string
  name: string
  description: string | null
  isHard: boolean
  dailyEarnCap: number | null
}

export interface UpdateCurrencyRequest {
  name: string
  description: string | null
  enabled: boolean
  dailyEarnCap: number | null
}

export interface BalanceDto {
  /** The stable currency *key* ("coins"), not the row id. */
  currency: string

  /** Absolute authoritative balance, never a delta. */
  amount: number
}

export interface BalancesResponse {
  balances: BalanceDto[]
}

export interface AdminGrantCurrencyRequest {
  currencyId: string
  amount: number
  reason: string | null
}

export interface WalletMutationResult {
  currencyId: string
  currency: string
  amount: number
  ledgerEntryId: number
}

// ---- curriculum (Share7.Application.Curriculum.Models) -------------------

/**
 * Every level of the tree carries the same shape, which is why one component renders all five
 * columns. `langId` is the language the `name` was resolved in, not a filter.
 */
export interface CurriculumNode {
  id: string
  name: string
  langId: string
  order: number
}

export interface GradeDto extends CurriculumNode {}
export interface TermDto extends CurriculumNode { gradeId: string }
export interface SubjectDto extends CurriculumNode { termId: string }
export interface ChapterDto extends CurriculumNode { subjectId: string }

export interface LessonDto extends CurriculumNode {
  chapterId: string
  questionsVersion: number
  hasQuestions: boolean
}

export interface CurriculumNodeTranslationRequest {
  langId: string
  name: string
}

export interface CreateCurriculumNodeRequest {
  translations: CurriculumNodeTranslationRequest[]
  order?: number
}

/**
 * What a delete would remove, returned in the 409 body when a node still has descendants and
 * `force` was not set — and in the 200 body as what it actually removed.
 */
export interface CurriculumNodeChildCounts {
  subjects: number
  chapters: number
  lessons: number
  questions: number
  hasChildren: boolean
}

/** The 409 shape from ServiceResultExtensions.ToErrorResult<T>. */
export interface DeleteConflict {
  errors: string[]
  details?: CurriculumNodeChildCounts
}

// ---- question pools ------------------------------------------------------

/**
 * Which pool an operation targets. The two are structural clones with independent version
 * counters — a lesson can sit at questions v1 and recovery v4 — so every call is parameterised by
 * pool rather than duplicated.
 */
export type QuestionPool = 'questions' | 'recovery'

/**
 * APPEND keeps what is published and adds after it; REPLACE publishes instead of it, which is
 * also how a question is edited or deleted. Required, with no default — and **both produce a new
 * version**, because a published set is immutable.
 */
export type ManualQuestionMode = 'APPEND' | 'REPLACE'

/** Correctness is positional: `correctChoice` is the right one, matching column 2 of the sheet. */
export interface ManualQuestionInput {
  text: string
  correctChoice: string
  wrongChoice1: string
  wrongChoice2: string
}

export interface ManualQuestionSetRequest {
  mode: ManualQuestionMode
  questions: ManualQuestionInput[]
}

/** `row` is the sheet row, or the 1-based position in `questions[]` for a manual publish. */
export interface QuestionImportError {
  row: number | null
  message: string
}

export interface QuestionImportResult {
  succeeded: boolean
  lessonId: string
  langId: string
  version: number
  importedCount: number
  replacedCount: number
  errors: QuestionImportError[]
}

export interface AnswerDto {
  id: string
  text: string
}

/** Correctness travels as `correctAnswerId`, not as a flag per answer or by position. */
export interface QuestionDto {
  questionId: string
  text: string
  correctAnswerId: string
  answers: AnswerDto[]
}

export interface LessonQuestionsDto {
  lessonId: string
  langId: string
  version: number
  questions: QuestionDto[]
}

// ---- shared -------------------------------------------------------------

/**
 * The translation shape used by commerce, games, objectives and boards.
 *
 * Note the asymmetry, which is in the C# and not a mistake here: requests take
 * `langId` + text, responses add `langCode`. Editors therefore build request
 * rows from response rows rather than reusing the object.
 */
export interface CommerceTranslationRequest {
  langId: string
  name: string
  description: string | null
}

export interface CommerceTranslationDto extends CommerceTranslationRequest {
  langCode: string
}

// ---- games (Share7.Application.Games.Models) -----------------------------

export interface GameTranslationRequest {
  langId: string
  displayName: string
  description: string
}

export interface GameAdminDto {
  gameId: string
  gameKey: string
  /** Resolved into the caller's language, for a listing to print. `translations`
   *  stays the source an editor fills from — saving is a full replace, so a form
   *  built from this field alone would delete every other language. */
  displayName: string
  description: string
  langId: string
  minPlayers: number
  maxPlayers: number
  readyTimeoutSeconds: number
  supportsSinglePlayer: boolean
  supportsMultiplayer: boolean
  useLobby: boolean
  useMatchmaking: boolean
  isActive: boolean
  translations: GameTranslationRequest[]
}

export interface SaveGameRequest {
  gameKey: string
  minPlayers: number
  maxPlayers: number
  readyTimeoutSeconds: number
  supportsSinglePlayer: boolean
  supportsMultiplayer: boolean
  useLobby: boolean
  useMatchmaking: boolean
  isActive: boolean
  translations: GameTranslationRequest[]
}

// ---- objectives (Share7.Application.Objectives.Models) -------------------

export interface ObjectiveTranslationRequest {
  langId: string
  name: string
  description: string | null
}

export interface ObjectiveAdminDto {
  objectiveId: string
  key: string
  kind: string
  metric: string
  scope: string | null
  target: number
  aggregation: string
  gameId: string | null
  gradeId: string | null
  langId: string | null
  availableFromUtc: string | null
  availableToUtc: string | null
  iconKey: string | null
  sortOrder: number
  isActive: boolean
  translations: ObjectiveTranslationRequest[]
}

export interface CreateObjectiveRequest {
  key: string
  kind: string
  metric: string
  scope: string | null
  target: number
  aggregation: string
  gameId: string | null
  gradeId: string | null
  langId: string | null
  availableFromUtc: string | null
  availableToUtc: string | null
  iconKey: string | null
  sortOrder: number
  isActive: boolean
  translations: ObjectiveTranslationRequest[]
}

/** Update deliberately omits key, kind, metric, scope and aggregation — the
 *  identity and the meaning of an objective are fixed once anyone has progress
 *  against it. Only the goalposts and the schedule move. */
export interface UpdateObjectiveRequest {
  target: number
  availableFromUtc: string | null
  availableToUtc: string | null
  iconKey: string | null
  sortOrder: number
  isActive: boolean
  translations: ObjectiveTranslationRequest[]
}

// ---- leaderboards (Share7.Application.Leaderboards.Models) ---------------

export interface LeaderboardBoardTranslationRequest {
  langId: string
  name: string
  description: string | null
}

export interface LeaderboardBoardAdminDto {
  boardId: string
  boardKey: string
  metric: string
  sortDirection: string
  aggregation: string
  period: string
  supportedCohorts: string
  gameId: string | null
  gradeId: string | null
  langId: string | null
  visibleRankLimit: number | null
  graceSeconds: number
  isActive: boolean
  cycleCount: number
  translations: LeaderboardBoardTranslationRequest[]
}

export interface SaveLeaderboardBoardRequest {
  boardKey: string
  metric: string
  sortDirection: string
  aggregation: string
  period: string
  supportedCohorts: string
  gameId: string | null
  gradeId: string | null
  langId: string | null
  visibleRankLimit: number | null
  graceSeconds: number
  isActive: boolean
  translations: LeaderboardBoardTranslationRequest[]
}

export interface LeaderboardCycleDto {
  cycleId: string
  startsAtUtc: string
  endsAtUtc: string | null
  state: string
  totalRanked: number
}

export interface CreateLeaderboardCycleRequest {
  startsAtUtc: string
  endsAtUtc: string
}

export interface MetricBoundDto {
  id: string
  gameId: string | null
  metric: string
  maxValue: number | null
  maxResultsPerDay: number | null
  maxValuePerDay: number | null
  enabled: boolean
}

export interface SaveMetricBoundRequest {
  gameId: string | null
  metric: string
  maxValue: number | null
  maxResultsPerDay: number | null
  maxValuePerDay: number | null
  enabled: boolean
}

export interface FlaggedResultDto {
  resultId: string
  userId: string
  displayName: string
  gameId: string
  metric: string
  value: number
  occurredAtUtc: string
  flagReason: string | null
}

export interface ResolveFlagRequest {
  legitimate: boolean
}

// ---- progression --------------------------------------------------------

export interface LevelThresholdDto {
  level: number
  cumulativeXp: number
}

export interface ReplaceLevelCurveRequest {
  levels: LevelThresholdDto[]
}

export interface PlayerLevelDto {
  level: number
  xp: number
  xpIntoLevel: number
  xpForNextLevel: number
  xpToNextLevel: number
  isMaxLevel: boolean
}

// ---- runs and signal valuations (Share7.Application.Runs.Models) ---------

/**
 * What one collectable is worth.
 *
 * `pickupKind` on the C# DTO is a compatibility alias that returns `signalKind`
 * verbatim, so it is deliberately not mirrored here — two fields that can never
 * disagree invite code that reads the wrong one.
 */
export interface SignalValuationDto {
  id: string
  gameId: string | null
  gameKey: string | null
  signalKind: string
  surface: string
  currencyId: string
  currency: string
  currencyIsHard: boolean
  currencyEnabled: boolean
  unitValue: number
  maxPerRun: number
  maxPerDay: number | null
  maxPerSecond: number | null
  enabled: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateSignalValuationRequest {
  gameId: string | null
  signalKind: string
  currencyId: string
  unitValue: number
  maxPerRun: number
  maxPerDay: number | null
  maxPerSecond: number | null
  enabled: boolean
}

export interface UpdateSignalValuationRequest {
  unitValue: number
  maxPerRun: number
  maxPerDay: number | null
  maxPerSecond: number | null
  enabled: boolean
}

export interface RunCollectedDto {
  kind: string
  count: number
}

/** One line of a run's settlement: what was collected, what survived the caps,
 *  and what was actually paid. `netAmount` is the only figure that moved money. */
export interface RunPayoutDto {
  source: string
  currency: string
  collectedCount: number
  paidCount: number
  unitValue: number
  grossAmount: number
  cappedAmount: number
  netAmount: number
}

export interface RunAdminDto {
  runId: string
  userId: string
  gameId: string
  state: string
  outcome: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationMs: number
  seed: number
  layoutVersion: number
  sessionId: string | null
  isFlagged: boolean
  flagReason: string | null
  capReached: boolean
  capMessage: string | null
  collected: RunCollectedDto[]
  payouts: RunPayoutDto[]
  reviewedAtUtc: string | null
  reviewedByUserId: string | null
  reviewNote: string | null
}

export interface ReviewRunRequest {
  note: string | null
}

// ---- reward rules (Share7.Application.Rewards.Models) --------------------

export interface RewardGrantRequest {
  currencyId: string
  amount: number
}

export interface RewardRuleGrantDto {
  currencyId: string
  currency: string
  amount: number
  currencyEnabled: boolean
}

export interface RewardRuleDto {
  ruleId: string
  name: string
  eventType: string
  referenceKey: string | null
  repeatPolicy: string
  cooldownSeconds: number | null
  dailyLimit: number | null
  transactionType: string
  enabled: boolean
  grants: RewardRuleGrantDto[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateRewardRuleRequest {
  name: string
  eventType: string
  referenceKey: string | null
  repeatPolicy: string
  cooldownSeconds: number | null
  dailyLimit: number | null
  transactionType: string | null
  grants: RewardGrantRequest[]
  entitlementProductIds: string[]
  enabled: boolean
}

/** Update omits eventType, referenceKey and entitlements: the trigger of a rule
 *  is its identity, and changing it in place would silently re-point history. */
export interface UpdateRewardRuleRequest {
  name: string
  repeatPolicy: string
  cooldownSeconds: number | null
  dailyLimit: number | null
  transactionType: string | null
  grants: RewardGrantRequest[]
  enabled: boolean
}

// ---- commerce: products, kinds, grants -----------------------------------

export interface ProductGrantDto {
  kind: string
  reference: string
  quantity: number
}

export interface AdminProductGrantDto {
  grantId: string
  productId: string
  kind: string
  reference: string
  quantity: number
}

export interface CreateProductGrantRequest {
  productId: string
  reference: string
  quantity: number
}

export interface UpdateProductGrantRequest {
  reference: string
  quantity: number
}

export interface ProductKindDto {
  productKindId: string
  name: string
  kind: string
  translations: CommerceTranslationDto[]
  productCount: number
}

export interface CreateProductKindRequest {
  name: string
  translations: CommerceTranslationRequest[]
}

export interface UpdateProductKindRequest {
  name: string
  translations: CommerceTranslationRequest[]
}

export interface AdminProductDto {
  productId: string
  key: string
  translations: CommerceTranslationDto[]
  imageUrl: string | null
  active: boolean
  productKindId: string
  kindName: string
  kind: string
  grants: AdminProductGrantDto[]
  ownerCount: number
}

export interface CreateProductRequest {
  key: string
  translations: CommerceTranslationRequest[]
  imageUrl: string | null
  productKindId: string
  active: boolean
}

/** Update has no `key`: it is the stable identifier other systems reference. */
export interface UpdateProductRequest {
  translations: CommerceTranslationRequest[]
  imageUrl: string | null
  productKindId: string
  active: boolean
}

export interface GrantEntitlementRequest {
  userId: string
  productId: string
}

export interface EntitlementDto {
  entitlementId: string
  productId: string
  grantedAtUtc: string
  source: string
}

// ---- offers --------------------------------------------------------------

export interface AdminOfferProductDto {
  productId: string
  key: string
  name: string
  kind: string
  active: boolean
  grantCount: number
  grants: ProductGrantDto[]
}

export interface AdminOfferDto {
  offerId: string
  name: string
  description: string | null
  currencyId: string
  currency: string
  price: number
  originalPrice: number | null
  availability: string
  purchaseLimit: number | null
  expiresAtUtc: string | null
  sortOrder: number
  badgeKey: string | null
  products: AdminOfferProductDto[]
  purchaseCount: number
  expired: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateOfferRequest {
  translations: CommerceTranslationRequest[]
  currencyId: string
  price: number
  originalPrice: number | null
  availability: string
  purchaseLimit: number | null
  expiresAtUtc: string | null
  sortOrder: number
  badgeKey: string | null
  productIds: string[]
}

// ---- multiplayer ---------------------------------------------------------

export interface MultiplayerSessionSummaryDto {
  id: string
  gameId: string
  hostUserId: string
  transportSessionName: string
  transportRegion: string | null
  joinCode: string | null
  state: string
  visibility: string
  minPlayers: number
  maxPlayers: number
  currentPlayerCount: number
  protocolVersion: number
  isRanked: boolean
  lessonId: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  endedAtUtc: string | null
  lastHeartbeatAtUtc: string
  closedReason: string | null
}

export interface MultiplayerAdminSessionsDto {
  sessions: MultiplayerSessionSummaryDto[]
  totalMatching: number
  serverTimeUtc: string
}

// ---- users ---------------------------------------------------------------

export interface UserProfileDto {
  userId: string
  userName: string
  fullName: string | null
  age: number | null
  phoneNumber: string | null
  email: string | null
  gradeId: string | null
  preferredLanguageId: string | null
  isProfileComplete: boolean
  isSelf: boolean
  createdAtUtc: string | null
  updatedAtUtc: string | null
}

/** A roster row. Backed by `GET /api/admin/users`, which this console added —
 *  see AdminUsersController. Lighter than UserProfileDto on purpose: a list of
 *  a hundred thousand accounts should not carry a phone number per row. */
export interface AdminUserListItemDto {
  userId: string
  userName: string
  fullName: string | null
  email: string | null
  age: number | null
  gradeId: string | null
  roles: string[]
  isProfileComplete: boolean
  createdAtUtc: string | null
  lastSeenAtUtc: string | null
}

export interface AdminUserPageDto {
  users: AdminUserListItemDto[]
  total: number
  page: number
  pageSize: number
}

/** One account in full, from `GET /api/admin/users/{id}`. */
export interface AdminUserDetailDto {
  userId: string
  userName: string
  fullName: string | null
  email: string | null
  phoneNumber: string | null
  age: number | null
  gradeId: string | null
  gradeName: string | null
  preferredLanguageId: string | null
  preferredLanguageCode: string | null
  roles: string[]
  isProfileComplete: boolean
  createdAtUtc: string | null
  updatedAtUtc: string | null
  lastSeenAtUtc: string | null
  runCount: number
  flaggedRunCount: number
  entitlementCount: number
  purchaseCount: number
  lessonsCompleted: number
}

/** Signed movement of currency. `balanceAfter` is what makes the ledger auditable. */
export interface AdminLedgerEntryDto {
  id: number
  currencyId: string
  currency: string
  amount: number
  balanceAfter: number
  transactionType: string
  sourceType: string
  sourceId: string | null
  createdAtUtc: string
}

export interface AdminUserWalletDto {
  balances: BalanceDto[]
  recent: AdminLedgerEntryDto[]
  ledgerCount: number
}

export interface StreakDto {
  current: number
  best: number
  freezesRemaining: number
}

/** The player-facing objective shape, resolved for one account. */
export interface ObjectiveDto {
  key: string
  kind: string
  name: string
  description: string | null
  iconKey: string | null
  value: number
  target: number
  state: string
  canClaim: boolean
  cycleEndsAtUtc: string | null
  sortOrder: number
}

export interface AdminUserProgressionDto {
  level: PlayerLevelDto
  streak: StreakDto
  objectives: ObjectiveDto[]
}

export interface AdminUserEntitlementDto {
  entitlementId: string
  productId: string
  productKey: string
  kindName: string
  productActive: boolean
  grantedAtUtc: string
  source: string
  sourceId: string | null
}

/** Lighter than RunAdminDto — a history row, not the cheat-review payload. */
export interface AdminUserRunDto {
  runId: string
  gameId: string
  state: string
  outcome: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationMs: number
  isFlagged: boolean
  flagReason: string | null
  reviewed: boolean
  netPaid: number
}

// ---- overview ------------------------------------------------------------

/** Aggregate counters for the landing page. Backed by `GET /api/admin/overview`,
 *  added alongside this console — the figures existed only as separate list
 *  endpoints, and drawing a dashboard from a dozen full-table fetches is how a
 *  console becomes the slowest client of its own API. */
export interface AdminOverviewDto {
  users: number
  usersAddedLast7Days: number
  games: number
  activeGames: number
  grades: number
  lessons: number
  lessonsWithQuestions: number
  questions: number
  currencies: number
  offers: number
  activeOffers: number
  products: number
  objectives: number
  activeObjectives: number
  rewardRules: number
  enabledRewardRules: number
  signalValuations: number
  boards: number
  openCycles: number
  liveSessions: number
  flaggedRuns: number
  flaggedResults: number
  runsLast24Hours: number
  serverTimeUtc: string
}

// ===========================================================================
// Analytics
//
// Backed by /api/admin/analytics/*. Everything here reads a rollup or a ledger
// — see Docs/AnalyticsArchitecture.md, Rule 4 — so a range that looks expensive
// on screen is a primary-key read underneath.
//
// NOTE the nullables. `d1`/`d7`/`d30` and `uniqueUsers` are null until the data
// behind them has matured or been computed, and the difference between null and
// zero is the difference between "not yet known" and "nobody did it". Render
// them apart; a zero in place of a null is how a team spends a week fixing
// retention that was never broken.
// ===========================================================================

export interface AnalyticsBreakdownDto {
  key: string
  count: number
  share: number
}

export interface AnalyticsOverviewDto {
  fromDayUtc: string
  toDayUtc: string
  dau: number
  wau: number
  mau: number
  stickiness: number
  newUsers: number
  sessions: number
  averageSessionSeconds: number
  sessionsPerActiveUser: number
  totalPlaySeconds: number
  totalEvents: number
  d1: number | null
  d7: number | null
  d30: number | null
  d1CohortCount: number
  d7CohortCount: number
  d30CohortCount: number
  platforms: AnalyticsBreakdownDto[]

  /** How far behind the projector is. A stalled projector looks exactly like a
   *  collapse in engagement — every figure goes flat — and this pair is the only
   *  thing that tells them apart. */
  projectionLagSeconds: number
  pendingEvents: number
}

export interface RetentionCohortRowDto {
  cohortDayUtc: string
  cohortSize: number

  /** Indexed by day. SHORTER than `maxDayIndex` for a cohort that has not aged
   *  that far — a missing cell is "not yet known", not zero. */
  cells: number[]
}

export interface RetentionCurvePointDto {
  dayIndex: number
  retention: number
  cohortCount: number
  userCount: number
}

export interface RetentionReportDto {
  fromCohortDayUtc: string
  toCohortDayUtc: string
  maxDayIndex: number
  cohorts: RetentionCohortRowDto[]
  curve: RetentionCurvePointDto[]
  computedAtUtc: string | null
}

export interface TimeseriesPointDto {
  dayUtc: string
  count: number

  /** Null until the nightly pass computes it. Render "pending", never zero. */
  uniqueUsers: number | null
}

export interface TimeseriesSeriesDto {
  key: string
  points: TimeseriesPointDto[]
}

export interface TimeseriesDto {
  metric: string
  dimension: string | null
  series: TimeseriesSeriesDto[]
}

export type TelemetryCategory = 'Operational' | 'Behavioural' | 'Unknown'

export interface EventCatalogueRowDto {
  name: string
  group: string
  description: string
  category: TelemetryCategory
  sampleRate: number
  retentionDays: number | null
  enabled: boolean
  rollUpDaily: boolean
  dimensions: string
  firstSeenAtUtc: string | null
  count: number
}

export interface EventCatalogueDto {
  events: EventCatalogueRowDto[]

  /** Names seen in the wild with no registration. Stored, but never rolled up
   *  until somebody says what they are. */
  unregistered: EventCatalogueRowDto[]
}

export interface EventParameterDto {
  key: string
  topValues: AnalyticsBreakdownDto[]
  distinctValues: number
}

export interface EventDetailDto {
  schema: EventCatalogueRowDto
  daily: TimeseriesPointDto[]
  parameters: EventParameterDto[]

  /** The breakdown above is from this many recent rows, not from the whole
   *  range. It answers "what does this event look like", not "how many". */
  sampleSize: number
}

export interface FunnelStepDto {
  index: number
  name: string
  users: number
  conversionFromStart: number
  conversionFromPrevious: number
}

export interface FunnelReportDto {
  steps: FunnelStepDto[]
  windowHours: number
  fromDayUtc: string
  toDayUtc: string
}

export interface EconomyDailyPointDto {
  dayUtc: string
  sourced: number
  sunk: number
}

export interface EconomyCurrencyDto {
  currencyId: string
  code: string
  sourced: number
  sunk: number

  /** Sustained positive net is inflation: the economy mints faster than it
   *  removes, and every price in the shop is quietly getting cheaper. */
  net: number
  sources: AnalyticsBreakdownDto[]
  sinks: AnalyticsBreakdownDto[]
  daily: EconomyDailyPointDto[]
}

export interface EconomyReportDto {
  fromDayUtc: string
  toDayUtc: string
  currencies: EconomyCurrencyDto[]
}

export type TimelineSourceKind =
  | 'Telemetry'
  | 'CurrencyLedger'
  | 'Reward'
  | 'Purchase'
  | 'Entitlement'
  | 'Run'
  | 'Attempt'

export interface TimelineEntryDto {
  source: TimelineSourceKind
  atUtc: string
  kind: string
  summary: string
  refId: string | null
  gameId: string | null
  runId: string | null
  sessionId: string | null

  /** Only the ledger and single-line rewards carry these. A telemetry event
   *  never does — which is what stops the trace appearing to double-count a
   *  grant it merely described. */
  amount: number | null
  currencyCode: string | null
  balanceAfter: number | null
  data: Record<string, string>
}

export interface UserTimelineDto {
  userId: string
  entries: TimelineEntryDto[]

  /** Pass as `before` for the next page. A timestamp rather than an offset,
   *  because the trace merges seven independently ordered sources. */
  nextBeforeUtc: string | null
}

export interface UserBalanceDto {
  currencyId: string
  code: string
  balance: number
}

export interface UserCurrencyFlowDto {
  currencyId: string
  code: string
  earned: number
  spent: number
}

export interface UserActivityDayDto {
  dayUtc: string
  sessions: number
  playSeconds: number
  events: number
  runs: number
  attempts: number
}

export interface UserAnalyticsProfileDto {
  userId: string
  userName: string | null
  firstSeenAtUtc: string | null
  lastSeenAtUtc: string | null
  cohortDayUtc: string | null
  dayIndex: number | null
  activeDays: number
  totalSessions: number
  totalEvents: number
  totalPlaySeconds: number
  installAppVersion: string | null
  installPlatform: string | null
  lastAppVersion: string | null
  lastPlatform: string | null
  runCount: number
  flaggedRunCount: number
  attemptCount: number
  purchaseCount: number
  entitlementCount: number
  balances: UserBalanceDto[]
  currencyFlow: UserCurrencyFlowDto[]
  recentDays: UserActivityDayDto[]
}

export interface UpsertEventSchemaRequest {
  group: string
  description: string
  category: TelemetryCategory
  sampleRate: number
  retentionDays: number | null
  enabled: boolean
  rollUpDaily: boolean
  dimensions: string
}
