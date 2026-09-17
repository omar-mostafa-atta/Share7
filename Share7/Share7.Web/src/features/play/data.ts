import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  EconomyProfileDto,
  EventAwardDto,
  GameModeAdminDto,
  GameWorldAdminDto,
  PlayEventAdminDto,
  PrizeClaimAdminDto,
  SaveEconomyProfileRequest,
  SaveGameModeRequest,
  SaveGameWorldRequest,
  SavePlayEventRequest,
  UpdatePrizeClaimRequest,
} from '../../types/api'

// ===========================================================================
// Play context — data access
//
// Four surfaces of the same domain, kept apart because the pages that use them
// do not need each other:
//
//   modes     the rule-sets a game offers, and what each may be worth
//   worlds    which environments exist and how each is come by
//   events    competitions, their ladders and their prize tables
//   claims    the real-world prize queue, which only a person moves
//
// Everything is authoring-shaped: each row carries every translation, because
// saving is a full replace and a form filled from a one-language read would
// delete the rest.
// ===========================================================================

// ---------------------------------------------------------------------------
// Modes
// ---------------------------------------------------------------------------

export function useGameModes(gameId: string | null) {
  const resource = useResourceList<GameModeAdminDto>(
    gameId ? `/api/admin/modes?gameId=${gameId}` : '/api/admin/modes',
  )

  const create = useCallback(
    async (request: SaveGameModeRequest) => {
      await api.post<GameModeAdminDto>('/api/admin/modes', request)
      toast.success('Mode created', `"${request.modeKey}" is in the catalogue.`)

      // A full reload rather than an insert: creating a default demotes whichever
      // mode held the flag, and that row is on screen too.
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (modeId: string, request: SaveGameModeRequest) => {
      const updated = await api.put<GameModeAdminDto>(`/api/admin/modes/${modeId}`, request)

      if (request.isDefault) await resource.reload()
      else resource.set((rows) => rows.map((m) => (m.modeId === modeId ? updated : m)))

      toast.success(
        request.isActive ? 'Mode updated' : 'Mode withdrawn',
        request.isActive
          ? `"${updated.modeKey}" saved.`
          : `"${updated.modeKey}" is hidden from clients. Runs already recorded keep their mode.`,
      )

      return updated
    },
    [resource],
  )

  const remove = useCallback(
    async (mode: GameModeAdminDto, force: boolean) => {
      await api.del(`/api/admin/modes/${mode.modeId}${force ? '?force=true' : ''}`)
      resource.set((rows) => rows.filter((m) => m.modeId !== mode.modeId))
      toast.success('Mode deleted', `"${mode.modeKey}" is gone.`)
    },
    [resource],
  )

  return { ...resource, modes: resource.data, create, update, remove }
}

/** A blank mode, matching the C# defaults so a create that touches nothing saves what the API would. */
export function blankMode(gameId: string): SaveGameModeRequest {
  return {
    gameId,
    modeKey: '',
    topologies: ['solo'],
    minPlayers: 1,
    maxPlayers: 1,
    isActive: true,
    isDefault: false,
    availableFromUtc: null,
    availableToUtc: null,
    requiresEntitlement: false,
    entitlementProductId: null,
    minGradeOrder: 0,
    countsTowardMastery: true,
    settlesEconomy: true,
    countsTowardRanking: true,
    economyProfileId: null,
    sortOrder: 0,
    translations: [],
  }
}

export function modeToRequest(mode: GameModeAdminDto): SaveGameModeRequest {
  return {
    gameId: mode.gameId,
    modeKey: mode.modeKey,
    topologies: [...mode.topologies],
    minPlayers: mode.minPlayers,
    maxPlayers: mode.maxPlayers,
    isActive: mode.isActive,
    isDefault: mode.isDefault,
    availableFromUtc: mode.availableFromUtc,
    availableToUtc: mode.availableToUtc,
    requiresEntitlement: mode.requiresEntitlement,
    entitlementProductId: mode.entitlementProductId,
    minGradeOrder: mode.minGradeOrder,
    countsTowardMastery: mode.countsTowardMastery,
    settlesEconomy: mode.settlesEconomy,
    countsTowardRanking: mode.countsTowardRanking,
    economyProfileId: mode.economyProfileId,
    sortOrder: mode.sortOrder,
    translations: mode.translations.map((t) => ({ ...t })),
  }
}

// ---------------------------------------------------------------------------
// Worlds
// ---------------------------------------------------------------------------

export function useGameWorlds(gameId: string | null) {
  const resource = useResourceList<GameWorldAdminDto>(
    gameId ? `/api/admin/worlds?gameId=${gameId}` : '/api/admin/worlds',
  )

  const create = useCallback(
    async (request: SaveGameWorldRequest) => {
      await api.post<GameWorldAdminDto>('/api/admin/worlds', request)
      toast.success('World added', `"${request.worldKey}" is in the catalogue.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (worldId: string, request: SaveGameWorldRequest) => {
      const updated = await api.put<GameWorldAdminDto>(`/api/admin/worlds/${worldId}`, request)

      if (request.isDefault) await resource.reload()
      else resource.set((rows) => rows.map((w) => (w.worldId === worldId ? updated : w)))

      toast.success('World updated', `"${updated.worldKey}" saved.`)
      return updated
    },
    [resource],
  )

  const remove = useCallback(
    async (world: GameWorldAdminDto) => {
      await api.del(`/api/admin/worlds/${world.worldId}`)
      resource.set((rows) => rows.filter((w) => w.worldId !== world.worldId))

      // Worth saying plainly: entitlements point at products, not at this row, so removing the
      // policy does not take the world away from anyone who bought it.
      toast.success('World removed', `"${world.worldKey}" is no longer offered. Nobody was un-owned.`)
    },
    [resource],
  )

  return { ...resource, worlds: resource.data, create, update, remove }
}

export function blankWorld(gameId: string): SaveGameWorldRequest {
  return {
    gameId,
    worldKey: '',
    unlockKind: 'free',
    productId: null,
    minLevel: 0,
    minGradeOrder: 0,
    sortOrder: 0,
    isActive: true,
    isDefault: false,
    translations: [],
  }
}

export function worldToRequest(world: GameWorldAdminDto): SaveGameWorldRequest {
  return {
    gameId: world.gameId,
    worldKey: world.worldKey,
    unlockKind: world.unlockKind,
    productId: world.productId,
    minLevel: world.minLevel,
    minGradeOrder: world.minGradeOrder,
    sortOrder: world.sortOrder,
    isActive: world.isActive,
    isDefault: world.isDefault,
    translations: world.translations.map((t) => ({ ...t })),
  }
}

// ---------------------------------------------------------------------------
// Economy profiles
// ---------------------------------------------------------------------------

export function useEconomyProfiles() {
  const resource = useResourceList<EconomyProfileDto>('/api/admin/economy-profiles')

  const create = useCallback(
    async (request: SaveEconomyProfileRequest) => {
      await api.post<EconomyProfileDto>('/api/admin/economy-profiles', request)
      toast.success('Profile created', `"${request.profileKey}" pays ${request.payoutPercent}%.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (profileId: string, request: SaveEconomyProfileRequest) => {
      await api.put<EconomyProfileDto>(`/api/admin/economy-profiles/${profileId}`, request)
      toast.success('Profile updated', `"${request.profileKey}" saved.`)
      await resource.reload()
    },
    [resource],
  )

  const remove = useCallback(
    async (profile: EconomyProfileDto) => {
      await api.del(`/api/admin/economy-profiles/${profile.profileId}`)
      resource.set((rows) => rows.filter((p) => p.profileId !== profile.profileId))
      toast.success('Profile deleted', `"${profile.profileKey}" is gone.`)
    },
    [resource],
  )

  return { ...resource, profiles: resource.data, create, update, remove }
}

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

export function usePlayEvents(gameId: string | null) {
  const resource = useResourceList<PlayEventAdminDto>(
    gameId ? `/api/admin/events?gameId=${gameId}` : '/api/admin/events',
  )

  const create = useCallback(
    async (request: SavePlayEventRequest) => {
      const created = await api.post<PlayEventAdminDto>('/api/admin/events', request)
      toast.success(
        'Event created',
        `"${request.eventKey}" has its own board and cycle. It opens ${new Date(request.startsAtUtc).toLocaleString()}.`,
      )
      await resource.reload()
      return created
    },
    [resource],
  )

  const update = useCallback(
    async (eventId: string, request: SavePlayEventRequest) => {
      const updated = await api.put<PlayEventAdminDto>(`/api/admin/events/${eventId}`, request)
      resource.set((rows) => rows.map((e) => (e.eventId === eventId ? updated : e)))
      toast.success('Event updated', `"${updated.eventKey}" saved.`)
      return updated
    },
    [resource],
  )

  const cancel = useCallback(
    async (eventId: string, reason: string) => {
      const cancelled = await api.post<PlayEventAdminDto>(`/api/admin/events/${eventId}/cancel`, { reason })
      resource.set((rows) => rows.map((e) => (e.eventId === eventId ? cancelled : e)))

      toast.success(
        'Event cancelled',
        'Entries stop now, the ladder is closed, and no prize will be awarded.',
      )

      return cancelled
    },
    [resource],
  )

  const duplicate = useCallback(
    async (eventId: string) => {
      const copy = await api.post<PlayEventAdminDto>(`/api/admin/events/${eventId}/duplicate`, {})
      toast.success(
        'Copied to the next window',
        `"${copy.eventKey}" starts when this one ends. It is inactive until you publish it.`,
      )
      await resource.reload()
      return copy
    },
    [resource],
  )

  return { ...resource, events: resource.data, create, update, cancel, duplicate }
}

/** What one event paid out, for reconciliation. */
export function useEventAwards(eventId: string | null) {
  const resource = useResourceList<EventAwardDto>(eventId ? `/api/admin/events/${eventId}/awards` : null)

  return { ...resource, awards: resource.data }
}

export function blankEvent(gameId: string, modeId: string): SavePlayEventRequest {
  const start = new Date()
  start.setUTCMinutes(0, 0, 0)

  const end = new Date(start)
  end.setUTCDate(end.getUTCDate() + 7)

  return {
    eventKey: '',
    gameId,
    modeId,
    worldKey: null,
    grantsWorldForDuration: true,
    metric: 'CORRECT_ANSWERS',
    aggregation: 'sum',
    startsAtUtc: start.toISOString(),
    endsAtUtc: end.toISOString(),
    prizeCohort: 'all',
    maxEntriesPerDay: null,
    maxEntriesTotal: null,
    minGradeOrder: 0,
    maxGradeOrder: 0,
    minLevel: 0,
    entryProductId: null,
    claimWindowDays: 30,
    economyProfileId: null,
    bannerAddress: null,
    accentColor: null,
    sortOrder: 0,

    // Created switched off. An event that went live the instant it was saved would publish a
    // competition nobody had read through, prize table included.
    isActive: false,
    translations: [],
    prizeTiers: [],
  }
}

export function eventToRequest(event: PlayEventAdminDto): SavePlayEventRequest {
  return {
    eventKey: event.eventKey,
    gameId: event.gameId,
    modeId: event.modeId,
    worldKey: event.worldKey,
    grantsWorldForDuration: event.grantsWorldForDuration,
    metric: event.metric,

    // Not returned on the admin read — the board owns it and it cannot change once the event
    // exists. Sent back as the shape the request needs.
    aggregation: 'sum',
    startsAtUtc: event.startsAtUtc,
    endsAtUtc: event.endsAtUtc,
    prizeCohort: event.prizeCohort.toLowerCase(),
    maxEntriesPerDay: event.maxEntriesPerDay,
    maxEntriesTotal: event.maxEntriesTotal,
    minGradeOrder: event.minGradeOrder,
    maxGradeOrder: event.maxGradeOrder,
    minLevel: event.minLevel,
    entryProductId: event.entryProductId,
    claimWindowDays: event.claimWindowDays,
    economyProfileId: event.economyProfileId,
    bannerAddress: event.bannerAddress,
    accentColor: event.accentColor,
    sortOrder: event.sortOrder,
    isActive: event.isActive,
    translations: event.translations.map((t) => ({ ...t })),
    prizeTiers: event.prizeTiers.map((tier) => ({
      tierId: tier.tierId,
      fromRank: tier.fromRank,
      toRank: tier.toRank,
      kind: tier.kind,
      grants: tier.grants.map((g) => ({ ...g })),
      declaredValueMinor: tier.declaredValueMinor,
      valueCurrencyCode: tier.valueCurrencyCode,
      quantity: tier.quantity,
      sortOrder: tier.sortOrder,
      translations: tier.translations.map((t) => ({ ...t })),
    })),
  }
}

// ---------------------------------------------------------------------------
// Prize claims
// ---------------------------------------------------------------------------

export function usePrizeClaims(state: string | null) {
  const resource = useResourceList<PrizeClaimAdminDto>(
    state ? `/api/admin/prize-claims?state=${state}` : '/api/admin/prize-claims',
  )

  const update = useCallback(
    async (claimId: string, request: UpdatePrizeClaimRequest) => {
      const updated = await api.put<PrizeClaimAdminDto>(`/api/admin/prize-claims/${claimId}`, request)
      resource.set((rows) => rows.map((c) => (c.claimId === claimId ? updated : c)))
      toast.success('Claim updated', `Now ${updated.state.replace('_', ' ').toLowerCase()}.`)
      return updated
    },
    [resource],
  )

  return { ...resource, claims: resource.data, update }
}
