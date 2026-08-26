// ===========================================================================
// Curriculum tree — data access and expansion state
//
// Rewritten from a five-column cascade to an actual tree.
//
// The cascade held ONE selection per level and one list of children per level,
// so the screen could only ever show a single path: picking a different term
// wiped the subjects, chapters and lessons of the one before it. That is fine
// for drilling down and useless for comparing — "does Grade 2 Term 1 have the
// same chapters as Term 2" meant clicking back and forth and remembering.
//
// Children are now keyed by parent id, so any number of branches can be open
// at once and expanding one never discards another. Loading stays lazy: a
// branch fetches its children the first time it is opened, and never again
// unless something changes underneath it.
// ===========================================================================

import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../lib/client'
import { ApiError } from '../../lib/errors'
import { toast } from '../../store/toast'
import { LEVELS, LEVEL_ORDER, childLevelOf, isEditable, type Level } from './levels'
import type {
  CreateCurriculumNodeRequest,
  CurriculumNode,
  CurriculumNodeChildCounts,
  DeleteConflict,
  GradeDto,
  LessonDto,
} from '../../types/api'

export type TreeNode = CurriculumNode & Partial<Pick<LessonDto, 'hasQuestions' | 'questionsVersion'>>

/** A node plus everything above it, which is what the detail pane needs. */
export interface Selected {
  level: Level
  node: TreeNode

  /** Root-to-parent. Empty for a grade. */
  ancestors: TreeNode[]
}

