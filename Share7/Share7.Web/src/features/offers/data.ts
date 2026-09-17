import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type { AdminOfferDto, CreateOfferRequest } from '../../types/api'

// ===========================================================================
// Offers — data access
//
// Note what is missing: there is no `update`.
//
// AdminOffersController exposes GET, GET/{id}, POST and DELETE — no PUT. That
// is a deliberate API design rather than an oversight: an offer that has been
// purchased is the record of a price someone paid, and editing it in place
// would retroactively change that. Replacing an offer means creating the new
// one and deleting the old.
//
// This file does not invent a client-side update that stitches those two calls
// together. A create-then-delete pair is not atomic, and a helper that looked
// like `update()` would hide the moment where both offers are live at once.
// ===========================================================================

export function useOffers() {
  const resource = useResourceList<AdminOfferDto>('/api/admin/offers')

  const create = useCallback(
    async (request: CreateOfferRequest) => {
      const created = await api.post<AdminOfferDto>('/api/admin/offers', request)
      toast.success('Offer created', `"${created.name || 'Untitled'}" is now in the catalogue.`)
      await resource.reload()
      return created
    },
    [resource],
  )

  const remove = useCallback(
    async (offer: AdminOfferDto) => {
      await api.del(`/api/admin/offers/${offer.offerId}`)
      resource.set((rows) => rows.filter((o) => o.offerId !== offer.offerId))
      toast.success('Offer deleted', `"${offer.name}" is no longer sold.`)
    },
    [resource],
  )

  return { ...resource, offers: resource.data, create, remove }
}
