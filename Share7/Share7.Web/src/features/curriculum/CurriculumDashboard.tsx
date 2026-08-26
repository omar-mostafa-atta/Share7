import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  AlertTriangle,
  BookOpen,
  CheckCircle2,
  ChevronRight,
  Languages,
  Layers,
  ListChecks,
  RefreshCw,
  ShieldAlert,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, IconButton, SkeletonRows } from '../../components/ui/primitives'
import { Meter, Note, Segmented } from '../../components/ui/bits'
import { Stat, StatRow } from '../../components/ui/Stat'
import { listVariants } from '../../components/ui/motion'
import { useCurriculumHealth } from './insight'
import type { CurriculumIssueDto, CurriculumIssueKind } from '../../types/api'

// ===========================================================================
// Curriculum dashboard
//
// The tree tells you what exists. This tells you whether any of it works.
//
// Two questions, in this order: how much of the curriculum is finished, and
// what specifically is broken. Coverage first because it is the number anyone
// actually asks for, findings second because they are only actionable once you
// know whether you are looking at three problems or three hundred.
//
// Errors and warnings are kept apart rather than summed. An error means a child
// can reach the node and find nothing usable; a warning means the content plays
// but is unfinished, usually in one language. A single "42 issues" number would
// bury the four that break the app under the thirty-eight that need a translator.
// ===========================================================================

const ISSUE_LABELS: Record<CurriculumIssueKind, string> = {
  GradeWithoutTerms: 'Grade has no terms',
  TermWithoutSubjects: 'Term has no subjects',
  SubjectWithoutChapters: 'Subject has no chapters',
  ChapterWithoutLessons: 'Chapter has no lessons',
  LessonWithoutQuestions: 'Lesson has no questions',
  LessonWithoutRecovery: 'Lesson has no recovery pool',
  LessonLanguageGap: 'Missing in one language',
  LessonVersionDrift: 'Languages at different versions',
  MissingTranslation: 'Node has no name in one language',
}

type Lens = 'errors' | 'warnings' | 'all'

export function CurriculumDashboard({
  onOpenLesson,
}: {
  /**
   * Opens the question editor for a lesson-level finding, so a problem is one click from its fix.
   *
   * Only lessons. A finding on a chapter or a term is "this branch is empty", and the fix is to
   * author something under it in the tree — there is no editor to jump to, and a button that
   * scrolled a tree to a node it has not loaded would be a button that usually does nothing.
   */
  onOpenLesson: (lessonId: string, path: string[]) => void
}) {
  const { health, loading, reload } = useCurriculumHealth()
  const [lens, setLens] = useState<Lens>('errors')

  const issues = useMemo(() => {
    if (!health) return []
    if (lens === 'all') return health.issues
    const wanted = lens === 'errors' ? 'Error' : 'Warning'
    return health.issues.filter((i) => i.severity === wanted)
  }, [health, lens])

  const grouped = useMemo(() => {
    const map = new Map<CurriculumIssueKind, CurriculumIssueDto[]>()
    for (const issue of issues) {
      map.set(issue.kind, [...(map.get(issue.kind) ?? []), issue])
    }
    return [...map.entries()].sort((a, b) => b[1].length - a[1].length)
  }, [issues])

  const stats = health?.stats

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible" className="s7-stack">
      <StatRow>
        <Stat
          icon={<Layers size={16} />}
          label="Lessons"
          value={stats?.lessons ?? 0}
          sub={`${stats?.grades ?? 0} grades · ${stats?.subjects ?? 0} subjects · ${stats?.chapters ?? 0} chapters`}
        />
        <Stat
          icon={<BookOpen size={16} />}
          label="Published"
          value={stats?.lessonsWithQuestions ?? 0}
          sub={percentOf(stats?.lessonsWithQuestions, stats?.lessons)}
          tone="info"
        />
        <Stat
          icon={<Languages size={16} />}
          label="Bilingual"
          value={stats?.lessonsFullyBilingual ?? 0}
          sub={percentOf(stats?.lessonsFullyBilingual, stats?.lessons)}
          tone="info"
        />
        <Stat
          icon={<CheckCircle2 size={16} />}
          label="Ready to play"
          value={stats?.lessonsReady ?? 0}
          sub="published, bilingual, with recovery"
          tone={stats && stats.lessonsReady === stats.lessons ? 'success' : 'warning'}
        />
        <Stat
          icon={<ListChecks size={16} />}
          label="Questions"
          value={(stats?.questionsEn ?? 0) + (stats?.questionsAr ?? 0)}
          sub={`+ ${(stats?.recoveryQuestionsEn ?? 0) + (stats?.recoveryQuestionsAr ?? 0)} recovery`}
        />
        <Stat
          icon={<ShieldAlert size={16} />}
          label="Problems"
          value={health ? `${health.errorCount} / ${health.warningCount}` : '—'}
          sub="errors / warnings"
          tone={health && health.errorCount > 0 ? 'danger' : 'success'}
        />
      </StatRow>

      <Card>
        <CardHeader
          icon={<ShieldAlert size={16} />}
          title="Findings"
          actions={
            <IconButton label="Re-scan" busy={loading} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          {stats ? (
            <div className="s7-stack" style={{ marginBottom: '0.9rem' }}>
              <Coverage label="Questions published" value={stats.lessonsWithQuestions} max={stats.lessons} />
              <Coverage label="Recovery pool present" value={stats.lessonsWithRecovery} max={stats.lessons} />
              <Coverage label="Both languages" value={stats.lessonsFullyBilingual} max={stats.lessons} />
            </div>
          ) : null}

          <div className="s7-bar">
            <Segmented
              layoutId="health-lens"
              value={lens}
              onChange={setLens}
              options={[
                { value: 'errors', label: `Errors (${health?.errorCount ?? 0})` },
                { value: 'warnings', label: `Warnings (${health?.warningCount ?? 0})` },
                { value: 'all', label: 'All' },
              ]}
            />
          </div>

          {health?.truncated ? (
            <Note tone="warning">
              Showing the first {health.issues.length} findings. The counts above are exact — fix a
              batch and re-scan to see the rest.
            </Note>
          ) : null}

          {loading && !health ? (
            <SkeletonRows rows={4} />
          ) : grouped.length === 0 ? (
            <EmptyState icon={<CheckCircle2 size={20} />}>
              {lens === 'errors'
                ? 'No errors. Every branch has content and every published lesson has a recovery pool.'
                : 'Nothing here.'}
            </EmptyState>
          ) : (
            <div className="s7-stack">
              {grouped.map(([kind, list]) => (
                <IssueGroup key={kind} kind={kind} issues={list} onOpenLesson={onOpenLesson} />
              ))}
            </div>
          )}
        </CardBody>
      </Card>
    </motion.div>
  )
}

