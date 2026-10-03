import { useState } from 'react'
import { ShieldCheck, RefreshCw } from 'lucide-react'
import { Button, Card, CardBody, CardHeader } from '../components/ui/primitives'
import { Note, PageTitle, CopyId } from '../components/ui/bits'
import { Field, Input, Select, Switch } from '../components/ui/form'
import { DataTable } from '../components/ui/DataTable'
import { api } from '../lib/client'
import { useResource, useResourceList } from '../lib/resource'
import { toast } from '../store/toast'
import { useAuth } from '../store/auth'
import { SocialContentEditor, OfficialActivityEditor } from './SocialContent'

interface Case { id: string; sequence: number; reporterId: string; reportedUserId: string; reason: string; state: string; evidenceJson: string; createdAtUtc: string }
interface Appeal { restrictionId: string; userId: string; reportId: string; reasonCode: string; appealedAtUtc: string }
interface Page { items: Case[]; nextAfter: number | null }

export function SocialOperations() {
  const [after, setAfter] = useState(0)
  const cases = useResource<Page>(`/api/admin/social/reports?state=Open&after=${after}`, { items: [], nextAfter: null })
  const appeals = useResourceList<Appeal>('/api/admin/social/appeals')
  const [selected, setSelected] = useState<Case | null>(null)
  const [decision, setDecision] = useState('unsafe_behaviour')
  const [days, setDays] = useState(7)
  const [restrict, setRestrict] = useState(true)
  const [permanent, setPermanent] = useState(false)
  const superAdmin = useAuth(s => s.roles.includes('SuperAdmin'))
  const [busy, setBusy] = useState(false)
  const [appealReason, setAppealReason] = useState('reviewed_appeal')
  const mutate = async (call: () => Promise<unknown>) => {
    setBusy(true)
    try { await call(); toast.success('Decision recorded'); setSelected(null); await cases.reload(); await appeals.reload() }
    catch { /* The shared API error handler keeps the pending decision visible. */ }
    finally { setBusy(false) }
  }
  return <div className="s7-stack">
    <PageTitle icon={<ShieldCheck />} title="Social operations" subtitle="Server-captured reports and player appeals. Every moderation decision is audited."
      actions={<Button variant="ghost" onClick={() => { void cases.reload(); void appeals.reload() }}><RefreshCw size={16} />Refresh</Button>} />
    <Card><CardHeader title="Open reports" /><CardBody>
      <DataTable rows={cases.data.items} getId={row => row.id} loading={cases.loading} selectedId={selected?.id} onRowClick={setSelected}
        columns={[{ key: 'reason', header: 'Reason', render: row => row.reason },
          { key: 'player', header: 'Reported player', render: row => <CopyId id={row.reportedUserId} /> },
          { key: 'created', header: 'Received', render: row => new Date(row.createdAtUtc).toLocaleString() }]} empty="No open reports in this page." />
      <div className="s7-row"><Button variant="ghost" disabled={after === 0} onClick={() => setAfter(0)}>First page</Button>
        <Button disabled={cases.data.nextAfter === null} onClick={() => setAfter(cases.data.nextAfter!)}>Next page</Button></div>
    </CardBody></Card>
    {selected && <Card><CardHeader title="Review evidence" /><CardBody><div className="s7-stack">
      <Note>Review the captured context before acting. A report is an allegation, and repeated opponents are a signal to investigate.</Note>
      <pre style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{JSON.stringify(JSON.parse(selected.evidenceJson), null, 2)}</pre>
      <Field label="Decision reason"><Select value={decision} onChange={e => setDecision(e.target.value)}>
        {['unsafe_behaviour', 'impersonation', 'cheating', 'spam', 'insufficient_evidence', 'no_violation'].map(code => <option key={code}>{code}</option>)}
      </Select></Field>
      <Switch checked={restrict} onChange={setRestrict} label="Apply a temporary social restriction" />
      {superAdmin && restrict && <Switch checked={permanent} onChange={setPermanent} label="Permanent restriction, with a player appeal route" />}
      {restrict && !permanent && <Field label="Restriction days"><Input type="number" min={1} max={365} value={days} onChange={e => setDays(Number(e.target.value))} /></Field>}
      <div className="s7-row"><Button disabled={busy} onClick={() => void mutate(() => api.post(`/api/admin/social/reports/${selected.id}/decision`,
        { state: 'Actioned', decisionCode: decision, restrictDays: restrict && !permanent ? days : null, permanentRestriction: restrict && permanent && superAdmin }))}>Record action</Button>
        <Button variant="ghost" disabled={busy} onClick={() => void mutate(() => api.post(`/api/admin/social/reports/${selected.id}/decision`,
          { state: 'Dismissed', decisionCode: decision, restrictDays: null }))}>Dismiss report</Button></div>
    </div></CardBody></Card>}
    <Card><CardHeader title="Player appeals" /><CardBody><div className="s7-stack">
      <Field label="Review reason"><Input value={appealReason} maxLength={40} onChange={e => setAppealReason(e.target.value)} /></Field>
      <DataTable rows={appeals.data} getId={row => row.restrictionId} loading={appeals.loading} columns={[
        { key: 'player', header: 'Player', render: row => <CopyId id={row.userId} /> },
        { key: 'reason', header: 'Appeal', render: row => row.reasonCode },
        { key: 'action', header: 'Decision', render: row => <div className="s7-row">{[true, false].map(revoke => <Button key={String(revoke)} variant="ghost" disabled={busy || !appealReason}
          onClick={() => void mutate(() => api.post(`/api/admin/social/restrictions/${row.restrictionId}/appeal-review`, { revoke, reasonCode: appealReason }))}>{revoke ? 'Revoke restriction' : 'Uphold restriction'}</Button>)}</div> }
      ]} empty="No active appeals." />
    </div></CardBody></Card>
    <IdentityEditor />
    <SocialContentEditor />
    <OfficialActivityEditor />
  </div>
}

