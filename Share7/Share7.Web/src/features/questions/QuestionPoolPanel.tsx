import { motion } from 'motion/react'
import { AlertTriangle, BookOpen, LifeBuoy } from 'lucide-react'
import { useState } from 'react'
import { Card, CardBody, CardHeader, Divider } from '../../components/ui/primitives'
import { springSoft } from '../../components/ui/motion'
import { useLanguages } from '../../store/languages'
import { ManualEditor } from './ManualEditor'
import { SheetUpload } from './SheetUpload'
import { POOLS, useQuestionPool } from './data'
import type { QuestionPool } from '../../types/api'
import type { TreeNode } from '../curriculum/data'

const TABS: Array<{ pool: QuestionPool; icon: typeof BookOpen }> = [
  { pool: 'questions', icon: BookOpen },
  { pool: 'recovery', icon: LifeBuoy },
]

/**
 * One pool's two publishing paths. They accept identical content and differ only in which endpoint
 * they publish to, so the editor is parameterised rather than written twice.
 */
function PoolBody({
  pool,
  lesson,
  onPublished,
}: {
  pool: QuestionPool
  lesson: TreeNode
  onPublished: () => void
}) {
  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  // The pool's own language, seeded from the tree's but independent of it: publishing the Arabic
  // set while browsing the tree in English is a normal thing to want, and coupling them would
  // force a token round-trip to do it.
  const [langId, setLangId] = useState(selectedLangId)
  const { config, busy, errors, clearErrors, uploadSheet, publishManual, loadPublished } =
    useQuestionPool(pool)

  const effectiveLangId = langId || selectedLangId
  const badRows = errors.map((e) => e.row).filter((r): r is number => r != null)

  return (
    <div>
      {/*
        Entrance animation only, and no AnimatePresence — deliberately.
        A validation panel that outlives the failure it describes is worse than one that vanishes
        without a transition: it sits above a *successful* publish claiming nothing was published.
        Animating the exit means the node lingers until the frameloop finishes it, so this is
        driven purely by whether there are errors.
      */}
      {errors.length ? (
        <motion.div
          key="errors"
          initial={{ opacity: 0, height: 0 }}
          animate={{ opacity: 1, height: 'auto' }}
          transition={{ duration: 0.22 }}
          style={{ overflow: 'hidden' }}
        >
          <div className="s7-errors">
              <div className="s7-errors-title">
                <AlertTriangle size={15} />
                Nothing was published — fix these first:
              </div>
              <ul>
                {errors.map((e, i) => (
                  <li key={i}>
                    {e.row != null ? <strong>#{e.row}: </strong> : null}
                    {e.message}
                  </li>
                ))}
              </ul>
          </div>
        </motion.div>
      ) : null}

      <SheetUpload
        languages={languages}
        langId={effectiveLangId}
        onLangChange={(id) => {
          clearErrors()
          setLangId(id)
        }}
        busy={busy}
        onUpload={async (file, hasHeaderRow) => {
          const result = await uploadSheet(lesson.id, effectiveLangId, file, hasHeaderRow)
          if (result) onPublished()
          return result
        }}
      />

      <Divider />

      <ManualEditor
        poolNoun={config.noun}
        lessonName={lesson.name}
        languages={languages}
        langId={effectiveLangId}
        onLangChange={(id) => {
          clearErrors()
          setLangId(id)
        }}
        busy={busy}
        badRows={badRows}
        onPublish={async (mode, questions) => {
          const result = await publishManual(lesson.id, effectiveLangId, mode, questions)
          if (result) onPublished()
          return result
        }}
        onLoadPublished={() => loadPublished(lesson.id, effectiveLangId)}
      />
    </div>
  )
}

export function QuestionPoolPanel({
  lesson,
  path,
  onPublished,
}: {
  lesson: TreeNode
  /** Grade → chapter trail, so the lesson being published to is unambiguous. */
  path: string[]
  onPublished: () => void
}) {
  const [active, setActive] = useState<QuestionPool>('questions')

  return (
    <Card>
      <CardHeader
        icon={<BookOpen size={16} />}
        title="Question pools"
        actions={
          lesson.hasQuestions ? (
            <span className="s7-version">questions v{lesson.questionsVersion}</span>
          ) : (
            <span className="s7-badge s7-badge-muted">no questions yet</span>
          )
        }
      />

      <CardBody>
        <div className="s7-lesson-bar">
          <span>
            Publishing to <strong>{lesson.name}</strong>
          </span>
          {path.length ? <span className="s7-lesson-path">{path.join(' › ')}</span> : null}
        </div>

        <div className="s7-tabs" role="tablist">
          {TABS.map(({ pool, icon: Icon }) => (
            <button
              key={pool}
              type="button"
              role="tab"
              aria-selected={active === pool}
              className={`s7-tab ${active === pool ? 'is-active' : ''}`}
              onClick={() => setActive(pool)}
            >
              {active === pool ? (
                <motion.span
                  layoutId="s7-pool-tab"
                  className="s7-tab-marker"
                  transition={springSoft}
                />
              ) : null}
              <span>
                <Icon size={14} />
                {POOLS[pool].label}
              </span>
            </button>
          ))}
        </div>

        <div style={{ marginTop: '1rem' }}>
          {/*
            Both pools stay mounted, keyed by pool, so switching tabs does not discard a
            half-typed question set. The recovery pool versions independently of the main one —
            a lesson can sit at questions v1 and recovery v4 — so they are genuinely separate
            drafts, not two views of one.
          */}
          <div style={{ display: active === 'questions' ? 'block' : 'none' }}>
            <PoolBody pool="questions" lesson={lesson} onPublished={onPublished} />
          </div>
          <div style={{ display: active === 'recovery' ? 'block' : 'none' }}>
            <PoolBody pool="recovery" lesson={lesson} onPublished={onPublished} />
          </div>
        </div>
      </CardBody>
    </Card>
  )
}
