import { useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Clock, Coins, Gift, Plus, RefreshCw, Repeat, Trash2, Zap } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { CopyId, Note, PageTitle, SearchBox, Segmented } from '../components/ui/bits'
import { Stat, StatRow } from '../components/ui/Stat'
import { DataTable } from '../components/ui/DataTable'
import type { Column } from '../components/ui/DataTable'
import { Drawer } from '../components/ui/Drawer'
import { Field, Input, Select, Switch } from '../components/ui/form'
import {
  COMMON_EVENT_TYPES,
  REPEAT_POLICIES,
  blankRule,
  useRewardRules,
} from '../features/rewards/data'
import { useCurrencies } from '../features/currencies/data'
import { useProducts } from '../features/shop/data'
import { formatDateTime } from '../lib/time'
import { listVariants } from '../components/ui/motion'
import type {
  CreateRewardRuleRequest,
  RewardRuleDto,
  UpdateRewardRuleRequest,
} from '../types/api'

// ===========================================================================
// Reward Rules
//
// "When X happens, pay Y." The event type is the trigger, the grants are the
// payout, and the repeat policy plus cooldown plus daily limit are what stop a
// rule from being farmed.
//
// Two API facts shape this page:
//   - There is no DELETE. A rule that has paid out is referenced by every
//     RewardTransaction it created, so it is disabled rather than removed.
//   - Update cannot change eventType, referenceKey or entitlements. The trigger
//     is the rule's identity; re-pointing it would silently re-interpret its
//     history.
// ===========================================================================

type Filter = 'all' | 'enabled' | 'disabled'

