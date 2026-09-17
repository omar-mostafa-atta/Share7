import { useCallback, useEffect, useState } from 'react'
import { api, request } from '../../lib/client'
import { ApiError } from '../../lib/errors'
import { toast } from '../../store/toast'
import type { LessonSheetDto, LessonSheetResult, LessonSheetRow } from '../../types/api'

// ===========================================================================
// Lesson sheet — data access
//
// One lesson, both languages, both pools. The four per-language endpoints are
// still there and still work; this is the one the console uses, because the
// unit an author edits is a lesson and editing a quarter of one is how the
// four sets drifted apart in the first place.
//
// Every write is a full replace, so `rows` in the editor is the whole truth
// about the lesson — not a patch against it.
// ===========================================================================

export interface SheetState {
  sheet: LessonSheetDto | null
  loading: boolean
  /** Row-level messages from the last rejected publish, keyed by row number. */
  errors: { row: number | null; message: string }[]
  reload: () => Promise<void>
  save: (rows: LessonSheetRow[]) => Promise<boolean>
  remove: (rowNumber: number) => Promise<boolean>
  upload: (file: File, hasHeaderRow: boolean) => Promise<boolean>
}

export function useLessonSheet(lessonId: string | null): SheetState {
  const [sheet, setSheet] = useState<LessonSheetDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [errors, setErrors] = useState<SheetState['errors']>([])

  const reload = useCallback(async () => {
    if (!lessonId) {
      setSheet(null)
      return
    }

    setLoading(true)
    try {
      setSheet(await api.get<LessonSheetDto>(`/api/admin/lessons/${lessonId}/sheet`))
    } finally {
      setLoading(false)
    }
  }, [lessonId])

  useEffect(() => {
    setErrors([])
    void reload().catch(() => undefined)
  }, [reload])

  // A rejected publish is not an exception to report and forget — the row numbers in it are the
  // whole point, and they have to reach the editor so it can mark the offending rows. So these
  // are caught rather than left to the global handler, and the body is unpacked.
  const attempt = useCallback(
    async (run: () => Promise<LessonSheetResult>, success: (r: LessonSheetResult) => void) => {
      setErrors([])

      try {
        const result = await run()
        success(result)
        await reload()
        return true
      } catch (error) {
        if (error instanceof ApiError && isSheetResult(error.payload)) {
          setErrors(error.payload.errors ?? [])
          return false
        }

        throw error
      }
    },
    [reload],
  )

  const save = useCallback(
    (rows: LessonSheetRow[]) =>
      attempt(
        () =>
          api.put<LessonSheetResult>(
            `/api/admin/lessons/${lessonId}/sheet`,
            { rows },
            { silent: true },
          ),
        (r) =>
          toast.success(
            'Questions published',
            `${r.mainCount} main (v${r.mainVersion}) and ${r.recoveryCount} recovery (v${r.recoveryVersion}), in both languages.`,
          ),
      ),
    [attempt, lessonId],
  )

  const remove = useCallback(
    (rowNumber: number) =>
      attempt(
        () =>
          request<LessonSheetResult>(
            'DELETE',
            `/api/admin/lessons/${lessonId}/sheet/${rowNumber}`,
            undefined,
            { silent: true },
          ),
        () => toast.success('Question deleted', 'Removed from both languages and both pools.'),
      ),
    [attempt, lessonId],
  )

  const upload = useCallback(
    (file: File, hasHeaderRow: boolean) => {
      const form = new FormData()
      form.append('file', file)

      return attempt(
        () =>
          api.post<LessonSheetResult>(
            `/api/admin/lessons/${lessonId}/sheet/upload?hasHeaderRow=${hasHeaderRow}`,
            form,
            { form: true, silent: true },
          ),
        (r) =>
          toast.success(
            'Sheet published',
            `${r.mainCount} main and ${r.recoveryCount} recovery questions, in both languages.`,
          ),
      )
    },
    [attempt, lessonId],
  )

  return { sheet, loading, errors, reload, save, remove, upload }
}

function isSheetResult(body: unknown): body is LessonSheetResult {
  return typeof body === 'object' && body !== null && 'errors' in body
}

/** A blank row for the editor, with no row number — the server assigns one on save. */
export function blankRow(isRecovery: boolean): LessonSheetRow {
  return {
    rowNumber: 0,
    questionEn: '',
    correctEn: '',
    wrongEn1: '',
    wrongEn2: '',
    questionAr: '',
    correctAr: '',
    wrongAr1: '',
    wrongAr2: '',
    isRecovery,
  }
}
