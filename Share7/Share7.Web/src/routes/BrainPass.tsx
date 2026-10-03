import { useState } from 'react'
import { Gift, Plus } from 'lucide-react'
import { Button, Card, CardBody, CardHeader } from '../components/ui/primitives'
import { PageTitle, Note } from '../components/ui/bits'
import { Field, Input, Select } from '../components/ui/form'
import { DataTable } from '../components/ui/DataTable'
import { api } from '../lib/client'
import { useResourceList } from '../lib/resource'
import { useRewardRules } from '../features/rewards/data'
import { useProducts } from '../features/shop/data'
import { formatDateTime, toLocalInput, fromLocalInput } from '../lib/time'
import { toast } from '../store/toast'

interface Rule { metric: string; minimumValue: number; unitValue: number; xpPerUnit: number; maxSourceXp: number; dailyCap: number }
interface Tier { number: number; track: 'Free' | 'Premium'; requiredXp: number; rewardRuleId: string }
interface Configuration { key: string; nameEn: string; nameAr: string; startsAtUtc: string; endsAtUtc: string; claimUntilUtc: string;
  premiumProductId: string | null; objectiveGroupKey: string | null; rules: Rule[]; tiers: Tier[]; expectedVersion: number }
interface Season { id: string; state: 'Draft' | 'Published' | 'Disabled'; version: number; configuration: Configuration }
const metrics = ['LESSONS_COMPLETED', 'LESSONS_ACED', 'LESSON_BEST_PERCENT', 'RUN_SECONDS', 'MATCHES_PLAYED']
const blank = (): Configuration => ({ key: '', nameEn: '', nameAr: '', startsAtUtc: '', endsAtUtc: '', claimUntilUtc: '', premiumProductId: null,
  objectiveGroupKey: null, expectedVersion: 0, tiers: [], rules: [{ metric: 'LESSONS_COMPLETED', minimumValue: 1, unitValue: 1, xpPerUnit: 10, maxSourceXp: 10, dailyCap: 100 }] })

