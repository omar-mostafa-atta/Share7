import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Boxes, Plus, RefreshCw, Trash2 } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox } from '../components/ui/bits'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input } from '../components/ui/form'
import { TranslationsEditor } from '../components/ui/Translations'
import { Modal } from '../components/ui/Modal'
import { useProductKinds } from '../features/shop/data'
import { toWire } from '../lib/format'
import { listVariants } from '../components/ui/motion'
import type { CreateProductKindRequest, ProductKindDto } from '../types/api'

// ===========================================================================
// Product kinds
//
// The category a product belongs to. Small table, one subtlety worth surfacing:
// the server derives a wire `kind` from the display name — "Content Pack"
// becomes CONTENT_PACK — and *that* is what Unity switches on. Renaming a kind
// therefore changes the contract the client is coded against, which is why the
// editor shows the derived value live rather than leaving it invisible.
// ===========================================================================

export function ProductKinds() {
  const { kinds, loading, refreshing, reload, create, update, remove } = useProductKinds()

  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<ProductKindDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<ProductKindDto | null>(null)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()
    if (!term) return kinds

    return kinds.filter((k) =>
      [k.name, k.kind, ...k.translations.map((t) => t.name)].join(' ').toLowerCase().includes(term),
    )
  }, [kinds, search])

  const columns = useMemo<Column<ProductKindDto>[]>(
    () => [
      { key: 'name', header: 'Name', sort: (k) => k.name, render: (k) => <strong>{k.name}</strong> },
      {
        key: 'wire',
        header: 'Wire value',
        sort: (k) => k.kind,
        render: (k) => <code className="s7-key">{k.kind}</code>,
      },
      {
        key: 'products',
        header: 'Products',
        numeric: true,
        sort: (k) => k.productCount,
        render: (k) =>
          k.productCount ? k.productCount.toLocaleString() : <span className="s7-muted">none</span>,
      },
      {
        key: 'langs',
        header: 'Translations',
        numeric: true,
        sort: (k) => k.translations.filter((t) => t.name?.trim()).length,
        render: (k) => <Badge tone="muted">{k.translations.filter((t) => t.name?.trim()).length}</Badge>,
      },
      { key: 'id', header: 'Id', render: (k) => <CopyId id={k.productKindId} label="productKindId" /> },
    ],
    [],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Boxes size={22} />}
        title="Product Kinds"
        subtitle="Categories for products. The server derives a wire value from the name, and the client switches on that value — so a rename is a contract change."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New kind
          </Button>
        }
      />

      <Card>
        <CardHeader
          icon={<Boxes size={16} />}
          title={`${rows.length} of ${kinds.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search kinds…" />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(k) => k.productKindId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.productKindId ?? null}
            empty={kinds.length ? 'No kind matches that search.' : 'No product kinds yet.'}
          />
        </CardBody>
      </Card>

      <KindEditor
        key={editing?.productKindId ?? (creating ? 'new' : 'closed')}
        kind={editing}
        open={!!editing || creating}
        onClose={() => {
          setEditing(null)
          setCreating(false)
        }}
        onSave={async (request) => {
          if (editing) await update(editing.productKindId, request)
          else await create(request)
          setEditing(null)
          setCreating(false)
        }}
        onDelete={editing ? () => setConfirmDelete(editing) : undefined}
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
              disabled={!!confirmDelete?.productCount}
              onClick={async () => {
                if (!confirmDelete) return
                await remove(confirmDelete)
                setConfirmDelete(null)
                setEditing(null)
              }}
            >
              Delete kind
            </Button>
          </>
        }
      >
        {confirmDelete?.productCount ? (
          <Note tone="danger">
            {confirmDelete.productCount.toLocaleString()} product
            {confirmDelete.productCount === 1 ? '' : 's'} still use this kind. Move them to another
            kind first — the API will refuse this delete.
          </Note>
        ) : (
          <Note>No products use this kind, so nothing else is affected.</Note>
        )}
      </Modal>
    </motion.div>
  )
}

function KindEditor({
  kind,
  open,
  onClose,
  onSave,
  onDelete,
}: {
  kind: ProductKindDto | null
  open: boolean
  onClose: () => void
  onSave: (request: CreateProductKindRequest) => Promise<void>
  onDelete?: () => void
}) {
  const isNew = !kind

  const [form, setForm] = useState<CreateProductKindRequest>(() => ({
    name: kind?.name ?? '',
    translations:
      kind?.translations.map((t) => ({
        langId: t.langId,
        name: t.name,
        description: t.description,
      })) ?? [],
  }))

  const [saving, setSaving] = useState(false)

  // Shown live so the consequence of a rename is visible while typing, not
  // discovered when a client stops recognising the category.
  const derived = toWire(form.name)
  const renaming = !isNew && kind && derived !== kind.kind

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New product kind' : kind.name}
      subtitle={isNew ? undefined : `${kind.productCount} product${kind.productCount === 1 ? '' : 's'}`}
      footer={
        <>
          <Button
            loading={saving}
            disabled={!form.name.trim()}
            onClick={async () => {
              setSaving(true)
              try {
                await onSave(form)
              } finally {
                setSaving(false)
              }
            }}
          >
            {isNew ? 'Create kind' : 'Save changes'}
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
        <Field
          label="Name"
          hint={
            <>
              The client receives <code className="s7-key">{derived || '—'}</code>
            </>
          }
        >
          <Input
            value={form.name}
            onChange={(e) => setForm((c) => ({ ...c, name: e.target.value }))}
            placeholder="Cosmetic"
          />
        </Field>

        {renaming ? (
          <Note tone="warning">
            The wire value changes from <code className="s7-key">{kind.kind}</code> to{' '}
            <code className="s7-key">{derived}</code>. Any client branching on the old value stops
            recognising these products.
          </Note>
        ) : null}

        <div>
          <h3 className="s7-subhead">Display names</h3>
          <TranslationsEditor
            value={form.translations}
            onChange={(rows) =>
              setForm((c) => ({
                ...c,
                translations: rows.map((r) => ({
                  langId: r.langId,
                  name: r.name,
                  description: r.description ?? null,
                })),
              }))
            }
          />
        </div>
      </div>
    </Drawer>
  )
}
