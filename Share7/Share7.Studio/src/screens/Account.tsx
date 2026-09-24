import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { useLedge } from '../App'
import { Mark, Sheet, Wiping, Write, useSaying, useTelling } from '../board/pieces'
import { setSurface, useSurface } from '../board/surface'
import { useI18n } from '../i18n/i18n'
import { studioApi, type InterfaceLanguage } from '../lib/api'
import { reloadMe, renewSession, signOut, useMe } from '../lib/session'
import { useDoing, useLoad, copyText } from '../lib/use'

// ===========================================================================
// Your account
//
// Who you are, what you may change, and the three things that are yours to
// decide: your password, 2-step, and which face of the board you work on.
//
// No jargon anywhere on this screen (decided 22 Sep 2026). It says "2-step",
// "backup codes" and "where you are signed in" — never "TOTP", "token" or
// "session".
// ===========================================================================

export function Account() {
  const { t, language, setLanguage } = useI18n()
  const me = useMe()
  const surface = useSurface()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()

  const devices = useLoad(() => studioApi.devices(), [])

  useLedge(
    <>
      <span className="engraved">{t('account.title')}</span>
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => void signOut()}>
          {t('common.signOut')}
        </button>
      </div>
    </>,
    [t],
  )

  const swapLanguage = (next: InterfaceLanguage) =>
    run(async () => {
      setLanguage(next)
      try {
        await studioApi.setLanguage(next)
        await reloadMe()
      } catch (error) {
        saying(error)
      }
    })

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{me.fullName}</h1>
        <span className="engraved">{me.jobTitle ?? t('account.title')}</span>
      </div>

      <section className="band">
        <div className="pair">
          <div className="stack tight">
            <span className="engraved">{t('account.role')}</span>
            <p style={{ fontSize: 'var(--t-md)' }}>{t(`role.${me.studioRole}`)}</p>
            <p className="said">{t(`role.${me.studioRole}.said`)}</p>
          </div>
          <div className="stack tight">
            <span className="engraved">{t('account.you')}</span>
            <p style={{ fontSize: 'var(--t-md)' }} dir="ltr">
              {me.username}
            </p>
          </div>
        </div>

        <div className="stack tight">
          <span className="engraved">{t('account.scope')}</span>
          <div className="people">
            {me.scope.allNodes ? (
              <Mark stroke="written">{t('activate.everywhere')}</Mark>
            ) : (
              me.scope.nodes.map((node) => (
                <Mark key={node.id} stroke={node.exists ? 'written' : 'ended'}>
                  {node.trail.map((title) => title.en).join(' · ')}
                </Mark>
              ))
            )}
          </div>
          <div className="people">
            {me.scope.allLanguages ? (
              <Mark stroke="written">{t('activate.everyLanguage')}</Mark>
            ) : (
              me.scope.languages.map((one) => (
                <Mark key={one.id} stroke="written">
                  {one.name}
                </Mark>
              ))
            )}
          </div>
        </div>
      </section>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('account.interface')}</h2>
        <div className="segments" role="group" aria-label={t('account.interface')}>
          <button type="button" aria-pressed={language === 'en'} disabled={busy} onClick={() => void swapLanguage('en')} lang="en">
            English
          </button>
          <button type="button" aria-pressed={language === 'ar'} disabled={busy} onClick={() => void swapLanguage('ar')} lang="ar">
            العربية
          </button>
        </div>

        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('account.surface')}</h2>
        <div className="segments" role="group" aria-label={t('account.surface')}>
          <button type="button" aria-pressed={surface === 'board'} onClick={() => setSurface('board')}>
            {t('account.surface.board')}
          </button>
          <button type="button" aria-pressed={surface === 'whiteboard'} onClick={() => setSurface('whiteboard')}>
            {t('account.surface.whiteboard')}
          </button>
        </div>
        <p className="beside">{t('account.surfaceSaid')}</p>
      </section>

      <Password />

      <TwoStep />

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('account.devices')}</h2>

        {devices.loading ? (
          <Wiping rows={3} />
        ) : (
          <div className="rows">
            {devices.data?.map((device) => (
              <div className="row" key={device.id}>
                <span className="row-main">
                  <span className="row-title">{device.device ?? t('common.nothingYet')}</span>
                  <span className="quiet" style={{ fontSize: 'var(--t-sm)' }} dir="ltr">
                    {here(device.ipAddress) ? t('account.thisComputer') : (device.ipAddress ?? '')}
                  </span>
                </span>
                <span className="row-side">
                  {device.isCurrent ? <Mark stroke="written">{t('account.thisDevice')}</Mark> : null}
                  <LastSeen at={device.lastSeenAtUtc} />
                  {device.isCurrent ? null : (
                    <button
                      type="button"
                      className="act small grave"
                      disabled={busy}
                      onClick={() =>
                        void run(async () => {
                          try {
                            await studioApi.signOutDevice(device.id)
                            devices.reload()
                            say(t('common.done'))
                          } catch (error) {
                            saying(error)
                          }
                        })
                      }
                    >
                      {t('account.signOutDevice')}
                    </button>
                  )}
                </span>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}

/** The Studio runs beside the API in development, so a device is often this very machine. */
function here(address: string | null) {
  return address === '::1' || address === '127.0.0.1' || address === null
}

function LastSeen({ at }: { at: string }) {
  const { t, formatRelative } = useI18n()
  return (
    <time className="quiet" style={{ fontSize: 'var(--t-sm)' }} dateTime={at}>
      {t('account.lastSeen', { when: formatRelative(at) })}
    </time>
  )
}

// ---------------------------------------------------------------------------

function Password() {
  const { t } = useI18n()
  const me = useMe()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [problem, setProblem] = useState<string | null>(null)

  const change = () =>
    run(async () => {
      if (next !== again) {
        setProblem(t('activate.mismatch'))
        return
      }
      setProblem(null)
      try {
        const session = await studioApi.changePassword(current, next)
        renewSession(session)
        setCurrent('')
        setNext('')
        setAgain('')
        say(t('account.passwordChanged'))
      } catch (error) {
        saying(error)
      }
    })

  return (
    <section className="band">
      <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('account.password')}</h2>

      <div className="stack" style={{ maxWidth: 420 }}>
        <Write
          label={t('account.currentPassword')}
          type="password"
          value={current}
          autoComplete="current-password"
          dir="ltr"
          onChange={(event) => setCurrent(event.target.value)}
        />
        <Write
          label={t('account.newPassword')}
          type="password"
          value={next}
          autoComplete="new-password"
          dir="ltr"
          hint={t('activate.rules', { length: me.passwordRules.minimumLength })}
          onChange={(event) => setNext(event.target.value)}
        />
        <Write
          label={t('account.repeatPassword')}
          type="password"
          value={again}
          autoComplete="new-password"
          dir="ltr"
          problem={problem ?? undefined}
          onChange={(event) => setAgain(event.target.value)}
        />
        <button
          type="button"
          className="act"
          style={{ justifySelf: 'start' }}
          disabled={busy || current === '' || next === ''}
          onClick={() => void change()}
        >
          {t('account.changePassword')}
        </button>
      </div>
    </section>
  )
}

// ---------------------------------------------------------------------------

function TwoStep() {
  const { t } = useI18n()
  const me = useMe()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()
  const [setup, setSetup] = useState<{ sharedKey: string; authenticatorUri: string } | null>(null)
  const [picture, setPicture] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [codes, setCodes] = useState<string[] | null>(null)
  const [password, setPassword] = useState('')

  // The authenticator link becomes a square you can point a phone at. It is
  // drawn here, in the browser, so the key never travels anywhere else.
  useEffect(() => {
    if (!setup) {
      setPicture(null)
      return
    }
    let alive = true
    QRCode.toDataURL(setup.authenticatorUri, { margin: 1, width: 240 })
      .then((url) => {
        if (alive) setPicture(url)
      })
      .catch(() => {
        // The key can still be typed in by hand; the square is a convenience.
      })
    return () => {
      alive = false
    }
  }, [setup])

  const begin = () =>
    run(async () => {
      try {
        setSetup(await studioApi.beginTwoStep())
      } catch (error) {
        saying(error)
      }
    })

  const confirm = () =>
    run(async () => {
      try {
        const done = await studioApi.confirmTwoStep(code.trim())
        renewSession(done.session)
        setCodes(done.recoveryCodes)
        setSetup(null)
        setCode('')
        await reloadMe()
      } catch (error) {
        saying(error)
      }
    })

  const turnOff = () =>
    run(async () => {
      try {
        const session = await studioApi.disableTwoStep(password)
        renewSession(session)
        setPassword('')
        await reloadMe()
        say(t('account.twoStepOff'))
      } catch (error) {
        saying(error)
      }
    })

  const fresh = () =>
    run(async () => {
      try {
        const made = await studioApi.newRecoveryCodes(password)
        setCodes(made.codes)
        setPassword('')
        await reloadMe()
      } catch (error) {
        saying(error)
      }
    })

  return (
    <section className="band">
      <div className="band-head">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('account.twoStep')}</h2>
        {me.twoStep.enabled ? (
          <Mark stroke="written">{t('account.twoStepOn')}</Mark>
        ) : (
          <span className="quiet">{t('account.twoStepOff')}</span>
        )}
        {me.twoStep.required ? <Mark stroke="live">{t('account.twoStepRequired')}</Mark> : null}
      </div>

      <p className="said">{t('account.twoStepSaid')}</p>

      {me.twoStep.enabled ? (
        <div className="stack">
          <Mark stroke="written">{t('account.twoStep.backupLeft', { count: me.twoStep.recoveryCodesLeft })}</Mark>

          <div className="stack" style={{ maxWidth: 420 }}>
            <Write
              label={t('account.twoStep.confirmPassword')}
              type="password"
              value={password}
              dir="ltr"
              autoComplete="current-password"
              onChange={(event) => setPassword(event.target.value)}
            />
            <div className="acts">
              <button type="button" className="act" disabled={busy || password === ''} onClick={() => void fresh()}>
                {t('account.twoStep.newBackup')}
              </button>
              {me.twoStep.required ? null : (
                <button
                  type="button"
                  className="act grave"
                  disabled={busy || password === ''}
                  onClick={() => void turnOff()}
                >
                  {t('account.turnOffTwoStep')}
                </button>
              )}
            </div>
          </div>
        </div>
      ) : setup ? (
        <div className="stack" style={{ maxWidth: 420 }}>
          <p className="label">{t('account.twoStep.scan')}</p>
          {picture ? (
            <img src={picture} alt="" width={240} height={240} style={{ background: '#fff', padding: 8, borderRadius: 2 }} />
          ) : (
            <Wiping rows={1} tall />
          )}

          <p className="label">{t('account.twoStep.orType')}</p>
          <p className="key" dir="ltr">
            {setup.sharedKey}
          </p>
          <button
            type="button"
            className="act small"
            style={{ justifySelf: 'start' }}
            onClick={() => void copyText(setup.sharedKey).then((done) => done && say(t('common.copied')))}
          >
            {t('common.copy')}
          </button>

          <Write
            label={t('account.twoStep.confirm')}
            value={code}
            inputMode="numeric"
            autoComplete="one-time-code"
            dir="ltr"
            onChange={(event) => setCode(event.target.value)}
          />

          <div className="acts">
            <button type="button" className="act" onClick={() => setSetup(null)}>
              {t('common.cancel')}
            </button>
            <button type="button" className="act first" disabled={busy || code.trim().length < 6} onClick={() => void confirm()}>
              {t('account.twoStep.turnOn')}
            </button>
          </div>
        </div>
      ) : (
        <button type="button" className="act" style={{ justifySelf: 'start' }} disabled={busy} onClick={() => void begin()}>
          {t('account.turnOnTwoStep')}
        </button>
      )}

      <Sheet
        open={codes !== null}
        onClose={() => setCodes(null)}
        title={t('account.twoStep.backupTitle')}
        actions={
          <>
            <button
              type="button"
              className="act"
              onClick={() => void copyText((codes ?? []).join('\n')).then((done) => done && say(t('common.copied')))}
            >
              {t('common.copy')}
            </button>
            <button type="button" className="act first" onClick={() => setCodes(null)}>
              {t('common.done')}
            </button>
          </>
        }
      >
        <div className="stack">
          <p className="said">{t('account.twoStep.backupSaid')}</p>
          <div className="codes" dir="ltr">
            {(codes ?? []).map((one) => (
              <span key={one}>{one}</span>
            ))}
          </div>
        </div>
      </Sheet>
    </section>
  )
}
