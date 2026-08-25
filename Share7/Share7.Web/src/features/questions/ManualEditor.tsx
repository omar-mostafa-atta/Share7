import { AnimatePresence, motion } from 'motion/react'
import { Check, DownloadCloud, Keyboard, Plus, Trash2, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Button, Subhead } from '../../components/ui/primitives'
import { Field, Input, Select } from '../../components/ui/form'
import { rowVariants } from '../../components/ui/motion'
import { toast } from '../../store/toast'
import type {
  Language,
  LessonQuestionsDto,
  ManualQuestionInput,
  ManualQuestionMode,
} from '../../types/api'

interface Row extends ManualQuestionInput {
  /** Local identity, so React keys survive removals and reorderings. */
  key: number
}

let nextKey = 1
const blankRow = (values?: Partial<ManualQuestionInput>): Row => ({
  key: nextKey++,
  text: values?.text ?? '',
  correctChoice: values?.correctChoice ?? '',
  wrongChoice1: values?.wrongChoice1 ?? '',
  wrongChoice2: values?.wrongChoice2 ?? '',
})

export function ManualEditor({
  poolNoun,
  lessonName,
  languages,
  langId,
  onLangChange,
  busy,
  badRows,
  onPublish,
  onLoadPublished,
}: {
  poolNoun: string
  lessonName: string
  languages: Language[]
  langId: string
  onLangChange: (langId: string) => void
  busy: boolean
  /** 1-based positions the server rejected, so the offending rows can be marked. */
  badRows: number[]
  onPublish: (mode: ManualQuestionMode, questions: ManualQuestionInput[]) => Promise<unknown>
  onLoadPublished: () => Promise<LessonQuestionsDto | null>
}) {
  // Opens with one empty question, so the first thing an admin sees is somewhere to type rather
  // than a button they have to find first.
  const [rows, setRows] = useState<Row[]>([blankRow()])
  const [mode, setMode] = useState<ManualQuestionMode>('APPEND')
  const [confirmReplace, setConfirmReplace] = useState(false)

  // Arabic content is typed right-to-left, and the language selector is what says so. Existing
  // rows have to follow a language change too, not just rows added afterwards — which is why this
  // is derived on render rather than stamped onto each input when it is created.
  const rtl = useMemo(
    () => languages.find((l) => l.id === langId)?.code === 'ar',
    [languages, langId],
  )

  // A pending REPLACE confirmation must not survive the admin switching to APPEND, or the next
  // press would publish under a mode they did not confirm.
  useEffect(() => {
    setConfirmReplace(false)
  }, [mode, langId])

  const update = (key: number, field: keyof ManualQuestionInput, value: string) =>
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, [field]: value } : r)))

  const remove = (key: number) =>
    setRows((prev) => (prev.length === 1 ? [blankRow()] : prev.filter((r) => r.key !== key)))

  /**
   * Rows left entirely blank are dropped rather than submitted, mirroring the sheet parser's
   * tolerance of a spacer row — an admin who adds one row too many should not have the whole
   * publish refused for it.
   */
  const collect = (): ManualQuestionInput[] =>
    rows
      .map((r) => ({
        text: r.text.trim(),
        correctChoice: r.correctChoice.trim(),
        wrongChoice1: r.wrongChoice1.trim(),
        wrongChoice2: r.wrongChoice2.trim(),
      }))
      .filter((q) => q.text || q.correctChoice || q.wrongChoice1 || q.wrongChoice2)

  const loadPublished = async () => {
    const published = await onLoadPublished()
    if (!published) return

    const questions = published.questions ?? []
    if (!questions.length) {
      toast.info('Nothing published yet', `This lesson has no ${poolNoun} in that language.`)
      return
    }

    // The answers arrive in stored order with correctness carried by id, not by position — so the
    // correct one is found rather than assumed to be first.
    setRows(
      questions.map((q) => {
        const answers = q.answers ?? []
        const correct = answers.find((a) => a.id === q.correctAnswerId)
        const wrong = answers.filter((a) => a.id !== q.correctAnswerId)
        return blankRow({
          text: q.text,
          correctChoice: correct?.text ?? '',
          wrongChoice1: wrong[0]?.text ?? '',
          wrongChoice2: wrong[1]?.text ?? '',
        })
      }),
    )

    // Loading in order to edit means replacing — appending the set to itself would double it.
    setMode('REPLACE')
    toast.info(
      `Loaded v${published.version}`,
      `${questions.length} question(s) in the editor. Mode switched to Replace.`,
    )
  }

  const publish = async () => {
    const questions = collect()
    if (!questions.length) {
      toast.error('Nothing to publish', 'Add at least one question.')
      return
    }

    // REPLACE retires everything currently published, so it asks once. The confirmation lives in
    // the button itself rather than a dialog, because a second dialog on top of this panel is
    // more friction than the risk warrants.
    if (mode === 'REPLACE' && !confirmReplace) {
      setConfirmReplace(true)
      return
    }

    const result = await onPublish(mode, questions)
    setConfirmReplace(false)

    if (result) {
      setRows([blankRow()])
      setMode('APPEND')
    }
  }

  return (
    <div>
      <Subhead icon={<Keyboard size={15} />}>Type {poolNoun} by hand</Subhead>

      <div className="s7-row" style={{ gap: '0.75rem', flexWrap: 'wrap', marginBottom: '0.85rem' }}>
        <div style={{ minWidth: 170 }}>
          <Field label="Language">
            <Select value={langId} onChange={(e) => onLangChange(e.target.value)}>
              {languages.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name} ({l.code})
                </option>
              ))}
            </Select>
          </Field>
        </div>

        <div style={{ minWidth: 190 }}>
          <Field
            label="Mode"
            hint={
              mode === 'APPEND'
                ? 'Keeps what is published and adds after it.'
                : 'Publishes these instead — the current set is retired.'
            }
          >
            <Select value={mode} onChange={(e) => setMode(e.target.value as ManualQuestionMode)}>
              <option value="APPEND">Append</option>
              <option value="REPLACE">Replace</option>
            </Select>
          </Field>
        </div>

        <div className="s7-row s7-spacer" style={{ gap: '0.4rem' }}>
          <Button variant="ghost" onClick={loadPublished} disabled={busy}>
            <DownloadCloud size={14} />
            Load current set
          </Button>
        </div>
      </div>

      <AnimatePresence initial={false}>
        {rows.map((row, index) => {
          const bad = badRows.includes(index + 1)
          return (
            <motion.div
              key={row.key}
              variants={rowVariants}
              initial="hidden"
              animate="visible"
              exit="exit"
              layout
              className={`s7-qrow ${bad ? 'is-bad' : ''}`}
            >
              <div className="s7-qrow-head">
                <span className="s7-qrow-index">{index + 1}</span>
                <span className="s7-label" style={{ margin: 0 }}>
                  Question
                </span>
                <button
                  type="button"
                  className="s7-qrow-remove"
                  onClick={() => remove(row.key)}
                  aria-label={`Remove question ${index + 1}`}
                  title="Remove this question"
                >
                  <X size={14} />
                </button>
              </div>

              <Input
                value={row.text}
                dir={rtl ? 'rtl' : undefined}
                placeholder="Question text"
                onChange={(e) => update(row.key, 'text', e.target.value)}
                style={{ marginBottom: '0.4rem' }}
              />

              <div className="s7-qrow-choices">
                {/* Marked and first, because position is the contract: the server reads this one
                    as correct, exactly as it reads column 2 of the sheet. */}
                <div className="s7-choice">
                  <span className="s7-choice-mark" title="This one is the correct answer">
                    <Check size={13} />
                  </span>
                  <Input
                    value={row.correctChoice}
                    dir={rtl ? 'rtl' : undefined}
                    placeholder="Correct choice"
                    onChange={(e) => update(row.key, 'correctChoice', e.target.value)}
                  />
                </div>

                <Input
                  value={row.wrongChoice1}
                  dir={rtl ? 'rtl' : undefined}
                  placeholder="Wrong choice 1"
                  onChange={(e) => update(row.key, 'wrongChoice1', e.target.value)}
                />

                <Input
                  value={row.wrongChoice2}
                  dir={rtl ? 'rtl' : undefined}
                  placeholder="Wrong choice 2"
                  onChange={(e) => update(row.key, 'wrongChoice2', e.target.value)}
                />
              </div>
            </motion.div>
          )
        })}
      </AnimatePresence>

      <div className="s7-row" style={{ gap: '0.4rem', marginTop: '0.6rem', flexWrap: 'wrap' }}>
        <Button variant="ghost" onClick={() => setRows((prev) => [...prev, blankRow()])}>
          <Plus size={14} />
          Add question
        </Button>

        <Button
          variant="ghost"
          onClick={() => {
            setRows([blankRow()])
            setMode('APPEND')
          }}
        >
          <Trash2 size={14} />
          Clear
        </Button>

        <div className="s7-spacer" />

        <Button
          variant={confirmReplace ? 'danger' : 'primary'}
          onClick={publish}
          loading={busy}
        >
          {/* "typed" distinguishes this from the sheet upload above, which publishes to the same
              pool and previously carried an identical label. */}
          {busy
            ? 'Publishing…'
            : confirmReplace
              ? `Replace the whole set for "${lessonName}"?`
              : `Publish typed ${poolNoun}`}
        </Button>
      </div>

      <div className="s7-hint">
        Both modes produce a <strong>new version</strong> — a published set is immutable, so
        appending republishes the existing questions alongside these. Client caches key on that
        version, so every publish costs them a re-download of this lesson.
      </div>
    </div>
  )
}