export function Rewards() {
  const { rules, loading, refreshing, reload, create, update } = useRewardRules()
  const { currencies } = useCurrencies()
  const { products } = useProducts()

  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [editing, setEditing] = useState<RewardRuleDto | null>(null)
  const [creating, setCreating] = useState(false)

  const rows = useMemo(() => {
    const term = search.trim().toLowerCase()

    return rules.filter((rule) => {
      if (filter === 'enabled' && !rule.enabled) return false
      if (filter === 'disabled' && rule.enabled) return false
      if (!term) return true

      return [rule.name, rule.eventType, rule.referenceKey ?? '', ...rule.grants.map((g) => g.currency)]
        .join(' ')
        .toLowerCase()
        .includes(term)
    })
  }, [rules, search, filter])

  // A rule paying a retired currency is enabled, looks configured, and pays
  // nothing. Same failure as on the signals page and worth the same warning.
  const broken = rules.filter((r) => r.enabled && r.grants.some((g) => !g.currencyEnabled))

  // An ALWAYS rule with no cooldown and no daily limit can be triggered as fast
  // as the client can fire the event. That is a farm, not a reward.
  const unbounded = rules.filter(
    (r) => r.enabled && r.repeatPolicy === 'ALWAYS' && r.cooldownSeconds == null && r.dailyLimit == null,
  )

  const columns = useMemo<Column<RewardRuleDto>[]>(
    () => [
      {
        key: 'name',
        header: 'Rule',
        sort: (r) => r.name,
        render: (r) => (
          <div>
            <div style={{ fontWeight: 600 }}>{r.name}</div>
            <code className="s7-key">{r.eventType}</code>
            {r.referenceKey ? (
              <span className="s7-muted" style={{ fontSize: '0.72rem' }}> · {r.referenceKey}</span>
            ) : null}
          </div>
        ),
      },
      {
        key: 'pays',
        header: 'Pays',
        sort: (r) => r.grants.reduce((sum, g) => sum + g.amount, 0),
        render: (r) =>
          !r.grants.length ? (
            <Badge tone="danger">nothing</Badge>
          ) : (
            <span className="s7-inline">
              {r.grants.map((g) => (
                <Badge key={g.currencyId} tone={g.currencyEnabled ? 'info' : 'danger'}>
                  {g.amount.toLocaleString()} {g.currency}
                </Badge>
              ))}
            </span>
          ),
      },
      {
        key: 'repeat',
        header: 'Repeats',
        sort: (r) => r.repeatPolicy,
        render: (r) => (
          <Badge tone={r.repeatPolicy === 'ONCE' ? 'muted' : r.repeatPolicy === 'ALWAYS' ? 'warning' : 'info'}>
            {r.repeatPolicy}
          </Badge>
        ),
      },
      {
        key: 'limits',
        header: 'Throttle',
        render: (r) => {
          const parts: string[] = []
          if (r.cooldownSeconds != null) parts.push(`${r.cooldownSeconds}s cooldown`)
          if (r.dailyLimit != null) parts.push(`${r.dailyLimit}/day`)

          return parts.length ? (
            <span className="s7-muted" style={{ fontSize: '0.78rem' }}>{parts.join(' · ')}</span>
          ) : r.repeatPolicy === 'ALWAYS' ? (
            <Badge tone="warning">unthrottled</Badge>
          ) : (
            <span className="s7-muted">—</span>
          )
        },
      },
      {
        key: 'enabled',
        header: 'State',
        sort: (r) => r.enabled,
        render: (r) => (r.enabled ? <Badge tone="success">On</Badge> : <Badge tone="muted">Off</Badge>),
      },
      {
        key: 'updated',
        header: 'Updated',
        sort: (r) => r.updatedAtUtc,
        render: (r) => (
          <span className="s7-muted" style={{ fontSize: '0.78rem' }}>{formatDateTime(r.updatedAtUtc)}</span>
        ),
      },
      { key: 'id', header: 'Id', render: (r) => <CopyId id={r.ruleId} label="ruleId" /> },
    ],
    [],
  )

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Gift size={22} />}
        title="Reward Rules"
        subtitle="When an event fires, pay this. The repeat policy, cooldown and daily limit are the only things standing between a rule and a farm."
        actions={
          <Button onClick={() => setCreating(true)}>
            <Plus size={15} /> New rule
          </Button>
        }
      />

      <StatRow>
        <Stat icon={<Zap size={13} />} label="Enabled" value={rules.filter((r) => r.enabled).length} sub={`of ${rules.length} rules`} tone="success" />
        <Stat icon={<Repeat size={13} />} label="Unthrottled" value={unbounded.length} sub="ALWAYS with no cooldown or cap" tone={unbounded.length ? 'danger' : 'success'} />
        <Stat icon={<Coins size={13} />} label="Paying a dead currency" value={broken.length} sub="Enabled but pays nothing" tone={broken.length ? 'warning' : 'success'} />
      </StatRow>

      {unbounded.length ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone="danger">
            <strong>{unbounded.length}</strong> enabled rule
            {unbounded.length === 1 ? '' : 's'} repeat ALWAYS with neither a cooldown nor a daily
            limit ({unbounded.slice(0, 3).map((r) => r.name).join(', ')}
            {unbounded.length > 3 ? ', …' : ''}). Each can be triggered as fast as a client can fire
            the event.
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<Gift size={16} />}
          title={`${rows.length} of ${rules.length}`}
          actions={
            <IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          <div className="s7-bar">
            <SearchBox value={search} onChange={setSearch} placeholder="Search name, event or currency…" />
            <Segmented
              layoutId="rewards-filter"
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: 'All' },
                { value: 'enabled', label: 'On' },
                { value: 'disabled', label: 'Off' },
              ]}
            />
          </div>

          <DataTable
            rows={rows}
            columns={columns}
            getId={(r) => r.ruleId}
            loading={loading}
            onRowClick={setEditing}
            selectedId={editing?.ruleId ?? null}
            empty={rules.length ? 'No rule matches that filter.' : 'No reward rules yet — nothing pays out.'}
          />
        </CardBody>
      </Card>

      <RuleEditor
        key={editing?.ruleId ?? (creating ? 'new' : 'closed')}
        rule={editing}
        open={!!editing || creating}
        currencies={currencies.map((c) => ({ id: c.currencyId, key: c.key, enabled: c.enabled }))}
        products={products.map((p) => ({ id: p.productId, key: p.key }))}
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
          await update(editing.ruleId, request)
          setEditing(null)
        }}
      />
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------

