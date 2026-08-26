import { motion } from 'motion/react'
import {
  ChevronsDownUp,
  FileText,
  Languages,
  Network,
  Plus,
  RefreshCw,
  Search,
  Trash2,
} from 'lucide-react'
import { useMemo, useState } from 'react'
import { useEffect } from 'react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton, SkeletonRows } from '../components/ui/primitives'
import { CopyId, Def, DefList, Note, PageTitle } from '../components/ui/bits'
import { Select } from '../components/ui/form'
import { listVariants } from '../components/ui/motion'
import { AddNodeModal } from '../features/curriculum/AddNodeModal'
import { DeleteNodeDialog, type PendingDelete } from '../features/curriculum/DeleteNodeDialog'
import { TreeView } from '../features/curriculum/TreeView'
import { CurriculumDashboard } from '../features/curriculum/CurriculumDashboard'
import { QuestionBrowser } from '../features/curriculum/QuestionBrowser'
import { Drawer } from '../components/ui/Drawer'
import { Segmented } from '../components/ui/bits'
import { useCurriculumTree, type TreeNode } from '../features/curriculum/data'
import {
  LEVEL_LABELS,
  LEVEL_NOUN,
  childLevelOf,
  isEditable,
  type Level,
} from '../features/curriculum/levels'
import { LessonSheetPanel } from '../features/questions/LessonSheetPanel'
import { useLanguages } from '../store/languages'
import type { CreateCurriculumNodeRequest } from '../types/api'

// ===========================================================================
// Curriculum
//
// A tree on the left, the selected node on the right.
//
// This replaces a five-column Miller cascade. The cascade drilled well and
// compared badly: it held one selection per level, so opening a second term to
// see what was in it discarded everything under the first. Each column was also
// a fifth of the width, which truncated most names, and the only way to answer
// "where am I" was to read five headers.
//
// The tree keeps every branch you open, indents to show depth, and hands the
// whole remaining width to the detail pane — where the breadcrumb answers
// "where am I" in one line and the question editor gets room to work.
// ===========================================================================

