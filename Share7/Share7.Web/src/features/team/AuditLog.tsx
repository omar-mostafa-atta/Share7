import { useMemo, useState } from 'react'
import { Download, RefreshCw, ScrollText } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../../components/ui/primitives'
import { CopyId, Def, DefList, Pagination, SearchBox } from '../../components/ui/bits'
import { DataTable, type Column } from '../../components/ui/DataTable'
import { Input, Select } from '../../components/ui/form'
import { api } from '../../lib/client'
import { roleInfo } from '../../lib/access'
import { useResource } from '../../lib/resource'
import { formatDateTime } from '../../lib/time'
import type { AuditEvent, AuditFacets, AuditPage } from './data'

// ===========================================================================
// The audit log — what everybody did, newest first
//
// The same ledger as the team: filter above, one row per act, and a row opens
// in place to show everything the trail recorded about it. Exported as CSV
// exactly as filtered, because the question someone brings here is usually
// "what did this person do last week", and the answer goes in an email.
// ===========================================================================

const PAGE_SIZE = 50

export function AuditLog() {
  const [actor, setActor] = useState('')
  const [area, setArea] = useState('')
  const [action, setAction] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZE)
  const [open, setOpen] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  const facets = useResource<AuditFacets>('/api/admin/audit/facets', { areas: [], actions: [], actors: [] })

  const params = useMemo(() => {
    const p = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
    if (actor) p.set('actorUserId', actor)
    if (area) p.set('area', area)
    if (action) p.set('action', action)
    // The day as the admin means it: from the start of the first day to the end of the last.
    if (from) p.set('fromUtc', new Date(`${from}T00:00:00`).toISOString())
    if (to) p.set('toUtc', new Date(`${to}T23:59:59`).toISOString())
    if (search.trim()) p.set('search', search.trim())
    return p
  }, [actor, area, action, from, to, search, page, pageSize])

  const trail = useResource<AuditPage>(`/api/admin/audit?${params}`, { items: [], page: 1, pageSize: PAGE_SIZE, total: 0 })

  const reset = <T,>(set: (v: T) => void) => (value: T) => {
    set(value)
    setPage(1)
  }

  const columns = useMemo<Column<AuditEvent>[]>(
    () => [
      {
        key: 'when',
        header: 'When',
        width: '11rem',
        render: (e) => <time className="s7-muted" style={{ fontSize: '0.78rem' }}>{formatDateTime(e.occurredAtUtc)}</time>,
      },
      {
        key: 'who',
        header: 'Who',
        render: (e) =>
          e.actor ? (
            <span className="s7-stack-tight">
              <span style={{ fontWeight: 600 }}>{e.actor.name}</span>
              <span className="s7-inline">
                {e.actorRoles.map((r) => (
                  <Badge key={r} tone={roleInfo(r).tone}>
                    {roleInfo(r).label}
                  </Badge>
                ))}
              </span>
            </span>
          ) : (
            <span className="s7-muted">The platform</span>
          ),
      },
      {
        key: 'what',
        header: 'What',
        render: (e) => (
          <span className="s7-stack-tight">
            <span>{e.summary}</span>
            <span className="s7-hint">{e.action}</span>
          </span>
        ),
      },
      { key: 'area', header: 'Area', render: (e) => <Badge tone="muted">{e.area}</Badge> },
    ],
    [],
  )

  async function exportCsv() {
    setExporting(true)
    try {
      const p = new URLSearchParams(params)
      p.delete('page')
      p.delete('pageSize')
      await api.download(`/api/admin/audit/export?${p}`, `share7-audit-${new Date().toISOString().slice(0, 10)}.csv`)
    } finally {
      setExporting(false)
    }
  }

  const filtered = actor || area || action || from || to || search.trim()

  return (
    <Card>
      <CardHeader
        icon={<ScrollText size={16} />}
        title="Audit log"
        actions={
          <>
            <Button variant="ghost" loading={exporting} onClick={() => void exportCsv()}>
              {exporting ? null : <Download size={15} />} Export CSV
            </Button>
            <IconButton label="Refresh" busy={trail.refreshing} onClick={() => void trail.reload()}>
              <RefreshCw size={15} />
            </IconButton>
          </>
        }
      />
      <CardBody>
        <div className="s7-bar s7-bar-wrap">
          <SearchBox value={search} onChange={reset(setSearch)} placeholder="Search what was done…" />
          <Select value={actor} onChange={(e) => reset(setActor)(e.target.value)} aria-label="Person" style={{ maxWidth: '12rem' }}>
            <option value="">Anybody</option>
            {facets.data.actors.map((a) => (
              <option key={a.userId} value={a.userId}>
                {a.name}
              </option>
            ))}
          </Select>
          <Select value={area} onChange={(e) => reset(setArea)(e.target.value)} aria-label="Area" style={{ maxWidth: '10rem' }}>
            <option value="">Any area</option>
            {facets.data.areas.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </Select>
          <Select value={action} onChange={(e) => reset(setAction)(e.target.value)} aria-label="Action" style={{ maxWidth: '14rem' }}>
            <option value="">Any action</option>
            {facets.data.actions.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </Select>
          <label className="s7-inline s7-hint">
            From
            <Input type="date" value={from} onChange={(e) => reset(setFrom)(e.target.value)} style={{ maxWidth: '10rem' }} />
          </label>
          <label className="s7-inline s7-hint">
            to
            <Input type="date" value={to} onChange={(e) => reset(setTo)(e.target.value)} style={{ maxWidth: '10rem' }} />
          </label>
        </div>

        <DataTable
          rows={trail.data.items}
          columns={columns}
          getId={(e) => String(e.sequence)}
          loading={trail.loading}
          paginate={false}
          selectedId={open}
          onRowClick={(e) => setOpen(open === String(e.sequence) ? null : String(e.sequence))}
          expanded={(e) => <AuditDetail event={e} />}
          empty={filtered ? 'Nothing recorded matches that.' : 'Nothing has been recorded yet.'}
        />

        <Pagination
          page={page}
          pageSize={pageSize}
          total={trail.data.total}
          onPageChange={setPage}
          onPageSizeChange={(size) => {
            setPageSize(size)
            setPage(1)
          }}
          noun="entries"
        />
      </CardBody>
    </Card>
  )
}

/** Everything the trail kept about one act. */
function AuditDetail({ event }: { event: AuditEvent }) {
  const data = useMemo(() => {
    if (!event.dataJson) return null
    try {
      return JSON.stringify(JSON.parse(event.dataJson), null, 2)
    } catch {
      return event.dataJson
    }
  }, [event.dataJson])

  return (
    <div className="s7-record" style={{ gap: '0.75rem' }}>
      <div className="s7-record-grid">
        <DefList>
          <Def label="Recorded">
            {formatDateTime(event.occurredAtUtc)} · entry {event.sequence}
          </Def>
          <Def label="About">
            {event.targetType ? (
              <span className="s7-inline">
                {event.targetType} {event.targetId ? <CopyId id={event.targetId} /> : null}
              </span>
            ) : null}
          </Def>
          <Def label="From">{[event.ipAddress, event.device].filter(Boolean).join(' · ') || null}</Def>
        </DefList>
        {data ? <pre className="s7-code-block">{data}</pre> : <span className="s7-hint">Nothing more was recorded.</span>}
      </div>
    </div>
  )
}
