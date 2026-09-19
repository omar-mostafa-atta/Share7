import { useState } from 'react'
import { AlertOctagon, ShieldAlert } from 'lucide-react'
import { Modal } from '../../components/ui/Modal'
import { Button } from '../../components/ui/primitives'
import { Field, Input } from '../../components/ui/form'
import type { GuidanceFlowAdminDto } from '../../types/api'

interface KillSwitchConfirmModalProps {
  flow: GuidanceFlowAdminDto | null
  onClose: () => void
  onConfirm: (flowId: string, isKillSwitched: boolean, reason: string) => Promise<void>
}

export function KillSwitchConfirmModal({ flow, onClose, onConfirm }: KillSwitchConfirmModalProps) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)

  if (!flow) return null

  const isActivating = !flow.isKillSwitched

  const handleConfirm = async () => {
    if (!reason.trim()) return
    setBusy(true)
    try {
      await onConfirm(flow.id, isActivating, reason.trim())
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={Boolean(flow)}
      onClose={onClose}
      icon={isActivating ? <AlertOctagon size={18} color="var(--s7-danger)" /> : <ShieldAlert size={18} />}
      title={isActivating ? `Emergency Kill Switch: "${flow.key}"` : `Restore Flow: "${flow.key}"`}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button
            variant={isActivating ? 'danger' : 'primary'}
            loading={busy}
            disabled={!reason.trim()}
            onClick={handleConfirm}
          >
            {isActivating ? 'Activate Kill Switch' : 'Restore Flow'}
          </Button>
        </>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        <p style={{ margin: 0, fontSize: '0.88rem', lineHeight: 1.5 }}>
          {isActivating ? (
            <span style={{ color: 'var(--s7-danger)', fontWeight: 600 }}>
              WARNING: Activating the kill switch will instantly exclude this flow from all client catalogs.
              Any active sessions attempting to trigger this flow will immediately ignore it.
            </span>
          ) : (
            <span>
              Restoring this flow will re-enable it in the client catalog for all eligible players.
            </span>
          )}
        </p>

        <Field label="Reason for this action (recorded in audit log)" hint="Explain why this flow is being paused or restored">
          <Input
            placeholder="e.g. Broken anchor on v1.4 update causing client soft-lock"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            autoFocus
          />
        </Field>
      </div>
    </Modal>
  )
}
