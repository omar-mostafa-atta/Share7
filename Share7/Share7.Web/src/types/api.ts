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
