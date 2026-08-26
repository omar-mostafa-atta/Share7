import { useCallback } from 'react'
import { api } from '../../lib/client'
import { useResourceList } from '../../lib/resource'
import { toast } from '../../store/toast'
import type {
  AdminProductDto,
  AdminProductGrantDto,
  CreateProductGrantRequest,
  CreateProductKindRequest,
  CreateProductRequest,
  ProductKindDto,
  UpdateProductGrantRequest,
  UpdateProductKindRequest,
  UpdateProductRequest,
} from '../../types/api'

// ===========================================================================
// Shop — data access
//
// Three tables, one hierarchy:
//
//   ProductKind   what sort of thing this is      ("Cosmetic")
//     └ Product   a sellable thing                ("hat_pirate")
//         └ Grant what owning it actually hands over
//
// A product with no grants is the trap. It sells, it appears in the player's
// inventory, and it gives them nothing — so both the product table and the
// editor call that out rather than treating an empty grant list as normal.
// ===========================================================================

// ---------------------------------------------------------------------------
// Products
// ---------------------------------------------------------------------------

export function useProducts() {
  const resource = useResourceList<AdminProductDto>('/api/admin/products')

  const create = useCallback(
    async (request: CreateProductRequest) => {
      await api.post<AdminProductDto>('/api/admin/products', request)
      toast.success('Product created', `"${request.key}" is in the catalogue.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (productId: string, request: UpdateProductRequest) => {
      const updated = await api.put<AdminProductDto>(`/api/admin/products/${productId}`, request)
      resource.set((rows) => rows.map((p) => (p.productId === productId ? updated : p)))

      toast.success(
        request.active ? 'Product updated' : 'Product deactivated',
        request.active
          ? `"${updated.key}" saved.`
          : `"${updated.key}" can no longer be sold. Players who own it keep it.`,
      )

      return updated
    },
    [resource],
  )

  const remove = useCallback(
    async (product: AdminProductDto) => {
      await api.del(`/api/admin/products/${product.productId}`)
      resource.set((rows) => rows.filter((p) => p.productId !== product.productId))
      toast.success('Product deleted', `"${product.key}" is gone.`)
    },
    [resource],
  )

  return { ...resource, products: resource.data, create, update, remove }
}

// ---------------------------------------------------------------------------
// Product kinds
// ---------------------------------------------------------------------------

export function useProductKinds() {
  const resource = useResourceList<ProductKindDto>('/api/admin/product-kinds')

  const create = useCallback(
    async (request: CreateProductKindRequest) => {
      await api.post<ProductKindDto>('/api/admin/product-kinds', request)
      toast.success('Product kind created', `"${request.name}" is available.`)
      await resource.reload()
    },
    [resource],
  )

  const update = useCallback(
    async (productKindId: string, request: UpdateProductKindRequest) => {
      const updated = await api.put<ProductKindDto>(
        `/api/admin/product-kinds/${productKindId}`,
        request,
      )
      resource.set((rows) => rows.map((k) => (k.productKindId === productKindId ? updated : k)))
      toast.success('Product kind updated', `"${updated.name}" saved.`)
      return updated
    },
    [resource],
  )

  const remove = useCallback(
    async (kind: ProductKindDto) => {
      await api.del(`/api/admin/product-kinds/${kind.productKindId}`)
      resource.set((rows) => rows.filter((k) => k.productKindId !== kind.productKindId))
      toast.success('Product kind deleted', `"${kind.name}" is gone.`)
    },
    [resource],
  )

  return { ...resource, kinds: resource.data, create, update, remove }
}

// ---------------------------------------------------------------------------
// Grants
//
// What a product actually hands over. Managed through their own endpoints
// rather than as a nested collection on the product, which is why they reload
// the product list after a write — `AdminProductDto.grants` is a projection of
// this table and goes stale the moment one changes.
// ---------------------------------------------------------------------------

export function useProductGrants(onChanged?: () => void) {
  const resource = useResourceList<AdminProductGrantDto>('/api/admin/product-grants')

  const create = useCallback(
    async (request: CreateProductGrantRequest) => {
      await api.post<AdminProductGrantDto>('/api/admin/product-grants', request)
      toast.success('Grant added', `The product now hands over ${request.quantity} × ${request.reference}.`)
      await resource.reload()
      onChanged?.()
    },
    [resource, onChanged],
  )

  const update = useCallback(
    async (grantId: string, request: UpdateProductGrantRequest) => {
      const updated = await api.put<AdminProductGrantDto>(
        `/api/admin/product-grants/${grantId}`,
        request,
      )
      resource.set((rows) => rows.map((g) => (g.grantId === grantId ? updated : g)))
      toast.success('Grant updated', `${request.quantity} × ${request.reference}.`)
      onChanged?.()
      return updated
    },
    [resource, onChanged],
  )

  const remove = useCallback(
    async (grant: AdminProductGrantDto) => {
      await api.del(`/api/admin/product-grants/${grant.grantId}`)
      resource.set((rows) => rows.filter((g) => g.grantId !== grant.grantId))
      toast.success('Grant removed', `${grant.reference} is no longer handed over.`)
      onChanged?.()
    },
    [resource, onChanged],
  )

  return { ...resource, grants: resource.data, create, update, remove }
}

export function blankProduct(): CreateProductRequest {
  return {
    key: '',
    translations: [],
    imageUrl: null,
    productKindId: '',
    active: true,
  }
}
