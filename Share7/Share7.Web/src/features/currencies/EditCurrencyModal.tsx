import { AlertTriangle, Lock, Pencil } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Badge, Button } from '../../components/ui/primitives'
import { Field, Input, Switch } from '../../components/ui/form'
import { Modal } from '../../components/ui/Modal'
import type { CurrencyDto, UpdateCurrencyRequest } from '../../types/api'

export function EditCurrencyModal({
  currency,
  onClose,
  onSave,
}: {
  currency: CurrencyDto | null
  onClose: () => void
  onSave: (currencyId: string, request: UpdateCurrencyRequest) => Promise<unknown>
}) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [dailyEarnCap, setDailyEarnCap] = useState('')
  const [busy, setBusy] = useState(false)

  // Reset from the row each time a different currency is opened, so the dialog never shows the
  // previous one's values for an instant.
  useEffect(() => {
    if (!currency) return
    setName(currency.name)
    setDescription(currency.description ?? '')
    setEnabled(currency.enabled)
    setDailyEarnCap(currency.dailyEarnCap != null ? String(currency.dailyEarnCap) : '')
  }, [currency])

  const save = async () => {
    if (!currency || !name.trim()) return

    setBusy(true)
    try {
      await onSave(currency.currencyId, {
        name: name.trim(),
        description: description.trim() || null,
        enabled,
        dailyEarnCap: dailyEarnCap.trim() ? Number(dailyEarnCap) : null,
      })
      onClose()
    } catch {
      // Surfaced globally; the dialog stays open so the values are not lost.
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={!!currency}
      onClose={onClose}
      icon={<Pencil size={17} />}
      title={currency ? `Edit ${currency.key}` : 'Edit currency'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={save} loading={busy} disabled={!name.trim()}>
            {busy ? 'Saving…' : 'Save changes'}
          </Button>
        </>
      }
    >
      {currency ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.85rem' }}>
          {/* The two immutable facts, shown rather than hidden — they are the context for every
              other decision in this dialog. */}
          <div className="s7-row" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
            <Badge tone="brand">
              <Lock size={11} /> {currency.key}
            </Badge>
            <Badge tone={currency.isHard ? 'warning' : 'info'}>
              <Lock size={11} /> {currency.isHard ? 'hard' : 'soft'}
            </Badge>
            <span className="s7-hint" style={{ margin: 0 }}>
              Key and type are permanent.
            </span>
          </div>

          <Field label="Name">
            <Input value={name} maxLength={64} onChange={(e) => setName(e.target.value)} />
          </Field>

          <Field label="Description">
            <Input
              value={description}
              maxLength={512}
              placeholder="optional"
              onChange={(e) => setDescription(e.target.value)}
            />
          </Field>

          <Field
            label="Daily earn cap"
            hint={
              currency.isHard
                ? 'Empty means zero gameplay earning on a hard currency, not unlimited.'
                : 'Empty lifts the ceiling.'
            }
          >
            <Input
              type="number"
              min={0}
              value={dailyEarnCap}
              placeholder={currency.isHard ? '0' : 'no ceiling'}
              onChange={(e) => setDailyEarnCap(e.target.value)}
            />
          </Field>

          <Switch
            checked={enabled}
            onChange={setEnabled}
            label={enabled ? 'Active' : 'Retired'}
          />

          {!enabled ? (
            <div
              className="s7-row"
              style={{
                padding: '0.6rem 0.7rem',
                borderRadius: 'var(--s7-radius)',
                background: 'var(--s7-warning-bg)',
                color: '#92400e',
                fontSize: '0.78rem',
                alignItems: 'flex-start',
              }}
            >
              <AlertTriangle size={15} style={{ flex: '0 0 auto', marginTop: '0.1rem' }} />
              <span>
                Retiring refuses all further credits and debits for <code>{currency.key}</code>.
                Existing balances and ledger history are kept.
              </span>
            </div>
          ) : null}
        </div>
      ) : null}
    </Modal>
  )
}
