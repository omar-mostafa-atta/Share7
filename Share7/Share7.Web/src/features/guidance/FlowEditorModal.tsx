import { useEffect, useState } from 'react'
import {
  FileCode,
  History,
  Layers,
  ListOrdered,
  Plus,
  Send,
  Sliders,
  Trash2,
  TrendingDown,
} from 'lucide-react'
import { Modal } from '../../components/ui/Modal'
import { Badge, Button, IconButton, Subhead } from '../../components/ui/primitives'
import { Field, Input, Select, Switch } from '../../components/ui/form'
import { Note, Segmented } from '../../components/ui/bits'
import { formatDateTime, formatRelative } from '../../lib/time'
import { useGuidanceAuditLogs, useGuidanceFlow } from './data'
import { FlowFunnelTab } from './FlowFunnelTab'
import type { GuidanceFlowAdminDto } from '../../types/api'

interface FlowEditorModalProps {
  flowId: string | null
  onClose: () => void
  onToggleKillSwitch: (flow: GuidanceFlowAdminDto) => void
}

type Tab = 'steps' | 'config' | 'analytics' | 'publish' | 'audit'

interface StepViewModel {
  stepId: string
  anchor: string
  kind: string
  locKey: string
  mood: string
  holdSeconds: number
  optional: boolean
  maxSeconds: number
}

function parseStepsJson(jsonStr: string): StepViewModel[] {
  try {
    const raw = JSON.parse(jsonStr)
    if (!Array.isArray(raw)) return []
    return raw.map((item, idx) => ({
      stepId: item.stepId || `step_${idx + 1}`,
      anchor: item.beat?.anchor || '',
      kind: item.beat?.kind || 'MascotBeat',
      locKey: item.beat?.lines?.[0]?.key || '',
      mood: item.beat?.lines?.[0]?.mood || 'happy',
      holdSeconds: item.beat?.lines?.[0]?.holdSeconds || 0,
      optional: Boolean(item.optional),
      maxSeconds: item.maxSeconds || 45,
    }))
  } catch {
    return []
  }
}

function serializeSteps(steps: StepViewModel[]): string {
  const payload = steps.map((s) => ({
    stepId: s.stepId,
    beat: {
      kind: s.kind,
      lines: [
        {
          key: s.locKey,
          mood: s.mood,
          holdSeconds: s.holdSeconds,
        },
      ],
      anchor: s.anchor,
      slot: 'BesideAnchor',
      pointer: s.anchor ? 'Tap' : 'None',
    },
    optional: s.optional,
    maxSeconds: s.maxSeconds,
  }))
  return JSON.stringify(payload, null, 2)
}