function IdentityEditor() {
  const [user, setUser] = useState('')
  const [kind, setKind] = useState('Creator')
  const [nameEn, setNameEn] = useState(''); const [nameAr, setNameAr] = useState('')
  const [titleEn, setTitleEn] = useState(''); const [titleAr, setTitleAr] = useState('')
  const [verified, setVerified] = useState(false); const [discoverable, setDiscoverable] = useState(false)
  const [busy, setBusy] = useState(false)
  return <Card><CardHeader title="Curated official identity" /><CardBody><div className="s7-stack">
    <Note>Approved adult or staff presentation only. Presentation badges do not grant account roles.</Note>
    <Field label="Account ID"><Input value={user} onChange={e => setUser(e.target.value)} /></Field>
    <Field label="Identity"><Select value={kind} onChange={e => setKind(e.target.value)}>
      {['Creator', 'Official', 'Developer', 'Designer', 'Moderator', 'Educator', 'Partner', 'EventHost'].map(value => <option key={value}>{value}</option>)}</Select></Field>
    <Field label="English display name"><Input maxLength={80} value={nameEn} onChange={e => setNameEn(e.target.value)} /></Field>
    <Field label="Arabic display name"><Input dir="rtl" maxLength={80} value={nameAr} onChange={e => setNameAr(e.target.value)} /></Field>
    <Field label="English title"><Input maxLength={80} value={titleEn} onChange={e => setTitleEn(e.target.value)} /></Field>
    <Field label="Arabic title"><Input dir="rtl" maxLength={80} value={titleAr} onChange={e => setTitleAr(e.target.value)} /></Field>
    <Switch checked={verified} onChange={setVerified} label="Verified presentation" />
    <Switch checked={discoverable} onChange={setDiscoverable} label="Include in official discovery" />
    <Button disabled={busy || !user || !nameEn || !nameAr || !titleEn || !titleAr} onClick={async () => {
      setBusy(true)
      try { await api.put(`/api/admin/social/identities/${user}`, { kind, verified, discoverable, displayNameEn: nameEn, displayNameAr: nameAr, titleEn, titleAr }); toast.success('Identity saved') }
      catch { /* Shared error presentation. */ } finally { setBusy(false) }
    }}>Save identity</Button>
  </div></CardBody></Card>
}
