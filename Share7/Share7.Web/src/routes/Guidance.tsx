import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { AlertOctagon, CheckCircle2, Compass, Plus, RefreshCw, Sparkles, Users } from 'lucide-react'
import { PageHeader } from '../components/layout/AppShell'
import { Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { SearchBox } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { listVariants } from '../components/ui/motion'
import { useFlowsSummaryStats, useGuidanceFlows } from '../features/guidance/data'
import { FlowListTable } from '../features/guidance/FlowListTable'
import { CreateFlowModal } from '../features/guidance/CreateFlowModal'
import { FlowEditorModal } from '../features/guidance/FlowEditorModal'
import { KillSwitchConfirmModal } from '../features/guidance/KillSwitchConfirmModal'
import { MissingAnchorsBanner } from '../features/guidance/MissingAnchorsBanner'
import type { GuidanceFlowAdminDto } from '../types/api'

export function Guidance() {
  const { flows, loading, refreshing, reload, createFlow, toggleKillSwitch } = useGuidanceFlows()
  const { summaryStats, reload: reloadStats } = useFlowsSummaryStats(30)

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<'all' | 'published' | 'draft' | 'killed'>('all')

  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [editingFlowId, setEditingFlowId] = useState<string | null>(null)
  const [killSwitchTarget, setKillSwitchTarget] = useState<GuidanceFlowAdminDto | null>(null)

  const filteredFlows = useMemo(() => {
    let list = flows
    if (search.trim()) {
      const q = search.toLowerCase().trim()
      list = list.filter((f) => f.key.toLowerCase().includes(q) || f.title.toLowerCase().includes(q))
    }
    if (filter === 'published') {
      list = list.filter((f) => f.activeVersionNumber > 0 && !f.isKillSwitched)
    } else if (filter === 'draft') {
      list = list.filter((f) => Boolean(f.draftVersion))
    } else if (filter === 'killed') {
      list = list.filter((f) => f.isKillSwitched)
    }
    return list
  }, [flows, search, filter])

  const totalFlows = flows.length
  const liveCount = flows.filter((f) => f.activeVersionNumber > 0 && !f.isKillSwitched).length
  const killSwitchCount = flows.filter((f) => f.isKillSwitched).length

  const totalEntrants = summaryStats.reduce((sum, s) => sum + s.totalStarted, 0)
  const totalCompleted = summaryStats.reduce((sum, s) => sum + s.totalCompleted, 0)
  const avgCompletionRate = totalEntrants > 0 ? (totalCompleted / totalEntrants) * 100 : 0

  return (
    <>
      <PageHeader icon={<Compass size={22} />} title="Guidance Platform">
        Remotely author, version, publish, and emergency-manage child onboarding and in-game guidance flows.
      </PageHeader>

      <motion.div variants={listVariants} initial="hidden" animate="visible" style={{ display: 'flex', flexDirection: 'column', gap: '1.2rem' }}>
        <StatRow>
          <Stat icon={<Compass size={14} />} label="Total Flows" value={totalFlows} tone="brand" />
          <Stat icon={<Sparkles size={14} />} label="Live Published" value={liveCount} tone="success" />
          <Stat icon={<Users size={14} />} label="30d Entrants" value={totalEntrants.toLocaleString()} tone="info" />
          <Stat
            icon={<CheckCircle2 size={14} />}
            label="Avg Completion"
            value={`${avgCompletionRate.toFixed(0)}%`}
            tone={avgCompletionRate >= 60 ? 'success' : 'warning'}
          />
          <Stat
            icon={<AlertOctagon size={14} />}
            label="Kill-Switched"
            value={killSwitchCount}
            tone={killSwitchCount > 0 ? 'danger' : 'cool'}
          />
        </StatRow>

        <MissingAnchorsBanner
          onSelectFlow={(flowKey) => {
            const target = flows.find((f) => f.key.toLowerCase() === flowKey.toLowerCase())
            if (target) setEditingFlowId(target.id)
          }}
        />

        <Card>
          <CardHeader
            icon={<Compass size={16} />}
            title="Guidance Catalogue"
            actions={
              <div style={{ display: 'flex', gap: '0.6rem', alignItems: 'center' }}>
                <IconButton
                  label="Refresh catalogue"
                  busy={refreshing}
                  onClick={() => {
                    void reload()
                    void reloadStats()
                  }}
                >
                  <RefreshCw size={14} />
                </IconButton>
                <Button onClick={() => setCreateModalOpen(true)}>
                  <Plus size={14} />
                  New Flow
                </Button>
              </div>
            }
          />
          <CardBody>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem', flexWrap: 'wrap', gap: '0.8rem' }}>
              <div style={{ maxWidth: '300px', width: '100%' }}>
                <SearchBox placeholder="Search flows by key or title..." value={search} onChange={setSearch} />
              </div>

              <div style={{ display: 'flex', gap: '0.4rem' }}>
                {(
                  [
                    { id: 'all', label: 'All Flows' },
                    { id: 'published', label: 'Live' },
                    { id: 'draft', label: 'Drafts' },
                    { id: 'killed', label: 'Kill Switched' },
                  ] as const
                ).map((btn) => (
                  <Button
                    key={btn.id}
                    variant={filter === btn.id ? 'primary' : 'ghost'}
                    onClick={() => setFilter(btn.id)}
                  >
                    {btn.label}
                  </Button>
                ))}
              </div>
            </div>

            <FlowListTable
              flows={filteredFlows}
              loading={loading}
              summaryStats={summaryStats}
              onEdit={(flow) => setEditingFlowId(flow.id)}
              onToggleKillSwitch={(flow) => setKillSwitchTarget(flow)}
            />
          </CardBody>
        </Card>
      </motion.div>

      <CreateFlowModal
        open={createModalOpen}
        onClose={() => setCreateModalOpen(false)}
        onCreate={async (req) => {
          const res = (await createFlow(req)) as GuidanceFlowAdminDto
          if (res?.id) {
            setEditingFlowId(res.id)
          }
        }}
      />

      <FlowEditorModal
        flowId={editingFlowId}
        onClose={() => setEditingFlowId(null)}
        onToggleKillSwitch={(flow) => setKillSwitchTarget(flow)}
      />

      <KillSwitchConfirmModal
        flow={killSwitchTarget}
        onClose={() => setKillSwitchTarget(null)}
        onConfirm={async (id, isKilled, reason) => {
          await toggleKillSwitch(id, isKilled, reason)
        }}
      />
    </>
  )
}