function RuleEditor({
  rule,
  open,
  currencies,
  products,
  onClose,
  onCreate,
  onUpdate,
}: {
  rule: RewardRuleDto | null
  open: boolean
  currencies: { id: string; key: string; enabled: boolean }[]
  products: { id: string; key: string }[]
  onClose: () => void
  onCreate: (request: CreateRewardRuleRequest) => Promise<void>
  onUpdate: (request: UpdateRewardRuleRequest) => Promise<void>
}) {
  const isNew = !rule

  const [form, setForm] = useState<CreateRewardRuleRequest>(() =>
    rule
      ? {
          name: rule.name,
          eventType: rule.eventType,
          referenceKey: rule.referenceKey,
          repeatPolicy: rule.repeatPolicy,
          cooldownSeconds: rule.cooldownSeconds,
          dailyLimit: rule.dailyLimit,
          transactionType: rule.transactionType,
          grants: rule.grants.map((g) => ({ currencyId: g.currencyId, amount: g.amount })),
          entitlementProductIds: [],
          enabled: rule.enabled,
        }
      : blankRule(),
  )

  const [saving, setSaving] = useState(false)

  const blocked =
    !form.name.trim() ||
    !form.eventType.trim() ||
    !form.grants.length ||
    form.grants.some((g) => !g.currencyId || g.amount < 1)

  const farmable =
    form.repeatPolicy === 'ALWAYS' && form.cooldownSeconds == null && form.dailyLimit == null

  function patch(next: Partial<CreateRewardRuleRequest>) {
    setForm((current) => ({ ...current, ...next }))
  }

  function setGrant(index: number, next: Partial<{ currencyId: string; amount: number }>) {
    setForm((current) => ({
      ...current,
      grants: current.grants.map((g, i) => (i === index ? { ...g, ...next } : g)),
    }))
  }

  async function save() {
    setSaving(true)
    try {
      if (isNew) {
        await onCreate(form)
      } else {
        await onUpdate({
          name: form.name,
          repeatPolicy: form.repeatPolicy,
          cooldownSeconds: form.cooldownSeconds,
          dailyLimit: form.dailyLimit,
          transactionType: form.transactionType,
          grants: form.grants,
          enabled: form.enabled,
        })
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={isNew ? 'New reward rule' : form.name}
      subtitle={isNew ? 'The trigger is permanent once saved.' : `Triggered by ${rule.eventType}`}
      footer={
        <>
          <Button loading={saving} disabled={blocked} onClick={save}>
            {isNew ? 'Create rule' : 'Save changes'}
          </Button>
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
        </>
      }
    >
      <div className="s7-stack">
        {!isNew ? (
          <Note>
            There is no delete for reward rules — every payout this rule has made still references
            it. Switch it off instead; the history stays readable.
          </Note>
        ) : null}

        <Field label="Name" hint="Shown to admins only.">
          <Input value={form.name} onChange={(e) => patch({ name: e.target.value })} placeholder="First lesson bonus" />
        </Field>

        <Field label="Event type" hint="The trigger. Permanent — it is the rule's identity.">
          <Input
            mono
            list="s7-event-types"
            value={form.eventType}
            disabled={!isNew}
            onChange={(e) => patch({ eventType: e.target.value.trim() })}
            placeholder="LESSON_COMPLETED"
          />
          <datalist id="s7-event-types">
            {COMMON_EVENT_TYPES.map((t) => (
              <option key={t} value={t} />
            ))}
          </datalist>
        </Field>

        <Field
          label="Reference key"
          hint="Optional. Narrows the trigger to one specific thing — a lesson id, an objective key. Permanent."
        >
          <Input
            mono
            value={form.referenceKey ?? ''}
            disabled={!isNew}
            onChange={(e) => patch({ referenceKey: e.target.value || null })}
            placeholder="(any)"
          />
        </Field>

        <h3 className="s7-subhead">
          <Repeat size={15} /> Repetition
        </h3>

        <Field label="Repeat policy">
          <Select value={form.repeatPolicy} onChange={(e) => patch({ repeatPolicy: e.target.value })}>
            {REPEAT_POLICIES.map((p) => (
              <option key={p.value} value={p.value}>
                {p.value} — {p.blurb}
              </option>
            ))}
          </Select>
        </Field>

        <div className="s7-form-grid-2">
          <Field label="Cooldown (seconds)" hint="Minimum gap between payouts. Empty for none.">
            <Input
              type="number"
              min={1}
              max={86400}
              value={form.cooldownSeconds ?? ''}
              onChange={(e) =>
                patch({ cooldownSeconds: e.target.value === '' ? null : Number(e.target.value) })
              }
              placeholder="none"
            />
          </Field>

          <Field label="Daily limit" hint="Payouts per player per UTC day. Empty for none.">
            <Input
              type="number"
              min={1}
              max={1000}
              value={form.dailyLimit ?? ''}
              onChange={(e) =>
                patch({ dailyLimit: e.target.value === '' ? null : Number(e.target.value) })
              }
              placeholder="none"
            />
          </Field>
        </div>

        {farmable ? (
          <Note tone="danger">
            This rule pays every time the event fires, with no cooldown and no daily cap. Whatever
            triggers it can be repeated indefinitely.
          </Note>
        ) : null}

        <h3 className="s7-subhead">
          <Coins size={15} /> Payout
        </h3>

        {!form.grants.length ? (
          <Note tone="warning">A rule must grant at least one currency.</Note>
        ) : (
          <div className="s7-stack" style={{ gap: '0.5rem' }}>
            {form.grants.map((grant, index) => {
              const currency = currencies.find((c) => c.id === grant.currencyId)

              return (
                <div key={index} className="s7-bar" style={{ marginBottom: 0 }}>
                  <Select
                    value={grant.currencyId}
                    onChange={(e) => setGrant(index, { currencyId: e.target.value })}
                    style={{ flex: '1 1 10rem' }}
                  >
                    <option value="">Currency…</option>
                    {currencies.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.key}
                        {!c.enabled ? ' — retired' : ''}
                      </option>
                    ))}
                  </Select>

                  <Input
                    type="number"
                    min={1}
                    value={grant.amount}
                    onChange={(e) => setGrant(index, { amount: Number(e.target.value) || 1 })}
                    style={{ width: '8rem' }}
                  />

                  {currency && !currency.enabled ? <Badge tone="danger">retired</Badge> : null}

                  <button
                    type="button"
                    className="s7-btn s7-btn-ghost s7-btn-icon"
                    aria-label="Remove grant"
                    onClick={() =>
                      patch({ grants: form.grants.filter((_, i) => i !== index) })
                    }
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              )
            })}
          </div>
        )}

        <Button
          variant="ghost"
          onClick={() => patch({ grants: [...form.grants, { currencyId: '', amount: 1 }] })}
        >
          <Plus size={15} /> Add currency
        </Button>

        {isNew ? (
          <Field
            label="Also grant products"
            hint="Entitlements handed over alongside the currency. Permanent — they cannot be changed after creation."
          >
            <div className="s7-dt-wrap" style={{ maxHeight: '12rem' }}>
              <table className="s7-dt">
                <tbody>
                  {products.map((product) => (
                    <tr key={product.id}>
                      <td style={{ width: '2.5rem' }}>
                        <input
                          type="checkbox"
                          checked={form.entitlementProductIds.includes(product.id)}
                          onChange={() =>
                            patch({
                              entitlementProductIds: form.entitlementProductIds.includes(product.id)
                                ? form.entitlementProductIds.filter((p) => p !== product.id)
                                : [...form.entitlementProductIds, product.id],
                            })
                          }
                        />
                      </td>
                      <td>
                        <code className="s7-key">{product.key}</code>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Field>
        ) : null}

        <Field label="Transaction type" hint="Optional label stored on every payout this rule makes.">
          <Input
            mono
            value={form.transactionType ?? ''}
            onChange={(e) => patch({ transactionType: e.target.value || null })}
            placeholder="(default)"
          />
        </Field>

        <Field label="Enabled">
          <Switch
            checked={form.enabled}
            onChange={(v) => patch({ enabled: v })}
            label={
              form.enabled ? (
                <>
                  <Clock size={13} style={{ verticalAlign: -2 }} /> On — pays when the event fires
                </>
              ) : (
                'Off — the event fires and nothing is paid'
              )
            }
          />
        </Field>
      </div>
    </Drawer>
  )
}
