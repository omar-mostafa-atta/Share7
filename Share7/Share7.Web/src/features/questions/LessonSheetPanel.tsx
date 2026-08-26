import { useEffect, useMemo, useRef, useState } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import {
  AlertTriangle,
  BookOpen,
  Download,
  Plus,
  RefreshCw,
  Save,
  Trash2,
  Upload,
} from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, IconButton, SkeletonRows } from '../../components/ui/primitives'
import { Def, DefList, Note, Segmented } from '../../components/ui/bits'
import { Field, Input, Switch } from '../../components/ui/form'
import { Modal } from '../../components/ui/Modal'
import { listVariants } from '../../components/ui/motion'
import { blankRow, useLessonSheet } from './sheet'
import type { LessonSheetRow } from '../../types/api'
import type { TreeNode } from '../curriculum/data'

// ===========================================================================
// Lesson questions
//
// One lesson, one sheet: English and Arabic side by side, a Recovery switch,
// and one upload for all of it.
//
// The panel this replaces was four panels wearing a tab strip — a pool picker
// crossed with the console's language selector. Nothing in it could tell you
// that a question existed in English and not in Arabic, because each view only
// ever had a quarter of the lesson in front of it, and that gap is exactly what
// shipped: lessons playable in one language and blank in the other.
//
// Editing is local until Publish. Every write is a full replace of all four
// sets, so what is on screen is what the lesson will be — which is also why
// Publish is one button rather than one per pool.
// ===========================================================================

type Pool = 'main' | 'recovery'

