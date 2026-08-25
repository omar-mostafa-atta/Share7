import { AnimatePresence, motion } from 'motion/react'
import { AlertTriangle, PlusCircle, Sparkles } from 'lucide-react'
import { useState } from 'react'
import { Badge, Button, Subhead } from '../../components/ui/primitives'
import { Field, Input, Switch } from '../../components/ui/form'
import { KEY_PATTERN, slugify } from '../../lib/format'
import type { CreateCurrencyRequest } from '../../types/api'

interface Errors {
  key?: string
  name?: string
}

export function CreateCurrencyForm({
  onCreate,
}: {
  onCreate: (request: CreateCurrencyRequest) => Promise<void>
}) {
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [isHard, setIsHard] = useState(false)
  const [dailyEarnCap, setDailyEarnCap] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  const validate = (): Errors => {
    const next: Errors = {}

    // Same rules the server enforces, checked here so an obvious mistake does not need a
    // round-trip to be told about.
    if (!key.trim()) next.key = 'Required. Lowercase letters, digits and underscores.'
    else if (!KEY_PATTERN.test(key.trim()))
      next.key = 'Must start with a letter, then lowercase letters, digits or underscores.'
    else if (key.trim().length > 32) next.key = 'At most 32 characters.'

    if (!name.trim()) next.name = 'A human-readable name for the currency.'
    else if (name.trim().length > 64) next.name = 'At most 64 characters.'

    return next
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()

    const found = validate()
    setErrors(found)
    if (Object.keys(found).length) return

    setBusy(true)
    try {
      await onCreate({
        key: key.trim(),
        name: name.trim(),
        description: description.trim() || null,
        isHard,
        dailyEarnCap: dailyEarnCap.trim() ? Number(dailyEarnCap) : null,
      })

      setKey('')
      setName('')
      setDescription('')
      setIsHard(false)
      setDailyEarnCap('')
      setErrors({})
    } catch {
      // Already surfaced by the global error handler; the form keeps its values so the admin can
      // correct one field rather than retyping all of them.
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit}>
      <Subhead icon={<PlusCircle size={15} />}>
        Define a currency
        <Badge tone="warning">Admin only</Badge>
      </Subhead>

      <div className="s7-form-grid">
        <Field label="Key (permanent)" error={errors.key}>
          <Input
            value={key}
            mono
            invalid={!!errors.key}
            placeholder="coins"
            maxLength={32}
            onChange={(e) => setKey(e.target.value)}
            // Normalising on blur rather than on every keystroke: rewriting the value as it is
            // typed fights the admin, since a trailing separator is a legitimate intermediate
            // state on the way to "battle_pass".
            onBlur={() => setKey((v) => slugify(v))}
          />
        </Field>

        <Field label="Name" error={errors.name}>
          <Input
            value={name}
            invalid={!!errors.name}
            placeholder="Coins"
            maxLength={64}
            onChange={(e) => setName(e.target.value)}
          />
        </Field>

        <Field label="Description">
          <Input
            value={description}
            placeholder="optional"
            maxLength={512}
            onChange={(e) => setDescription(e.target.value)}
          />
        </Field>
      </div>

      <div className="s7-row" style={{ marginTop: '0.85rem', flexWrap: 'wrap', gap: '1rem' }}>
        <Switch
          checked={isHard}
          onChange={setIsHard}
          label={
            <span className="s7-row" style={{ gap: '0.35rem' }}>
              Hard currency
              <Sparkles size={13} className="s7-muted" />
            </span>
          }
        />

        <div style={{ minWidth: 170, flex: '0 1 auto' }}>
          <Field label={isHard ? 'Daily earn cap (required)' : 'Daily earn cap'}>
            <Input
              type="number"
              min={0}
              value={dailyEarnCap}
              placeholder={isHard ? '0' : 'no ceiling'}
              onChange={(e) => setDailyEarnCap(e.target.value)}
            />
          </Field>
        </div>
      </div>

      {/*
        This warning is the whole reason the field above is worth exposing. On a hard currency an
        empty cap is read by the server as ZERO, not as "unlimited" — the opposite of what the
        same blank field means on a soft one. Leaving that to be discovered by a currency that
        silently earns nothing is how a live economy gets debugged at the wrong end.
      */}
      <AnimatePresence initial={false}>
        {isHard ? (
          <motion.div
            key="hard-warning"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.24 }}
            style={{ overflow: 'hidden' }}
          >
            <div
              className="s7-row"
              style={{
                marginTop: '0.75rem',
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
                <strong>Hard currency is permanent and cannot be flipped later.</strong> Leaving
                the cap empty means <strong>zero</strong> gameplay earning, not unlimited — the
                only safe default for something with a price attached. Pickup valuations and
                <code> EVERY_TIME</code> reward rules for it are refused without a limit.
              </span>
            </div>
          </motion.div>
        ) : null}
      </AnimatePresence>

      <Button type="submit" loading={busy} style={{ marginTop: '0.85rem' }}>
        {busy ? 'Creating…' : 'Create'}
      </Button>

      <div className="s7-hint">
        The key is what the client speaks and <strong>cannot be changed later</strong>. Lowercase
        letters, digits and underscores.
      </div>
    </form>
  )
}
