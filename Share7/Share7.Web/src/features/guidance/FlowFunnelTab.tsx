import { useState } from 'react'
import {
  AlertTriangle,
  Clock,
  Filter,
  Layers,
  RefreshCw,
  TrendingDown,
  Users,
  CheckCircle2,
  XCircle,
  SkipForward,
} from 'lucide-react'
import { Badge, IconButton } from '../../components/ui/primitives'
import { Select } from '../../components/ui/form'
import { Segmented } from '../../components/ui/bits'
import { Stat, StatRow } from '../../components/ui/Stat'
import { formatRelative } from '../../lib/time'
import { useGuidanceFunnel, useMissingAnchors } from './data'
import type { GuidanceFlowAdminDto } from '../../types/api'

interface FlowFunnelTabProps {
  flow: GuidanceFlowAdminDto
}

function percent(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return '0.0%'
  return `${(n * 100).toFixed(1)}%`
}

function formatMs(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

export function FlowFunnelTab({ flow }: FlowFunnelTabProps) {
  const [days, setDays] = useState<number>(30)
  const [selectedVersion, setSelectedVersion] = useState<number | null>(
    flow.activeVersionNumber > 0 ? flow.activeVersionNumber : null,
  )

  const { funnel, loading, reload } = useGuidanceFunnel(flow.id, selectedVersion, days)
  const { missingAnchors } = useMissingAnchors(flow.key, days)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.2rem' }}>
      {/* Controls */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: '0.8rem',
          padding: '0.8rem 1rem',
          background: 'var(--s7-surface-secondary, rgba(255,255,255,0.03))',
          borderRadius: '8px',
          border: '1px solid var(--s7-border, rgba(255,255,255,0.08))',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.8rem', flexWrap: 'wrap' }}>
          <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--s7-text-muted)' }}>
            Time Window:
          </span>
          <Segmented
            layoutId="guidance-funnel-days"
            value={String(days)}
            onChange={(val) => setDays(Number(val))}
            options={[
              { value: '7', label: 'Last 7 Days' },
              { value: '30', label: 'Last 30 Days' },
              { value: '90', label: 'Last 90 Days' },
            ]}
          />

          <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', marginLeft: '0.5rem' }}>
            <span style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var(--s7-text-muted)' }}>
              Version:
            </span>
            <Select
              value={selectedVersion === null ? 'all' : String(selectedVersion)}
              onChange={(e) => {
                const val = e.target.value
                setSelectedVersion(val === 'all' ? null : Number(val))
              }}
              style={{ width: '160px', padding: '0.3rem 0.5rem', fontSize: '0.85rem' }}
            >
              <option value="all">All Versions</option>
              {flow.versions.map((v) => (
                <option key={v.id} value={v.versionNumber}>
                  v{v.versionNumber} ({v.status})
                </option>
              ))}
            </Select>
          </div>
        </div>

        <IconButton
          label="Refresh analytics"
          busy={loading}
          onClick={() => void reload()}
        >
          <RefreshCw size={14} />
        </IconButton>
      </div>

      {/* Headline Stats */}
      {funnel ? (
        <StatRow>
          <Stat
            icon={<Users size={14} />}
            label="Total Started"
            value={funnel.totalStarted}
            tone="brand"
          />
          <Stat
            icon={<CheckCircle2 size={14} />}
            label="Completed"
            value={`${funnel.totalCompleted} (${percent(funnel.completionRate)})`}
            tone="success"
          />
          <Stat
            icon={<XCircle size={14} />}
            label="Abandoned"
            value={funnel.totalAbandoned}
            tone={funnel.totalAbandoned > 0 ? 'warning' : 'cool'}
          />
          <Stat
            icon={<SkipForward size={14} />}
            label="Skipped"
            value={funnel.totalSkipped}
            tone="cool"
          />
          <Stat
            icon={<Clock size={14} />}
            label="Avg Dwell Time"
            value={`${funnel.averageDurationSeconds}s`}
            tone="info"
          />
        </StatRow>
      ) : null}

      {/* Step Drop-off Funnel Chart */}
      {funnel && funnel.steps.length > 0 ? (
        <div
          style={{
            background: 'var(--s7-surface-secondary, rgba(255,255,255,0.02))',
            borderRadius: '8px',
            border: '1px solid var(--s7-border, rgba(255,255,255,0.08))',
            padding: '1.2rem',
          }}
        >
          <div style={{ marginBottom: '1rem' }}>
            <h4 style={{ margin: 0, fontSize: '0.95rem', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <Filter size={16} /> Step Drop-off & Conversion Funnel
            </h4>
            <p style={{ margin: '0.25rem 0 0 0', fontSize: '0.8rem', color: 'var(--s7-text-muted)' }}>
              Unique players who advanced through each step in order, highlighting drop-off cliffs and dwell time.
            </p>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            {funnel.steps.map((step) => (
              <div
                key={step.stepIndex}
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  gap: '0.4rem',
                  padding: '0.75rem 1rem',
                  background: 'var(--s7-surface-card, rgba(255,255,255,0.04))',
                  borderRadius: '6px',
                  border: '1px solid var(--s7-border, rgba(255,255,255,0.06))',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                    <span
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        width: '24px',
                        height: '24px',
                        borderRadius: '50%',
                        background: 'var(--s7-brand-subtle, rgba(99,102,241,0.15))',
                        color: 'var(--s7-brand, #818cf8)',
                        fontSize: '0.75rem',
                        fontWeight: 700,
                      }}
                    >
                      {step.stepIndex + 1}
                    </span>
                    <strong style={{ fontSize: '0.88rem' }}>{step.stepId}</strong>
                    {step.anchor ? (
                      <Badge tone="muted">
                        #{step.anchor}
                      </Badge>
                    ) : null}
                    {step.locKey ? (
                      <span style={{ fontSize: '0.75rem', color: 'var(--s7-text-muted)', fontFamily: 'monospace' }}>
                        {step.locKey}
                      </span>
                    ) : null}
                  </div>

                  <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', fontSize: '0.82rem' }}>
                    <span>
                      <strong style={{ color: 'var(--s7-text-high)' }}>{step.reachedCount.toLocaleString()}</strong> players
                    </span>
                    <span style={{ color: 'var(--s7-text-muted)' }}>
                      <Clock size={12} style={{ display: 'inline', verticalAlign: '-1px', marginRight: '3px' }} />
                      {formatMs(step.averageDurationMs)}
                    </span>
                  </div>
                </div>

                {/* Bar */}
                <div
                  style={{
                    height: '10px',
                    width: '100%',
                    background: 'rgba(255,255,255,0.08)',
                    borderRadius: '5px',
                    overflow: 'hidden',
                  }}
                >
                  <div
                    style={{
                      height: '100%',
                      width: `${Math.max(1, step.conversionFromStart * 100)}%`,
                      background:
                        step.stepIndex === 0
                          ? 'var(--s7-brand, #6366f1)'
                          : step.dropOffRate > 0.3
                            ? 'var(--s7-tone-danger, #ef4444)'
                            : 'var(--s7-brand, #6366f1)',
                      borderRadius: '5px',
                      transition: 'width 0.3s ease',
                    }}
                  />
                </div>

                {/* Conversion & Drop-off stats */}
                <div
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    fontSize: '0.75rem',
                    color: 'var(--s7-text-muted)',
                  }}
                >
                  <div style={{ display: 'flex', gap: '1rem' }}>
                    <span>
                      From Start: <strong style={{ color: 'var(--s7-text-high)' }}>{percent(step.conversionFromStart)}</strong>
                    </span>
                    {step.stepIndex > 0 ? (
                      <span>
                        From Prev Step:{' '}
                        <strong style={{ color: 'var(--s7-text-high)' }}>{percent(step.conversionFromPrevious)}</strong>
                      </span>
                    ) : null}
                  </div>

                  {step.stepIndex > 0 && step.dropOffCount > 0 ? (
                    <span
                      style={{
                        color: step.dropOffRate > 0.25 ? 'var(--s7-tone-danger, #f87171)' : 'var(--s7-tone-warning, #fbbf24)',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '3px',
                      }}
                    >
                      <TrendingDown size={12} />
                      Drop-off: {step.dropOffCount} ({percent(step.dropOffRate)})
                    </span>
                  ) : null}
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : funnel && funnel.totalStarted === 0 ? (
        <div
          style={{
            padding: '2.5rem 1rem',
            textAlign: 'center',
            color: 'var(--s7-text-muted)',
            background: 'var(--s7-surface-secondary, rgba(255,255,255,0.02))',
            borderRadius: '8px',
            border: '1px dashed var(--s7-border, rgba(255,255,255,0.1))',
          }}
        >
          <Layers size={32} style={{ margin: '0 auto 0.75rem', opacity: 0.5 }} />
          <p style={{ margin: 0, fontWeight: 600 }}>No telemetry events recorded in this time range.</p>
          <p style={{ margin: '0.25rem 0 0 0', fontSize: '0.8rem' }}>
            Events emitted by Unity clients (`guidance_flow_start`, `guidance_step`, `guidance_flow_end`) will appear here automatically.
          </p>
        </div>
      ) : null}

      {/* Missing Anchor Alerts for this flow */}
      {missingAnchors.length > 0 ? (
        <div
          style={{
            padding: '1rem',
            borderRadius: '8px',
            border: '1px solid rgba(239, 68, 68, 0.3)',
            background: 'rgba(239, 68, 68, 0.05)',
            display: 'flex',
            flexDirection: 'column',
            gap: '0.6rem',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'var(--s7-tone-danger, #f87171)' }}>
            <AlertTriangle size={18} />
            <strong style={{ fontSize: '0.9rem' }}>
              Missing Anchor Warnings ({missingAnchors.length})
            </strong>
          </div>
          <p style={{ margin: 0, fontSize: '0.8rem', color: 'var(--s7-text-muted)' }}>
            Unity clients reported missing UI targets while executing this flow. Verify that the following anchors exist in client scenes:
          </p>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.4rem', marginTop: '0.3rem' }}>
            {missingAnchors.map((ma, idx) => (
              <div
                key={idx}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: '0.5rem 0.8rem',
                  background: 'rgba(0,0,0,0.2)',
                  borderRadius: '4px',
                  fontSize: '0.82rem',
                }}
              >
                <div>
                  <code style={{ color: '#fca5a5', fontWeight: 600 }}>#{ma.anchorId}</code>
                  <span style={{ marginLeft: '0.6rem', color: 'var(--s7-text-muted)' }}>
                    Step {ma.stepIndex + 1}
                  </span>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                  <Badge tone="danger">{ma.occurrenceCount}x reported</Badge>
                  <span style={{ fontSize: '0.75rem', color: 'var(--s7-text-muted)' }}>
                    Last seen {formatRelative(ma.lastSeenUtc)}
                  </span>
                  {ma.platforms.map((p) => (
                    <Badge key={p} tone="info">
                      {p}
                    </Badge>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  )
}