export function LessonSheetPanel({
  lesson,
  path,
  onPublished,
}: {
  lesson: TreeNode
  path: string[]

  /** Lets the tree redraw the lesson's "has questions" badge, which a publish can flip. */
  onPublished: () => void
}) {
  const { sheet, loading, errors, reload, save, remove, upload } = useLessonSheet(lesson.id)

  const [rows, setRows] = useState<LessonSheetRow[]>([])
  const [pool, setPool] = useState<Pool>('main')
  const [dirty, setDirty] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<LessonSheetRow | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  // Server state is the source until the admin touches something. Re-syncing on every fetch would
  // discard edits in progress the moment a background reload landed.
  useEffect(() => {
    setRows(sheet?.rows ?? [])
    setDirty(false)
  }, [sheet])

  const visible = useMemo(
    () => rows.filter((r) => (pool === 'recovery' ? r.isRecovery : !r.isRecovery)),
    [rows, pool],
  )

  const mainCount = rows.filter((r) => !r.isRecovery).length
  const recoveryCount = rows.filter((r) => r.isRecovery).length

  // The server refuses a publish with no recovery row, so saying so here — before the round trip —
  // is the difference between a blocked button with a reason and a rejected save.
  const blocked = rows.length > 0 && recoveryCount === 0

  const errorsByRow = useMemo(() => {
    const map = new Map<number, string[]>()
    for (const error of errors) {
      const key = error.row ?? 0
      map.set(key, [...(map.get(key) ?? []), error.message])
    }
    return map
  }, [errors])

  const generalErrors = errorsByRow.get(0) ?? []

  function patch(row: LessonSheetRow, changes: Partial<LessonSheetRow>) {
    setDirty(true)
    setRows((current) => current.map((r) => (r === row ? { ...r, ...changes } : r)))
  }

  function add() {
    setDirty(true)
    setRows((current) => [...current, blankRow(pool === 'recovery')])
  }

  /** A row never published has no server-side counterpart, so it just leaves the array. */
  function dropLocal(row: LessonSheetRow) {
    setDirty(true)
    setRows((current) => current.filter((r) => r !== row))
  }

  async function run(action: () => Promise<boolean>) {
    setBusy(true)
    try {
      // Only on success: a rejected publish changed nothing, and refreshing the branch would redraw
      // the badge to the same value while implying something happened.
      if (await action()) onPublished()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader
        icon={<BookOpen size={16} />}
        title="Questions"
        actions={
          <span className="s7-inline">
            <Badge tone={mainCount ? 'info' : 'muted'}>{mainCount} main</Badge>
            <Badge tone={recoveryCount ? 'success' : 'danger'}>{recoveryCount} recovery</Badge>
            <IconButton label="Reload" busy={loading} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          </span>
        }
      />

      <CardBody>
        <p className="s7-hint" style={{ marginBottom: '0.75rem' }}>
          {path.join(' › ')} › <strong>{lesson.name}</strong>
        </p>

        {sheet ? (
          <DefList>
            <Def label="Main version">
              {versionLabel(sheet.mainVersionEn, sheet.mainVersionAr)}
            </Def>
            <Def label="Recovery version">
              {versionLabel(sheet.recoveryVersionEn, sheet.recoveryVersionAr)}
            </Def>
          </DefList>
        ) : null}

        {sheet?.unpairedRowNumbers.length ? (
          <Note tone="warning">
            <AlertTriangle size={14} /> {sheet.unpairedRowNumbers.length} question(s) exist in only
            one language (row {sheet.unpairedRowNumbers.join(', ')}). They were uploaded before this
            editor paired the two. Fill the blank side and publish — a publish from here always
            writes both.
          </Note>
        ) : null}

        {blocked ? (
          <Note tone="danger">
            This lesson has no recovery questions. A child who answers wrong has nothing to be
            offered, so publishing is refused until at least one row is marked Recovery.
          </Note>
        ) : null}

        {generalErrors.length ? (
          <Note tone="danger">
            {generalErrors.map((message) => (
              <div key={message}>{message}</div>
            ))}
          </Note>
        ) : null}

        <div className="s7-bar" style={{ marginTop: '0.75rem' }}>
          <Segmented
            layoutId="sheet-pool"
            value={pool}
            onChange={setPool}
            options={[
              { value: 'main', label: `Main (${mainCount})` },
              { value: 'recovery', label: `Recovery (${recoveryCount})` },
            ]}
          />

          <span className="s7-inline" style={{ marginInlineStart: 'auto' }}>
            <Button variant="ghost" onClick={add}>
              <Plus size={15} /> Add question
            </Button>

            <Button variant="ghost" onClick={() => fileInput.current?.click()}>
              <Upload size={15} /> Upload sheet
            </Button>

            <a
              className="s7-btn s7-btn-ghost"
              href={`/api/admin/lessons/${lesson.id}/sheet/template`}
            >
              <Download size={15} /> Template
            </a>

            <Button
              loading={busy}
              disabled={!dirty || blocked}
              onClick={() => void run(() => save(rows))}
            >
              <Save size={15} /> Publish
            </Button>
          </span>
        </div>

        <input
          ref={fileInput}
          type="file"
          accept=".xlsx"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            // Cleared unconditionally: picking the same corrected file twice must fire onChange
            // again, and it will not if the input still holds it.
            e.target.value = ''
            if (file) void run(() => upload(file, true))
          }}
        />

        {loading && !sheet ? (
          <SkeletonRows rows={4} />
        ) : visible.length === 0 ? (
          <EmptyState icon={<BookOpen size={20} />}>
            {pool === 'recovery'
              ? 'No recovery questions. Every lesson needs at least one.'
              : 'No questions yet. Add one, or upload a nine-column sheet.'}
          </EmptyState>
        ) : (
          <motion.div variants={listVariants} initial="hidden" animate="visible" className="s7-stack">
            {visible.map((row, index) => (
              <RowEditor
                key={row.rowNumber || `new-${index}`}
                row={row}
                errors={errorsByRow.get(row.rowNumber) ?? []}
                onPatch={(changes) => patch(row, changes)}
                onDelete={() => (row.rowNumber > 0 ? setConfirmDelete(row) : dropLocal(row))}
              />
            ))}
          </motion.div>
        )}
      </CardBody>

      <Modal
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        icon={<Trash2 size={18} />}
        title={`Delete question ${confirmDelete?.rowNumber}?`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              loading={busy}
              onClick={async () => {
                if (!confirmDelete) return
                await run(() => remove(confirmDelete.rowNumber))
                setConfirmDelete(null)
              }}
            >
              Delete everywhere
            </Button>
          </>
        }
      >
        <Note tone="danger">
          This removes the question in <strong>both languages</strong> and from{' '}
          <strong>both pools</strong>, then republishes what is left. Rows already answered by a
          student are retired rather than erased, so their history stays readable. Refused if it
          would leave the lesson with no recovery questions.
        </Note>
      </Modal>
    </Card>
  )
}

