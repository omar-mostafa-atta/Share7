import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  AlertTriangle,
  Anchor,
  Ban,
  Database,
  FlaskConical,
  Gauge,
  RefreshCw,
  Target,
  Unlink,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, IconButton } from '../components/ui/primitives'
import { Note, PageTitle } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable, type Column } from '../components/ui/DataTable'
import { Modal } from '../components/ui/Modal'
import { Field, Input, Select } from '../components/ui/form'
import { useResource } from '../lib/resource'
import { listVariants } from '../components/ui/motion'
import {
  EXCLUSION_REASONS,
  FLAG_COPY,
  useQualityActions,
  type ItemQuality,
  type QualityFlag,
  type QualitySummary,
} from '../features/quality/data'

// ===========================================================================
// Content Quality
//
// What children's answers say about the CONTENT, not about the children.
//
// This is the first screen the educational evidence layer makes possible, and
// it deliberately arrives before any learner-facing proficiency view: the
// platform can tell an editor that a question is mis-keyed long before it can
// honestly tell a parent how good their child is at mathematics. Everything
// here is actionable today and none of it needs a psychometric model.
//
// The one number to read carefully is FACILITY: proportion correct on first,
// controlled encounters, where HIGHER MEANS EASIER. It is not a score and it
// is not a quality rating — a facility of 0.35 on a hard question is exactly
// right, and the same figure on an easy one means the key is probably wrong.
//
// Docs/EducationalArchitecture.md §16.4.
// ===========================================================================

const EMPTY_SUMMARY: QualitySummary = {
  items: 0,
  itemsWithResponses: 0,
  itemsAboveReportingFloor: 0,
  itemsUnmapped: 0,
  anchorItems: 0,
  responses: 0,
  observations: 0,
  observationsExcluded: 0,
  pendingResponses: 0,
  observationsByStrength: {},
  flagCounts: {},
  placeholderTargets: 0,
  authoredTargets: 0,
  reportingFloor: 30,
  curriculumProjection: {
    liveNodes: 0,
    retiredNodes: 0,
    legacyRows: 0,
    versionLabel: '—',
    isAuthoritative: false,
    nodesByKind: {},
    missing: 0,
  },
}

