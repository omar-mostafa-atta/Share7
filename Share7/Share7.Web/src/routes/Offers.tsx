import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Plus, RefreshCw, Tag, Trash2, TicketPercent } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import { useOffers } from '../features/offers/data'
import { useProducts } from '../features/shop/data'
import { useCurrencies } from '../features/currencies/data'
import { formatDateTime, fromLocalInput, toLocalInput } from '../lib/time'
import { listVariants } from '../components/ui/motion'
import type { AdminOfferDto, CreateOfferRequest } from '../types/api'

// ===========================================================================
// Offers
//
// A priced bundle of products.
//
// The API has no update endpoint — GET, POST and DELETE only. That is not an
// omission this console papers over: an offer that has been purchased is a
// historical price, and editing it in place would rewrite what people paid.
// So editing is genuinely "create the replacement, retire the old one", and
// the UI says exactly that instead of offering a Save button that 404s.
// ===========================================================================

type Filter = 'all' | 'live' | 'expired'

export function Offers() {
  const { offers, loading, refreshing, reload, create, remove } = useOffers()
  const { products } = useProducts()
  const { currencies } = useCurrencies()

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [viewing, setViewing] = useState<AdminOfferDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<AdminOfferDto | null>(null)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()

    return offers.filter((offer) => {
      const live = offer.availability === 'Available' && !offer.expired

      if (filter === 'live' && !live) return false
      if (filter === 'expired' && !offer.expired) return false
      if (!term) return true

      return [offer.name, offer.currency, offer.badgeKey ?? '', ...offer.products.map((p) => p.key)]
        .join(' ')
        .toLowerCase()
        .includes(term)
    })
  }, [offers, search, filter])

  const live = offers.filter((o) => o.availability === 'Available' && !o.expired)
  const emptyOffers = offers.filter((o) => !o.products.length)

  const columns = useMemo<Column<AdminOfferDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Offer',
        sort: (o) => o.name,
        render: (o) => (
          <div>
            <div style={{ fontWeight: 600 }}>
              {o.name}
              {o.badgeKey ? <Badge tone="brand">{o.badgeKey}</Badge> : null}
            </div>
            <div className="s7-muted" style={{ fontSize: '0.72rem' }}>
              {o.products.length
                ? o.products.map((p) => p.key).join(', ')
                : 'contains no products'}
            </div>
          </div>
        ),
      },
      {
        key: 'price',
        header: 'Price',
        numeric: true,
        sort: (o) => o.price,
        render: (o) => (
          <span>
            {o.originalPrice && o.originalPrice > o.price ? (
              <span className="s7-muted" style={{ textDecoration: 'line-through', marginInlineEnd: 4 }}>
                {o.originalPrice.toLocaleString()}
              </span>
            ) : null}
            <strong>{o.price.toLocaleString()}</strong>{' '}
            <span className="s7-muted" style={{ fontSize: '0.75rem' }}>
              {o.currency}
            </span>
          </span>
        ),
      },
      {
        key: 'sold',
        header: 'Purchases',
        numeric: true,
        sort: (o) => o.purchaseCount,
        render: (o) => o.purchaseCount.toLocaleString(),
      },
      {
        key: 'limit',
        header: 'Limit',
        numeric: true,
        sort: (o) => o.purchaseLimit,
        render: (o) =>
          o.purchaseLimit == null ? <span className="s7-muted">none</span> : `${o.purchaseLimit} per player`,
      },
      {
        key: 'expires',
        header: 'Expires',
        sort: (o) => o.expiresAtUtc ?? '',
        render: (o) =>
          !o.expiresAtUtc ? (
            <span className="s7-muted">never</span>
          ) : (
            <span className="s7-muted" style={{ fontSize: '0.78rem' }}>
              {formatDateTime(o.expiresAtUtc)}
            </span>
          ),
      },
      {
        key: 'state',
        header: 'State',
        sort: (o) => (o.expired ? 0 : o.availability === 'Available' ? 2 : 1),
        render: (o) =>
          o.expired ? (
            <Badge tone="danger">Expired</Badge>
          ) : o.availability === 'Available' ? (
            <Badge tone="success">On sale</Badge>
          ) : (
            <Badge tone="muted">{o.availability}</Badge>
          ),
      },
      { key: 'id', header: 'Id', render: (o) => <CopyId id={o.offerId} label="offerId" /> },
    ],
    [],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Tag size={22} />}
        title="Offers"
        subtitle="Priced bundles of products. Offers cannot be edited after creation — a purchased offer is a historical price, so changing one would rewrite what people paid."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New offer
          </Button>
        }
      />

      <StatRow>
        <Stat icon={<TicketPercent size={13} />} label="On sale" value={live.length} sub={`of ${offers.length} total`} tone="success" />
        <Stat
          icon={<Tag size={13} />}
          label="Purchases"
          value={offers.reduce((sum, o) => sum + o.purchaseCount, 0)}
          sub="All time, across every offer"
          tone="brand"
        />
        <Stat
          icon={<Trash2 size={13} />}
          label="Empty bundles"
          value={emptyOffers.length}
          sub="Contain no products"
          tone={emptyOffers.length ? 'danger' : 'success'}
        />
      </StatRow>

      {emptyOffers.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="danger">
            <strong>{emptyOffers.length}</strong> offer{emptyOffers.length === 1 ? '' : 's'} contain
            no products. A player can pay for {emptyOffers.length === 1 ? 'it' : 'them'} and receive
            nothing.
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<Tag size={16} />}
          title={`${rows.length} of ${offers.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search offer or product…" />
            <Segmented
              layoutId="offers-filter"
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: 'All' },
                { value: 'live', label: 'On sale' },
                { value: 'expired', label: 'Expired' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(o) => o.offerId}
            loading={loading}
            onRowClick={setViewing}
            selectedId={viewing?.offerId ?? null}
            empty={offers.length ? 'No offer matches that filter.' : 'No offers yet.'}
          />
        </CardBody>
      </Card>

      <OfferDetail
        offer={viewing}
        onClose={() => setViewing(null)}
        onDelete={() => {
          if (viewing) setConfirmDelete(viewing)
        }}
      />

      <OfferCreator
        key={creating ? 'new' : 'closed'}
        open={creating}
        products={products.map((p) => ({ id: p.productId, key: p.key, kind: p.kindName, grants: p.grants.length }))}
        currencies={currencies.map((c) => ({ id: c.currencyId, key: c.key, enabled: c.enabled }))}
        onClose={() => setCreating(false)}
        onCreate={async (request) => {
          await create(request)
          setCreating(false)
        }}
      />

      <Modal
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        icon={<Trash2 size={18} />}
        title={`Delete "${confirmDelete?.name}"?`}
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
                setViewing(null)
              }}
            >
              Delete offer
            </Button>
          </>
        }
      >
        <Note tone="danger">
          {confirmDelete?.purchaseCount
            ? `This offer has been bought ${confirmDelete.purchaseCount.toLocaleString()} time${confirmDelete.purchaseCount === 1 ? '' : 's'}. Players keep what they bought, but the purchase history loses the offer it points at.`
            : 'Nobody has bought this offer.'}
        </Note>
      </Modal>
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Read-only detail
// ---------------------------------------------------------------------------

function OfferDetail({
  offer,
  onClose,
  onDelete,
}: {
  offer: AdminOfferDto | null
  onClose: () => void
  onDelete: () => void
}) {
  return (
    <Drawer
      open={!!offer}
      onClose={onClose}
      title={offer?.name ?? 'Offer'}
      subtitle={offer ? `${offer.price.toLocaleString()} ${offer.currency}` : undefined}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            Close
          </Button>
          <Button variant="danger" onClick={onDelete} style={{ marginInlineStart: 'auto' }}>
            <Trash2 size={15} /> Delete
          </Button>
        </>
      }
    >
      {!offer ? null : (
        <div className="s7-stack">
          <Note>
            Offers are immutable. To change a price, a bundle or a window, create a replacement and
            delete this one — the API deliberately exposes no update.
          </Note>

          <dl className="s7-dl">
            <dt>Description</dt>
            <dd>{offer.description || <span className="s7-muted">none</span>}</dd>
            <dt>Price</dt>
            <dd>
              {offer.price.toLocaleString()} {offer.currency}
              {offer.originalPrice && offer.originalPrice > offer.price
                ? ` (was ${offer.originalPrice.toLocaleString()})`
                : ''}
            </dd>
            <dt>Availability</dt>
            <dd>{offer.availability}</dd>
            <dt>Purchase limit</dt>
            <dd>{offer.purchaseLimit == null ? 'unlimited' : `${offer.purchaseLimit} per player`}</dd>
            <dt>Purchases</dt>
            <dd>{offer.purchaseCount.toLocaleString()}</dd>
            <dt>Expires</dt>
            <dd>{offer.expiresAtUtc ? formatDateTime(offer.expiresAtUtc) : 'never'}</dd>
            <dt>Sort order</dt>
            <dd>{offer.sortOrder}</dd>
            <dt>Created</dt>
            <dd>{formatDateTime(offer.createdAtUtc)}</dd>
          </dl>

          <div>
            <h3 className="s7-subhead">Contents</h3>
            {!offer.products.length ? (
              <Note tone="danger">This offer contains no products.</Note>
            ) : (
              <div className="s7-dt-wrap">
                <table className="s7-dt">
                  <thead>
                    <tr>
                      <th>Product</th>
                      <th>Kind</th>
                      <th>Hands over</th>
                    </tr>
                  </thead>
                  <tbody>
                    {offer.products.map((p) => (
                      <tr key={p.productId}>
                        <td>
                          <div style={{ fontWeight: 600 }}>{p.name || p.key}</div>
                          <code className="s7-key">{p.key}</code>
                        </td>
                        <td>
                          <Badge tone="muted">{p.kind}</Badge>
                        </td>
                        <td>
                          {!p.grants.length ? (
                            <Badge tone="danger">nothing</Badge>
                          ) : (
                            <span className="s7-inline">
                              {p.grants.map((g, i) => (
                                <Badge key={i} tone="info">
                                  {g.quantity} × {g.reference}
                                </Badge>
                              ))}
                            </span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </Drawer>
  )
}

// ---------------------------------------------------------------------------
// Creator
// ---------------------------------------------------------------------------

function OfferCreator({
  open,
  products,
  currencies,
  onClose,
  onCreate,
}: {
  open: boolean
  products: { id: string; key: string; kind: string; grants: number }[]
  currencies: { id: string; key: string; enabled: boolean }[]
  onClose: () => void
  onCreate: (request: CreateOfferRequest) => Promise<void>
}) {
  const [form, setForm] = useState<CreateOfferRequest>({
    translations: [],
    currencyId: '',
    price: 0,
    originalPrice: null,
    availability: 'AVAILABLE',
    purchaseLimit: null,
    expiresAtUtc: null,
    sortOrder: 0,
    badgeKey: null,
    productIds: [],
  })

  const [saving, setSaving] = useState(false)

  const blocked = !form.currencyId || !form.productIds.length || form.price < 0

  function patch(next: Partial<CreateOfferRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  function toggleProduct(id: string) {
    setForm((current) => ({
      ...current,
      productIds: current.productIds.includes(id)
        ? current.productIds.filter((p) => p !== id)
        : [...current.productIds, id],
    }))
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title="New offer"
      subtitle="Immutable once created — check the price and the bundle before saving."
      footer={
        <>
          <Button
            loading={saving}
            disabled={blocked}
            onClick={async () => {
              setSaving(true)
              try {
                await onCreate(form)
              } finally {
                setSaving(false)
              }
            }}
          >
            Create offer
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        <div className="s7-form-grid-2">
          <Field label="Currency">
            <Select value={form.currencyId} onChange={(e) => patch({ currencyId: e.target.value })}>
              <option value="">Choose…</option>
              {currencies.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.key}
                  {!c.enabled ? ' — retired' : ''}
                </option>
              ))}
            </Select>
          </Field>

          <Field label="Price">
            <Input
              type="number"
              min={0}
              value={form.price}
              onChange={(e) => patch({ price: Number(e.target.value) || 0 })}
            />
          </Field>
        </div>

        <Field
          label="Original price"
          hint="Shown struck through beside the price. Leave empty for no discount framing."
        >
          <Input
            type="number"
            min={0}
            value={form.originalPrice ?? ''}
            onChange={(e) =>
              patch({ originalPrice: e.target.value === '' ? null : Number(e.target.value) })
            }
            placeholder="(none)"
          />
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Availability">
            <Select value={form.availability} onChange={(e) => patch({ availability: e.target.value })}>
              <option value="AVAILABLE">Available</option>
              <option value="DISABLED">Disabled</option>
            </Select>
          </Field>

          <Field label="Purchase limit" hint="Per player. Empty for unlimited.">
            <Input
              type="number"
              min={1}
              value={form.purchaseLimit ?? ''}
              onChange={(e) =>
                patch({ purchaseLimit: e.target.value === '' ? null : Number(e.target.value) })
              }
              placeholder="unlimited"
            />
          </Field>
        </div>

        <div className="s7-form-grid-2">
          <Field label="Expires" hint="Local time, sent as UTC. Empty never expires.">
            <Input
              type="datetime-local"
              value={toLocalInput(form.expiresAtUtc)}
              onChange={(e) => patch({ expiresAtUtc: fromLocalInput(e.target.value) })}
            />
          </Field>

          <Field label="Sort order">
            <Input
              type="number"
              value={form.sortOrder}
              onChange={(e) => patch({ sortOrder: Number(e.target.value) || 0 })}
            />
          </Field>
        </div>

        <Field label="Badge key" hint="Optional. The client resolves it to a ribbon or tag.">
          <Input
            mono
            value={form.badgeKey ?? ''}
            onChange={(e) => patch({ badgeKey: e.target.value || null })}
            placeholder="(none)"
          />
        </Field>

        <div>
          <h3 className="s7-subhead">Products in this bundle</h3>
          {!products.length ? (
            <Note tone="warning">No products exist yet — create one before building an offer.</Note>
          ) : (
            <div className="s7-dt-wrap" style={{ maxHeight: '18rem' }}>
              <table className="s7-dt">
                <tbody>
                  {products.map((product) => (
                    <tr
                      key={product.id}
                      onClick={() => toggleProduct(product.id)}
                      className={form.productIds.includes(product.id) ? 'is-selected' : undefined}
                      style={{ cursor: 'pointer' }}
                    >
                      <td style={{ width: '2.5rem' }}>
                        <input
                          type="checkbox"
                          checked={form.productIds.includes(product.id)}
                          onChange={() => toggleProduct(product.id)}
                          onClick={(e) => e.stopPropagation()}
                        />
                      </td>
                      <td>
                        <code className="s7-key">{product.key}</code>
                      </td>
                      <td>
                        <Badge tone="muted">{product.kind}</Badge>
                      </td>
                      <td className="s7-num">
                        {product.grants ? (
                          <span className="s7-muted">
                            {product.grants} grant{product.grants === 1 ? '' : 's'}
                          </span>
                        ) : (
                          <Badge tone="danger">no grants</Badge>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <div>
          <h3 className="s7-subhead">Name and description</h3>
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
      </div>
    </Drawer>
  )
}
