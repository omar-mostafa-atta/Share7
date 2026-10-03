import { useState } from 'react'
import { Button, Card, CardBody, CardHeader } from '../components/ui/primitives'
import { Note } from '../components/ui/bits'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { DataTable } from '../components/ui/DataTable'
import { api } from '../lib/client'
import { useResourceList } from '../lib/resource'
import { useProducts } from '../features/shop/data'
import { fromLocalInput, toLocalInput } from '../lib/time'
import { toast } from '../store/toast'

interface Content { key: string; kind: string; assetKey: string; fallbackKey: string | null; productId: string | null; officialOnly: boolean; enabled: boolean; minimumQuality: number; contractVersion: number }
const blank: Content = { key: '', kind: 'Character', assetKey: '', fallbackKey: null, productId: null, officialOnly: false, enabled: true, minimumQuality: 0, contractVersion: 1 }
const kinds = ['Character', 'Scene', 'Pose', 'Animation', 'Camera', 'Prop', 'Effect', 'Theme']

export function SocialContentEditor() {
  const content = useResourceList<Content>('/api/admin/social/showcase/content')
  const { products } = useProducts()
  const [draft, setDraft] = useState<Content>({ ...blank }); const [editing, setEditing] = useState(false); const [busy, setBusy] = useState(false)
  const update = <K extends keyof Content>(key: K, value: Content[K]) => setDraft(d => ({ ...d, [key]: value }))
  return <Card><CardHeader title="Showcase content contracts" /><CardBody><div className="s7-stack">
    <Note>Asset keys must match approved Unity content. Existing definitions are immutable; a new rendition needs a new key. Only availability can change.</Note>
    <DataTable rows={content.data} getId={row => row.key} loading={content.loading} selectedId={editing ? draft.key : undefined}
      onRowClick={row => { setDraft({ ...row }); setEditing(true) }} columns={[
        { key: 'key', header: 'Key', render: row => row.key }, { key: 'kind', header: 'Kind', render: row => row.kind },
        { key: 'asset', header: 'Asset', render: row => row.assetKey }, { key: 'enabled', header: 'Availability', render: row => row.enabled ? 'Enabled' : 'Disabled' }
      ]} empty="No content authored. Players keep the existing avatar fallback." />
    <Button variant="ghost" onClick={() => { setDraft({ ...blank }); setEditing(false) }}>New definition</Button>
    <Field label="Versioned key"><Input disabled={editing} maxLength={80} value={draft.key} onChange={e => update('key', e.target.value)} /></Field>
    <Field label="Kind"><Select disabled={editing} value={draft.kind} onChange={e => update('kind', e.target.value)}>{kinds.map(kind => <option key={kind}>{kind}</option>)}</Select></Field>
    <Field label="Unity asset key"><Input disabled={editing} maxLength={160} value={draft.assetKey} onChange={e => update('assetKey', e.target.value)} /></Field>
    <Field label="Free base-quality fallback"><Select disabled={editing} value={draft.fallbackKey ?? ''} onChange={e => update('fallbackKey', e.target.value || null)}><option value="">Existing renderer fallback</option>
      {content.data.filter(c => c.kind === draft.kind && c.enabled && !c.productId && !c.officialOnly && c.minimumQuality === 0 && !c.fallbackKey).map(c => <option key={c.key}>{c.key}</option>)}</Select></Field>
    <Field label="Required owned product"><Select disabled={editing} value={draft.productId ?? ''} onChange={e => update('productId', e.target.value || null)}><option value="">Free</option>{products.map(p => <option key={p.productId} value={p.productId}>{p.key}</option>)}</Select></Field>
    <Field label="Minimum quality"><Select disabled={editing} value={draft.minimumQuality} onChange={e => update('minimumQuality', Number(e.target.value))}>{['Base', 'Balanced', 'High'].map((label, value) => <option key={label} value={value}>{label}</option>)}</Select></Field>
    <Switch checked={draft.officialOnly} onChange={value => { if (!editing) update('officialOnly', value) }} label="Official presentation only" />
    <Switch checked={draft.enabled} onChange={value => update('enabled', value)} label="Available to players" />
    <Button disabled={busy || !draft.key || !draft.assetKey} onClick={async () => { setBusy(true); try { await api.put('/api/admin/social/showcase/content', draft); toast.success('Content saved'); setEditing(true); await content.reload() } catch { /* Shared API errors. */ } finally { setBusy(false) } }}>Save content</Button>
  </div></CardBody></Card>
}

export function OfficialActivityEditor() {
  const [user, setUser] = useState(''); const [key, setKey] = useState(''); const [en, setEn] = useState(''); const [ar, setAr] = useState('')
  const [event, setEvent] = useState(''); const [start, setStart] = useState(''); const [end, setEnd] = useState(''); const [busy, setBusy] = useState(false)
  return <Card><CardHeader title="Official activity publication" /><CardBody><div className="s7-stack">
    <Note>A single pull-based publication for followers. Publication keys make retries safe. Only real event IDs create event links.</Note>
    <Field label="Approved identity account ID"><Input value={user} onChange={e => setUser(e.target.value)} /></Field>
    <Field label="Publication key"><Input maxLength={64} value={key} onChange={e => setKey(e.target.value)} /></Field>
    <Field label="English title"><Input maxLength={160} value={en} onChange={e => setEn(e.target.value)} /></Field>
    <Field label="Arabic title"><Input dir="rtl" maxLength={160} value={ar} onChange={e => setAr(e.target.value)} /></Field>
    <Field label="Optional existing event ID"><Input value={event} onChange={e => setEvent(e.target.value)} /></Field>
    <Field label="Starts"><Input type="datetime-local" value={toLocalInput(start)} onChange={e => setStart(fromLocalInput(e.target.value) ?? '')} /></Field>
    <Field label="Expires (within 30 days)"><Input type="datetime-local" value={toLocalInput(end)} onChange={e => setEnd(fromLocalInput(e.target.value) ?? '')} /></Field>
    <Button disabled={busy || !user || !key || !en || !ar || !start || !end} onClick={async () => { setBusy(true); try { await api.post(`/api/admin/social/identities/${user}/activity`, { publicationKey: key, titleEn: en, titleAr: ar, eventId: event || null, startsAtUtc: start, expiresAtUtc: end }); toast.success('Activity published') } catch { /* Shared API errors. */ } finally { setBusy(false) } }}>Publish activity</Button>
  </div></CardBody></Card>
}
