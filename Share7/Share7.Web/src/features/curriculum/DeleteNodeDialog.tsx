import { AlertTriangle, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Button } from '../../components/ui/primitives'
import { Modal } from '../../components/ui/Modal'
import type { Level } from './levels'
import type { CurriculumNodeChildCounts } from '../../types/api'
import type { TreeNode } from './data'

export interface PendingDelete {
  level: Level
  node: TreeNode
  /** Present once the server has refused and reported what force would remove. */
  counts?: CurriculumNodeChildCounts
}

/**
 * Two-stage delete.
 *
 * Stage one asks plainly; if the node is empty the server accepts and it is over. If it refuses
 * with a 409, the counts from that response become stage two — a confirmation that states the
 * actual blast radius rather than asking the admin to authorise an unbounded cascade up front.
 */
export function DeleteNodeDialog({
  pending,
  onClose,
  onConfirm,
}: {
  pending: PendingDelete | null
  onClose: () => void
  onConfirm: (force: boolean) => Promise<void>
}) {
  const [busy, setBusy] = useState(false)
  const counts = pending?.counts
  const cascading = !!counts

  const run = async (force: boolean) => {
    setBusy(true)
    try {
      await onConfirm(force)
    } finally {
      setBusy(false)
    }
  }

  const rows: Array<[string, number]> = counts
    ? (
        [
          ['Subjects', counts.subjects],
          ['Chapters', counts.chapters],
          ['Lessons', counts.lessons],
          ['Questions', counts.questions],
        ] as Array<[string, number]>
      ).filter(([, n]) => n > 0)
    : []

  return (
    <Modal
      open={!!pending}
      onClose={onClose}
      icon={<Trash2 size={17} />}
      title={
        pending
          ? cascading
            ? `Delete ${pending.level} and its contents?`
            : `Delete ${pending.level} "${pending.node.name}"?`
          : 'Delete'
      }
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button variant="danger" onClick={() => run(cascading)} loading={busy}>
            {busy ? 'Deleting…' : cascading ? 'Delete everything' : 'Delete'}
          </Button>
        </>
      }
    >
      {pending ? (
        <>
          {cascading ? (
            <>
              <p className="s7-sm">
                <strong>{pending.node.name}</strong> still has descendants. Deleting it removes all
                of this as well:
              </p>

              <div className="s7-count-grid">
                {rows.map(([label, value]) => (
                  <div key={label} className="s7-count">
                    <div className="s7-count-value">{value.toLocaleString()}</div>
                    <div className="s7-count-label">{label}</div>
                  </div>
                ))}
              </div>

              <div
                className="s7-row"
                style={{
                  padding: '0.6rem 0.7rem',
                  borderRadius: 'var(--s7-radius)',
                  background: 'var(--s7-danger-bg)',
                  color: '#b91c1c',
                  fontSize: '0.78rem',
                  alignItems: 'flex-start',
                }}
              >
                <AlertTriangle size={15} style={{ flex: '0 0 auto', marginTop: '0.1rem' }} />
                <span>
                  This cannot be undone. Student progress recorded against these lessons is keyed
                  to ids that will no longer exist.
                </span>
              </div>
            </>
          ) : (
            <p className="s7-sm">
              Deleting <strong>{pending.node.name}</strong> cannot be undone. If it still has
              anything under it, the server will say so before anything is removed.
            </p>
          )}
        </>
      ) : null}
    </Modal>
  )
}
