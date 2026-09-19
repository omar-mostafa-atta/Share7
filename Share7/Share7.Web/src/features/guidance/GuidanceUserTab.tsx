import { useState } from 'react'
import { AlertTriangle, Compass, RefreshCw, RotateCcw } from 'lucide-react'
import { Badge, Button, IconButton, SkeletonRows, Subhead } from '../../components/ui/primitives'
import { Note } from '../../components/ui/bits'
import { Stat, StatRow } from '../../components/ui/Stat'
import { Field, Input } from '../../components/ui/form'
import { Modal } from '../../components/ui/Modal'
import { useUserGuidanceState } from './data'

interface GuidanceUserTabProps {
  userId: string
}

export function GuidanceUserTab({ userId }: GuidanceUserTabProps) {
  const { state, loading, resetting, reload, resetGuidance } = useUserGuidanceState(userId)
  const [resetModalOpen, setResetModalOpen] = useState(false)
  const [resetReason, setResetReason] = useState('')

  if (loading) {
    return <SkeletonRows rows={6} />
  }

  const snapshot = state?.snapshot
  const generation = state?.generation ?? 1
  const flows = snapshot?.flows ?? []
  const shown = snapshot?.shown ?? []

  const handleReset = async () => {
    if (!resetReason.trim()) return
    await resetGuidance(resetReason.trim())
    setResetReason('')
    setResetModalOpen(false)
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h3 className="s7-subhead" style={{ margin: 0 }}>Guidance & Onboarding State</h3>
          <p className="s7-hint" style={{ margin: '0.2rem 0 0' }}>
            Authoritative server-synchronized guidance journal state for this student.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          <IconButton label="Refresh" onClick={reload}>
            <RefreshCw size={13} />
          </IconButton>
          <Button variant="danger" onClick={() => setResetModalOpen(true)}>
            <RotateCcw size={13} />
            Reset Guidance
          </Button>
        </div>
      </div>

      <StatRow>
        <Stat label="Generation" value={generation} tone="brand" />
        <Stat
          label="Onboarding Complete"
          value={flows.some((f) => f.id.includes('onboarding') && f.finished) ? 'Completed' : 'Pending'}
          tone={flows.some((f) => f.id.includes('onboarding') && f.finished) ? 'success' : 'warning'}
        />
        <Stat label="Flows Recorded" value={flows.length} tone="info" />
        <Stat label="Tips Shown" value={shown.length} tone="cool" />
      </StatRow>

      <Note>
        <strong>Generation {generation}:</strong> Replays and progress wipes are controlled monotonically.
        When you reset guidance, generation is incremented to {generation + 1}, forcing all student devices to rebase
        and replay onboarding on next sync.
      </Note>

      <div>
        <Subhead icon={<Compass size={14} />}>Authored Flows Progress ({flows.length})</Subhead>
        {!flows.length ? (
          <p className="s7-hint">No flows recorded for this user yet.</p>
        ) : (
          <div className="s7-dt-wrap" style={{ maxHeight: '18rem', marginTop: '0.4rem' }}>
            <table className="s7-dt">
              <thead>
                <tr>
                  <th>Flow ID</th>
                  <th>Furthest Step</th>
                  <th>Completed Version</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {flows.map((f) => (
                  <tr key={f.id}>
                    <td>
                      <code className="s7-input-mono">{f.id}</code>
                    </td>
                    <td>Step {f.furthestStep + 1}</td>
                    <td>{f.completedVersion > 0 ? `v${f.completedVersion}` : '—'}</td>
                    <td>
                      <Badge tone={f.finished ? 'success' : 'warning'}>
                        {f.finished ? 'Finished' : 'In Progress'}
                      </Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {shown.length > 0 && (
        <div>
          <Subhead icon={<Compass size={14} />}>Reactive Tips & Nudges Shown ({shown.length})</Subhead>
          <div className="s7-dt-wrap" style={{ maxHeight: '14rem', marginTop: '0.4rem' }}>
            <table className="s7-dt">
              <thead>
                <tr>
                  <th>Tip ID</th>
                  <th>Total Shows</th>
                  <th>Today Shows</th>
                  <th>Last Session</th>
                </tr>
              </thead>
              <tbody>
                {shown.map((s) => (
                  <tr key={s.id}>
                    <td>
                      <code className="s7-input-mono">{s.id}</code>
                    </td>
                    <td>{s.count}</td>
                    <td>{s.dayCount}</td>
                    <td>Session #{s.sessionOrdinal}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <Modal
        open={resetModalOpen}
        onClose={() => setResetModalOpen(false)}
        icon={<AlertTriangle size={18} color="var(--s7-danger)" />}
        title="Reset User Guidance Progress"
        footer={
          <>
            <Button variant="ghost" onClick={() => setResetModalOpen(false)} disabled={resetting}>
              Cancel
            </Button>
            <Button
              variant="danger"
              loading={resetting}
              disabled={!resetReason.trim()}
              onClick={handleReset}
            >
              Reset to Generation {generation + 1}
            </Button>
          </>
        }
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
          <p style={{ margin: 0, fontSize: '0.88rem', lineHeight: 1.5 }}>
            Resetting guidance will increment this student's generation counter from{' '}
            <strong>{generation}</strong> to <strong>{generation + 1}</strong> and wipe their completed flows.
            When their client next connects, it will observe the newer generation, purge local journal cache,
            and replay onboarding from the beginning.
          </p>

          <Field
            label="Reason for reset"
            hint="Recorded in the guidance audit log for accountability"
          >
            <Input
              placeholder="e.g. Student requested replay of home onboarding tutorial"
              value={resetReason}
              onChange={(e) => setResetReason(e.target.value)}
              autoFocus
            />
          </Field>
        </div>
      </Modal>
    </div>
  )
}
