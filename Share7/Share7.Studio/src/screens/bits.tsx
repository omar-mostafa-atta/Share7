import { Link } from 'react-router-dom'
import { useLanguages } from '../App'
import { Mark, type Stroke } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { useTitle } from '../lib/use'
import type {
  DraftStatus,
  DraftSummary,
  NodeKind,
  ReleaseStatus,
  SkillReviewState,
  StudioNode,
  TrailStep,
} from '../lib/studio'

// ===========================================================================
// Readings every screen shares
//
// A state is a stroke AND a name, always together — these are the only places
// the Studio turns one of its statuses into something on screen, so the mark
// and the word can never drift apart.
// ===========================================================================

const draftStrokes: Record<DraftStatus, Stroke> = {
  Editing: 'writing',
  InReview: 'waiting',
  ChangesRequested: 'wrong',
  Approved: 'written',
  Released: 'written',
  Discarded: 'ended',
}

const releaseStrokes: Record<ReleaseStatus, Stroke> = {
  Building: 'waiting',
  Scheduled: 'waiting',
  Publishing: 'waiting',
  Published: 'written',
  Failed: 'wrong',
  Cancelled: 'ended',
}

export function DraftState({ status }: { status: DraftStatus }) {
  const { t } = useI18n()
  return <Mark stroke={draftStrokes[status]}>{t(`status.${status}`)}</Mark>
}

export function ReleaseState({ status }: { status: ReleaseStatus }) {
  const { t } = useI18n()
  return <Mark stroke={releaseStrokes[status]}>{t(`releaseStatus.${status}`)}</Mark>
}

/** "Grade 5 · Term 1 · Science · Chapter 2" — everything above the thing itself. */
export function TrailLine({ trail, drop = 0 }: { trail: TrailStep[]; drop?: number }) {
  const languages = useLanguages()
  const title = useTitle()
  const steps = drop ? trail.slice(0, trail.length - drop) : trail

  if (steps.length === 0) return null

  return (
    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
      {steps.map((step) => title(step.titles, languages)).join(' · ')}
    </span>
  )
}

export function KindName({ kind }: { kind: NodeKind }) {
  const { t } = useI18n()
  return <>{t(`curriculum.kind.${kind}`)}</>
}

/** Where a draft belongs, and where the Studio sends you when you open it. */
export function draftHref(draft: DraftSummary): string {
  if (draft.kind === 'LessonContent' && draft.nodeId && !draft.isPractice) return `/lessons/${draft.nodeId}`
  return `/drafts/${draft.id}`
}

export function DraftRow({ draft, aside }: { draft: DraftSummary; aside?: React.ReactNode }) {
  const { t, formatRelative } = useI18n()

  return (
    <Link to={draftHref(draft)} className="row">
      <span className="row-main">
        <span className="row-title">{draft.title}</span>
        <span className="spread" style={{ gap: 'var(--s3)' }}>
          <TrailLine trail={draft.trail} drop={draft.kind === 'LessonContent' ? 1 : 0} />
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            {t(`kind.${draft.kind}`)}
          </span>
          {draft.isPractice ? (
            <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
              {t('lesson.practice')}
            </span>
          ) : null}
        </span>
      </span>
      <span className="row-side">
        {draft.isOutOfDate ? <Mark stroke="wrong">{t('blocker.outOfDate')}</Mark> : null}
        {draft.openComments > 0 ? (
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            <span className="num">{draft.openComments}</span> {t('review.comments')}
          </span>
        ) : null}
        <DraftState status={draft.status} />
        <time className="quiet" style={{ fontSize: 'var(--t-sm)' }} dateTime={draft.updatedAtUtc}>
          {formatRelative(draft.updatedAtUtc)}
        </time>
        {aside}
      </span>
    </Link>
  )
}

/** What a lesson has, per language, as the curriculum lists it. */
export function LessonCounts({ node }: { node: StudioNode }) {
  const { t } = useI18n()
  const languages = useLanguages()

  if (node.kind !== 'lesson') return null

  const counts = node.questionCounts ?? {}
  const missing = node.missingLanguages ?? []
  const written = languages
    .filter((language) => language.isContentLanguage)
    .sort((a, b) => a.sortOrder - b.sortOrder)

  if (written.length === 0) return null

  const total = Object.values(counts).reduce((sum, one) => sum + one, 0)
  if (total === 0 && missing.length === 0) {
    return <Mark stroke="waiting">{t('curriculum.noQuestions')}</Mark>
  }

  return (
    <span className="people">
      {written.map((language) => {
        const count = counts[language.id] ?? 0
        const gone = missing.includes(language.id)
        return (
          <Mark key={language.id} stroke={gone ? 'wrong' : count > 0 ? 'written' : 'waiting'}>
            {t('curriculum.questionsIn', { count, language: language.name })}
          </Mark>
        )
      })}
    </span>
  )
}

/**
 * What the inbox says happened, in one line. A kind this build has never heard
 * of still gets a sentence rather than a blank row: the server may learn about
 * something before a browser tab left open overnight does.
 */
const notified = new Set<string>([
  'draft.submitted',
  'draft.approved',
  'draft.changes_requested',
  'draft.commented',
  'draft.released',
  'assignment.created',
  'assignment.done',
])

export function Notified({ kind, actor }: { kind: string; actor?: string }) {
  const { t } = useI18n()
  if (!notified.has(kind)) return <>{t('notify.unknown')}</>
  return <>{t(`notify.${kind}` as 'notify.draft.submitted', { name: actor ?? '' })}</>
}

// ===========================================================================
// Phase 5's shared readings
// ===========================================================================

const skillStrokes: Record<SkillReviewState, Stroke> = {
  Reviewed: 'written',
  Unreviewed: 'waiting',
  Deprecated: 'ended',
}

export function SkillState({ state }: { state: SkillReviewState }) {
  const { t } = useI18n()
  return <Mark stroke={skillStrokes[state]}>{t(`skills.state.${state}`)}</Mark>
}

/**
 * A short column of counted facts. Rules and figures that line up, not a card
 * and not a grid of big numbers — this board says how many of something there
 * are in the same voice it says everything else.
 */
export function Tally({ lines }: { lines: [string, number | string][] }) {
  return (
    <div className="rows tally">
      {lines.map(([name, value]) => (
        <div className="row" key={name}>
          <span className="row-main">
            <span className="row-title">{name}</span>
          </span>
          <span className="row-side">
            <span className="num">{value}</span>
          </span>
        </div>
      ))}
    </div>
  )
}

/**
 * What went wrong, in the member's language. The API answers a refusal with a
 * message key; anything else — a dropped connection, a proxy — has no key, and
 * a board that showed the raw text would be showing somebody else's words.
 */
export function saidWrong(tError: (key: string) => string, error: unknown): string {
  const key = (error as { messageKey?: string } | null)?.messageKey
  return tError(key ?? 'errors.unexpected')
}
