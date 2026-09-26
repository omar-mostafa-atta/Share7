import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useLanguages } from '../App'
import { useI18n } from '../i18n/i18n'
import type { MessageKey } from '../i18n/en'
import { servedLevels, studio, type NodeKind, type StudioCurriculum, type TrailStep } from './studio'
import { useTitle } from './use'

// ===========================================================================
// The curricula, and what their levels are called
//
// Read once and shared, like the content languages: every board that names a
// level needs to know whose level it is. The curriculum the game serves names
// its five levels through the Studio's own dictionaries; one declared in the
// Studio names its levels itself, in every language its content is written in.
//
// Everything that asks "what goes under this?" or "what is this called?" asks
// here, so a curriculum that calls its top level "Year" says "Add — Year" on
// every board and never "Add — Grade".
// ===========================================================================

interface Curricula {
  list: StudioCurriculum[]
  loading: boolean
  reload: () => void
}

const CurriculaContext = createContext<Curricula>({ list: [], loading: true, reload: () => undefined })

export function CurriculaProvider({ children }: { children: ReactNode }) {
  const [list, setList] = useState<StudioCurriculum[]>([])
  const [loading, setLoading] = useState(true)
  const [turn, setTurn] = useState(0)

  useEffect(() => {
    let alive = true
    studio
      .curricula()
      .then((next) => {
        if (alive) setList(next)
      })
      .catch(() => {
        // The board still works without them: levels fall back to the served curriculum's names.
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [turn])

  const reload = useCallback(() => setTurn((n) => n + 1), [])
  const value = useMemo(() => ({ list, loading, reload }), [list, loading, reload])

  return <CurriculaContext.Provider value={value}>{children}</CurriculaContext.Provider>
}

export function useCurricula() {
  return useContext(CurriculaContext)
}

/** The curriculum a node belongs to. */
export function useCurriculumOf(curriculumId: string | null | undefined): StudioCurriculum | undefined {
  const { list } = useCurricula()
  return curriculumId ? list.find((one) => one.id === curriculumId) : undefined
}

/**
 * The curriculum a trail runs through: a declared one when the trail starts at its root, the one
 * the game serves when it starts at a grade.
 */
export function curriculumOfTrail(list: StudioCurriculum[], trail: TrailStep[] | undefined): StudioCurriculum | undefined {
  const top = trail?.[0]
  if (!top) return list.find((one) => one.isServed)
  return top.kind === 'curriculum' ? list.find((one) => one.rootNodeId === top.id) : list.find((one) => one.isServed)
}

// ---------------------------------------------------------------------------
// Levels
// ---------------------------------------------------------------------------

const servedUnder: Record<string, NodeKind | null> = {
  grade: 'term',
  term: 'subject',
  subject: 'chapter',
  chapter: 'lesson',
  lesson: null,
}

/** The level that goes under `kind` — or, for no kind, the curriculum's top level. */
export function levelUnder(curriculum: StudioCurriculum | undefined, kind: NodeKind | null): NodeKind | null {
  if (!curriculum || curriculum.isServed) return kind === null ? 'grade' : (servedUnder[kind] ?? null)

  if (kind === null || kind === 'curriculum') return curriculum.levels[0]?.key ?? null
  const at = curriculum.levels.findIndex((level) => level.key === kind)
  return at >= 0 ? (curriculum.levels[at + 1]?.key ?? null) : null
}

/** The level `kind` goes under, and how many levels deep that is from the top (1 = the top level). */
export function levelAbove(curriculum: StudioCurriculum | undefined, kind: NodeKind): { kind: NodeKind | null; depth: number } {
  if (!curriculum || curriculum.isServed) {
    const at = servedLevels.indexOf(kind as (typeof servedLevels)[number])
    return at > 0 ? { kind: servedLevels[at - 1], depth: at } : { kind: null, depth: 0 }
  }

  const at = curriculum.levels.findIndex((level) => level.key === kind)
  return at > 0 ? { kind: curriculum.levels[at - 1].key, depth: at } : { kind: 'curriculum', depth: 0 }
}

/** Whether a level is the one students play — where questions are written. */
export function levelIsPlayable(curriculum: StudioCurriculum | undefined, kind: NodeKind | null | undefined): boolean {
  if (!kind) return false
  if (!curriculum || curriculum.isServed) return kind === 'lesson'
  return curriculum.levels.some((level) => level.key === kind && level.isPlayable)
}

/** Whether nodes of this level can be added, renamed, moved and retired: never the served grades, never a root. */
export function levelIsEditable(curriculum: StudioCurriculum | undefined, kind: NodeKind): boolean {
  if (kind === 'curriculum') return false
  if (!curriculum || curriculum.isServed) return kind !== 'grade'
  return curriculum.levels.some((level) => level.key === kind)
}

/** What a level is called, in the interface language: "Chapter", "الموضوع". */
export function useLevelName() {
  const { t } = useI18n()
  const languages = useLanguages()
  const title = useTitle()

  return useCallback(
    (kind: NodeKind | null | undefined, curriculum?: StudioCurriculum): string => {
      if (!kind) return ''
      if (kind === 'curriculum') return t('curriculum.kind.curriculum')

      const declared = curriculum && !curriculum.isServed ? curriculum.levels.find((level) => level.key === kind) : undefined
      if (declared) return title(declared.names, languages)

      const key = `curriculum.kind.${kind}` as MessageKey
      const said = t(key)
      return said === key ? kind : said
    },
    [t, languages, title],
  )
}

/** A curriculum's own name: the one the game serves is named by the Studio, a declared one by its root. */
export function useCurriculumName() {
  const { t } = useI18n()
  const languages = useLanguages()
  const title = useTitle()

  return useCallback(
    (curriculum: StudioCurriculum | undefined): string => {
      if (!curriculum) return t('curriculum.title')
      if (curriculum.isServed) return t('curricula.served.name')
      return title(curriculum.titles, languages) || t('curriculum.title')
    },
    [t, languages, title],
  )
}
