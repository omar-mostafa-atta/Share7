import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import {
  AlertTriangle,
  BookMarked,
  CheckCircle2,
  FileText,
  GraduationCap,
  Layers,
  Lock,
  RefreshCw,
  ScrollText,
  ShieldQuestion,
  Sparkles,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState } from '../components/ui/primitives'
import { IconButton } from '../components/ui/primitives'
import { Note, PageTitle } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable, type Column } from '../components/ui/DataTable'
import { useResource } from '../lib/resource'
import { listVariants } from '../components/ui/motion'
import {
  blockingReason,
  useAssessmentActions,
  warningFor,
  type Blueprint,
  type CalibrationStatus,
  type ExamSummary,
} from '../features/assessment/data'

// ===========================================================================
// Examinations & blueprints
//
// The surface behind the one question this platform exists to answer honestly:
// "if this student walked into the exam today, what would we expect?"
//
// The answer it ships is COVERAGE, not a predicted mark, and this page is
// where the difference is visible. A blueprint says what a paper examines; a
// learner's coverage is arithmetic over that blueprint and their admitted
// evidence; and a predicted score needs observed pairs of (what we said, what
// happened), which cannot be reasoned into existence and has to be collected.
// The calibration table below says how far away that is, with a number, rather
// than saying nothing.
//
// Two gates are enforced here rather than suggested:
//
//   - A blueprint naming a lesson PLACEHOLDER cannot be published. A lesson
//     wearing a target's clothes cannot say what a paper covers, and coverage
//     built on one would be a completion percentage in disguise.
//
//   - A published blueprint is never edited. Every coverage figure ever
//     computed named the version it was computed against.
//
// Docs/EducationalArchitecture.md §4, §6.
// ===========================================================================

