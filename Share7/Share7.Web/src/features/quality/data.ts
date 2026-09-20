// ===========================================================================
// Content quality — data access
//
// The first surface the educational evidence layer pays for, and it pays an
// admin rather than a learner. Long before the platform can say how good a
// child is at mathematics, it can say which questions are mis-keyed, which
// distractors nobody ever picks, which items are answered too fast to have
// been read, and which content is mapped to no learning target and therefore
// cannot be measured at all.
//
// Every one of those is actionable today and none of them needs a psychometric
// model. See Docs/EducationalArchitecture.md §16.4.
// ===========================================================================

import { useCallback, useState } from 'react'
import { api } from '../../lib/client'
import { toast } from '../../store/toast'

export type QualityFlag =
  | 'insufficient_data'
  | 'too_easy'
  | 'too_hard'
  | 'distractor_beats_key'
  | 'dead_distractor'
  | 'answered_too_fast'
  | 'unmapped'

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

  /** Proportion correct on first, controlled encounters. HIGHER MEANS EASIER. */
  facility: number | null

  meanElapsedMs: number | null
  choices: ChoiceShare[]
  flags: QualityFlag[]
}

/**
 * Whether the generic node tree still agrees with the typed tables it is derived from.
 *
 * The projection is invisible in the authoring UI — an admin adds a chapter through
 * the curriculum tree editor and the node table follows silently, in the same
 * transaction. That is the right behaviour right up until it stops being true, and
 * without this nothing in the console would say so.
 */
export interface CurriculumProjectionStatus {
  liveNodes: number
  retiredNodes: number
  legacyRows: number
  versionLabel: string

  /** False while the typed tables are still the source of truth. */
  isAuthoritative: boolean

  nodesByKind: Record<string, number>

  /** Legacy rows with no node. Always zero unless something has gone wrong. */
  missing: number
}

export interface QualitySummary {
  items: number
  itemsWithResponses: number
  itemsAboveReportingFloor: number
  itemsUnmapped: number
  anchorItems: number

  responses: number
  observations: number
  observationsExcluded: number
  pendingResponses: number

  observationsByStrength: Record<string, number>
  flagCounts: Partial<Record<QualityFlag, number>>

  placeholderTargets: number
  authoredTargets: number

  curriculumProjection: CurriculumProjectionStatus

  /** Below this many first encounters, no facility figure is reported at all. */
  reportingFloor: number
}

/**
 * What each flag means, in a sentence an admin can act on.
 *
 * Written here rather than on the server because they are UI copy: the server
 * returns keys precisely so that the wording can change without a deployment,
 * and so that a second client can word them differently.
 */
export const FLAG_COPY: Record<QualityFlag, { label: string; hint: string; tone: 'danger' | 'warning' | 'info' | 'muted' }> = {
  distractor_beats_key: {
    label: 'Distractor beats the key',
    hint: 'More learners chose one particular wrong answer than the one marked right. The commonest cause is a wrong answer key.',
    tone: 'danger',
  },
  too_hard: {
    label: 'Almost nobody gets it',
    hint: 'Under 20% correct on first encounters. Sometimes a genuinely hard question; often a mis-keyed one.',
    tone: 'danger',
  },
  unmapped: {
    label: 'Measures nothing',
    hint: 'Mapped to no learning target, so no answer to it can ever inform a measurement.',
    tone: 'warning',
  },
  dead_distractor: {
    label: 'Dead distractor',
    hint: 'A wrong answer nobody picks. The question is effectively two-option, which makes guessing easier than intended.',
    tone: 'warning',
  },
  answered_too_fast: {
    label: 'Answered too fast',
    hint: 'Far quicker than the rest of the bank — quick enough that the stem cannot have been read.',
    tone: 'warning',
  },
  too_easy: {
    label: 'Almost everyone gets it',
    hint: 'Over 95% correct. Fine as an opener; it distinguishes nothing if the whole lesson reads like this.',
    tone: 'info',
  },
  insufficient_data: {
    label: 'Not enough answers yet',
    hint: 'Below the reporting floor. Not a fault — a statement that nothing can honestly be said yet.',
    tone: 'muted',
  },
}

/** Why an admin is withdrawing an item's observations. Mirrors ObservationExclusionReason. */
export const EXCLUSION_REASONS = [
  { value: 1, label: 'The answer key was wrong' },
  { value: 2, label: 'The contract that admitted it was withdrawn' },
  { value: 3, label: 'Integrity flag — not a person answering' },
  { value: 4, label: 'Misattributed — somebody else was answering' },
  { value: 5, label: 'Consent for this use was withdrawn' },
  { value: 6, label: 'The item was remapped to a different target' },
] as const

export function useQualityActions(onChanged: () => void) {
  const [busyId, setBusyId] = useState<string | null>(null)

  const setAnchor = useCallback(
    async (item: ItemQuality, isAnchor: boolean) => {
      setBusyId(item.itemId)
      try {
        await api.put(`/api/admin/education/items/${item.itemId}/anchor`, { isAnchor })
        toast.success(
          isAnchor ? 'Marked as an anchor' : 'No longer an anchor',
          isAnchor
            ? 'Administer it broadly from now on — anchors only work if the answers were already accumulating.'
            : undefined,
        )
        onChanged()
      } finally {
        setBusyId(null)
      }
    },
    [onChanged],
  )

  const exclude = useCallback(
    async (item: ItemQuality, reason: number, note: string) => {
      setBusyId(item.itemId)
      try {
        const result = await api.post<{ excluded: number }>(
          `/api/admin/education/items/${item.itemVersionId}/exclude-observations`,
          { reason, note: note.trim() || null },
        )
        toast.success(
          `${result.excluded} observations withdrawn`,
          'The answers themselves are untouched. Measurements rebuild from what is left.',
        )
        onChanged()
      } finally {
        setBusyId(null)
      }
    },
    [onChanged],
  )

  const project = useCallback(async () => {
    setBusyId('projection')
    try {
      const report = await api.post<{ responsesRead: number; observationsWritten: number; unmappedResponses: number }>(
        '/api/admin/education/projection/observations',
      )
      toast.success(
        `${report.observationsWritten} observations written`,
        report.unmappedResponses > 0
          ? `${report.unmappedResponses} answers produced nothing — their items map to no target.`
          : undefined,
      )
      onChanged()
    } finally {
      setBusyId(null)
    }
  }, [onChanged])

  return { busyId, setAnchor, exclude, project }
}
