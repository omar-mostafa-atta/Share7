import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Package, PackageOpen, Plus, RefreshCw, ShoppingBag, Trash2, Users2 } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import { blankProduct, useProductGrants, useProductKinds, useProducts } from '../features/shop/data'
import { KEY_PATTERN, slugify, textFor } from '../lib/format'
import { useLanguages } from '../store/languages'
import { listVariants } from '../components/ui/motion'
import type { AdminProductDto, CreateProductRequest, UpdateProductRequest } from '../types/api'

// ===========================================================================
// Shop — products and what they hand over
//
// A product is a sellable thing; a grant is what owning it actually gives.
// The two are separate tables and separate endpoints, so a product with zero
// grants is perfectly valid to the API and completely useless to a player.
// That case is surfaced everywhere it can be: in the table, in the editor, and
// as a page-level warning.
// ===========================================================================

type Filter = 'all' | 'active' | 'inactive' | 'empty'

export function Shop() {
  const { products, loading, refreshing, reload, create, update, remove } = useProducts()
  const { kinds } = useProductKinds()
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [editing, setEditing] = useState<AdminProductDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<AdminProductDto | null>(null)

  const emptyProducts = products.filter((p) => p.active && !p.grants.length)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()

    return products.filter((product) => {
      if (filter === 'active' && !product.active) return false
      if (filter === 'inactive' && product.active) return false
      if (filter === 'empty' && product.grants.length) return false
      if (!term) return true

      return [product.key, product.kindName, ...product.translations.map((t) => t.name)]
        .join(' ')
        .toLowerCase()
        .includes(term)
    })
  }, [products, search, filter])

  const columns = useMemo<Column<AdminProductDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Product',
        sort: (p) => textFor(p.translations, selectedLangId) || p.key,
        render: (p) => (
          <div>
            <div style={{ fontWeight: 600 }}>{textFor(p.translations, selectedLangId) || p.key}</div>
            <code className="s7-key">{p.key}</code>
          </div>
        ),
      },
      {
        key: 'kind',
        header: 'Kind',
        sort: (p) => p.kindName,
        render: (p) => <Badge tone="muted">{p.kindName}</Badge>,
      },
      {
        key: 'grants',
        header: 'Hands over',
        sort: (p) => p.grants.length,
        render: (p) =>
          !p.grants.length ? (
            <Badge tone="danger">nothing</Badge>
          ) : (
            <span className="s7-inline">
              {p.grants.slice(0, 3).map((g) => (
                <Badge key={g.grantId} tone="info">
                  {g.quantity} × {g.reference}
                </Badge>
              ))}
              {p.grants.length > 3 ? <span className="s7-muted">+{p.grants.length - 3}</span> : null}
            </span>
          ),
      },
      {
        key: 'owners',
        header: 'Owners',
        numeric: true,
        sort: (p) => p.ownerCount,
        render: (p) => (
          <span className="s7-inline" style={{ justifyContent: 'flex-end' }}>
            <Users2 size={13} className="s7-muted" />
            {p.ownerCount.toLocaleString()}
          </span>
        ),
      },
      {
        key: 'active',
        header: 'State',
        sort: (p) => p.active,
        render: (p) => (p.active ? <Badge tone="success">Active</Badge> : <Badge tone="muted">Off</Badge>),
      },
      { key: 'id', header: 'Id', render: (p) => <CopyId id={p.productId} label="productId" /> },
    ],
    [selectedLangId],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<ShoppingBag size={22} />}
        title="Shop"
        subtitle="Products and the grants they hand over. A product is only worth what its grants give — one with none sells fine and delivers nothing."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New product
          </Button>
        }
      />

      {emptyProducts.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="danger">
            <strong>{emptyProducts.length}</strong> active product
            {emptyProducts.length === 1 ? '' : 's'} hand
            {emptyProducts.length === 1 ? 's' : ''} over nothing:{' '}
            {emptyProducts.slice(0, 4).map((p) => p.key).join(', ')}
            {emptyProducts.length > 4 ? ', …' : ''}. A player can buy{' '}
            {emptyProducts.length === 1 ? 'it' : 'them'} and receive no items.
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<Package size={16} />}
          title={`${rows.length} of ${products.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search key, name or kind…" />
            <Segmented
              layoutId="shop-filter"
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: 'All' },
                { value: 'active', label: 'Active' },
                { value: 'inactive', label: 'Off' },
                { value: 'empty', label: 'No grants' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(p) => p.productId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.productId ?? null}
            empty={products.length ? 'No product matches that filter.' : 'No products yet.'}
          />
        </CardBody>
      </Card>

      <ProductEditor
        key={editing?.productId ?? (creating ? 'new' : 'closed')}
        product={editing}
        open={!!editing || creating}
        kinds={kinds.map((k) => ({ id: k.productKindId, name: k.name }))}
        onReloadProducts={reload}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onCreate={async (request) => {
          await create(request)
          setCreating(false)
        }}
        onUpdate={async (request) => {
          if (!editing) return
          await update(editing.productId, request)
          setEditing(null)
        }}
        onDelete={editing ? () => setConfirmDelete(editing) : undefined}
      />

      <Modal
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        icon={<Trash2 size={18} />}
        title={`Delete "${confirmDelete?.key}"?`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setConfirmDelete(null)}>
              Cancel
            </Button>
            <Button
              variant="danger"
              onClick={async () => {
                if (!confirmDelete) return
                await remove(confirmDelete)
                setConfirmDelete(null)
                setEditing(null)
              }}
            >
              Delete product
            </Button>
          </>
        }
      >
        <Note tone="danger">
          {confirmDelete?.ownerCount
            ? `${confirmDelete.ownerCount.toLocaleString()} player${confirmDelete.ownerCount === 1 ? '' : 's'} own this. Deactivating stops it being sold while leaving their entitlements intact — deleting does not.`
            : 'Nobody owns this product yet, so nothing is taken away.'}
        </Note>
      </Modal>
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Editor — product fields plus its grants
// ---------------------------------------------------------------------------

function ProductEditor({
  product,
  open,
  kinds,
  onClose,
  onCreate,
  onUpdate,
  onDelete,
  onReloadProducts,
}: {
  product: AdminProductDto | null
  open: boolean
  kinds: { id: string; name: string }[]
  onClose: () => void
  onCreate: (request: CreateProductRequest) => Promise<void>
  onUpdate: (request: UpdateProductRequest) => Promise<void>
  onDelete?: () => void
  onReloadProducts: () => Promise<void>
}) {
  const isNew = !product

  const [form, setForm] = useState<CreateProductRequest>(() =>
    product
      ? {
          key: product.key,
          translations: product.translations.map((t) => ({
            langId: t.langId,
            name: t.name,
            description: t.description,
          })),
          imageUrl: product.imageUrl,
          productKindId: product.productKindId,
          active: product.active,
        }
      : blankProduct(),
  )

  const [saving, setSaving] = useState(false)

  // Grants are only editable on an existing product: the create endpoint takes
  // no grants, and a grant needs a productId that does not exist yet.
  const { grants, create: addGrant, update: updateGrant, remove: removeGrant } =
    useProductGrants(onReloadProducts)

  const mine = grants.filter((g) => g.productId === product?.productId)

  const [newReference, setNewReference] = useState('')
  const [newQuantity, setNewQuantity] = useState(1)

  const keyError =
    isNew && form.key && !KEY_PATTERN.test(form.key)
      ? 'Lowercase letters, digits and underscores, starting with a letter.'
      : null

  const blocked = !!keyError || !form.key.trim() || !form.productKindId

  function patch(next: Partial<CreateProductRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  async function save() {
    setSaving(true)
    try {
      if (isNew) await onCreate(form)
      else
        await onUpdate({
          translations: form.translations,
          imageUrl: form.imageUrl,
          productKindId: form.productKindId,
          active: form.active,
        })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New product' : form.key}
      subtitle={isNew ? 'The key is permanent — offers and reward rules reference it.' : product?.kindName}
      footer={
        <>
          <Button loading={saving} disabled={blocked} onClick={save}>
            {isNew ? 'Create product' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          {onDelete ? (
            <Button variant="danger" onClick={onDelete} style={{ marginInlineStart: 'auto' }}>
              <Trash2 size={15} /> Delete
            </Button>
          ) : null}
        </>
      }
    >
      <div className="s7-stack">
        <Field label="Key" error={keyError} hint={isNew ? 'Permanent.' : 'Cannot be changed.'}>
          <Input
            mono
            value={form.key}
            disabled={!isNew}
            onChange={(e) => patch({ key: slugify(e.target.value) })}
            placeholder="hat_pirate"
          />
        </Field>

        <Field label="Kind" hint="Groups products for the client's inventory UI.">
          <Select value={form.productKindId} onChange={(e) => patch({ productKindId: e.target.value })}>
            <option value="">Choose a kind…</option>
            {kinds.map((k) => (
              <option key={k.id} value={k.id}>
                {k.name}
              </option>
            ))}
          </Select>
        </Field>

        <Field label="Image URL" hint="Optional. Resolved by the client.">
          <Input
            value={form.imageUrl ?? ''}
            onChange={(e) => patch({ imageUrl: e.target.value || null })}
            placeholder="(none)"
          />
        </Field>

        <Field label="Active">
          <Switch
            checked={form.active}
            onChange={(v) => patch({ active: v })}
            label={form.active ? 'Active — can be sold and granted' : 'Off — existing owners keep it'}
          />
        </Field>

        <div>
          <h3 className="s7-subhead">Names and descriptions</h3>
          <TranslationsEditor
            withDescription
            value={form.translations}
            onChange={(rows) =>
              patch({
                translations: rows.map((r) => ({
                  langId: r.langId,
                  name: r.name,
                  description: r.description ?? null,
                })),
              })
            }
          />
        </div>

        <div>
          <h3 className="s7-subhead">
            <PackageOpen size={15} /> What owning this hands over
          </h3>

          {isNew ? (
            <Note>Save the product first — a grant has to point at a product that exists.</Note>
          ) : !mine.length ? (
            <Note tone="danger">
              This product hands over nothing. A player can own it and receive no items.
            </Note>
          ) : (
            <div className="s7-dt-wrap" style={{ marginBottom: '0.7rem' }}>
              <table className="s7-dt">
                <thead>
                  <tr>
                    <th>Kind</th>
                    <th>Reference</th>
                    <th className="s7-num" style={{ width: '7rem' }}>
                      Quantity
                    </th>
                    <th style={{ width: '3rem' }} />
                  </tr>
                </thead>
                <tbody>
                  {mine.map((grant) => (
                    <tr key={grant.grantId}>
                      <td>
                        <Badge tone="muted">{grant.kind}</Badge>
                      </td>
                      <td>
                        <Input
                          mono
                          defaultValue={grant.reference}
                          onBlur={(e) => {
                            const reference = e.target.value.trim()
                            if (reference && reference !== grant.reference) {
                              void updateGrant(grant.grantId, { reference, quantity: grant.quantity })
                            }
                          }}
                        />
                      </td>
                      <td className="s7-num">
                        <Input
                          type="number"
                          min={1}
                          defaultValue={grant.quantity}
                          onBlur={(e) => {
                            const quantity = Number(e.target.value) || 1
                            if (quantity !== grant.quantity) {
                              void updateGrant(grant.grantId, { reference: grant.reference, quantity })
                            }
                          }}
                          style={{ width: '5.5rem', textAlign: 'right' }}
                        />
                      </td>
                      <td>
                        <button
                          type="button"
                          className="s7-btn s7-btn-ghost s7-btn-icon"
                          aria-label="Remove grant"
                          onClick={() => void removeGrant(grant)}
                        >
                          <Trash2 size={14} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {!isNew ? (
            <div className="s7-bar" style={{ marginBottom: 0 }}>
              <Input
                mono
                value={newReference}
                onChange={(e) => setNewReference(e.target.value)}
                placeholder="reference, e.g. coins or hat_pirate"
                style={{ flex: '1 1 12rem' }}
              />
              <Input
                type="number"
                min={1}
                value={newQuantity}
                onChange={(e) => setNewQuantity(Number(e.target.value) || 1)}
                style={{ width: '6rem' }}
              />
              <Button
                variant="ghost"
                disabled={!newReference.trim()}
                onClick={async () => {
                  if (!product) return
                  await addGrant({
                    productId: product.productId,
                    reference: newReference.trim(),
                    quantity: newQuantity,
                  })
                  setNewReference('')
                  setNewQuantity(1)
                }}
              >
                <Plus size={15} /> Add grant
              </Button>
            </div>
          ) : null}
        </div>
      </div>
    </Drawer>
  )
}