function versionLabel(en: number, ar: number) {
  if (en === 0 && ar === 0) return <span className="s7-muted">never published</span>
  if (en === ar) return <Badge tone="info">v{en}</Badge>

  // Only reachable on a lesson last written by the per-language importer. Worth showing rather
  // than averaging away: it is the shape the paired editor exists to correct.
  return (
    <span className="s7-inline">
      <Badge tone="warning">EN v{en}</Badge>
      <Badge tone="warning">AR v{ar}</Badge>
    </span>
  )
}

// ---------------------------------------------------------------------------
// One question, both languages
// ---------------------------------------------------------------------------

function RowEditor({
  row,
  errors,
  onPatch,
  onDelete,
}: {
  row: LessonSheetRow
  errors: string[]
  onPatch: (changes: Partial<LessonSheetRow>) => void
  onDelete: () => void
}) {
  const invalid = errors.length > 0

  return (
    <AnimatePresence initial={false}>
      <motion.div layout className="s7-panel" style={invalid ? { borderColor: 'var(--s7-danger)' } : undefined}>
        <div className="s7-bar">
          <Badge tone="muted">{row.rowNumber > 0 ? `Row ${row.rowNumber}` : 'New'}</Badge>

          <span className="s7-inline" style={{ marginInlineStart: 'auto' }}>
            <Switch
              checked={row.isRecovery}
              onChange={(v) => onPatch({ isRecovery: v })}
              label="Recovery"
            />
            <IconButton label="Delete question" onClick={onDelete}>
              <Trash2 size={15} />
            </IconButton>
          </span>
        </div>

        {invalid ? (
          <Note tone="danger">
            {errors.map((message) => (
              <div key={message}>{message}</div>
            ))}
          </Note>
        ) : null}

        <div className="s7-form-grid-2">
          <div className="s7-stack">
            <Field label="Question (English)">
              <Input
                value={row.questionEn}
                onChange={(e) => onPatch({ questionEn: e.target.value })}
                placeholder="What is 2 + 3?"
              />
            </Field>
            <Field label="Correct answer">
              <Input value={row.correctEn} onChange={(e) => onPatch({ correctEn: e.target.value })} />
            </Field>
            <Field label="Wrong answers">
              <div className="s7-inline">
                <Input value={row.wrongEn1} onChange={(e) => onPatch({ wrongEn1: e.target.value })} />
                <Input value={row.wrongEn2} onChange={(e) => onPatch({ wrongEn2: e.target.value })} />
              </div>
            </Field>
          </div>

          <div className="s7-stack" dir="rtl">
            <Field label="السؤال (بالعربية)">
              <Input
                value={row.questionAr}
                onChange={(e) => onPatch({ questionAr: e.target.value })}
                placeholder="ما ناتج 2 + 3؟"
              />
            </Field>
            <Field label="الإجابة الصحيحة">
              <Input value={row.correctAr} onChange={(e) => onPatch({ correctAr: e.target.value })} />
            </Field>
            <Field label="الإجابات الخاطئة">
              <div className="s7-inline">
                <Input value={row.wrongAr1} onChange={(e) => onPatch({ wrongAr1: e.target.value })} />
                <Input value={row.wrongAr2} onChange={(e) => onPatch({ wrongAr2: e.target.value })} />
              </div>
            </Field>
          </div>
        </div>
      </motion.div>
    </AnimatePresence>
  )
}