export function FlowEditorModal({ flowId, onClose, onToggleKillSwitch }: FlowEditorModalProps) {
  const { flow, updateDraft, publishVersion } = useGuidanceFlow(flowId)
  const { logs, reload: reloadLogs } = useGuidanceAuditLogs(flowId)

  const [tab, setTab] = useState<Tab>('steps')
  const [jsonMode, setJsonMode] = useState(false)

  // Config form state
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [kind, setKind] = useState('Tour')
  const [priority, setPriority] = useState(3)
  const [replayPolicy, setReplayPolicy] = useState(0)
  const [skippable, setSkippable] = useState(true)
  const [skipAfterStep, setSkipAfterStep] = useState(2)
  const [resumable, setResumable] = useState(false)
  const [targetAudienceJson, setTargetAudienceJson] = useState('')

  // Steps state
  const [steps, setSteps] = useState<StepViewModel[]>([])
  const [rawStepsJson, setRawStepsJson] = useState('[]')
  const [jsonError, setJsonError] = useState<string | null>(null)

  // Publishing state
  const [changeSummary, setChangeSummary] = useState('')
  const [savingDraft, setSavingDraft] = useState(false)
  const [publishing, setPublishing] = useState(false)

  // Sync state when flow loads
  useEffect(() => {
    if (flow) {
      setTitle(flow.title)
      setDescription(flow.description || '')
      setKind(flow.kind)
      setPriority(flow.priority)
      setReplayPolicy(flow.replayPolicy)
      setSkippable(flow.skippable)
      setSkipAfterStep(flow.skipAfterStep)
      setResumable(flow.resumable)
      setTargetAudienceJson(flow.targetAudienceJson || '')

      const effectiveJson = flow.draftVersion?.stepsJson || flow.activeVersion?.stepsJson || '[]'
      setRawStepsJson(effectiveJson)
      setSteps(parseStepsJson(effectiveJson))
      setJsonError(null)
    }
  }, [flow])

  if (!flowId || !flow) return null

  const handleSaveDraft = async () => {
    let finalJson = rawStepsJson
    if (!jsonMode) {
      finalJson = serializeSteps(steps)
      setRawStepsJson(finalJson)
    } else {
      try {
        JSON.parse(rawStepsJson)
      } catch {
        setJsonError('Invalid JSON structure.')
        return
      }
    }

    setSavingDraft(true)
    try {
      await updateDraft({
        title: title.trim(),
        description: description.trim(),
        kind,
        priority,
        replayPolicy,
        skippable,
        skipAfterStep,
        resumable,
        targetAudienceJson: targetAudienceJson.trim() || null,
        stepsJson: finalJson,
      })
      reloadLogs()
    } finally {
      setSavingDraft(false)
    }
  }

  const handlePublish = async () => {
    setPublishing(true)
    try {
      await publishVersion({
        changeSummary: changeSummary.trim() || 'Published from SuperAdmin CMS',
      })
      setChangeSummary('')
      reloadLogs()
    } finally {
      setPublishing(false)
    }
  }

  const addStep = () => {
    const nextIdx = steps.length + 1
    const newStep: StepViewModel = {
      stepId: `step_${nextIdx}`,
      anchor: '',
      kind: 'MascotBeat',
      locKey: `guidance.${flow.key}.step_${nextIdx}`,
      mood: 'happy',
      holdSeconds: 0,
      optional: false,
      maxSeconds: 45,
    }
    const updated = [...steps, newStep]
    setSteps(updated)
    setRawStepsJson(serializeSteps(updated))
  }

  const removeStep = (index: number) => {
    const updated = steps.filter((_, i) => i !== index)
    setSteps(updated)
    setRawStepsJson(serializeSteps(updated))
  }

  const updateStepField = <K extends keyof StepViewModel>(index: number, field: K, val: StepViewModel[K]) => {
    const updated = [...steps]
    updated[index] = { ...updated[index], [field]: val }
    setSteps(updated)
    setRawStepsJson(serializeSteps(updated))
  }

  return (
    <Modal
      open={Boolean(flowId)}
      onClose={onClose}
      icon={<Sliders size={18} />}
      title={
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', flexWrap: 'wrap' }}>
          <span>{flow.title}</span>
          <code className="s7-input-mono" style={{ fontSize: '0.8rem', opacity: 0.8 }}>
            {flow.key}
          </code>
          {flow.isKillSwitched ? (
            <Badge tone="danger">KILL SWITCH ACTIVE</Badge>
          ) : flow.activeVersionNumber > 0 ? (
            <Badge tone="success">v{flow.activeVersionNumber} LIVE</Badge>
          ) : (
            <Badge tone="warning">DRAFT ONLY</Badge>
          )}
        </div>
      }
      footer={
        <div style={{ display: 'flex', justifyContent: 'space-between', width: '100%', alignItems: 'center' }}>
          <div>
            <Button
              variant={flow.isKillSwitched ? 'primary' : 'danger'}
              onClick={() => onToggleKillSwitch(flow)}
            >
              {flow.isKillSwitched ? 'Restore From Kill Switch' : 'Emergency Kill Switch'}
            </Button>
          </div>
          <div style={{ display: 'flex', gap: '0.6rem' }}>
            <Button variant="ghost" onClick={onClose}>
              Close
            </Button>
            <Button loading={savingDraft} onClick={handleSaveDraft}>
              Save to Draft
            </Button>
          </div>
        </div>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        <Segmented
          layoutId="guidance-flow-editor"
          options={[
            {
              value: 'steps',
              label: (
                <span style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                  <ListOrdered size={14} /> Beats ({steps.length})
                </span>
              ),
            },
            {
              value: 'config',
              label: (
                <span style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                  <Sliders size={14} /> Settings
                </span>
              ),
            },
            {
              value: 'analytics',
              label: (
                <span style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                  <TrendingDown size={14} /> Drop-off & Funnel
                </span>
              ),
            },
            {
              value: 'publish',
              label: (
                <span style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                  <Send size={14} /> Publish & Versions
                </span>
              ),
            },
            {
              value: 'audit',
              label: (
                <span style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                  <History size={14} /> Audit Log
                </span>
              ),
            },
          ]}
          value={tab}
          onChange={(val) => setTab(val as Tab)}
        />

        {tab === 'steps' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.8rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span className="s7-hint">
                Authored beats executed sequentially by Unity's <code>GuidanceFlowRunner</code>.
              </span>
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <Button variant="ghost" onClick={() => setJsonMode(!jsonMode)}>
                  <FileCode size={14} />
                  {jsonMode ? 'Visual Editor' : 'Raw JSON'}
                </Button>
                {!jsonMode && (
                  <Button variant="primary" onClick={addStep}>
                    <Plus size={14} />
                    Add Beat
                  </Button>
                )}
              </div>
            </div>

            {jsonMode ? (
              <Field label="Raw Steps JSON" error={jsonError} hint="Must conform to GuidanceStep[] structure">
                <textarea
                  className="s7-input s7-input-mono"
                  style={{ minHeight: '320px', resize: 'vertical' }}
                  value={rawStepsJson}
                  onChange={(e) => {
                    setRawStepsJson(e.target.value)
                    setJsonError(null)
                    try {
                      setSteps(parseStepsJson(e.target.value))
                    } catch {
                      // ignore parse errors while typing raw json
                    }
                  }}
                />
              </Field>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.8rem', maxHeight: '55vh', overflowY: 'auto' }}>
                {steps.length === 0 ? (
                  <Note>This flow has no steps yet. Click "Add Beat" to create the first step.</Note>
                ) : (
                  steps.map((step, idx) => (
                    <div
                      key={idx}
                      style={{
                        background: 'var(--s7-surface-elevated, #1a1a24)',
                        border: '1px solid var(--s7-border, #333)',
                        borderRadius: '6px',
                        padding: '0.8rem',
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '0.6rem',
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                          <Badge tone="brand">Beat #{idx + 1}</Badge>
                          <code style={{ fontSize: '0.8rem' }}>{step.stepId}</code>
                        </div>
                        <IconButton label="Delete beat" onClick={() => removeStep(idx)}>
                          <Trash2 size={13} color="var(--s7-danger)" />
                        </IconButton>
                      </div>

                      <div style={{ display: 'grid', gridTemplateColumns: '1.2fr 1.5fr 1fr', gap: '0.6rem' }}>
                        <Field label="Step ID">
                          <Input
                            mono
                            value={step.stepId}
                            onChange={(e) => updateStepField(idx, 'stepId', e.target.value)}
                          />
                        </Field>

                        <Field label="Anchor Target" hint="Screen element ID (leave empty for unanchored)">
                          <Input
                            mono
                            placeholder="e.g. hud.streak, home.play_button"
                            value={step.anchor}
                            onChange={(e) => updateStepField(idx, 'anchor', e.target.value)}
                          />
                        </Field>

                        <Field label="Presentation Kind">
                          <Select
                            value={step.kind}
                            onChange={(e) => updateStepField(idx, 'kind', e.target.value)}
                          >
                            <option value="MascotBeat">MascotBeat</option>
                            <option value="Spotlight">Spotlight</option>
                            <option value="Celebration">Celebration</option>
                            <option value="Banner">Banner</option>
                          </Select>
                        </Field>
                      </div>

                      <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr', gap: '0.6rem' }}>
                        <Field label="Loc Key (Speech Bubble Text)" hint="Resolved via Unity Localization catalogue">
                          <Input
                            mono
                            value={step.locKey}
                            onChange={(e) => updateStepField(idx, 'locKey', e.target.value)}
                          />
                        </Field>

                        <Field label="Mascot Mood">
                          <Select
                            value={step.mood}
                            onChange={(e) => updateStepField(idx, 'mood', e.target.value)}
                          >
                            <option value="happy">happy</option>
                            <option value="curious">curious</option>
                            <option value="proud">proud</option>
                            <option value="excited">excited</option>
                            <option value="thinking">thinking</option>
                          </Select>
                        </Field>

                        <Field label="Max Duration (s)" hint="Hard escape hatch">
                          <Input
                            type="number"
                            min={5}
                            max={300}
                            value={step.maxSeconds}
                            onChange={(e) => updateStepField(idx, 'maxSeconds', Number(e.target.value))}
                          />
                        </Field>
                      </div>

                      <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
                        <Switch
                          label="Optional Beat (Skip if anchor missing)"
                          checked={step.optional}
                          onChange={(val) => updateStepField(idx, 'optional', val)}
                        />
                      </div>
                    </div>
                  ))
                )}
              </div>
            )}
          </div>
        )}

        {tab === 'config' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.8rem' }}>
            <Field label="Title">
              <Input value={title} onChange={(e) => setTitle(e.target.value)} />
            </Field>

            <Field label="Description">
              <Input value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0.8rem' }}>
              <Field label="Presentation Kind">
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
              <Switch label="Skippable" checked={skippable} onChange={setSkippable} />
              <Field label="Skip After Step">
                <Input
                  type="number"
                  min={0}
                  max={10}
                  value={skipAfterStep}
                  onChange={(e) => setSkipAfterStep(Number(e.target.value))}
                  disabled={!skippable}
                />
              </Field>
              <Switch label="Resumable" checked={resumable} onChange={setResumable} />
            </div>

            <Field label="Audience Targeting JSON" hint="Optional targeting predicates (grades, levels, segments)">
              <textarea
                className="s7-input s7-input-mono"
                style={{ minHeight: '70px', resize: 'vertical' }}
                value={targetAudienceJson}
                onChange={(e) => setTargetAudienceJson(e.target.value)}
                placeholder='{"minLevel": 1, "grades": ["G1", "G2"]}'
              />
            </Field>
          </div>
        )}

        {tab === 'analytics' && <FlowFunnelTab flow={flow} />}

        {tab === 'publish' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div
              style={{
                background: 'var(--s7-surface-elevated, #1a1a24)',
                padding: '1rem',
                borderRadius: '6px',
                border: '1px solid var(--s7-border, #333)',
                display: 'flex',
                flexDirection: 'column',
                gap: '0.8rem',
              }}
            >
              <Subhead icon={<Send size={15} />}>Publish Current Draft to Production</Subhead>
              <p style={{ margin: 0, fontSize: '0.85rem' }}>
                Publishing freezes the current draft into an immutable published version. All connected
                Unity clients will receive this version when their catalog syncs.
              </p>

              <Field label="Change Summary" hint="Describe what changed in this version for audit tracking">
                <Input
                  placeholder="e.g. Added step 3 to explain the new daily streak bonus"
                  value={changeSummary}
                  onChange={(e) => setChangeSummary(e.target.value)}
                />
              </Field>

              <div>
                <Button
                  loading={publishing}
                  disabled={!flow.draftVersion}
                  onClick={handlePublish}
                >
                  <Send size={14} />
                  {flow.draftVersion
                    ? `Publish Version ${flow.draftVersion.versionNumber}`
                    : 'No Pending Draft'}
                </Button>
              </div>
            </div>

            <div>
              <Subhead icon={<Layers size={15} />}>Version History</Subhead>
              <div className="s7-dt-wrap" style={{ maxHeight: '20rem', marginTop: '0.5rem' }}>
                <table className="s7-dt">
                  <thead>
                    <tr>
                      <th>Version</th>
                      <th>Status</th>
                      <th>Change Summary</th>
                      <th>Created</th>
                      <th>Published</th>
                    </tr>
                  </thead>
                  <tbody>
                    {flow.versions?.map((v) => (
                      <tr key={v.id}>
                        <td>
                          <strong>v{v.versionNumber}</strong>
                        </td>
                        <td>
                          <Badge
                            tone={
                              v.status === 'Published'
                                ? 'success'
                                : v.status === 'Draft'
                                  ? 'warning'
                                  : 'muted'
                            }
                          >
                            {v.status}
                          </Badge>
                        </td>
                        <td>{v.changeSummary || '—'}</td>
                        <td>{formatDateTime(v.createdAtUtc)}</td>
                        <td>{v.publishedAtUtc ? formatDateTime(v.publishedAtUtc) : '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}

        {tab === 'audit' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
            <Subhead icon={<History size={15} />}>Audit Trail</Subhead>
            {!logs.length ? (
              <Note>No audit logs recorded for this flow yet.</Note>
            ) : (
              <div className="s7-dt-wrap" style={{ maxHeight: '26rem' }}>
                <table className="s7-dt">
                  <thead>
                    <tr>
                      <th>When</th>
                      <th>Action</th>
                      <th>User</th>
                      <th>Details</th>
                    </tr>
                  </thead>
                  <tbody>
                    {logs.map((log) => (
                      <tr key={log.id}>
                        <td style={{ fontSize: '0.78rem' }}>{formatRelative(log.timestampUtc)}</td>
                        <td>
                          <Badge tone="info">{log.action}</Badge>
                        </td>
                        <td>{log.userEmail || 'System'}</td>
                        <td style={{ fontSize: '0.78rem' }}>
                          <code className="s7-input-mono">{log.detailsJson || '—'}</code>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