export function Curriculum() {
  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)
  const loadLanguages = useLanguages((s) => s.load)
  const applyLanguage = useLanguages((s) => s.apply)

  // Three answers to three different questions: what exists (tree), whether it works (dashboard),
  // and what is actually in it (questions). Tabs rather than one page, because the tree's split
  // layout wants the full width and the other two want a single column.
  const [view, setView] = useState<'tree' | 'dashboard' | 'questions'>('tree')

  /** The lesson whose question editor is open over the top, from wherever it was opened. */
  const [editingLesson, setEditingLesson] = useState<{ id: string; path: string[] } | null>(null)

  const [switching, setSwitching] = useState(false)
  const [filter, setFilter] = useState('')
  const [addUnder, setAddUnder] = useState<{ node: TreeNode; level: Level } | null>(null)
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

  const selected = tree.selected

  // The node directly above the selection, which delete and refresh both need in
  // order to refetch the branch the change happened in.
  const parent = selected?.ancestors.length
    ? selected.ancestors[selected.ancestors.length - 1]
    : null

  const parentLevel: Level | null = selected
    ? (['grade', 'term', 'subject', 'chapter', 'lesson'] as Level[])[selected.ancestors.length - 1] ??
      null
    : null

  const confirmDelete = async (force: boolean) => {
    if (!pendingDelete) return

    try {
      const result = await tree.deleteNode(
        pendingDelete.level,
        pendingDelete.node,
        parent,
        parentLevel,
        force,
      )

      if (result.deleted) setPendingDelete(null)
      // Refused because it still has children — reopen the same dialog carrying the counts, so
      // the second press is an informed one.
      else setPendingDelete({ ...pendingDelete, counts: result.counts })
    } catch {
      setPendingDelete(null)
    }
  }

  const submitAdd = async (_level: Level, request: CreateCurriculumNodeRequest) => {
    if (!addUnder) return
    await tree.addChild(addUnder.node, addUnder.level, request)
  }

  const openBranches = Object.keys(tree.children).length

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Network size={22} />}
        title="Curriculum"
        subtitle="Grades down to lessons. Open as many branches as you like — each one loads on demand and stays open."
        actions={
          <div className="s7-inline">
            <Segmented
              layoutId="curriculum-view"
              value={view}
              onChange={setView}
              options={[
                { value: 'tree', label: 'Tree' },
                { value: 'dashboard', label: 'Dashboard' },
                { value: 'questions', label: 'Questions' },
              ]}
            />
            {switching ? (
              <span className="s7-inline s7-hint" style={{ margin: 0 }}>
                <Languages size={13} className="s7-spin" />
                Applying…
              </span>
            ) : null}
            <Select
              value={selectedLangId}
              onChange={(e) => void changeLanguage(e.target.value)}
              disabled={switching || !languages.length}
              aria-label="Content language"
              style={{ maxWidth: '11rem' }}
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
          </div>
        }
      />

      {view === 'dashboard' ? (
        <CurriculumDashboard
          onOpenLesson={(id, path) => setEditingLesson({ id, path })}
        />
      ) : null}

      {view === 'questions' ? (
        <QuestionBrowser
          // Scope follows the tree's selection, so switching tabs keeps your place instead of
          // resetting to the whole curriculum every time.
          scopeLevel={selected?.level ?? null}
          scopeId={selected?.node.id ?? null}
          scopeName={selected?.node.name ?? 'the curriculum'}
          onOpenLesson={(id, path) => setEditingLesson({ id, path })}
        />
      ) : null}

      <div className="s7-curriculum" hidden={view !== 'tree'}>
        {/* ── tree ── */}
        <motion.div className="s7-tree-pane" variants={listVariants}>
          <div className="s7-tree-head">
            <div className="s7-search" style={{ flex: '1 1 auto' }}>
              <Search size={14} />
              <input
                type="search"
                value={filter}
                placeholder="Filter open branches…"
                onChange={(e) => setFilter(e.target.value)}
              />
            </div>

            <IconButton
              label="Collapse everything"
              onClick={() => tree.collapseAll()}
            >
              <ChevronsDownUp size={15} />
            </IconButton>

            <IconButton
              label="Reload grades"
              busy={tree.loadingGrades}
              onClick={() => void tree.reloadGrades()}
            >
              <RefreshCw size={15} />
            </IconButton>
          </div>

          <div className="s7-tree-scroll">
            {tree.loadingGrades && !tree.grades.length ? (
              <SkeletonRows rows={6} />
            ) : (
              <TreeView
                grades={tree.grades}
                nodes={tree.children}
                busy={tree.busy}
                expanded={tree.expanded}
                selected={tree.selected}
                filter={filter}
                onToggle={tree.toggle}
                onSelect={tree.select}
                onAddUnder={(node, level) => setAddUnder({ node, level })}
              />
            )}
          </div>

          <div className="s7-tree-foot">
            {tree.grades.length} grade{tree.grades.length === 1 ? '' : 's'}
            {openBranches ? ` · ${openBranches} branch${openBranches === 1 ? '' : 'es'} loaded` : ''}
            {filter.trim() ? ' · filtering loaded branches only' : ''}
          </div>
        </motion.div>

        {/* ── detail ── */}
        <motion.div variants={listVariants}>
          {!selected ? (
            <div className="s7-detail-empty">
              <div>
                <Network size={30} className="s7-muted" />
                <h2 style={{ marginTop: '0.6rem', fontSize: '1rem' }}>Pick something from the tree</h2>
                <p>
                  Grades are read-only — the API has no create-grade endpoint, so the ladder is
                  seeded server-side. Everything below one can be added, renamed by language, and
                  deleted. Choose a lesson to publish its questions.
                </p>
              </div>
            </div>
          ) : (
            <NodeDetail
              key={selected.node.id}
              level={selected.level}
              node={selected.node}
              ancestors={selected.ancestors}
              onCrumb={(node, level, ancestors) => tree.select(node, level, ancestors)}
              onAddChild={() => setAddUnder({ node: selected.node, level: selected.level })}
              onDelete={() => setPendingDelete({ level: selected.level, node: selected.node })}
              onPublished={() => void tree.refreshBranch(parent, parentLevel)}
            />
          )}
        </motion.div>
      </div>

      {/* The question editor, opened from a finding or a search hit. It carries its own lesson id,
          so it does not need the tree to have that branch loaded — which is the whole reason the
          dashboard and the browser can link straight to it. */}
      <Drawer
        open={!!editingLesson}
        onClose={() => setEditingLesson(null)}
        title="Lesson questions"
        subtitle={editingLesson?.path.join(' › ')}
      >
        {editingLesson ? (
          <LessonSheetPanel
            key={editingLesson.id}
            lesson={{ id: editingLesson.id, name: editingLesson.path.at(-1) ?? '', langId: '', order: 0 }}
            path={editingLesson.path.slice(0, -1)}
            onPublished={() => void tree.reloadGrades()}
          />
        ) : null}
      </Drawer>

      <AddNodeModal
        level={addUnder ? childLevelOf(addUnder.level) : null}
        parentName={addUnder?.node.name ?? ''}
        onClose={() => setAddUnder(null)}
        onSubmit={submitAdd}
      />

      <DeleteNodeDialog
        pending={pendingDelete}
        onClose={() => setPendingDelete(null)}
        onConfirm={confirmDelete}
      />
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Detail pane
// ---------------------------------------------------------------------------

