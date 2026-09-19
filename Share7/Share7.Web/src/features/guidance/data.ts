// ===========================================================================
// Guidance Platform CMS — data access
//
// Remote authoring, versioning, publishing, emergency kill switch, audit logs,
// and user guidance state management.
// ===========================================================================

import { useCallback, useEffect, useState } from 'react'
import { api } from '../../lib/client'
import { toast } from '../../store/toast'
import type {
  CreateGuidanceFlowRequest,
  GuidanceAuditLogDto,
  GuidanceFlowAdminDto,
  GuidanceStateResponseDto,
  PublishGuidanceFlowRequest,
  ResetUserGuidanceRequest,
  ToggleKillSwitchRequest,
  UpdateGuidanceFlowDraftRequest,
} from '../../types/api'

// ---------------------------------------------------------------------------
// Guidance Flows Catalogue (Admin)
// ---------------------------------------------------------------------------

export function useGuidanceFlows() {
  const [flows, setFlows] = useState<GuidanceFlowAdminDto[]>([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(async (isRefresh = false) => {
    if (isRefresh) setRefreshing(true)
    try {
      const data = await api.get<GuidanceFlowAdminDto[]>('/api/admin/guidance/flows?includeKillSwitched=true')
      setFlows(data ?? [])
    } catch {
      // Surfaced by global error handler
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const createFlow = useCallback(
    async (request: CreateGuidanceFlowRequest) => {
      const created = await api.post<GuidanceFlowAdminDto>('/api/admin/guidance/flows', request)
      toast.success('Guidance flow created', `Flow "${request.key}" is initialized with Draft v1.`)
      await load(true)
      return created
    },
    [load],
  )

  const toggleKillSwitch = useCallback(
    async (flowId: string, isKillSwitched: boolean, reason: string) => {
      const updated = await api.post<GuidanceFlowAdminDto>(`/api/admin/guidance/flows/${flowId}/kill-switch`, {
        isKillSwitched,
        reason,
      } as ToggleKillSwitchRequest)

      setFlows((prev) => prev.map((f) => (f.id === flowId ? updated : f)))

      toast.success(
        isKillSwitched ? 'Kill switch ACTIVATED' : 'Kill switch DEACTIVATED',
        isKillSwitched
          ? `Flow "${updated.key}" is immediately hidden from client catalogs.`
          : `Flow "${updated.key}" is restored to active client catalogs.`,
      )
      return updated
    },
    [],
  )

  return { flows, loading, refreshing, reload: () => load(true), createFlow, toggleKillSwitch }
}

// ---------------------------------------------------------------------------
// Single Flow Details & Versioning
// ---------------------------------------------------------------------------

export function useGuidanceFlow(flowId: string | null) {
  const [flow, setFlow] = useState<GuidanceFlowAdminDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(
    async (isRefresh = false) => {
      if (!flowId) {
        setFlow(null)
        return
      }
      if (isRefresh) setRefreshing(true)
      else setLoading(true)

      try {
        const data = await api.get<GuidanceFlowAdminDto>(`/api/admin/guidance/flows/${flowId}`)
        setFlow(data)
      } catch {
        // Surfaced by global error handler
      } finally {
        setLoading(false)
        setRefreshing(false)
      }
    },
    [flowId],
  )

  useEffect(() => {
    void load()
  }, [load])

  const updateDraft = useCallback(
    async (request: UpdateGuidanceFlowDraftRequest) => {
      if (!flowId) return null
      const updated = await api.put<GuidanceFlowAdminDto>(`/api/admin/guidance/flows/${flowId}/draft`, request)
      setFlow(updated)
      toast.success('Draft updated', `Changes saved to draft version of "${updated.key}".`)
      return updated
    },
    [flowId],
  )

  const publishVersion = useCallback(
    async (request: PublishGuidanceFlowRequest) => {
      if (!flowId) return null
      const updated = await api.post<GuidanceFlowAdminDto>(`/api/admin/guidance/flows/${flowId}/publish`, request)
      setFlow(updated)
      toast.success('Version published', `Version ${updated.activeVersionNumber} of "${updated.key}" is now LIVE!`)
      return updated
    },
    [flowId],
  )

  return { flow, loading, refreshing, reload: () => load(true), updateDraft, publishVersion }
}

// ---------------------------------------------------------------------------
// Audit Logs
// ---------------------------------------------------------------------------

export function useGuidanceAuditLogs(flowId?: string | null) {
  const [logs, setLogs] = useState<GuidanceAuditLogDto[]>([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(
    async (isRefresh = false) => {
      if (isRefresh) setRefreshing(true)
      try {
        const path = flowId
          ? `/api/admin/guidance/audit-logs?flowId=${flowId}`
          : '/api/admin/guidance/audit-logs'
        const data = await api.get<GuidanceAuditLogDto[]>(path)
        setLogs(data ?? [])
      } catch {
        // Surfaced by global error handler
      } finally {
        setLoading(false)
        setRefreshing(false)
      }
    },
    [flowId],
  )

  useEffect(() => {
    void load()
  }, [load])

  return { logs, loading, refreshing, reload: () => load(true) }
}

// ---------------------------------------------------------------------------
// Target User Guidance State & Admin Reset
// ---------------------------------------------------------------------------

export function useUserGuidanceState(userId: string | null) {
  const [state, setState] = useState<GuidanceStateResponseDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [resetting, setResetting] = useState(false)

  const load = useCallback(async () => {
    if (!userId) {
      setState(null)
      return
    }
    setLoading(true)
    try {
      const data = await api.get<GuidanceStateResponseDto>(`/api/admin/guidance/users/${userId}/state`)
      setState(data)
    } catch {
      // Surfaced by global error handler
    } finally {
      setLoading(false)
    }
  }, [userId])

  useEffect(() => {
    void load()
  }, [load])

  const resetGuidance = useCallback(
    async (reason: string) => {
      if (!userId) return
      setResetting(true)
      try {
        await api.post(`/api/admin/guidance/users/${userId}/reset`, {
          reason,
        } as ResetUserGuidanceRequest)
        toast.success(
          'Guidance progress reset',
          `Generation incremented. The user's guidance state will rebase and onboarding will replay on next sync.`,
        )
        await load()
      } finally {
        setResetting(false)
      }
    },
    [userId, load],
  )

  return { state, loading, resetting, reload: load, resetGuidance }
}

// ---------------------------------------------------------------------------
// Guidance Analytics: Funnel, Drop-off & Diagnostics
// ---------------------------------------------------------------------------

import type {
  GuidanceFlowFunnelDto,
  GuidanceFlowSummaryStatsDto,
  GuidanceMissingAnchorSummaryDto,
} from '../../types/api'

export function useGuidanceFunnel(
  flowId: string | null,
  version: number | null = null,
  days: number = 30,
) {
  const [funnel, setFunnel] = useState<GuidanceFlowFunnelDto | null>(null)
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    if (!flowId) {
      setFunnel(null)
      return
    }
    setLoading(true)
    try {
      const to = new Date().toISOString().slice(0, 10)
      const from = new Date(Date.now() - days * 86400000).toISOString().slice(0, 10)
      let url = `/api/admin/guidance/flows/${flowId}/funnel?from=${from}&to=${to}`
      if (version !== null && version !== undefined) {
        url += `&version=${version}`
      }
      const data = await api.get<GuidanceFlowFunnelDto>(url)
      setFunnel(data)
    } catch {
      // Handled globally
    } finally {
      setLoading(false)
    }
  }, [flowId, version, days])

  useEffect(() => {
    void load()
  }, [load])

  return { funnel, loading, reload: load }
}

export function useMissingAnchors(flowKey?: string | null, days: number = 30) {
  const [missingAnchors, setMissingAnchors] = useState<GuidanceMissingAnchorSummaryDto[]>([])
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const to = new Date().toISOString().slice(0, 10)
      const from = new Date(Date.now() - days * 86400000).toISOString().slice(0, 10)
      let url = `/api/admin/guidance/missing-anchors?from=${from}&to=${to}`
      if (flowKey) {
        url += `&flowKey=${encodeURIComponent(flowKey)}`
      }
      const data = await api.get<GuidanceMissingAnchorSummaryDto[]>(url)
      setMissingAnchors(data ?? [])
    } catch {
      // Handled globally
    } finally {
      setLoading(false)
    }
  }, [flowKey, days])

  useEffect(() => {
    void load()
  }, [load])

  return { missingAnchors, loading, reload: load }
}

export function useFlowsSummaryStats(days: number = 30) {
  const [summaryStats, setSummaryStats] = useState<GuidanceFlowSummaryStatsDto[]>([])
  const [loading, setLoading] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const to = new Date().toISOString().slice(0, 10)
      const from = new Date(Date.now() - days * 86400000).toISOString().slice(0, 10)
      const data = await api.get<GuidanceFlowSummaryStatsDto[]>(
        `/api/admin/guidance/flows-summary?from=${from}&to=${to}`,
      )
      setSummaryStats(data ?? [])
    } catch {
      // Handled globally
    } finally {
      setLoading(false)
    }
  }, [days])

  useEffect(() => {
    void load()
  }, [load])

  return { summaryStats, loading, reload: load }
}
