import { useState } from 'react'
import { PlusCircle } from 'lucide-react'
import { Modal } from '../../components/ui/Modal'
import { Button } from '../../components/ui/primitives'
import { Field, Input, Select, Switch } from '../../components/ui/form'
import type { CreateGuidanceFlowRequest } from '../../types/api'

interface CreateFlowModalProps {
  open: boolean
  onClose: () => void
  onCreate: (request: CreateGuidanceFlowRequest) => Promise<unknown>
}

const DEFAULT_STEPS = JSON.stringify(
  [
    {
      stepId: 'step_1',
      beat: {
        kind: 'MascotBeat',
        lines: [
          {
            key: 'guidance.example.line1',
            mood: 'happy',
            holdSeconds: 0,
          },
        ],
        anchor: '',
        slot: 'BesideAnchor',
        pointer: 'Tap',
      },
      optional: false,
      maxSeconds: 45,
    },
  ],
  null,
  2,
)

export function CreateFlowModal({ open, onClose, onCreate }: CreateFlowModalProps) {
  const [key, setKey] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [kind, setKind] = useState('Tour')
  const [priority, setPriority] = useState(3)
  const [replayPolicy, setReplayPolicy] = useState(0)
  const [skippable, setSkippable] = useState(true)
  const [skipAfterStep, setSkipAfterStep] = useState(2)
  const [resumable, setResumable] = useState(false)
  const [stepsJson, setStepsJson] = useState(DEFAULT_STEPS)
  const [jsonError, setJsonError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const handleSubmit = async () => {
    if (!key.trim() || !title.trim()) return

    try {
      JSON.parse(stepsJson)
      setJsonError(null)
    } catch {
      setJsonError('Invalid JSON structure for steps.')
      return
    }

    setBusy(true)
    try {
      await onCreate({
        key: key.trim(),
        title: title.trim(),
        description: description.trim(),
        kind,
        priority,
        replayPolicy,
        skippable,
        skipAfterStep,
        resumable,
        initialStepsJson: stepsJson,
      })
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      icon={<PlusCircle size={18} />}
      title="Create Guidance Flow"
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button
            loading={busy}
            disabled={!key.trim() || !title.trim()}
            onClick={handleSubmit}
          >
            Create Flow
          </Button>
        </>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.8rem' }}>
          <Field label="Flow Key" hint="Matches client guidance id (e.g. onboarding.home)">
            <Input
              mono
              placeholder="onboarding.home"
              value={key}
              onChange={(e) => setKey(e.target.value)}
              autoFocus
            />
          </Field>

          <Field label="Title" hint="Human-readable admin title">
            <Input
              placeholder="Home Screen Onboarding"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </Field>
        </div>

        <Field label="Description" hint="Target audience or purpose">
          <Input
            placeholder="Guides child through first home screen navigation."
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </Field>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0.8rem' }}>
          <Field label="Kind">
            <Select value={kind} onChange={(e) => setKind(e.target.value)}>
              <option value="Tour">Tour</option>
              <option value="Nudge">Nudge</option>
              <option value="Spotlight">Spotlight</option>
              <option value="Banner">Banner</option>
              <option value="Celebration">Celebration</option>
            </Select>
          </Field>

          <Field label="Priority Tier">
            <Select value={priority} onChange={(e) => setPriority(Number(e.target.value))}>
              <option value={0}>0 - Ambient (Background)</option>
              <option value={1}>1 - Queued</option>
              <option value={2}>2 - Immediate</option>
              <option value={3}>3 - Blocking (Onboarding)</option>
            </Select>
          </Field>

          <Field label="Replay Policy">
            <Select value={replayPolicy} onChange={(e) => setReplayPolicy(Number(e.target.value))}>
              <option value={0}>0 - Never</option>
              <option value={1}>1 - Always</option>
              <option value={2}>2 - OnVersionChange</option>
            </Select>
          </Field>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0.8rem', alignItems: 'center' }}>
          <Switch
            label="Skippable"
            checked={skippable}
            onChange={setSkippable}
          />

          <Field label="Skip After Step" hint="0-indexed beat count">
            <Input
              type="number"
              min={0}
              max={10}
              value={skipAfterStep}
              onChange={(e) => setSkipAfterStep(Number(e.target.value))}
              disabled={!skippable}
            />
          </Field>

          <Switch
            label="Resumable"
            checked={resumable}
            onChange={setResumable}
          />
        </div>

        <Field label="Initial Steps (JSON)" error={jsonError} hint="Valid GuidanceStep[] array">
          <textarea
            className="s7-input s7-input-mono"
            style={{ minHeight: '120px', resize: 'vertical' }}
            value={stepsJson}
            onChange={(e) => {
              setStepsJson(e.target.value)
              setJsonError(null)
            }}
          />
        </Field>
      </div>
    </Modal>
  )
}
