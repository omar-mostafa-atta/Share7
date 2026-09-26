import { useEffect, useState } from 'react'
import { Lock } from 'lucide-react'
import { Button, Card, CardBody, CardHeader, SkeletonRows } from '../../components/ui/primitives'
import { Note } from '../../components/ui/bits'
import { Field, Input, Switch } from '../../components/ui/form'
import { ApiError } from '../../lib/errors'
import { useResource } from '../../lib/resource'
import { formatDateTime } from '../../lib/time'
import { toast } from '../../store/toast'
import { team, type StaffSecuritySettings } from './data'

// ===========================================================================
// How the content team signs in
//
// Five numbers and one switch. The switch is the one with consequences, so it
// says who it will reach before it is saved: every active member without
// 2-step is asked to set it up the next time they open the Studio, and can do
// nothing else until they have.
// ===========================================================================

type Form = Omit<StaffSecuritySettings, 'updatedAtUtc' | 'updatedBy' | 'activeMembersWithoutTwoStep'>

export function SecuritySettings() {
  const settings = useResource<StaffSecuritySettings | null>('/api/admin/team/security', null)
  const [form, setForm] = useState<Form | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!settings.data) return
    const { updatedAtUtc: _a, updatedBy: _b, activeMembersWithoutTwoStep: _c, ...rest } = settings.data
    setForm(rest)
  }, [settings.data])

  if (settings.loading || !form || !settings.data) return <SkeletonRows rows={5} />

  const saved = settings.data
  const changed = (Object.keys(form) as (keyof Form)[]).some((key) => form[key] !== saved[key])
  const number = (key: keyof Form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [key]: e.target.value === '' ? 0 : Number(e.target.value) })

  async function save() {
    if (!form) return
    setBusy(true)
    setError(null)
    try {
      await team.updateSecurity(form)
      await settings.reload()
      toast.success('Sign-in settings saved')
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The settings could not be saved.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <CardHeader icon={<Lock size={16} />} title="How the content team signs in" />
      <CardBody>
        <div className="s7-stack" style={{ maxWidth: '44rem' }}>
          <Switch
            checked={form.requireTwoStep}
            onChange={(requireTwoStep) => setForm({ ...form, requireTwoStep })}
            label="Everyone signs in with 2-step: a code from an app on their phone, after their password"
          />
          {form.requireTwoStep && !saved.requireTwoStep && saved.activeMembersWithoutTwoStep > 0 ? (
            <Note tone="warning">
              {saved.activeMembersWithoutTwoStep === 1
                ? '1 active member has not turned 2-step on.'
                : `${saved.activeMembersWithoutTwoStep} active members have not turned 2-step on.`}{' '}
              The next time they open the Studio they will be asked to, and can do nothing else until they have.
            </Note>
          ) : null}

          <div className="s7-form-pair">
            <Field label="Stay signed in for" hint="Hours. After this they sign in again, however busy they have been.">
              <Input type="number" min={1} value={form.sessionLifetimeHours} onChange={number('sessionLifetimeHours')} />
            </Field>
            <Field label="Sign out after being away for" hint="Hours without doing anything.">
              <Input type="number" min={1} value={form.idleTimeoutHours} onChange={number('idleTimeoutHours')} />
            </Field>
          </div>
          <div className="s7-form-pair">
            <Field label="Shortest password" hint="Characters. Applies to the next password each member chooses.">
              <Input type="number" min={8} value={form.minimumPasswordLength} onChange={number('minimumPasswordLength')} />
            </Field>

          </div>

          {error ? <Note tone="danger">{error}</Note> : null}

          <div className="s7-inline">
            <Button loading={busy} disabled={!changed} onClick={() => void save()}>
              Save
            </Button>
            <span className="s7-hint">
              Last changed {formatDateTime(saved.updatedAtUtc)}
              {saved.updatedBy ? ` by ${saved.updatedBy.name}` : ''}.
            </span>
          </div>
        </div>
      </CardBody>
    </Card>
  )
}
