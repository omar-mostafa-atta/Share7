import { useEffect, useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  ArrowRight,
  BadgeCheck,
  Braces,
  ChevronRight,
  History,
  RefreshCw,
  Sparkles,
  Target,
  Wand2,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, IconButton } from '../components/ui/primitives'
import { Note, PageTitle } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable, type Column } from '../components/ui/DataTable'
import { Modal } from '../components/ui/Modal'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { useResource } from '../lib/resource'
import { listVariants } from '../components/ui/motion'
import {
  useAssessmentActions,
  useTargetActions,
  type AuthoredTarget,
  type PlaceholderTarget,
} from '../features/assessment/data'

// ===========================================================================
// Learning targets
//
// The gate on the whole exam feature, and the screen where the recompute line
// finally pays for itself.
//
// The migration minted one PLACEHOLDER target per lesson — a lesson wearing a
// target's clothes. That is honest as far as it goes: per-lesson measurement
// over one is a real claim about the items in that lesson. What it cannot do
// is answer "how good at mathematics", or say what an examination covers,
// because rolling placeholders up produces a completion percentage dressed as
// proficiency. So a blueprint naming one cannot be published, and this page is
// where that gets fixed.
//
// Promoting is cheap and its consequences are not: because items reference
// targets through a mapping table rather than owning them, re-targeting a
// thousand questions is an UPDATE — and because observations are derived from
// the immutable response log rather than stored as truth, every historical
// answer is RE-INTERPRETED against the real claim without anybody replaying a
// lesson. In a system that stored progress as current state this operation
// does not exist, because the evidence it needs was overwritten when it was
// recorded.
//
// Docs/EducationalArchitecture.md §20.5.
// ===========================================================================

interface TreeRow {
  id: string
  name: string
}

const EN = '9C4D7F2A-3E51-4B6C-8D0A-2F7B1E5C9A34'
const AR = '4B8E1D6F-7A29-4C35-9E10-6D3F8B2A5C71'

