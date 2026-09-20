// ===========================================================================
// Assessment and coverage — data access
//
// Two things live behind this file, and both are human work that nothing in
// the platform can generate:
//
//   1. What an examination covers. A blueprint is a claim about a real paper.
//      Nothing is seeded, deliberately — a plausible blueprint shipped in a
//      migration would wear the schema's authority without anybody having read
//      a syllabus.
//
//   2. What a lesson actually teaches. The migration minted one placeholder
//      target per lesson, which is honest as far as it goes and incapable of
//      saying what an examination covers. Promoting them is the gate on the
//      whole exam feature, and it is the first operation that makes the
//      recompute line pay for itself: re-targeting is an UPDATE on a join
//      table, and every historical answer is re-interpreted from the immutable
//      response log without anybody replaying a lesson.
//
// See Docs/EducationalArchitecture.md §4, §6 and §20.5.
// ===========================================================================

import { useCallback, useState } from 'react'
import { api } from '../../lib/client'
import { toast } from '../../store/toast'

// ---------------------------------------------------------------- blueprints

export interface BlueprintLine {
  lineId: string
  targetId: string
  statement: string
  isPlaceholder: boolean
  weight: number
  itemCount: number
  difficultyBandLow: number | null
  difficultyBandHigh: number | null

  /** Questions in the bank mapped to this target. Zero means the line is unservable. */
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
  sourceNote: string | null
  isPublished: boolean
  minCoverageRatio: number
  minAreaCoverageRatio: number
  minObservationsOverall: number
  minObservationsPerArea: number
  maxMedianEvidenceAgeDays: number
  areas: BlueprintArea[]

  /** Lines naming a lesson placeholder. Non-zero blocks publication. */
  placeholderLines: number

  /** Lines no item can satisfy — the orphan a syllabus change produces. */
  unservableLines: number
}

export interface ExamSummary {
  examSpecificationVersionId: string
  specificationKey: string
  name: string
  versionLabel: string
  authorityName: string
  subjectLabel: string | null
  sittingDate: string | null
  isPublished: boolean
  targetCount: number
  placeholderTargetCount: number
  reportedOutcomes: number
  calibrationUsableOutcomes: number
}

export interface CalibrationStatus {
  examSpecificationVersionId: string
  name: string
  versionLabel: string
  reportedOutcomes: number
  usableOutcomes: number
  withdrawnConsent: number
  disputed: number
  byVerification: Record<string, number>

  /** Matched pairs needed before a predicted outcome renders at all. */
  requiredForCalibration: number
  isCalibrated: boolean
}

// ------------------------------------------------------------------ targets

export interface PlaceholderTarget {
  targetId: string
  statement: string
  nodeId: string | null
  nodeTitle: string | null
  itemCount: number
  observationCount: number
  isSuperseded: boolean
}

export interface AuthoredTarget {
  targetId: string
  targetKey: string
  statement: string
  targetKindKey: string
  reviewState: 'Unreviewed' | 'Reviewed' | 'Deprecated'
  difficultyBand: number | null
  itemCount: number
  supersededPlaceholders: number
}

export interface PromotionReport {
  targetId: string
  targetKey: string
  placeholdersSuperseded: number
  itemMappingsMoved: number
  nodeMappingsMoved: number

  /** Historical answers re-interpreted against the new claim, from the response log. */
  observationsRebuilt: number

  /** Human exclusion decisions carried across the rebuild. Never derived, so never lost. */
  exclusionsPreserved: number
}

/**
 * Why a blueprint cannot be published yet, in the words an admin needs.
 *
 * The placeholder case is not a validation nicety: publishing a blueprint over
 * lesson placeholders would put a completion percentage behind an exam-readiness
 * claim, which is the specific fabrication the architecture exists to prevent.
 */
export function blockingReason(blueprint: Blueprint): string | null {
  if (blueprint.placeholderLines > 0) {
    return `${blueprint.placeholderLines} of its lines still name a lesson rather than a competency. A lesson cannot say what an examination covers — author real targets first.`
  }

  if (blueprint.areas.every((a) => a.lines.length === 0)) {
    return 'It has no lines, so it covers nothing.'
  }

  return null
}

/** Warnings worth showing that do not block publication. */
export function warningFor(blueprint: Blueprint): string | null {
  if (blueprint.unservableLines > 0) {
    return `${blueprint.unservableLines} line(s) have no question in the bank mapped to them. A learner cannot close those gaps here, and a form generated from this blueprint will be short.`
  }

  return null
}

export function useAssessmentActions(onChanged: () => void) {
  const [busyId, setBusyId] = useState<string | null>(null)

  const publishBlueprint = useCallback(
    async (blueprint: Blueprint) => {
      setBusyId(blueprint.blueprintId)
      try {
        await api.post(`/api/admin/assessment/blueprints/${blueprint.blueprintId}/publish`)
        toast.success(
          'Blueprint published',
          'It is now immutable. A revision creates the next version rather than editing this one, because every coverage figure ever computed named the version it was computed against.',
        )
        onChanged()
      } finally {
        setBusyId(null)
      }
    },
    [onChanged],
  )

  const publishExam = useCallback(
    async (exam: ExamSummary) => {
      setBusyId(exam.examSpecificationVersionId)
      try {
        await api.post(`/api/admin/assessment/exams/${exam.examSpecificationVersionId}/publish`)
        toast.success('Examination published', 'Learners can now ask for their coverage against it.')
        onChanged()
      } finally {
        setBusyId(null)
      }
    },
    [onChanged],
  )

  const generateBenchmark = useCallback(
    async (subjectNodeId: string, langId: string) => {
      setBusyId('benchmark')
      try {
        const report = await api.post<{ key: string; areas: number; lines: number; warning: string | null }>(
          `/api/admin/assessment/exams/benchmark?langId=${langId}`,
          { subjectNodeId },
        )
        toast.success(
          `Benchmark built — ${report.areas} areas, ${report.lines} lines`,
          report.warning ??
            'Derived from this platform’s own content with equal weights, and its source note says so. It is a benchmark, not a published examination syllabus.',
        )
        onChanged()
        return report
      } finally {
        setBusyId(null)
      }
    },
    [onChanged],
  )

  return { busyId, publishBlueprint, publishExam, generateBenchmark }
}

export function useTargetActions(onChanged: () => void) {
  const [busy, setBusy] = useState(false)

  const promote = useCallback(
    async (body: {
      targetKey: string
      statements: Record<string, string>
      targetKindKey: string
      difficultyBand: number | null
      replacesTargetIds: string[]
      markReviewed: boolean
    }) => {
      setBusy(true)
      try {
        const report = await api.post<PromotionReport>('/api/admin/assessment/targets/promote', body)

        toast.success(
          `${report.itemMappingsMoved} questions now measure a real claim`,
          report.observationsRebuilt > 0
            ? `${report.observationsRebuilt} historical answers were re-interpreted against it — nobody replayed a lesson.${
                report.exclusionsPreserved > 0
                  ? ` ${report.exclusionsPreserved} withdrawn observations stayed withdrawn.`
                  : ''
              }`
            : 'No learner has answered these questions yet, so there was nothing to re-interpret.',
        )

        onChanged()
        return report
      } finally {
        setBusy(false)
      }
    },
    [onChanged],
  )

  return { busy, promote }
}