export function ContentQuality() {
  const [flag, setFlag] = useState<QualityFlag | ''>('')

  const summary = useResource<QualitySummary>('/api/admin/education/quality/summary', EMPTY_SUMMARY)

  const items = useResource<ItemQuality[]>(
    `/api/admin/education/quality/items?take=100${flag ? `&flag=${flag}` : ''}`,
    [],
  )

  const reload = () => {
    void summary.reload()
    void items.reload()
  }

  const { busyId, setAnchor, exclude, project } = useQualityActions(reload)
  const [excluding, setExcluding] = useState<ItemQuality | null>(null)

  const s = summary.data

  // The flags that exist right now, with their counts, so the filter offers
  // only what the bank actually has rather than a menu of empty options.
  const flagOptions = useMemo(
    () =>
      (Object.keys(FLAG_COPY) as QualityFlag[])
        .map((key) => ({ key, count: s.flagCounts[key] ?? 0 }))
        .filter((f) => f.count > 0),
    [s.flagCounts],
  )

  const actionable = flagOptions
    .filter((f) => f.key !== 'insufficient_data')
    .reduce((total, f) => total + f.count, 0)

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<FlaskConical size={22} />}
        title="Content Quality"
        subtitle="What learners' answers say about the questions — mis-keyed items, dead distractors, and content nothing can measure."
        actions={
          <>
            <Button variant="ghost" loading={busyId === 'projection'} onClick={() => void project()}>
              <RefreshCw size={15} /> Fold pending answers
            </Button>
            <IconButton label="Reload" onClick={reload} busy={summary.refreshing || items.refreshing}>
              <RefreshCw size={16} />
            </IconButton>
          </>
        }
      />

      <StatRow>
        <Stat
          icon={<Database size={15} />}
          label="Items in the bank"
          value={s.items}
          sub={`${s.itemsWithResponses.toLocaleString()} have been answered at least once`}
          tone="brand"
        />
        <Stat
          icon={<Gauge size={15} />}
          label="Enough answers to judge"
          value={s.itemsAboveReportingFloor}
          sub={`${s.reportingFloor} first encounters is the floor — below it nothing is reported`}
          tone={s.itemsAboveReportingFloor > 0 ? 'success' : 'warning'}
        />
        <Stat
          icon={<AlertTriangle size={15} />}
          label="Needing attention"
          value={actionable}
          sub={actionable > 0 ? 'Items carrying a flag other than "not enough data"' : 'Nothing flagged'}
          tone={actionable > 0 ? 'danger' : 'success'}
        />
        <Stat
          icon={<Unlink size={15} />}
          label="Measuring nothing"
          value={s.itemsUnmapped}
          sub="Mapped to no learning target, so no answer to them can ever count"
          tone={s.itemsUnmapped > 0 ? 'warning' : 'success'}
        />
        <Stat
          icon={<Anchor size={15} />}
          label="Anchor items"
          value={s.anchorItems}
          sub="Administered broadly on purpose, so scales can be linked later"
          tone="cool"
        />
      </StatRow>

      <div className="s7-split">
        <Card>
          <CardHeader icon={<Target size={16} />} title="The evidence behind this page" />
          <CardBody>
            <div className="s7-kv">
              <Row label="Answers recorded" value={s.responses.toLocaleString()} />
              <Row
                label="Interpreted as observations"
                value={s.observations.toLocaleString()}
                hint="One response can say something about more than one learning target."
              />
              <Row
                label="Waiting to be folded"
                value={s.pendingResponses.toLocaleString()}
                hint="Answers the projection has not read yet. A number that keeps growing means the measurement layer is behind."
                tone={s.pendingResponses > 0 ? 'warning' : undefined}
              />
              <Row
                label="Withdrawn"
                value={s.observationsExcluded.toLocaleString()}
                hint="Annotated as not counting — the answers themselves are never deleted."
              />
            </div>

            <div className="s7-kv" style={{ marginTop: 14 }}>
              {Object.entries(s.observationsByStrength).map(([strength, count]) => (
                <Row
                  key={strength}
                  label={`${strength}-grade evidence`}
                  value={count.toLocaleString()}
                  hint={STRENGTH_HINT[strength]}
                />
              ))}
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader icon={<Target size={16} />} title="What the content claims to teach" />
          <CardBody>
            <div className="s7-kv">
              <Row label="Authored learning targets" value={s.authoredTargets.toLocaleString()} />
              <Row
                label="Lesson placeholders"
                value={s.placeholderTargets.toLocaleString()}
                hint="One per lesson, standing in until real targets are written."
              />
            </div>

            <div className="s7-kv" style={{ marginTop: 14 }}>
              <Row
                label="Nodes out of step"
                value={s.curriculumProjection.missing.toLocaleString()}
                hint={`Typed rows with no node, projected as "${s.curriculumProjection.versionLabel}". Always zero — a tree edit syncs the projection in the same transaction, and the node ids are the lesson and chapter ids you already have.`}
                tone={s.curriculumProjection.missing > 0 ? 'warning' : undefined}
              />
            </div>

            {/* The shape gets its own row rather than a value in the list. It is the one thing
                on this page that proves the curriculum is no longer hardcoded: these kinds are
                data, and a different curriculum would show a different set here with no code
                change at all. */}
            <div className="s7-badge-row" style={{ marginTop: 10 }}>
              {KIND_ORDER.filter((k) => s.curriculumProjection.nodesByKind[k]).map((kind) => (
                <Badge key={kind} tone="muted">
                  {s.curriculumProjection.nodesByKind[kind].toLocaleString()} {kind}
                </Badge>
              ))}
              {Object.keys(s.curriculumProjection.nodesByKind)
                .filter((k) => !KIND_ORDER.includes(k))
                .map((kind) => (
                  <Badge key={kind} tone="muted">
                    {s.curriculumProjection.nodesByKind[kind].toLocaleString()} {kind}
                  </Badge>
                ))}
            </div>

            {s.curriculumProjection.missing > 0 ? (
              <Note tone="warning">
                The node projection has fallen behind the curriculum tree. Nothing built on nodes —
                including every measurement — can see the missing content until it is rebuilt.
              </Note>
            ) : null}

            {s.authoredTargets === 0 ? (
              <Note tone="warning">
                Every target is still a placeholder — a lesson wearing a target's clothes. Per-lesson
                measurement over these is honest, because it claims no more than "the items in this
                lesson". <strong>They are deliberately excluded from any cross-lesson claim</strong>,
                so nothing here can be rolled up into "how good is this student at mathematics" until
                real targets are authored.
              </Note>
            ) : null}
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader
          icon={<AlertTriangle size={16} />}
          title="Items, worst first"
          actions={
            <Select value={flag} onChange={(e) => setFlag(e.target.value as QualityFlag | '')}>
              <option value="">All items</option>
              {flagOptions.map((f) => (
                <option key={f.key} value={f.key}>
                  {FLAG_COPY[f.key].label} ({f.count.toLocaleString()})
                </option>
              ))}
            </Select>
          }
        />
        <CardBody>
          {flag ? <Note>{FLAG_COPY[flag].hint}</Note> : null}

          <DataTable<ItemQuality>
            rows={items.data}
            loading={items.loading}
            getId={(row) => row.itemVersionId}
            empty={
              <EmptyState icon={<FlaskConical size={22} />}>
                Nothing matches. Once learners start answering, the questions worth fixing appear here.
              </EmptyState>
            }
            columns={columns(busyId, setAnchor, setExcluding)}
          />
        </CardBody>
      </Card>

      <ExcludeModal
        item={excluding}
        busy={busyId === excluding?.itemId}
        onClose={() => setExcluding(null)}
        onConfirm={async (reason, note) => {
          if (!excluding) return
          await exclude(excluding, reason, note)
          setExcluding(null)
        }}
      />
    </motion.div>
  )
}