export function BrainPass() {
  const seasons = useResourceList<Season>('/api/admin/brain-pass')
  const { rules: rewards } = useRewardRules(); const { products } = useProducts()
  const [editing, setEditing] = useState<string | null>(null); const [config, setConfig] = useState<Configuration | null>(null)
  const [busy, setBusy] = useState(false); const [confirmPublish, setConfirmPublish] = useState(false)
  const eligibleRewards = rewards.filter(r => r.enabled && r.eventType === 'BRAIN_PASS_TIER' && r.repeatPolicy === 'ONCE')
  const update = <K extends keyof Configuration>(key: K, value: Configuration[K]) => setConfig(c => c && ({ ...c, [key]: value }))
  const transact = async (call: () => Promise<unknown>, message: string) => {
    setBusy(true)
    try { await call(); toast.success(message); setConfig(null); setEditing(null); setConfirmPublish(false); await seasons.reload() }
    catch { /* Shared API error handler; keep the draft for correction. */ } finally { setBusy(false) }
  }
  return <div className="s7-stack">
    <PageTitle icon={<Gift />} title="Brain Pass" subtitle="Seasonal progression from real learning and gameplay results. Publishing freezes its tiers and XP caps."
      actions={<Button disabled={busy} onClick={() => { setEditing(null); setConfig(blank()); setConfirmPublish(false) }}><Plus size={16} />New draft</Button>} />
    <Card><CardHeader title="Seasons" /><CardBody>
      <DataTable rows={seasons.data} getId={s => s.id} loading={seasons.loading} onRowClick={s => { setEditing(s.id); setConfig({ ...s.configuration, expectedVersion: s.version }); setConfirmPublish(false) }}
        columns={[{ key: 'name', header: 'Season', render: s => s.configuration.nameEn }, { key: 'state', header: 'State', render: s => s.state },
          { key: 'window', header: 'Earning window', render: s => `${formatDateTime(s.configuration.startsAtUtc)} – ${formatDateTime(s.configuration.endsAtUtc)}` }]} empty="No seasons authored." />
    </CardBody></Card>
    {config && <Card><CardHeader title={editing ? 'Season configuration' : 'New seasonal draft'} /><CardBody><div className="s7-stack">
      <Note>Free tiers use the existing reward engine. Premium is a server-owned product entitlement; this screen does not create a child-payment flow.</Note>
      <fieldset disabled={busy || !!editing && seasons.data.find(s => s.id === editing)?.state !== 'Draft'} style={{ border: 0, padding: 0 }}>
        <div className="s7-stack">
          <Field label="Stable season key"><Input value={config.key} maxLength={64} onChange={e => update('key', e.target.value)} /></Field>
          <Field label="English season name"><Input value={config.nameEn} maxLength={80} onChange={e => update('nameEn', e.target.value)} /></Field>
          <Field label="Arabic season name"><Input dir="rtl" value={config.nameAr} maxLength={80} onChange={e => update('nameAr', e.target.value)} /></Field>
          {(['startsAtUtc', 'endsAtUtc', 'claimUntilUtc'] as const).map(key => <Field key={key} label={key === 'startsAtUtc' ? 'Earning starts' : key === 'endsAtUtc' ? 'Earning ends' : 'Final claim deadline'}>
            <Input type="datetime-local" value={toLocalInput(config[key])} onChange={e => update(key, fromLocalInput(e.target.value) ?? '')} /></Field>)}
          <Field label="Premium access product"><Select value={config.premiumProductId ?? ''} onChange={e => update('premiumProductId', e.target.value || null)}>
            <option value="">Free track only</option>{products.filter(p => p.active).map(p => <option value={p.productId} key={p.productId}>{p.key}</option>)}</Select></Field>
          <Field label="Existing quest group (optional)"><Input value={config.objectiveGroupKey ?? ''} maxLength={64} onChange={e => update('objectiveGroupKey', e.target.value || null)} /></Field>
          <CardHeader title="Result rules" />
          {config.rules.map((rule, index) => <div className="s7-stack" key={index}>
            <Field label="Result metric"><Select value={rule.metric} onChange={e => update('rules', config.rules.map((r, i) => i === index ? { ...r, metric: e.target.value,
              minimumValue: e.target.value === 'RUN_SECONDS' ? 60 : 1, unitValue: e.target.value === 'RUN_SECONDS' ? 60 : 1 } : r))}>
              {metrics.map(metric => <option key={metric}>{metric}</option>)}</Select></Field>
            {(['minimumValue', 'unitValue', 'xpPerUnit', 'maxSourceXp', 'dailyCap'] as const).map(key => <Field key={key} label={{ minimumValue: 'Minimum qualifying value', unitValue: 'Measured units per award', xpPerUnit: 'XP per award', maxSourceXp: 'XP cap per source', dailyCap: 'Daily XP cap' }[key]}>
              <Input type="number" min={1} value={rule[key]} onChange={e => update('rules', config.rules.map((r, i) => i === index ? { ...r, [key]: Number(e.target.value) } : r))} /></Field>)}
            <Button variant="ghost" disabled={config.rules.length === 1} onClick={() => update('rules', config.rules.filter((_, i) => i !== index))}>Remove rule</Button>
          </div>)}
          <Button variant="ghost" disabled={config.rules.length >= 5} onClick={() => update('rules', [...config.rules, { metric: metrics.find(m => !config.rules.some(r => r.metric === m)) ?? '', minimumValue: 1, unitValue: 1, xpPerUnit: 1, maxSourceXp: 10, dailyCap: 100 }])}>Add result rule</Button>
          <CardHeader title="Reward tiers" />
          {eligibleRewards.length === 0 && <Note>Author an enabled ONCE Brain Pass tier rule in Rewards before adding a tier.</Note>}
          {config.tiers.map((tier, index) => <div className="s7-stack" key={index}>
            <Field label="Tier number"><Input type="number" min={1} max={100} value={tier.number} onChange={e => update('tiers', config.tiers.map((t, i) => i === index ? { ...t, number: Number(e.target.value) } : t))} /></Field>
            <Field label="Track"><Select value={tier.track} onChange={e => update('tiers', config.tiers.map((t, i) => i === index ? { ...t, track: e.target.value as Tier['track'] } : t))}><option>Free</option><option disabled={!config.premiumProductId}>Premium</option></Select></Field>
            <Field label="Required XP"><Input type="number" min={1} value={tier.requiredXp} onChange={e => update('tiers', config.tiers.map((t, i) => i === index ? { ...t, requiredXp: Number(e.target.value) } : t))} /></Field>
            <Field label="Reward rule"><Select value={tier.rewardRuleId} onChange={e => update('tiers', config.tiers.map((t, i) => i === index ? { ...t, rewardRuleId: e.target.value } : t))}>
              <option value="">Choose an authored reward</option>{eligibleRewards.map(r => <option key={r.ruleId} value={r.ruleId}>{r.name}</option>)}</Select></Field>
            <Button variant="ghost" onClick={() => update('tiers', config.tiers.filter((_, i) => i !== index))}>Remove tier</Button>
          </div>)}
          <Button variant="ghost" disabled={config.tiers.length >= 200} onClick={() => update('tiers', [...config.tiers, { number: config.tiers.filter(t => t.track === 'Free').length + 1, track: 'Free', requiredXp: (config.tiers.at(-1)?.requiredXp ?? 0) + 100, rewardRuleId: '' }])}>Add tier</Button>
          <Button disabled={busy || !config.nameEn || !config.nameAr || !config.startsAtUtc || !config.endsAtUtc || !config.claimUntilUtc || config.tiers.length === 0}
            onClick={() => void transact(() => editing ? api.put(`/api/admin/brain-pass/${editing}`, config) : api.post('/api/admin/brain-pass', config), 'Draft saved')}>Save draft</Button>
        </div>
      </fieldset>
      {editing && seasons.data.find(s => s.id === editing)?.state === 'Draft' && <>
        <Button variant="ghost" disabled={busy} onClick={() => setConfirmPublish(!confirmPublish)}>Review publication</Button>
        {confirmPublish && <><Note>Publish the saved server draft? Its earning window, tiers, reward definitions and caps become immutable. Unsaved form changes are excluded.</Note>
          <Button disabled={busy} onClick={() => void transact(() => api.post(`/api/admin/brain-pass/${editing}/publish`, { expectedVersion: config.expectedVersion }), 'Season published')}>Publish saved draft</Button></>}
      </>}
      {editing && seasons.data.find(s => s.id === editing)?.state === 'Published' && <Button variant="danger" disabled={busy}
        onClick={() => void transact(() => api.post(`/api/admin/brain-pass/${editing}/disable`), 'Season disabled')}>Disable new earning and claims</Button>}
    </div></CardBody></Card>}
  </div>
}
