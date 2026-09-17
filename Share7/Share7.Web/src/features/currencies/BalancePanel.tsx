import { AnimatePresence, motion } from 'motion/react'
import { Gift, Wallet } from 'lucide-react'
import { useEffect, useState } from 'react'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Button, EmptyState, SkeletonRows, Subhead } from '../../components/ui/primitives'
import { Field, Input, Select } from '../../components/ui/form'
import { listVariants, riseVariants } from '../../components/ui/motion'
import { toast } from '../../store/toast'
import type { AdminGrantCurrencyRequest, BalanceDto, CurrencyDto } from '../../types/api'

// ---------------------------------------------------------------------------
// Balances
// ---------------------------------------------------------------------------

export function BalanceGrid({
  balances,
  loading,
}: {
  balances: BalanceDto[]
  loading: boolean
}) {
  if (loading) return <SkeletonRows rows={2} />

  if (!balances.length) {
    return <EmptyState icon={<Wallet size={26} />}>No balances yet.</EmptyState>
  }

  return (
    <motion.div className="s7-balances" variants={listVariants} initial="hidden" animate="visible">
      <AnimatePresence initial={false}>
        {balances.map((b) => (
          <motion.div key={b.currency} className="s7-balance" variants={riseVariants} layout>
            <div className="s7-balance-label">{b.currency}</div>
            <div className="s7-balance-amount">
              <AnimatedNumber value={b.amount} />
            </div>
          </motion.div>
        ))}
      </AnimatePresence>
    </motion.div>
  )
}

// ---------------------------------------------------------------------------
// Grant
// ---------------------------------------------------------------------------

export function GrantForm({
  currencies,
  onGrant,
}: {
  currencies: CurrencyDto[]
  onGrant: (request: AdminGrantCurrencyRequest) => Promise<unknown>
}) {
  const [currencyId, setCurrencyId] = useState('')
  const [amount, setAmount] = useState('100')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)

  // Only active currencies can be credited — the server refuses a retired one — so the picker
  // offers exactly what will be accepted rather than listing rows that are certain to fail.
  const grantable = currencies.filter((c) => c.enabled)

  useEffect(() => {
    // Default to the first grantable currency, and recover if the selected one is retired
    // while the page is open.
    if (!grantable.some((c) => c.currencyId === currencyId)) {
      setCurrencyId(grantable[0]?.currencyId ?? '')
    }
  }, [grantable, currencyId])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!currencyId) {
      toast.error('Select a currency', 'No active currency is available to credit.')
      return
    }

    const value = Number(amount)
    if (!value) {
      toast.error('Amount required', 'Positive to credit, negative to debit.')
      return
    }

    setBusy(true)
    try {
      await onGrant({ currencyId, amount: value, reason: reason.trim() || null })
      setReason('')
    } catch {
      // Surfaced globally — an overdraw is refused rather than clamped, and that refusal is the
      // message the admin needs to see.
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <Subhead icon={<Gift size={15} />}>Grant to myself</Subhead>

      <div className="s7-form-grid-2">
        <Field label="Currency">
          <Select
            value={currencyId}
            onChange={(e) => setCurrencyId(e.target.value)}
            disabled={!grantable.length}
          >
            {grantable.length ? (
              grantable.map((c) => (
                <option key={c.currencyId} value={c.currencyId}>
                  {c.key}
                </option>
              ))
            ) : (
              <option value="">No active currency</option>
            )}
          </Select>
        </Field>

        <Field label="Amount">
          <Input type="number" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
      </div>

      <div style={{ marginTop: '0.65rem' }}>
        <Field label="Reason">
          <Input
            value={reason}
            placeholder="optional note for the ledger"
            onChange={(e) => setReason(e.target.value)}
          />
        </Field>
      </div>

      <Button
        type="submit"
        loading={busy}
        disabled={!grantable.length}
        style={{ marginTop: '0.85rem' }}
      >
        {busy ? 'Granting…' : 'Grant'}
      </Button>

      <div className="s7-hint">
        Credits the signed-in account only. Negative amounts deduct; overdraw is refused. Recorded
        on the ledger as <code>ADMIN_GRANT</code>, or <code>ADMIN_ADJUSTMENT</code> when negative.
      </div>
    </form>
  )
}
