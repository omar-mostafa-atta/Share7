import { useState } from 'react'
import { AlertTriangle, ChevronDown, ChevronUp, ExternalLink } from 'lucide-react'
import { Badge, Button } from '../../components/ui/primitives'
import { formatRelative } from '../../lib/time'
import { useMissingAnchors } from './data'

interface MissingAnchorsBannerProps {
  onSelectFlow?: (flowKey: string) => void
}

export function MissingAnchorsBanner({ onSelectFlow }: MissingAnchorsBannerProps) {
  const { missingAnchors } = useMissingAnchors(null, 30)
  const [expanded, setExpanded] = useState(false)

  if (missingAnchors.length === 0) return null

  const totalReports = missingAnchors.reduce((sum, a) => sum + a.occurrenceCount, 0)

  return (
    <div
      style={{
        background: 'rgba(239, 68, 68, 0.08)',
        border: '1px solid rgba(239, 68, 68, 0.3)',
        borderRadius: '8px',
        padding: '0.9rem 1.2rem',
        display: 'flex',
        flexDirection: 'column',
        gap: '0.6rem',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: '0.5rem',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
          <AlertTriangle size={18} style={{ color: 'var(--s7-tone-danger, #ef4444)' }} />
          <div>
            <strong style={{ fontSize: '0.9rem', color: 'var(--s7-tone-danger, #f87171)' }}>
              Broken Scene Anchors Detected ({missingAnchors.length} unique anchors, {totalReports} reports)
            </strong>
            <p style={{ margin: '0.1rem 0 0 0', fontSize: '0.78rem', color: 'var(--s7-text-muted)' }}>
              Unity clients reported UI target anchors missing during guidance playback.
            </p>
          </div>
        </div>

        <Button
          variant="ghost"
          onClick={() => setExpanded(!expanded)}
          style={{ fontSize: '0.8rem', padding: '0.25rem 0.5rem' }}
        >
          {expanded ? (
            <>
              Hide details <ChevronUp size={14} />
            </>
          ) : (
            <>
              Inspect {missingAnchors.length} anchors <ChevronDown size={14} />
            </>
          )}
        </Button>
      </div>

      {expanded ? (
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: '0.4rem',
            marginTop: '0.4rem',
            maxHeight: '260px',
            overflowY: 'auto',
          }}
        >
          {missingAnchors.map((item, idx) => (
            <div
              key={idx}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '0.5rem 0.8rem',
                background: 'rgba(0, 0, 0, 0.25)',
                borderRadius: '6px',
                fontSize: '0.82rem',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.8rem' }}>
                <code style={{ color: '#fca5a5', fontWeight: 600 }}>#{item.anchorId}</code>
                <span style={{ color: 'var(--s7-text-muted)' }}>
                  Flow: <strong>{item.flowKey}</strong> (Step {item.stepIndex + 1})
                </span>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                <Badge tone="danger">{item.occurrenceCount} occurrences</Badge>
                <span style={{ fontSize: '0.75rem', color: 'var(--s7-text-muted)' }}>
                  {formatRelative(item.lastSeenUtc)}
                </span>
                {item.platforms.map((p) => (
                  <Badge key={p} tone="info">
                    {p}
                  </Badge>
                ))}
                {onSelectFlow ? (
                  <button
                    type="button"
                    onClick={() => onSelectFlow(item.flowKey)}
                    style={{
                      background: 'none',
                      border: 'none',
                      color: 'var(--s7-brand, #818cf8)',
                      cursor: 'pointer',
                      padding: '2px',
                      display: 'inline-flex',
                      alignItems: 'center',
                    }}
                    title="Open flow editor"
                  >
                    <ExternalLink size={14} />
                  </button>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </div>
  )
}
