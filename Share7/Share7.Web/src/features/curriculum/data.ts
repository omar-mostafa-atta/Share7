// ===========================================================================
// Curriculum tree — data access and selection state
//
// The cascade in the old console was per-level on purpose: reloading from the
// grade down after every add wiped the selection and hid the node just
// created. That property is preserved here — reloadLevel touches one level and
// leaves the chain above it alone.
// ===========================================================================

import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../lib/client'
import { ApiError } from '../../lib/errors'
import { toast } from '../../store/toast'
import { LEVELS, LEVEL_ORDER, isEditable, levelsBelow, type Level } from './levels'
import type {
  CreateCurriculumNodeRequest,
  CurriculumNode,
  CurriculumNodeChildCounts,
  DeleteConflict,
  GradeDto,
  LessonDto,
} from '../../types/api'

export type TreeNode = CurriculumNode & Partial<Pick<LessonDto, 'hasQuestions' | 'questionsVersion'>>

type ByLevel<T> = Record<Level, T>

const emptyItems = (): ByLevel<TreeNode[]> => ({
  grade: [],
  term: [],
  subject: [],
  chapter: [],
  lesson: [],
})

const emptySelection = (): ByLevel<TreeNode | null> => ({
  grade: null,
  term: null,
  subject: null,
  chapter: null,
  lesson: null,
})

export function useCurriculumTree(langId: string) {
  const [items, setItems] = useState<ByLevel<TreeNode[]>>(emptyItems)
  const [selection, setSelection] = useState<ByLevel<TreeNode | null>>(emptySelection)
  const [loading, setLoading] = useState<Partial<ByLevel<boolean>>>({ grade: true })

  // Selection is read inside async callbacks that would otherwise close over a stale snapshot.
  // A ref alongside the state gives those callbacks the current value without making every
  // callback depend on — and be recreated by — each selection change.
  //
  // The ref is the authority for reads and must be assigned *before* any load is kicked off, not
  // from inside a setState updater: React defers updaters until render, so a load started on the
  // next line would still see the previous selection and bail out as though nothing were
  // selected. That is exactly what made selecting a grade leave its terms column empty.
  const selectionRef = useRef(selection)

  const commitSelection = (next: ByLevel<TreeNode | null>) => {
    selectionRef.current = next
    setSelection(next)
  }

  const setLevelLoading = (level: Level, value: boolean) =>
    setLoading((prev) => ({ ...prev, [level]: value }))

  /** Fetch one level's children from the currently selected parent. */
  const loadLevel = useCallback(async (level: Level) => {
    if (!isEditable(level)) return

    const config = LEVELS[level]
    const parent = selectionRef.current[config.parent]
    if (!parent) return

    setLevelLoading(level, true)
    try {
      const rows = await api.get<TreeNode[]>(config.list(parent.id))
      setItems((prev) => ({ ...prev, [level]: rows }))
    } catch {
      setItems((prev) => ({ ...prev, [level]: [] }))
    } finally {
      setLevelLoading(level, false)
    }
  }, [])

  const loadGrades = useCallback(async () => {
    setLevelLoading('grade', true)
    try {
      // Grades are the one level that takes an explicit language. Everything below resolves from
      // the token claim, which is why the page applies the language rather than just storing it.
      const rows = await api.get<GradeDto[]>(`/api/grades?langId=${langId}`)
      setItems((prev) => ({ ...prev, grade: rows }))
      return rows
    } catch {
      setItems((prev) => ({ ...prev, grade: [] }))
      return []
    } finally {
      setLevelLoading('grade', false)
    }
  }, [langId])

  // The tree is one shared set of nodes with a name per language, so node ids are stable across
  // a language switch. Selections are therefore kept and every loaded level is refetched to pick
  // up the new names — rather than resetting the admin to the top of the tree.
  useEffect(() => {
    if (!langId) return

    void (async () => {
      await loadGrades()
      for (const level of LEVEL_ORDER) {
        if (isEditable(level) && selectionRef.current[LEVELS[level].parent]) {
          await loadLevel(level)
        }
      }
    })()
  }, [langId, loadGrades, loadLevel])

  /** Pick a node: clears every level below it, then loads the next one down. */
  const select = useCallback(
    (level: Level, node: TreeNode) => {
      const below = levelsBelow(level)

      const next = { ...selectionRef.current, [level]: node }
      for (const l of below) next[l] = null
      commitSelection(next)

      setItems((prev) => {
        const cleared = { ...prev }
        for (const l of below) cleared[l] = []
        return cleared
      })

      // Grade is not in LEVELS (it has no admin endpoints), so its child is named directly.
      const child = isEditable(level) ? LEVELS[level].next : 'term'
      if (child) void loadLevel(child)
    },
    [loadLevel],
  )

  const addNode = useCallback(
    async (level: Level, request: CreateCurriculumNodeRequest) => {
      if (!isEditable(level)) return

      const parent = selectionRef.current[LEVELS[level].parent]
      if (!parent) return

      await api.post(LEVELS[level].create(parent.id), request)
      toast.success(`${label(level)} added`, `Under "${parent.name}".`)
      await loadLevel(level)
    },
    [loadLevel],
  )

  /**
   * Delete a node, returning the child counts when the server refuses because it still has
   * descendants.
   *
   * A 409 is a question, not a failure: the body names exactly what `force` would destroy, so it
   * is handed back to the caller to confirm rather than surfaced as an error. The old console
   * required ticking a "force" checkbox *before* trying, which meant either a wasted attempt or
   * an unbounded cascade authorised blind.
   */
  const deleteNode = useCallback(
    async (
      level: Level,
      node: TreeNode,
      force: boolean,
    ): Promise<{ deleted: true } | { deleted: false; counts: CurriculumNodeChildCounts }> => {
      if (!isEditable(level)) return { deleted: true }

      const url = LEVELS[level].remove(node.id) + (force ? '?force=true' : '')

      try {
        await api.del(url, { silent: !force })
      } catch (error) {
        if (error instanceof ApiError && error.status === 409) {
          const counts = (error.payload as DeleteConflict | null)?.details
          if (counts) return { deleted: false, counts }
        }
        throw error
      }

      toast.success(
        `${label(level)} deleted`,
        force ? `"${node.name}" and everything under it.` : `"${node.name}".`,
      )

      // Drop the selection and everything under it, then refresh this level in place.
      const next = { ...selectionRef.current, [level]: null }
      for (const l of levelsBelow(level)) next[l] = null
      commitSelection(next)

      setItems((prev) => {
        const cleared = { ...prev }
        for (const l of levelsBelow(level)) cleared[l] = []
        return cleared
      })

      await loadLevel(level)
      return { deleted: true }
    },
    [loadLevel],
  )

  return {
    items,
    selection,
    loading,
    select,
    addNode,
    deleteNode,
    reloadLevel: (level: Level) => (level === 'grade' ? loadGrades() : loadLevel(level)),
  }
}

/** "term" → "Term", for messages. */
function label(level: Level): string {
  return level.charAt(0).toUpperCase() + level.slice(1)
}
