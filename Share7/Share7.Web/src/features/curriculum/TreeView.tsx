import { AnimatePresence, motion } from 'motion/react'
import { ChevronRight, FileText, Loader2, Plus } from 'lucide-react'
import { LEVEL_ORDER, childLevelOf, type Level } from './levels'
import type { Selected, TreeNode } from './data'

// ===========================================================================
// TreeView
//
// One recursive row renderer for all five levels. Replaces the five near-
// identical column components the cascade needed.
//
// Two separate hit targets per row, and the distinction is the whole point of a
// tree: the chevron opens without selecting, the row selects. Conflating them
// means you cannot look inside a chapter without also changing what the detail
// pane is showing.
// ===========================================================================

interface Props {
  grades: TreeNode[]
  /** Children keyed by parent id. Not named `children` — that is React's own slot. */
  nodes: Record<string, TreeNode[]>
  busy: Record<string, boolean>
  expanded: Set<string>
  selected: Selected | null
  filter: string
  onToggle: (node: TreeNode, level: Level) => void
  onSelect: (node: TreeNode, level: Level, ancestors: TreeNode[]) => void
  onAddUnder: (node: TreeNode, level: Level) => void
}

export function TreeView(props: Props) {
  const { grades, filter } = props

  const visible = filter.trim()
    ? grades.filter((g) => matches(g, 0, props.nodes, filter.trim().toLowerCase()))
    : grades

  if (!visible.length) {
    return (
      <div className="s7-tree-empty">
        {filter.trim() ? `Nothing matching “${filter}” in what is loaded.` : 'No grades.'}
      </div>
    )
  }

  return (
    <div className="s7-tree" role="tree">
      {visible.map((grade) => (
        <Row key={grade.id} node={grade} level="grade" ancestors={[]} depth={0} {...props} />
      ))}
    </div>
  )
}

/**
 * Whether a node or any of its LOADED descendants matches.
 *
 * Loaded is the caveat that matters: the API has no search endpoint, so this can
 * only look at branches someone has already opened. The panel says so rather
 * than implying the whole curriculum was searched.
 */
function matches(
  node: TreeNode,
  depth: number,
  nodes: Record<string, TreeNode[]>,
  term: string,
): boolean {
  if (node.name.toLowerCase().includes(term)) return true

  const kids = nodes[node.id]
  if (!kids) return false

  return kids.some((kid: TreeNode) => matches(kid, depth + 1, nodes, term))
}

function Row({
  node,
  level,
  ancestors,
  depth,
  ...props
}: Props & { node: TreeNode; level: Level; ancestors: TreeNode[]; depth: number }) {
  const { nodes, busy, expanded, selected, filter, onToggle, onSelect, onAddUnder } = props

  const childLevel = childLevelOf(level)
  const isOpen = expanded.has(node.id)
  const isSelected = selected?.node.id === node.id
  const rows = nodes[node.id]
  const isBusy = !!busy[node.id]

  const term = filter.trim().toLowerCase()
  const shown = term && rows ? rows.filter((k) => matches(k, depth + 1, nodes, term)) : rows

  return (
    <div className="s7-tree-branch" role="treeitem" aria-expanded={childLevel ? isOpen : undefined}>
      <div
        className={`s7-tree-row ${isSelected ? 'is-selected' : ''}`}
        // Indent by depth. A CSS variable rather than inline padding so the guide
        // line below can be positioned from the same number.
        style={{ ['--s7-depth' as string]: depth }}
      >
        {childLevel ? (
          <button
            type="button"
            className={`s7-tree-chevron ${isOpen ? 'is-open' : ''}`}
            aria-label={isOpen ? `Collapse ${node.name}` : `Expand ${node.name}`}
            onClick={(e) => {
              e.stopPropagation()
              onToggle(node, level)
            }}
          >
            {isBusy ? <Loader2 size={13} className="s7-spin" /> : <ChevronRight size={13} />}
          </button>
        ) : (
          <span className="s7-tree-leaf" aria-hidden>
            <FileText size={12} />
          </span>
        )}

        <button
          type="button"
          className="s7-tree-label"
          onClick={() => onSelect(node, level, ancestors)}
          title={node.name}
        >
          <span className="s7-tree-name">{node.name}</span>

          {/* Question state, lessons only. `hasQuestions` is per language: a lesson can be
              playable in English and not in Arabic, so this follows the language selector. */}
          {level === 'lesson' && node.hasQuestions === false ? (
            <span className="s7-badge s7-badge-warning">no Qs</span>
          ) : null}
          {level === 'lesson' && node.hasQuestions === true ? (
            <span className="s7-badge s7-badge-success">v{node.questionsVersion}</span>
          ) : null}

          {/* Child count, once known. Absent rather than 0 before a branch is opened —
              "0" would claim it is empty when nobody has looked. */}
          {childLevel && rows ? <span className="s7-tree-count">{rows.length}</span> : null}
        </button>

        {childLevel ? (
          <button
            type="button"
            className="s7-tree-add"
            title={`Add a ${childLevel} under ${node.name}`}
            aria-label={`Add a ${childLevel} under ${node.name}`}
            onClick={(e) => {
              e.stopPropagation()
              onAddUnder(node, level)
            }}
          >
            <Plus size={13} />
          </button>
        ) : null}
      </div>

      <AnimatePresence initial={false}>
        {isOpen ? (
          <motion.div
            key="kids"
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2, ease: [0.16, 1, 0.3, 1] }}
            style={{ overflow: 'hidden' }}
          >
            {isBusy && !rows ? (
              <div className="s7-tree-note" style={{ ['--s7-depth' as string]: depth + 1 }}>
                Loading…
              </div>
            ) : !shown?.length ? (
              <div className="s7-tree-note" style={{ ['--s7-depth' as string]: depth + 1 }}>
                {rows?.length ? 'No match here' : `No ${childLevel}s yet`}
              </div>
            ) : (
              shown.map((kid) => (
                <Row
                  key={kid.id}
                  node={kid}
                  level={LEVEL_ORDER[depth + 1]}
                  ancestors={[...ancestors, node]}
                  depth={depth + 1}
                  {...props}
                />
              ))
            )}
          </motion.div>
        ) : null}
      </AnimatePresence>
    </div>
  )
}