export function LearningTargets() {
  const [gradeId, setGradeId] = useState('')
  const [termId, setTermId] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [chapterId, setChapterId] = useState('')

  const grades = useResource<TreeRow[]>(`/api/grades?langId=${EN}`, [])
  const terms = useResource<TreeRow[]>(gradeId ? `/api/terms?gradeId=${gradeId}` : null, [])
  const subjects = useResource<TreeRow[]>(termId ? `/api/subjects?termId=${termId}` : null, [])
  const chapters = useResource<TreeRow[]>(subjectId ? `/api/chapters?subjectId=${subjectId}` : null, [])

  // The node whose placeholders are listed. A chapter when one is chosen, the subject otherwise —
  // authoring happens a chapter at a time in practice, and a subject-wide list is how you see how
  // much is left.
  const scope = chapterId || subjectId

  const placeholders = useResource<PlaceholderTarget[]>(
    scope ? `/api/admin/assessment/targets/placeholders?nodeId=${scope}&langId=${EN}` : null,
    [],
  )

  const authored = useResource<AuthoredTarget[]>(`/api/admin/assessment/targets?langId=${EN}`, [])

  const reload = () => {
    void placeholders.reload()
    void authored.reload()
  }

  const { busy, promote } = useTargetActions(reload)
  const { busyId, generateBenchmark } = useAssessmentActions(reload)

  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [promoting, setPromoting] = useState(false)

  // A scope change makes the old selection meaningless — those targets are no longer on screen,
  // and promoting them from here would be promoting something nobody can see.
  useEffect(() => setSelected(new Set()), [scope])

  const live = placeholders.data.filter((p) => !p.isSuperseded)
  const done = placeholders.data.filter((p) => p.isSuperseded)

  const carrying = live.reduce((total, p) => total + p.observationCount, 0)
  const questions = live.reduce((total, p) => total + p.itemCount, 0)

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (!next.delete(id)) next.add(id)
      return next
    })

  const columns: Column<PlaceholderTarget>[] = useMemo(
    () => [
      {
        key: 'pick',
        header: '',
        width: '34px',
        render: (p) =>
          p.isSuperseded ? (
            <BadgeCheck size={15} className="s7-success" />
          ) : (
            <input
              type="checkbox"
              checked={selected.has(p.targetId)}
              onChange={() => toggle(p.targetId)}
              onClick={(e) => e.stopPropagation()}
              aria-label={`Select ${p.statement}`}
            />
          ),
      },
      {
        key: 'statement',
        header: 'Stands in for',
        sort: (p) => p.statement,
        render: (p) => (
          <div>
            <div className={p.isSuperseded ? 's7-muted' : 's7-strong'}>{p.statement}</div>
            {p.nodeTitle && p.nodeTitle !== p.statement ? (
              <div className="s7-muted s7-small">{p.nodeTitle}</div>
            ) : null}
          </div>
        ),
      },
      {
        key: 'items',
        header: 'Questions',
        numeric: true,
        sort: (p) => p.itemCount,
        render: (p) => p.itemCount,
      },
      {
        key: 'evidence',
        header: 'Answers riding on it',
        numeric: true,
        sort: (p) => p.observationCount,
        // The number that decides what to work on first. A placeholder carrying four thousand
        // answers is four thousand answers currently attached to "Lesson 3" rather than to a
        // claim anybody could act on.
        render: (p) => (
          <span className={p.observationCount > 0 ? 's7-strong' : 's7-muted'}>
            {p.observationCount.toLocaleString()}
          </span>
        ),
      },
      {
        key: 'state',
        header: '',
        render: (p) => (p.isSuperseded ? <Badge tone="success">Promoted</Badge> : null),
      },
    ],
    [selected],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Target size={22} />}
        title="Learning targets"
        subtitle="Replacing lesson placeholders with claims a child can actually be said to have met."
        actions={
          <IconButton label="Reload" onClick={reload} busy={placeholders.refreshing || authored.refreshing}>
            <RefreshCw size={16} />
          </IconButton>
        }
      />

      <StatRow>
        <Stat
          icon={<Braces size={15} />}
          label="Placeholders here"
          value={live.length}
          sub={done.length > 0 ? `${done.length} already promoted in this scope` : 'Pick a subject to begin'}
          tone={live.length > 0 ? 'warning' : 'success'}
        />
        <Stat
          icon={<History size={15} />}
          label="Answers riding on them"
          value={carrying}
          sub="Re-interpreted automatically the moment a real claim replaces the placeholder"
          tone="brand"
        />
        <Stat
          icon={<Braces size={15} />}
          label="Questions attached"
          value={questions}
          sub="What a promotion would move onto the new claim"
          tone="cool"
        />
        <Stat
          icon={<Sparkles size={15} />}
          label="Authored targets"
          value={authored.data.length}
          sub={`${authored.data.filter((t) => t.reviewState === 'Reviewed').length} confirmed by a specialist`}
          tone={authored.data.length > 0 ? 'success' : 'warning'}
        />
      </StatRow>

      <Card>
        <CardHeader icon={<ChevronRight size={16} />} title="Where to work" />
        <CardBody>
          <div className="s7-filters">
            <Field label="Grade">
              <Select
                value={gradeId}
                onChange={(e) => {
                  setGradeId(e.target.value)
                  setTermId('')
                  setSubjectId('')
                  setChapterId('')
                }}
              >
                <option value="">Choose…</option>
                {grades.data.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Term">
              <Select
                value={termId}
                disabled={!gradeId}
                onChange={(e) => {
                  setTermId(e.target.value)
                  setSubjectId('')
                  setChapterId('')
                }}
              >
                <option value="">Choose…</option>
                {terms.data.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Subject">
              <Select
                value={subjectId}
                disabled={!termId}
                onChange={(e) => {
                  setSubjectId(e.target.value)
                  setChapterId('')
                }}
              >
                <option value="">Choose…</option>
                {subjects.data.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Chapter" hint="Leave blank to see the whole subject">
              <Select value={chapterId} disabled={!subjectId} onChange={(e) => setChapterId(e.target.value)}>
                <option value="">Whole subject</option>
                {chapters.data.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </Field>
          </div>

          {subjectId ? (
            <div className="s7-row-actions">
              <Button
                variant="ghost"
                loading={busyId === 'benchmark'}
                disabled={live.length > 0}
                onClick={() => void generateBenchmark(subjectId, EN)}
              >
                <Wand2 size={15} /> Build a benchmark from this subject
              </Button>
              {live.length > 0 ? (
                <span className="s7-muted s7-small">
                  Available once this subject has no placeholders left — a benchmark over one could not be
                  published anyway.
                </span>
              ) : null}
            </div>
          ) : null}
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          icon={<Target size={16} />}
          title="Placeholders"
          actions={
            selected.size > 0 ? (
              <Button onClick={() => setPromoting(true)}>
                <ArrowRight size={15} /> Promote {selected.size} into one claim
              </Button>
            ) : null
          }
        />
        <CardBody>
          <Note>
            Each of these is one lesson, typed as a target so that measurement could start before a framework
            existed. Per-lesson measurement over one is honest — it says no more than "on the items in this
            lesson". What it cannot do is say what an examination covers, which is why a blueprint naming one
            cannot be published.
          </Note>

          <DataTable
            rows={placeholders.data}
            columns={columns}
            getId={(p) => p.targetId}
            loading={placeholders.loading}
            initialSort={{ key: 'evidence', direction: 'desc' }}
            empty={
              <EmptyState icon={<Target size={28} />}>
                <strong>{scope ? 'No placeholders here' : 'Choose a subject'}</strong>
                <span className="s7-empty-hint">
                  {scope
                    ? 'Everything under this node already measures an authored claim.'
                    : 'Placeholders are listed per subject or chapter, because authoring real targets is subject work.'}
                </span>
              </EmptyState>
            }
          />
        </CardBody>
      </Card>

      <Card>
        <CardHeader icon={<Sparkles size={16} />} title="Authored targets" />
        <CardBody>
          <DataTable
            rows={authored.data}
            columns={[
              {
                key: 'statement',
                header: 'Claim',
                sort: (t) => t.statement,
                render: (t) => (
                  <div>
                    <div className="s7-strong">{t.statement}</div>
                    <div className="s7-muted s7-small s7-mono">{t.targetKey}</div>
                  </div>
                ),
              },
              {
                key: 'review',
                header: 'Review',
                sort: (t) => t.reviewState,
                render: (t) =>
                  t.reviewState === 'Reviewed' ? (
                    <Badge tone="success">
                      <BadgeCheck size={12} /> Specialist confirmed
                    </Badge>
                  ) : (
                    <Badge tone="warning">Unreviewed</Badge>
                  ),
              },
              {
                key: 'items',
                header: 'Questions',
                numeric: true,
                sort: (t) => t.itemCount,
                render: (t) => t.itemCount,
              },
              {
                key: 'replaced',
                header: 'Lessons replaced',
                numeric: true,
                sort: (t) => t.supersededPlaceholders,
                render: (t) => t.supersededPlaceholders,
              },
            ]}
            getId={(t) => t.targetId}
            loading={authored.loading}
            empty={
              <EmptyState icon={<Sparkles size={28} />}>
                <strong>Nothing authored yet</strong>
                <span className="s7-empty-hint">This framework starts empty on purpose. A real claim about what a child can do has to be written by somebody who knows the subject — there is no honest way to seed one.</span>
              </EmptyState>
            }
          />
        </CardBody>
      </Card>

      <PromoteModal
        open={promoting}
        onClose={() => setPromoting(false)}
        busy={busy}
        placeholders={live.filter((p) => selected.has(p.targetId))}
        onPromote={async (body) => {
          await promote({ ...body, replacesTargetIds: [...selected] })
          setSelected(new Set())
          setPromoting(false)
        }}
      />
    </motion.div>
  )
}

function PromoteModal({
  open,
  onClose,
  busy,
  placeholders,
  onPromote,
}: {
  open: boolean
  onClose: () => void
  busy: boolean
  placeholders: PlaceholderTarget[]
  onPromote: (body: {
    targetKey: string
    statements: Record<string, string>
    targetKindKey: string
    difficultyBand: number | null
    markReviewed: boolean
  }) => Promise<void>
}) {
  const [key, setKey] = useState('')
  const [english, setEnglish] = useState('')
  const [arabic, setArabic] = useState('')
  const [kind, setKind] = useState('skill')
  const [band, setBand] = useState('')
  const [reviewed, setReviewed] = useState(false)

  const evidence = placeholders.reduce((total, p) => total + p.observationCount, 0)
  const items = placeholders.reduce((total, p) => total + p.itemCount, 0)

  // Both languages, because a target with no Arabic statement is a target half the learners
  // cannot be shown.
  const ready = key.trim().length > 0 && english.trim().length > 0 && arabic.trim().length > 0

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<Target size={18} />}
      title={`Promote ${placeholders.length} placeholder${placeholders.length === 1 ? '' : 's'}`}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button
            loading={busy}
            disabled={!ready}
            onClick={() =>
              void onPromote({
                targetKey: key.trim(),
                statements: { [EN]: english.trim(), [AR]: arabic.trim() },
                targetKindKey: kind,
                difficultyBand: band ? Number(band) : null,
                markReviewed: reviewed,
              })
            }
          >
            <ArrowRight size={15} /> Promote
          </Button>
        </>
      }
    >
      <Note>
        {items} question{items === 1 ? '' : 's'} will move onto the new claim
        {evidence > 0 ? (
          <>
            , and <strong>{evidence.toLocaleString()} historical answers</strong> will be re-interpreted against
            it from the response log. Nobody replays a lesson, and any observations a human withdrew stay
            withdrawn.
          </>
        ) : (
          '. No learner has answered these yet, so there is nothing to re-interpret.'
        )}
      </Note>

      <ul className="s7-replacing">
        {placeholders.map((p) => (
          <li key={p.targetId}>
            <span>{p.statement}</span>
            <span className="s7-muted s7-small">{p.observationCount.toLocaleString()} answers</span>
          </li>
        ))}
      </ul>

      <Field label="Key" hint="Stable and human-readable, e.g. eg.p6.math.fractions.order">
        <Input value={key} onChange={(e) => setKey(e.target.value)} mono placeholder="eg.p6.math.fractions.order" />
      </Field>

      <Field
        label="The claim, in English"
        hint="Phrase it as something that can be true or false about a child: &ldquo;Can order fractions with unlike denominators.&rdquo;"
      >
        <Input
          value={english}
          onChange={(e) => setEnglish(e.target.value)}
          placeholder="Can order fractions with unlike denominators"
        />
      </Field>

      <Field label="The claim, in Arabic" hint="Required — a target with no Arabic statement is invisible to half the learners.">
        <Input value={arabic} onChange={(e) => setArabic(e.target.value)} dir="rtl" />
      </Field>

      <div className="s7-filters">
        <Field label="Kind">
          <Select value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="skill">Skill</option>
            <option value="concept">Concept</option>
            <option value="procedure">Procedure</option>
          </Select>
        </Field>

        <Field label="Difficulty band" hint="1 easiest to 5 hardest. Leave blank until somebody who knows has said.">
          <Select value={band} onChange={(e) => setBand(e.target.value)}>
            <option value="">Not stated</option>
            {[1, 2, 3, 4, 5].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </Select>
        </Field>
      </div>

      <Switch
        checked={reviewed}
        onChange={setReviewed}
        label={
          <>
            A subject specialist has confirmed this
            <span className="s7-muted s7-small">
              {' '}
              — only tick it if that is true. An unreviewed claim still measures; it simply says so.
            </span>
          </>
        }
      />
    </Modal>
  )
}
