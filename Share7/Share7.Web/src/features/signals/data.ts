import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  CreateSignalValuationRequest,
  SignalValuationDto,
  UpdateSignalValuationRequest,
} from '../../types/api'

// ===========================================================================
// Signal valuations — data access
//
// This is the newest surface in the platform and had no console at all: the
// XP/signal economy landed on main only after the vanilla panel was frozen, so
// unit values and ceilings could until now only be changed with SQL.
//
// The route is still `/api/admin/pickup-valuations`. The domain was renamed
// pickup -> signal, the path was not, and inventing `/signal-valuations` in the
// client would simply 404. The DTO carries both names for the same reason;
// only `signalKind` is mirrored in the TypeScript.
// ===========================================================================

const ROOT = '/api/admin/pickup-valuations'

export function useSignalValuations() {
  const resource = useResourceList<SignalValuationDto>(ROOT)

  const create = useCallback(
    async (request: CreateSignalValuationRequest) => {
      await api.post<SignalValuationDto>(ROOT, request)
      toast.success('Valuation created', `"${request.signalKind}" now pays out.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (id: string, request: UpdateSignalValuationRequest) => {
      const updated = await api.put<SignalValuationDto>(`${ROOT}/${id}`, request)
      resource.set((rows) => rows.map((v) => (v.id === id ? updated : v)))

      toast.success(
        request.enabled ? 'Valuation updated' : 'Valuation disabled',
        request.enabled
          ? `"${updated.signalKind}" pays ${request.unitValue.toLocaleString()} ${updated.currency} per unit.`
          : `"${updated.signalKind}" no longer pays out. Past runs are untouched.`,
      )

      return updated
    },
    [resource],
  )

  return { ...resource, valuations: resource.data, create, update }
}

/**
 * Signal kinds the platform currently emits, from Share7.Domain.Economy.SignalKinds.
 *
 * Offered as suggestions, not enforced: the field is a string on the wire and a
 * new mini-game can report a kind this console has never heard of. A valuation
 * for an unknown kind is harmless — it simply never matches a report.
 */
export const SIGNAL_KINDS = ['coin', 'near_miss', 'distance_m', 'correct_answer'] as const

/**
 * Which side reported the signal, and therefore how much it can be trusted.
 *
 * Server-side, so it is read-only here — the API derives it from the kind. It
 * matters to whoever is setting a ceiling: a Run signal is the client's word and
 * wants tight caps, an Attempt signal was graded against the server's own answer
 * key and does not.
 */
export function surfaceTone(surface: string): 'warning' | 'success' | 'muted' {
  if (surface === 'Run') return 'warning'
  if (surface === 'Attempt') return 'success'
  return 'muted'
}

export function blankValuation(): CreateSignalValuationRequest {
  return {
    gameId: null,
    signalKind: '',
    currencyId: '',
    unitValue: 1,
    maxPerRun: 100,
    maxPerDay: null,
    maxPerSecond: null,
    enabled: true,
  }
}
