// ===========================================================================
// Question pools — data access
//
// Ports the `pools` map and the upload/manual/load logic from
// wwwroot/js/curriculum.js. The two pools are structural clones of each other
// with independent version counters, so everything here is parameterised by
// pool rather than written twice — the same reason the backend shares
// QuestionContentRules between them.
// ===========================================================================

import { useCallback, useState } from 'react'
import { api } from '../../lib/client'
import { ApiError, describeError } from '../../lib/errors'
import { toast } from '../../store/toast'
import type {
  LessonQuestionsDto,
  ManualQuestionMode,
  ManualQuestionInput,
  QuestionImportError,
  QuestionImportResult,
  QuestionPool,
} from '../../types/api'

interface PoolConfig {
  label: string
  /** Lower-case form for mid-sentence use. */
  noun: string
  upload: (lessonId: string, langId: string, hasHeaderRow: boolean) => string
  manual: (lessonId: string, langId: string) => string
  read: (lessonId: string, langId: string) => string
}

export const POOLS: Record<QuestionPool, PoolConfig> = {
  questions: {
    label: 'Questions',
    noun: 'questions',
    upload: (lessonId, langId, hasHeaderRow) =>
      `/api/admin/lessons/${lessonId}/questions/upload?langId=${langId}&hasHeaderRow=${hasHeaderRow}`,
    manual: (lessonId, langId) => `/api/admin/lessons/${lessonId}/questions/manual?langId=${langId}`,
    read: (lessonId, langId) => `/api/admin/lessons/${lessonId}/questions?langId=${langId}`,
  },
  recovery: {
    label: 'Recovery questions',
    noun: 'recovery questions',
    upload: (lessonId, langId, hasHeaderRow) =>
      `/api/admin/lessons/${lessonId}/recovery-questions/upload?langId=${langId}&hasHeaderRow=${hasHeaderRow}`,
    manual: (lessonId, langId) =>
      `/api/admin/lessons/${lessonId}/recovery-questions/manual?langId=${langId}`,
    read: (lessonId, langId) => `/api/admin/lessons/${lessonId}/recovery-questions?langId=${langId}`,
  },
}

/**
 * Pulls the error list out of a rejected publish.
 *
 * Validation failures come back as a list naming each bad row, and a toast cannot hold them — the
 * caller renders them inline instead. Anything else (a 403, a 500) has no list, so the thrown
 * reason stands in rather than leaving an empty panel that looks like nothing happened.
 */
function errorsFrom(error: unknown): QuestionImportError[] {
  if (error instanceof ApiError) {
    const payload = error.payload as { errors?: unknown } | null
    if (payload && Array.isArray(payload.errors)) {
      return payload.errors.map((e) =>
        typeof e === 'object' && e !== null && 'message' in e
          ? (e as QuestionImportError)
          : { row: null, message: describeError(e) },
      )
    }
    return [{ row: null, message: error.message }]
  }
  return [{ row: null, message: String(error) }]
}

export function useQuestionPool(pool: QuestionPool) {
  const config = POOLS[pool]
  const [busy, setBusy] = useState(false)
  const [errors, setErrors] = useState<QuestionImportError[]>([])

  const clearErrors = useCallback(() => setErrors([]), [])

  /** Publish an .xlsx sheet as the next version. All-or-nothing on the server. */
  const uploadSheet = useCallback(
    async (lessonId: string, langId: string, file: File, hasHeaderRow: boolean) => {
      setBusy(true)
      setErrors([])
      try {
        const form = new FormData()
        // The parameter is named `file` on the action, so the form field must match.
        form.append('file', file)

        const result = await api.post<QuestionImportResult>(
          config.upload(lessonId, langId, hasHeaderRow),
          form,
          { form: true, silent: true },
        )

        toast.success(
          `${config.label} uploaded`,
          `Version ${result.version} — ${result.importedCount} live, ${result.replacedCount} retired.`,
        )
        return result
      } catch (error) {
        setErrors(errorsFrom(error))
        return null
      } finally {
        setBusy(false)
      }
    },
    [config],
  )

  /** Publish questions typed by hand. Same validation as the sheet, same all-or-nothing. */
  const publishManual = useCallback(
    async (
      lessonId: string,
      langId: string,
      mode: ManualQuestionMode,
      questions: ManualQuestionInput[],
    ) => {
      setBusy(true)
      setErrors([])
      try {
        const result = await api.post<QuestionImportResult>(
          config.manual(lessonId, langId),
          { mode, questions },
          { silent: true },
        )

        toast.success(
          `${config.label} published`,
          `Version ${result.version} — ${result.importedCount} live, ${result.replacedCount} retired.`,
        )
        return result
      } catch (error) {
        setErrors(errorsFrom(error))
        return null
      } finally {
        setBusy(false)
      }
    },
    [config],
  )

  /**
   * Read what is currently published, so a set can be edited rather than retyped.
   *
   * Uses the admin route with an explicit langId, never the player-facing one: an admin editing
   * Arabic on an English token would otherwise load English and republish it over the Arabic set.
   */
  const loadPublished = useCallback(
    async (lessonId: string, langId: string) => {
      setBusy(true)
      try {
        return await api.get<LessonQuestionsDto>(config.read(lessonId, langId))
      } catch {
        return null
      } finally {
        setBusy(false)
      }
    },
    [config],
  )

  return { config, busy, errors, clearErrors, setErrors, uploadSheet, publishManual, loadPublished }
}