function Coverage({ label, value, max }: { label: string; value: number; max: number }) {
  const short = max > 0 && value < max

  return (
    <div>
      <div className="s7-bar" style={{ marginBottom: '0.25rem' }}>
        <span className="s7-hint">{label}</span>
        <span className="s7-muted" style={{ marginInlineStart: 'auto' }}>
          {value} / {max} · {percentOf(value, max)}
        </span>
      </div>
      <Meter value={value} max={max} tone={short ? 'warning' : undefined} />
    </div>
  )
}

function IssueGroup({
  kind,
  issues,
  onOpenLesson,
}: {
  kind: CurriculumIssueKind
  issues: CurriculumIssueDto[]
  onOpenLesson: (lessonId: string, path: string[]) => void
}) {
  // Collapsed by default: a seeded tree can produce hundreds of one kind, and the count is the part
  // that matters until somebody decides to work on that kind.
  const [open, setOpen] = useState(false)
  const isError = issues[0]?.severity === 'Error'

  return (
    <div className="s7-panel">
      <button type="button" className="s7-bar s7-row-button" onClick={() => setOpen((v) => !v)}>
        <ChevronRight
          size={15}
          style={{ transform: open ? 'rotate(90deg)' : undefined, transition: 'transform 120ms' }}
        />
        {isError ? <ShieldAlert size={15} /> : <AlertTriangle size={15} />}
        <strong>{ISSUE_LABELS[kind]}</strong>
        <Badge tone={isError ? 'danger' : 'warning'}>{issues.length}</Badge>
      </button>

      {open ? (
        <div className="s7-stack" style={{ marginTop: '0.5rem' }}>
          {issues.slice(0, 50).map((issue) => (
            <div key={`${issue.nodeId}-${issue.kind}-${issue.detail}`} className="s7-bar">
              <span className="s7-hint" style={{ minWidth: 0 }}>
                {issue.path.join(' › ')}
              </span>
              <span className="s7-muted">{issue.detail}</span>
              {issue.nodeLevel === 'lesson' ? (
                <Button
                  variant="ghost"
                  style={{ marginInlineStart: 'auto' }}
                  onClick={() => onOpenLesson(issue.nodeId, issue.path)}
                >
                  Open
                </Button>
              ) : null}
            </div>
          ))}

          {issues.length > 50 ? (
            <span className="s7-muted">…and {issues.length - 50} more of this kind.</span>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}

function percentOf(value: number | undefined, max: number | undefined): string {
  if (!max) return '—'
  return `${Math.round(((value ?? 0) / max) * 100)}%`
}
