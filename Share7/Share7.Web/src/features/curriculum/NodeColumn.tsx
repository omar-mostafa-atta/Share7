import { AnimatePresence, motion } from 'motion/react'
import { Lock, Plus, Trash2 } from 'lucide-react'
import { listVariants, rowVariants, springSnappy, tapScale } from '../../components/ui/motion'
import { SkeletonRows } from '../../components/ui/primitives'
import { LEVEL_LABELS, isEditable, type Level } from './levels'
import type { TreeNode } from './data'

export function NodeColumn({
  level,
  parentLevel,
  items,
  selected,
  parentSelected,
  loading,
  onSelect,
  onAdd,
  onDelete,
}: {
  level: Level
  parentLevel: Level | null
  items: TreeNode[]
  selected: TreeNode | null
  parentSelected: boolean
  loading: boolean
  onSelect: (node: TreeNode) => void
  onAdd: () => void
  onDelete: (node: TreeNode) => void
}) {
  const editable = isEditable(level)

  return (
    <motion.div
      className={`s7-column ${selected ? 'is-focus' : ''}`}
      variants={rowVariants}
      layout
    >
      <header className="s7-column-head">
        <span className="s7-column-title">{LEVEL_LABELS[level]}</span>
        {!editable ? (
          <Lock
            size={11}
            className="s7-muted"
            aria-label="Read-only: the API has no create-grade endpoint"
          />
        ) : null}
        <span className="s7-spacer s7-column-count">{items.length}</span>
      </header>

      <div className="s7-column-body">
        {loading ? (
          <SkeletonRows rows={3} />
        ) : !parentSelected && parentLevel ? (
          <div className="s7-column-hint">
            Select a {parentLevel}
            <br />
            to see its {level}s
          </div>
        ) : !items.length ? (
          <div className="s7-column-hint">none</div>
        ) : (
          <motion.div variants={listVariants} initial="hidden" animate="visible">
            <AnimatePresence initial={false}>
              {items.map((node) => {
                const isSelected = selected?.id === node.id
                return (
                  <motion.div key={node.id} variants={rowVariants} exit="exit" layout>
                    <button
                      type="button"
                      className={`s7-node ${isSelected ? 'is-selected' : ''}`}
                      onClick={() => onSelect(node)}
                    >
                      {/* One marker per column, so it slides between rows here without
                          interfering with the markers in the other four columns. */}
                      {isSelected ? (
                        <motion.span
                          layoutId={`s7-node-marker-${level}`}
                          className="s7-node-marker"
                          transition={springSnappy}
                        />
                      ) : null}

                      <span className="s7-node-order">{node.order}</span>
                      <span className="s7-node-name" title={node.name}>
                        {node.name}
                      </span>

                      {/* Question state, lessons only. `hasQuestions` is per language: a lesson
                          can be playable in English and not in Arabic, so this follows the
                          language selector. */}
                      {level === 'lesson' && node.hasQuestions === false ? (
                        <span className="s7-badge s7-badge-muted">no Qs</span>
                      ) : null}
                      {level === 'lesson' && node.hasQuestions === true ? (
                        <span className="s7-badge s7-badge-info">v{node.questionsVersion}</span>
                      ) : null}
                    </button>
                  </motion.div>
                )
              })}
            </AnimatePresence>
          </motion.div>
        )}
      </div>

      {editable ? (
        <footer className="s7-column-foot">
          <div style={{ display: 'flex', gap: '0.3rem' }}>
            <motion.button
              type="button"
              className="s7-add-btn"
              onClick={onAdd}
              disabled={!parentSelected}
              title={parentSelected ? `Add a ${level}` : `Select a ${parentLevel} first`}
              whileHover={parentSelected ? { y: -1 } : undefined}
              whileTap={parentSelected ? tapScale : undefined}
            >
              <Plus size={13} />
              Add {level}
            </motion.button>

            <motion.button
              type="button"
              className="s7-btn s7-btn-danger s7-btn-icon"
              onClick={() => selected && onDelete(selected)}
              disabled={!selected}
              title={selected ? `Delete ${selected.name}` : `Select a ${level} to delete`}
              aria-label={`Delete selected ${level}`}
              whileHover={selected ? { y: -1 } : undefined}
              whileTap={selected ? tapScale : undefined}
            >
              <Trash2 size={13} />
            </motion.button>
          </div>
        </footer>
      ) : null}
    </motion.div>
  )
}