/**
 * The order the Egyptian tree happens to use. A curriculum with a different shape simply
 * falls through to the unordered pass below — the kinds are data, and this list is a
 * presentation preference rather than a schema.
 */
const KIND_ORDER = ['grade', 'term', 'subject', 'chapter', 'lesson']

const STRENGTH_HINT: Record<string, string> = {
  Assessment: 'A first, unhinted, no-retry administration. The only class an exam claim may read.',
  Practice: 'Collected under conditions that inflate performance — a replay, a hint, a retry.',
  Indicative: 'A designed gameplay signal rather than an item response. Enters at its contract weight.',
  None: 'Recorded, never counted.',
}

function Row({
  label,
  value,
  hint,
  tone,
}: {
  label: string
  value: string
  hint?: string
  tone?: 'warning'
}) {
  return (
    <div className="s7-kv-row">
      <div>
        <div className="s7-kv-label">{label}</div>
        {hint ? <div className="s7-kv-hint">{hint}</div> : null}
      </div>
      <div className={tone === 'warning' ? 's7-kv-value s7-text-warning' : 's7-kv-value'}>{value}</div>
    </div>
  )
}

function columns(
  busyId: string | null,
  setAnchor: (item: ItemQuality, next: boolean) => Promise<void>,
  setExcluding: (item: ItemQuality) => void,
): Column<ItemQuality>[] {
  return [
    {
      key: 'stem',
      header: 'Question',
      sort: (row) => row.stem ?? '',
      render: (row) => (
        <div>
          <div className="s7-cell-strong">{row.stem ?? <em>No rendering in this language</em>}</div>
          <div className="s7-cell-sub">
            {row.nodeTitle ?? 'Unplaced'} · v{row.versionNumber}
          </div>
        </div>
      ),
    },
    {
      key: 'flags',
      header: 'Flags',
      render: (row) => (
        <div className="s7-badge-row">
          {row.flags.length === 0 ? <Badge tone="success">Healthy</Badge> : null}
          {row.flags.map((f) => (
            <span key={f} title={FLAG_COPY[f].hint}>
              <Badge tone={FLAG_COPY[f].tone}>{FLAG_COPY[f].label}</Badge>
            </span>
          ))}
        </div>
      ),
    },
    {
      key: 'facility',
      header: 'Facility',
      numeric: true,
      sort: (row) => row.facility ?? -1,
      render: (row) =>
        // Null is not zero. Below the reporting floor there is no honest number to
        // show, and printing one would be exactly the fabrication this page exists
        // to catch elsewhere.
        row.facility === null ? (
          <span className="s7-muted" title="Below the reporting floor — nothing can be said yet.">
            —
          </span>
        ) : (
          <span title="Proportion correct on first, controlled encounters. Higher means easier.">
            {(row.facility * 100).toFixed(0)}%
          </span>
        ),
    },
    {
      key: 'n',
      header: 'Answers',
      numeric: true,
      sort: (row) => row.nTotal,
      render: (row) => (
        <span title={`${row.nFirstEncounter} of them first encounters`}>
          {row.nTotal.toLocaleString()}
          <span className="s7-muted"> / {row.nFirstEncounter.toLocaleString()}</span>
        </span>
      ),
    },
    {
      key: 'choices',
      header: 'Where the answers went',
      render: (row) => <ChoiceBars item={row} />,
    },
    {
      key: 'time',
      header: 'Median time',
      numeric: true,
      sort: (row) => row.meanElapsedMs ?? -1,
      render: (row) =>
        row.meanElapsedMs === null ? (
          <span className="s7-muted">—</span>
        ) : (
          `${(row.meanElapsedMs / 1000).toFixed(1)}s`
        ),
    },
    {
      key: 'actions',
      header: '',
      width: '104px',
      render: (row) => (
        <div className="s7-row-actions">
          <IconButton
            label={
              row.isAnchor
                ? 'An anchor: administered broadly so scales can be linked later'
                : 'Mark as an anchor — cheap now, impossible retroactively'
            }
            busy={busyId === row.itemId}
            onClick={() => void setAnchor(row, !row.isAnchor)}
          >
            <Anchor size={15} className={row.isAnchor ? 's7-text-brand' : undefined} />
          </IconButton>
          <IconButton
            label="Withdraw this item's observations — the answers are kept"
            onClick={() => setExcluding(row)}
          >
            <Ban size={15} />
          </IconButton>
        </div>
      ),
    },
  ]
}

