import { useMemo } from 'react'
import { AlertOctagon, Edit3, ShieldAlert } from 'lucide-react'
import { Badge, Button, SkeletonRows } from '../../components/ui/primitives'
import { DataTable } from '../../components/ui/DataTable'
import type { Column } from '../../components/ui/DataTable'
import type { GuidanceFlowAdminDto, GuidanceFlowSummaryStatsDto } from '../../types/api'

interface FlowListTableProps {
  flows: GuidanceFlowAdminDto[]
  loading: boolean
  summaryStats?: GuidanceFlowSummaryStatsDto[]
  onEdit: (flow: GuidanceFlowAdminDto) => void
  onToggleKillSwitch: (flow: GuidanceFlowAdminDto) => void
}

const PRIORITY_NAMES: Record<number, { label: string; tone: 'muted' | 'info' | 'warning' | 'danger' }> = {
  0: { label: 'Ambient', tone: 'muted' },
  1: { label: 'Queued', tone: 'info' },
  2: { label: 'Immediate', tone: 'warning' },
  3: { label: 'Blocking', tone: 'danger' },
}

export function FlowListTable({ flows, loading, summaryStats = [], onEdit, onToggleKillSwitch }: FlowListTableProps) {
  const statsMap = useMemo(() => {
    const map = new Map<string, GuidanceFlowSummaryStatsDto>()
    for (const s of summaryStats) {
      map.set(s.flowKey.toLowerCase(), s)
    }
    return map
  }, [summaryStats])

  const columns = useMemo<Column<GuidanceFlowAdminDto>[]>(
    () => [
      {
        key: 'key',
        header: 'Flow Key / Title',
        render: (flow: GuidanceFlowAdminDto) => (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.15rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <code className="s7-input-mono" style={{ fontWeight: 600 }}>
                {flow.key}
              </code>
              {flow.isKillSwitched && <Badge tone="danger">KILL SWITCHED</Badge>}
            </div>
            <span style={{ fontSize: '0.8rem', color: 'var(--s7-muted, #888)' }}>{flow.title}</span>
          </div>
        ),
      },
      {
        key: 'kind',
        header: 'Kind',
        render: (flow: GuidanceFlowAdminDto) => <Badge tone="brand">{flow.kind}</Badge>,
      },
      {
        key: 'priority',
        header: 'Priority',
        render: (flow: GuidanceFlowAdminDto) => {
          const cfg = PRIORITY_NAMES[flow.priority] ?? { label: `P${flow.priority}`, tone: 'muted' as const }
          return <Badge tone={cfg.tone}>{cfg.label}</Badge>
        },
      },
      {
        key: 'version',
        header: 'Active Version',
        render: (flow: GuidanceFlowAdminDto) => (
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
            {flow.activeVersionNumber > 0 ? (
              <Badge tone="success">v{flow.activeVersionNumber}</Badge>
            ) : (
              <span className="s7-muted" style={{ fontSize: '0.8rem' }}>
                None
              </span>
            )}
            {flow.draftVersion && <Badge tone="warning">Draft v{flow.draftVersion.versionNumber}</Badge>}
          </div>
        ),
      },
      {
        key: 'performance',
        header: 'Performance (30d)',
        render: (flow: GuidanceFlowAdminDto) => {
          const stat = statsMap.get(flow.key.toLowerCase())
          if (!stat || stat.totalStarted === 0) {
            return <span style={{ fontSize: '0.78rem', color: 'var(--s7-text-muted)' }}>No data</span>
          }
          const pct = `${(stat.completionRate * 100).toFixed(0)}%`
          return (
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', flexWrap: 'wrap' }}>
              <span style={{ fontSize: '0.8rem' }}>
                <strong>{stat.totalStarted}</strong> entrants
              </span>
              <Badge tone={stat.completionRate >= 0.7 ? 'success' : stat.completionRate >= 0.4 ? 'info' : 'warning'}>
                {pct} complete
              </Badge>
              {stat.missingAnchorCount > 0 && (
                <Badge tone="danger">⚠️ {stat.missingAnchorCount} broken</Badge>
              )}
            </div>
          )
        },
      },
      {
        key: 'policy',
        header: 'Replay Policy',
        render: (flow: GuidanceFlowAdminDto) => {
          const names = ['Never', 'Always', 'OnVersionChange']
          return <span style={{ fontSize: '0.8rem' }}>{names[flow.replayPolicy] || 'Never'}</span>
        },
      },
      {
        key: 'actions',
        header: 'Actions',
        numeric: true,
        render: (flow: GuidanceFlowAdminDto) => (
          <div style={{ display: 'flex', gap: '0.4rem', justifyContent: 'flex-end' }}>
            <Button
              variant="ghost"
              onClick={(e) => {
                e.stopPropagation()
                onToggleKillSwitch(flow)
              }}
              title={flow.isKillSwitched ? 'Restore flow' : 'Emergency Kill Switch'}
            >
              {flow.isKillSwitched ? (
                <ShieldAlert size={14} color="var(--s7-success)" />
              ) : (
                <AlertOctagon size={14} color="var(--s7-danger)" />
              )}
              {flow.isKillSwitched ? 'Restore' : 'Kill Switch'}
            </Button>

            <Button
              variant="primary"
              onClick={(e) => {
                e.stopPropagation()
                onEdit(flow)
              }}
            >
              <Edit3 size={13} />
              Edit
            </Button>
          </div>
        ),
      },
    ],
    [onEdit, onToggleKillSwitch],
  )

  if (loading) {
    return <SkeletonRows rows={6} />
  }

  return (
    <DataTable<GuidanceFlowAdminDto>
      columns={columns}
      rows={flows}
      getId={(f) => f.id}
      onRowClick={onEdit}
      empty={
        <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--s7-muted)' }}>
          No guidance flows defined yet. Click "Create Flow" to author the first remote flow.
        </div>
      }
    />
  )
}