export function useCurriculumTree(langId: string) {
  const [grades, setGrades] = useState<TreeNode[]>([])
  const [children, setChildren] = useState<Record<string, TreeNode[]>>({})
  const [busy, setBusy] = useState<Record<string, boolean>>({})
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [selected, setSelected] = useState<Selected | null>(null)
  const [loadingGrades, setLoadingGrades] = useState(true)

  // Read inside async callbacks that would otherwise close over a stale snapshot.
  const childrenRef = useRef(children)
  childrenRef.current = children

  const setNodeBusy = (id: string, value: boolean) =>
    setBusy((prev) => ({ ...prev, [id]: value }))

  /**
   * Fetch one node's children.
   *
   * `force` skips the already-loaded check. Expansion uses the cache; an add or
   * delete passes force so the branch it changed is refetched.
   */
  const loadChildren = useCallback(
    async (parent: TreeNode, parentLevel: Level, force = false) => {
      const level = childLevelOf(parentLevel)
      if (!level) return

      if (!force && childrenRef.current[parent.id]) return

      setNodeBusy(parent.id, true)
      try {
        const rows = await api.get<TreeNode[]>(LEVELS[level].list(parent.id))
        setChildren((prev) => ({ ...prev, [parent.id]: rows }))
      } catch {
        setChildren((prev) => ({ ...prev, [parent.id]: [] }))
      } finally {
        setNodeBusy(parent.id, false)
      }
    },
    [],
  )

  const loadGrades = useCallback(async () => {
    setLoadingGrades(true)
    try {
      // Grades are the one level that takes an explicit language. Everything below resolves from
      // the token claim, which is why the page applies the language rather than just storing it.
      const rows = await api.get<GradeDto[]>(`/api/grades?langId=${langId}`)
      setGrades(rows)
      return rows
    } catch {
      setGrades([])
      return []
    } finally {
      setLoadingGrades(false)
    }
  }, [langId])

  // A language switch keeps ids — the tree is one set of nodes with a name per language — so the
  // expansion and the selection survive it. Only the text changes, which means every branch
  // already open has to be refetched to pick up the new names.
  useEffect(() => {
    if (!langId) return

    void (async () => {
      // The freshly-loaded rows are used directly rather than read back from a ref. `loadGrades`
      // sets state, and state does not land before the next line of an async function — reading
      // the ref here would walk the *previous* language's tree and match nothing.
      const freshGrades = await loadGrades()

      // Snapshot which branches were open before any awaits, so the loop is iterating a stable
      // list rather than one being mutated by the refetches it is issuing.
      const openParents = Object.keys(childrenRef.current)
      if (!openParents.length) return

      const known = { ...childrenRef.current }

      for (const parentId of openParents) {
        const found = locate(parentId, freshGrades, known)
        if (found) await loadChildren(found.node, found.level, true)
      }
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [langId, loadGrades, loadChildren])

  const toggle = useCallback(
    (node: TreeNode, level: Level) => {
      setExpanded((prev) => {
        const next = new Set(prev)
        if (next.has(node.id)) next.delete(node.id)
        else {
          next.add(node.id)
          void loadChildren(node, level)
        }
        return next
      })
    },
    [loadChildren],
  )

  const expand = useCallback(
    (node: TreeNode, level: Level) => {
      setExpanded((prev) => {
        if (prev.has(node.id)) return prev
        const next = new Set(prev)
        next.add(node.id)
        return next
      })
      void loadChildren(node, level)
    },
    [loadChildren],
  )

  /**
   * Select a node, and open it.
   *
   * Selecting expands rather than merely highlighting: clicking a chapter to see
   * what is in it and having nothing happen is the single most confusing thing a
   * tree can do.
   */
  const select = useCallback(
    (node: TreeNode, level: Level, ancestors: TreeNode[]) => {
      setSelected({ node, level, ancestors })
      if (childLevelOf(level)) expand(node, level)
    },
    [expand],
  )

  const addChild = useCallback(
    async (parent: TreeNode, parentLevel: Level, request: CreateCurriculumNodeRequest) => {
      const level = childLevelOf(parentLevel)
      if (!level || !isEditable(level)) return

      await api.post(LEVELS[level].create(parent.id), request)
      toast.success(`${label(level)} added`, `Under "${parent.name}".`)

      // Force, and expand — the point of adding is to see the thing you added.
      await loadChildren(parent, parentLevel, true)
      setExpanded((prev) => new Set(prev).add(parent.id))
    },
    [loadChildren],
  )

  /**
   * Delete a node, returning the child counts when the server refuses because it still has
   * descendants.
   *
   * A 409 is a question, not a failure: the body names exactly what `force` would destroy, so it
   * is handed back to the caller to confirm rather than surfaced as an error.
   */
  const deleteNode = useCallback(
    async (
      level: Level,
      node: TreeNode,
      parent: TreeNode | null,
      parentLevel: Level | null,
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

      // Drop it from the selection and from the expansion set, and forget any children it had
      // cached — those rows are gone server-side and would otherwise reappear on re-expand.
      setSelected((current) => (current?.node.id === node.id ? null : current))

      setExpanded((prev) => {
        const next = new Set(prev)
        next.delete(node.id)
        return next
      })

      setChildren((prev) => {
        const next = { ...prev }
        delete next[node.id]
        return next
      })

      if (parent && parentLevel) await loadChildren(parent, parentLevel, true)
      return { deleted: true }
    },
    [loadChildren],
  )

  /** Refetch the branch a node sits in — after publishing questions, for instance. */
  const refreshBranch = useCallback(
    async (parent: TreeNode | null, parentLevel: Level | null) => {
      if (parent && parentLevel) await loadChildren(parent, parentLevel, true)
      else await loadGrades()
    },
    [loadChildren, loadGrades],
  )

  const collapseAll = useCallback(() => setExpanded(new Set()), [])

  return {
    grades,
    children,
    busy,
    expanded,
    selected,
    loadingGrades,
    toggle,
    select,
    setSelected,
    addChild,
    deleteNode,
    refreshBranch,
    collapseAll,
    reloadGrades: loadGrades,
  }
}

/** Find a node by id anywhere in what is currently loaded, with its level. */
function locate(
  id: string,
  grades: TreeNode[],
  children: Record<string, TreeNode[]>,
): { node: TreeNode; level: Level } | null {
  const walk = (rows: TreeNode[], depth: number): { node: TreeNode; level: Level } | null => {
    for (const row of rows) {
      if (row.id === id) return { node: row, level: LEVEL_ORDER[depth] }

      const kids = children[row.id]
      if (kids) {
        const found = walk(kids, depth + 1)
        if (found) return found
      }
    }
    return null
  }

  return walk(grades, 0)
}

/** "term" → "Term", for messages. */
function label(level: Level): string {
  return level.charAt(0).toUpperCase() + level.slice(1)
}