/**
 * The distractor breakdown, which is the genuinely useful half of this page.
 *
 * A wrong answer that half the class chooses is a misconception with a name.
 * A wrong answer nobody chooses is doing no work. Neither is visible from a
 * percentage correct, which is all the platform could see before the evidence
 * log recorded WHICH choice was made rather than merely whether it was right.
 */
function ChoiceBars({ item }: { item: ItemQuality }) {
  if (item.choices.length === 0) return <span className="s7-muted">—</span>

  const top = Math.max(...item.choices.map((c) => c.count), 1)

  return (
    <div className="s7-choice-bars">
      {item.choices.map((choice) => (
        <div key={choice.choiceId} className="s7-choice-bar" title={`${choice.text ?? ''} — ${choice.count} picks`}>
          <div
            className={choice.isCorrect ? 's7-choice-fill s7-choice-correct' : 's7-choice-fill'}
            style={{ width: `${Math.round((choice.count / top) * 100)}%` }}
          />
          <span className="s7-choice-label">
            {choice.isCorrect ? '✓ ' : ''}
            {(choice.text ?? '').slice(0, 18) || '—'}
          </span>
          <span className="s7-choice-count">{choice.count}</span>
        </div>
      ))}
    </div>
  )
}

function ExcludeModal({
  item,
  busy,
  onClose,
  onConfirm,
}: {
  item: ItemQuality | null
  busy: boolean
  onClose: () => void
  onConfirm: (reason: number, note: string) => Promise<void>
}) {
  const [reason, setReason] = useState<number>(1)
  const [note, setNote] = useState('')

  return (
    <Modal
      open={item !== null}
      onClose={onClose}
      icon={<Ban size={18} />}
      title="Withdraw this item's observations"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="danger" loading={busy} onClick={() => void onConfirm(reason, note)}>
            Withdraw
          </Button>
        </>
      }
    >
      <Note>
        <strong>The answers are not deleted.</strong> The learners did answer, and that is a fact that
        stays in the record. What changes is whether those answers are allowed to inform a
        measurement — and the reason you give is stored with them, so the audit trail can explain
        why the numbers moved.
      </Note>

      <Field label="Why">
        <Select value={reason} onChange={(e) => setReason(Number(e.target.value))}>
          {EXCLUSION_REASONS.map((r) => (
            <option key={r.value} value={r.value}>
              {r.label}
            </option>
          ))}
        </Select>
      </Field>

      <Field label="Note" hint="Stored with every withdrawn observation, so the change is explainable later.">
        <Input
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="What you found, in a sentence"
        />
      </Field>

      {item ? <div className="s7-muted">{item.stem}</div> : null}
    </Modal>
  )
}