const LEVEL_SEQUENCE: Level[] = ['grade', 'term', 'subject', 'chapter', 'lesson']

function NodeDetail({
  level,
  node,
  ancestors,
  onCrumb,
  onAddChild,
  onDelete,
  onPublished,
}: {
  level: Level
  node: TreeNode
  ancestors: TreeNode[]
  onCrumb: (node: TreeNode, level: Level, ancestors: TreeNode[]) => void
  onAddChild: () => void
  onDelete: () => void
  onPublished: () => void
}) {
  const childLevel = childLevelOf(level)

  // Grade → parent trail, as plain strings, for the question panel.
  const path = useMemo(() => ancestors.map((a) => a.name), [ancestors])

  return (
    <div className="s7-stack">
      <Card>
        <CardBody>
          <nav className="s7-crumbs" aria-label="Breadcrumb">
            {ancestors.map((crumb, i) => (
              <span key={crumb.id} className="s7-inline" style={{ gap: '0.15rem' }}>
                <button
                  type="button"
                  className="s7-crumb"
                  onClick={() => onCrumb(crumb, LEVEL_SEQUENCE[i], ancestors.slice(0, i))}
                >
                  {crumb.name}
                </button>
                <span className="s7-crumb-sep">/</span>
              </span>
            ))}
            <span className="s7-crumb is-current">{node.name}</span>
          </nav>

          <div className="s7-detail-head">
            <div style={{ minWidth: 0, flex: '1 1 auto' }}>
              <span className="s7-level-chip">{LEVEL_NOUN[level]}</span>
              <h2 style={{ marginTop: '0.35rem' }}>{node.name}</h2>
            </div>

            <div className="s7-inline">
              {childLevel ? (
                <Button variant="ghost" onClick={onAddChild}>
                  <Plus size={15} /> Add {LEVEL_NOUN[childLevel]}
                </Button>
              ) : null}

              {isEditable(level) ? (
                <Button variant="danger" onClick={onDelete}>
                  <Trash2 size={15} />
                </Button>
              ) : null}
            </div>
          </div>

          <DefList>
            <Def label="Level">{LEVEL_LABELS[level].replace(/s$/, '')}</Def>
            <Def label="Order">{node.order}</Def>
            <Def label="Id">
              <CopyId id={node.id} label={`${level}Id`} />
            </Def>
            {level === 'lesson' ? (
              <Def label="Questions">
                {node.hasQuestions ? (
                  <Badge tone="success">published · v{node.questionsVersion}</Badge>
                ) : (
                  <Badge tone="warning">none in this language</Badge>
                )}
              </Def>
            ) : null}
          </DefList>

          {!isEditable(level) ? (
            <Note>
              Grades are read-only. <code className="s7-key">AdminCurriculumController</code>{' '}
              exposes add and delete for terms, subjects, chapters and lessons — there is no
              create-grade or delete-grade endpoint anywhere in the API, and the ladder is seeded
              from <code className="s7-key">GradeIds.cs</code>.
            </Note>
          ) : null}
        </CardBody>
      </Card>

      {/* The question pools sit beside the tree rather than behind a tab. In the old console,
          switching to the Questions tab hid the tree — so the lesson being published to was off
          screen while you typed, and the only reminder of which one it was was a read-only text
          box. Here the tree stays put on the left. */}
      {level === 'lesson' ? (
        <LessonSheetPanel lesson={node} path={path} onPublished={onPublished} />
      ) : childLevel ? (
        <Card>
          <CardHeader icon={<FileText size={16} />} title={`What goes in a ${LEVEL_NOUN[level]}`} />
          <CardBody>
            <p className="s7-hint" style={{ margin: 0 }}>
              This {LEVEL_NOUN[level]} holds {LEVEL_NOUN[childLevel]}s. Expand it in the tree to see
              them, or use <strong>Add {LEVEL_NOUN[childLevel]}</strong> above. Questions are
              published on a lesson, which is the bottom of the ladder.
            </p>
          </CardBody>
        </Card>
      ) : null}
    </div>
  )
}
