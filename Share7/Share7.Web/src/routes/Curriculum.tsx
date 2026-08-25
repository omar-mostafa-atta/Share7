import { motion } from 'motion/react'
import { Languages, Network, RefreshCw } from 'lucide-react'
import { useEffect, useState } from 'react'
import { PageHeader } from '../components/layout/AppShell'
import { IconButton } from '../components/ui/primitives'
import { Field, Select } from '../components/ui/form'
import { listVariants } from '../components/ui/motion'
import { AddNodeModal } from '../features/curriculum/AddNodeModal'
import { DeleteNodeDialog, type PendingDelete } from '../features/curriculum/DeleteNodeDialog'
import { NodeColumn } from '../features/curriculum/NodeColumn'
import { useCurriculumTree, type TreeNode } from '../features/curriculum/data'
import { LEVELS, LEVEL_ORDER, isEditable, type Level } from '../features/curriculum/levels'
import { QuestionPoolPanel } from '../features/questions/QuestionPoolPanel'
import { useLanguages } from '../store/languages'

export function Curriculum() {
  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)
  const loadLanguages = useLanguages((s) => s.load)
  const applyLanguage = useLanguages((s) => s.apply)

  const [switching, setSwitching] = useState(false)
  const [addLevel, setAddLevel] = useState<Level | null>(null)
  const [pendingDelete, setPendingDelete] = useState<PendingDelete | null>(null)

  const tree = useCurriculumTree(selectedLangId)

  useEffect(() => {
    void loadLanguages().catch(() => undefined)
  }, [loadLanguages])

  const changeLanguage = async (langId: string) => {
    setSwitching(true)
    try {
      await applyLanguage(langId)
    } catch {
      // Surfaced globally. The selection is left alone so the tree keeps showing the language it
      // actually has a token for, rather than a label that does not match the content.
    } finally {
      setSwitching(false)
    }
  }

  const startDelete = (level: Level, node: TreeNode) => setPendingDelete({ level, node })

  const confirmDelete = async (force: boolean) => {
    if (!pendingDelete) return

    try {
      const result = await tree.deleteNode(pendingDelete.level, pendingDelete.node, force)

      if (result.deleted) {
        setPendingDelete(null)
      } else {
        // Refused because it still has children — reopen the same dialog carrying the counts, so
        // the second press is an informed one.
        setPendingDelete({ ...pendingDelete, counts: result.counts })
      }
    } catch {
      setPendingDelete(null)
    }
  }

  const parentNameFor = (level: Level | null) =>
    level && isEditable(level) ? (tree.selection[LEVELS[level].parent]?.name ?? '') : ''

  return (
    <>
      <PageHeader icon={<Network size={22} />} title="Curriculum">
        Walk the tree from grade to lesson. Every node carries a name per language.
      </PageHeader>

      <div className="s7-toolbar">
        <div style={{ minWidth: 210 }}>
          <Field
            label="Content language"
            hint="Switching reissues your token — the whole tree relabels."
          >
            <Select
              value={selectedLangId}
              onChange={(e) => void changeLanguage(e.target.value)}
              disabled={switching || !languages.length}
            >
              {languages.length ? (
                languages.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.name} ({l.code})
                  </option>
                ))
              ) : (
                <option value="">Loading…</option>
              )}
            </Select>
          </Field>
        </div>

        <div className="s7-row s7-spacer" style={{ gap: '0.4rem' }}>
          {switching ? (
            <span className="s7-row s7-hint" style={{ margin: 0, gap: '0.3rem' }}>
              <Languages size={13} className="s7-spin" />
              Applying language…
            </span>
          ) : null}
          <IconButton
            label="Reload grades"
            busy={!!tree.loading.grade}
            onClick={() => void tree.reloadLevel('grade')}
          >
            <RefreshCw size={14} />
          </IconButton>
        </div>
      </div>

      <motion.div className="s7-cascade" variants={listVariants} initial="hidden" animate="visible">
        {LEVEL_ORDER.map((level) => {
          const parentLevel = isEditable(level) ? LEVELS[level].parent : null
          return (
            <NodeColumn
              key={level}
              level={level}
              parentLevel={parentLevel}
              items={tree.items[level]}
              selected={tree.selection[level]}
              parentSelected={parentLevel ? !!tree.selection[parentLevel] : true}
              loading={!!tree.loading[level]}
              onSelect={(node) => tree.select(level, node)}
              onAdd={() => setAddLevel(level)}
              onDelete={(node) => startDelete(level, node)}
            />
          )
        })}
      </motion.div>

      {/*
        The question pools live under the cascade rather than behind a tab, which is the one place
        this deliberately departs from the old console. There, switching to the Questions tab hid
        the tree — so the lesson you were publishing to was off screen while you typed, and the
        only reminder of which one it was was a read-only text box. Here the selection stays
        visible above the editor.
      */}
      {/*
        Deliberately not wrapped in AnimatePresence with mode="wait". That would hold the incoming
        panel unmounted until the outgoing one's exit animation finished — so anything that stalls
        the frameloop leaves the editor missing entirely rather than merely un-animated. A panel
        that appears without its transition is a far better failure than one that never appears.
      */}
      {tree.selection.lesson ? (
        <motion.div
          key={tree.selection.lesson.id}
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.3 }}
          style={{ marginTop: '1rem' }}
        >
          <QuestionPoolPanel
              lesson={tree.selection.lesson}
              path={[
                tree.selection.grade?.name,
                tree.selection.term?.name,
                tree.selection.subject?.name,
                tree.selection.chapter?.name,
              ].filter((n): n is string => !!n)}
              // A publish bumps the lesson's version and may flip hasQuestions, both of which
              // the lessons column renders — so it is refreshed rather than left stale.
          onPublished={() => void tree.reloadLevel('lesson')}
          />
        </motion.div>
      ) : (
        <div className="s7-hint" style={{ marginTop: '1rem', textAlign: 'center' }}>
          Select a lesson to upload a question sheet or type questions by hand.
        </div>
      )}

      <AddNodeModal
        level={addLevel}
        parentName={parentNameFor(addLevel)}
        onClose={() => setAddLevel(null)}
        onSubmit={tree.addNode}
      />

      <DeleteNodeDialog
        pending={pendingDelete}
        onClose={() => setPendingDelete(null)}
        onConfirm={confirmDelete}
      />
    </>
  )
}