export function Exams() {
  const blueprints = useResource<Blueprint[]>('/api/admin/assessment/blueprints', [])
  const exams = useResource<ExamSummary[]>('/api/admin/assessment/exams', [])
  const calibration = useResource<CalibrationStatus[]>('/api/admin/assessment/calibration', [])

  const reload = () => {
    void blueprints.reload()
    void exams.reload()
    void calibration.reload()
  }

  const { busyId, publishBlueprint, publishExam } = useAssessmentActions(reload)
  const [open, setOpen] = useState<string | null>(null)

  const published = exams.data.filter((e) => e.isPublished).length
  const blocked = blueprints.data.filter((b) => blockingReason(b) !== null).length
  const usable = calibration.data.reduce((total, c) => total + c.usableOutcomes, 0)
  const calibrated = calibration.data.filter((c) => c.isCalibrated).length

  const examColumns: Column<ExamSummary>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'Examination',
        sort: (e) => e.name,
        render: (e) => (
          <div>
            <div className="s7-strong">{e.name}</div>
            <div className="s7-muted s7-small">
              {e.versionLabel}
              {e.subjectLabel ? ` · ${e.subjectLabel}` : ''}
            </div>
          </div>
        ),
      },
      {
        key: 'authority',
        header: 'Set by',
        sort: (e) => e.authorityName,
        // Shown on every row on purpose. A Share7 benchmark and a ministry's paper carry very
        // different weight with a parent reading the report, and the difference has to be
        // visible rather than inferable.
        render: (e) => <Badge tone={e.authorityName === 'Share7' ? 'info' : 'success'}>{e.authorityName}</Badge>,
      },
      {
        key: 'targets',
        header: 'Claims',
        numeric: true,
        sort: (e) => e.targetCount,
        render: (e) => (
          <span>
            {e.targetCount}
            {e.placeholderTargetCount > 0 ? (
              <span className="s7-danger s7-small"> · {e.placeholderTargetCount} placeholder</span>
            ) : null}
          </span>
        ),
      },
      {
        key: 'outcomes',
        header: 'Results reported',
        numeric: true,
        sort: (e) => e.reportedOutcomes,
        render: (e) => (
          <span>
            {e.calibrationUsableOutcomes}
            {e.reportedOutcomes !== e.calibrationUsableOutcomes ? (
              <span className="s7-muted s7-small"> of {e.reportedOutcomes}</span>
            ) : null}
          </span>
        ),
      },
      {
        key: 'state',
        header: '',
        render: (e) =>
          e.isPublished ? (
            <Badge tone="success">
              <CheckCircle2 size={12} /> Published
            </Badge>
          ) : (
            <Button
              variant="ghost"
              loading={busyId === e.examSpecificationVersionId}
              onClick={() => void publishExam(e)}
            >
              Publish
            </Button>
          ),
      },
    ],
    [busyId, publishExam],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<GraduationCap size={22} />}
        title="Examinations & blueprints"
        subtitle="What each paper examines, and how far the platform is from being able to say anything about an outcome."
        actions={
          <IconButton
            label="Reload"
            onClick={reload}
            busy={blueprints.refreshing || exams.refreshing || calibration.refreshing}
          >
            <RefreshCw size={16} />
          </IconButton>
        }
      />

      <StatRow>
        <Stat
          icon={<ScrollText size={15} />}
          label="Blueprints"
          value={blueprints.data.length}
          sub={`${blueprints.data.filter((b) => b.isPublished).length} published and therefore immutable`}
          tone="brand"
        />
        <Stat
          icon={<Lock size={15} />}
          label="Blocked from publishing"
          value={blocked}
          sub={blocked > 0 ? 'Still naming lessons rather than competencies' : 'Nothing blocked'}
          tone={blocked > 0 ? 'warning' : 'success'}
        />
        <Stat
          icon={<GraduationCap size={15} />}
          label="Examinations live"
          value={published}
          sub="Learners can ask for their coverage against these"
          tone="cool"
        />
        <Stat
          icon={<ShieldQuestion size={15} />}
          label="Results collected"
          value={usable}
          sub="Matched pairs of what we said and what happened"
          tone={usable > 0 ? 'brand' : 'warning'}
        />
        <Stat
          icon={<Sparkles size={15} />}
          label="Calibrated"
          value={calibrated}
          sub={
            calibrated === 0
              ? 'No examination can have an outcome predicted yet, and saying so is the honest answer'
              : 'Predicted outcome bands available'
          }
          tone={calibrated > 0 ? 'success' : 'warning'}
        />
      </StatRow>

      <Card>
        <CardHeader icon={<GraduationCap size={16} />} title="Examinations" />
        <CardBody>
          <DataTable
            rows={exams.data}
            columns={examColumns}
            getId={(e) => e.examSpecificationVersionId}
            loading={exams.loading}
            initialSort={{ key: 'name' }}
            empty={
              <EmptyState icon={<GraduationCap size={28} />}>
                <strong>No examination is defined</strong>
                <span className="s7-empty-hint">Nothing is seeded on purpose — a blueprint is a claim about a real paper, and a plausible one shipped in a migration would carry the schema's authority without anybody having read a syllabus. Build one from a subject on the Learning targets page, or author it here.</span>
              </EmptyState>
            }
          />
        </CardBody>
      </Card>

      <Card>
        <CardHeader icon={<ScrollText size={16} />} title="Blueprints" />
        <CardBody>
          {blueprints.data.length === 0 ? (
            <EmptyState icon={<ScrollText size={28} />}>
                <strong>No blueprint yet</strong>
                <span className="s7-empty-hint">A blueprint is what makes coverage computable: it says which claims a paper examines and in what proportions.</span>
              </EmptyState>
          ) : (
            <div className="s7-blueprints">
              {blueprints.data.map((b) => (
                <BlueprintCard
                  key={b.blueprintId}
                  blueprint={b}
                  open={open === b.blueprintId}
                  busy={busyId === b.blueprintId}
                  onToggle={() => setOpen(open === b.blueprintId ? null : b.blueprintId)}
                  onPublish={() => void publishBlueprint(b)}
                />
              ))}
            </div>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader icon={<ShieldQuestion size={16} />} title="Calibration — how far from a predicted outcome" />
        <CardBody>
          <Note>
            A coverage figure and a proficiency band need no calibration and work today. A{' '}
            <strong>predicted mark</strong> needs observed pairs of what the platform said and what the learner
            actually got, which cannot be derived from anything already here — it has to be collected. Until the
            bar below is reached, no examination renders an outcome band at all, and no field in the API can
            carry one.
          </Note>

          {calibration.data.length === 0 ? (
            <EmptyState icon={<ShieldQuestion size={28} />}>
                <strong>Nothing to calibrate yet</strong>
                <span className="s7-empty-hint">Results start accruing as soon as an examination is published and learners report what they got.</span>
              </EmptyState>
          ) : (
            <div className="s7-kv">
              {calibration.data.map((c) => (
                <div className="s7-kv-row" key={c.examSpecificationVersionId}>
                  <span className="s7-kv-label">
                    {c.name}
                    <span className="s7-muted s7-small"> · {c.versionLabel}</span>
                  </span>
                  <span className="s7-kv-value">
                    <span className="s7-mono">
                      {c.usableOutcomes} / {c.requiredForCalibration}
                    </span>
                    {c.isCalibrated ? (
                      <Badge tone="success">Calibrated</Badge>
                    ) : (
                      <Badge tone="muted">{c.requiredForCalibration - c.usableOutcomes} more needed</Badge>
                    )}
                    {c.withdrawnConsent > 0 ? (
                      <Badge tone="warning">{c.withdrawnConsent} consent withdrawn</Badge>
                    ) : null}
                  </span>
                </div>
              ))}
            </div>
          )}
        </CardBody>
      </Card>
    </motion.div>
  )
}

function BlueprintCard({
  blueprint,
  open,
  busy,
  onToggle,
  onPublish,
}: {
  blueprint: Blueprint
  open: boolean
  busy: boolean
  onToggle: () => void
  onPublish: () => void
}) {
  const blocker = blockingReason(blueprint)
  const warning = warningFor(blueprint)
  const lines = blueprint.areas.reduce((total, a) => total + a.lines.length, 0)

  return (
    <div className="s7-blueprint">
      <button type="button" className="s7-blueprint-head" onClick={onToggle}>
        <span className="s7-blueprint-title">
          <FileText size={15} />
          <span>
            <span className="s7-strong">{blueprint.name}</span>
            <span className="s7-muted s7-small">
              {' '}
              v{blueprint.versionNumber} · {blueprint.areas.length} areas · {lines} claims
            </span>
          </span>
        </span>
        <span className="s7-blueprint-badges">
          {blueprint.isPublished ? (
            <Badge tone="success">
              <Lock size={12} /> Published
            </Badge>
          ) : blocker ? (
            <Badge tone="danger">
              <AlertTriangle size={12} /> Blocked
            </Badge>
          ) : (
            <Badge tone="info">Draft</Badge>
          )}
        </span>
      </button>

      {open ? (
        <div className="s7-blueprint-body">
          {/* The provenance line, first, because it changes how every number under it should be
              read. A blueprint nobody authored is an assumption with a schema. */}
          {blueprint.sourceNote ? (
            <p className="s7-source-note">
              <BookMarked size={13} /> {blueprint.sourceNote}
            </p>
          ) : (
            <Note tone="warning">
              This blueprint records no source. Whoever reads a coverage figure built from it has no way to know
              where its weights came from.
            </Note>
          )}

          {blocker ? <Note tone="danger">Cannot be published: {blocker}</Note> : null}
          {warning ? <Note tone="warning">{warning}</Note> : null}

          <div className="s7-areas">
            {blueprint.areas.map((area) => (
              <div className="s7-area" key={area.areaId}>
                <div className="s7-area-head">
                  <Layers size={13} />
                  <span className="s7-strong">{area.label}</span>
                  <span className="s7-muted s7-small">{(area.normalisedWeight * 100).toFixed(0)}% of the paper</span>
                </div>
                <ul className="s7-lines">
                  {area.lines.map((line) => (
                    <li key={line.lineId} className={line.isPlaceholder ? 's7-line s7-line-placeholder' : 's7-line'}>
                      <span>{line.statement || <em className="s7-muted">untranslated</em>}</span>
                      <span className="s7-line-meta">
                        {line.isPlaceholder ? <Badge tone="danger">lesson placeholder</Badge> : null}
                        {line.availableItems === 0 ? (
                          <Badge tone="warning">no questions</Badge>
                        ) : (
                          <span className="s7-muted s7-small">{line.availableItems} questions</span>
                        )}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>

          <div className="s7-thresholds">
            <span>
              Coverage floor <strong>{(blueprint.minCoverageRatio * 100).toFixed(0)}%</strong>
            </span>
            <span>
              Per-area floor <strong>{(blueprint.minAreaCoverageRatio * 100).toFixed(0)}%</strong>
            </span>
            <span>
              Exam-like answers <strong>{blueprint.minObservationsOverall}</strong> overall,{' '}
              <strong>{blueprint.minObservationsPerArea}</strong> per area
            </span>
            <span>
              Evidence no older than <strong>{blueprint.maxMedianEvidenceAgeDays} days</strong>
            </span>
          </div>

          {!blueprint.isPublished && !blocker ? (
            <Button loading={busy} onClick={onPublish}>
              <Lock size={15} /> Publish — this makes it immutable
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}
