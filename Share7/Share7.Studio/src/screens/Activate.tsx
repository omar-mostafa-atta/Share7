import { useEffect, useState, type FormEvent } from 'react'
import { Beside, Mark, Steps, Wiping, Write, type Stroke } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { StudioError, studioApi, type InterfaceLanguage, type SetupLinkInfo, type SignInOutcome } from '../lib/api'
import { acceptSession } from '../lib/session'

// ===========================================================================
// The activation board
//
// A new member's first sight of the Studio. The secret is in the URL fragment
// (…/activate#abc), which browsers never send to a server and never write to a
// referrer, so it is read here, used once, and then wiped out of the address
// bar so a shoulder or a screenshot cannot take it away.
//
// The board says who they are and what they may do before it asks for
// anything, because a link handed over in person should prove itself first.
//
// Three steps, and the third one is a board rather than a promise: the journey
// has always said "Your link · Your password · Ready", and until now the third
// step was a redirect. It is now where the one rule everything else follows
// from is read once — and read with the same four marks the member will meet
// on every row afterwards, so the board is teaching its own notation at the
// same time. Nothing here is the only place any of it is said; the handbook
// has it, and every board chalks it again beside the work.
// ===========================================================================

export function Activate() {
  const { t, tError, language, setLanguage, formatDate } = useI18n()
  const [secret] = useState(() => window.location.hash.replace(/^#/, '').trim())
  const [link, setLink] = useState<SetupLinkInfo>()
  const [refused, setRefused] = useState<string | null>(null)
  const [password, setPassword] = useState('')
  const [again, setAgain] = useState('')
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    if (!secret) {
      setRefused('activate.noSecret')
      return
    }

    // Out of the address bar the moment it has been read.
    window.history.replaceState(null, '', window.location.pathname)

    let alive = true
    studioApi
      .inspectSetup(secret)
      .then((info) => {
        if (!alive) return
        setLink(info)
        setLanguage(info.interfaceLanguage)
      })
      .catch((error) => {
        if (alive) setRefused(error instanceof StudioError ? error.messageKey : 'errors.unexpected')
      })

    return () => {
      alive = false
    }
    // The secret is read once, at the very start, and never again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [secret])

  const finish = async (event: FormEvent) => {
    event.preventDefault()
    if (password !== again) {
      setProblem('activate.mismatch')
      return
    }

    setBusy(true)
    setProblem(null)
    try {
      const outcome: SignInOutcome = await studioApi.completeSetup(secret, password, language)
      if (outcome.status === 'signed-in') {
        // Signed in, but held on this board for the third step rather than
        // dropped onto Home mid-journey. The step draws itself as it advances,
        // which is the one thing that has actually just changed.
        await acceptSession(outcome)
        setReady(true)
      } else {
        // The account already has 2-step on, so the password alone does not
        // open it. The sign-in board is the only place that asks for a code.
        window.location.replace(import.meta.env.BASE_URL)
      }
    } catch (error) {
      setProblem(error instanceof StudioError ? error.messageKey : 'errors.unexpected')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="board">
      <header className="rail">
        <time className="engraved" dateTime={new Date().toISOString().slice(0, 10)}>
          {formatDate(new Date())}
        </time>
        <div className="rail-end">
          <div className="segments" role="group" aria-label={t('common.language')}>
            <button type="button" aria-pressed={language === 'en'} onClick={() => setLanguage('en')} lang="en">
              English
            </button>
            <button type="button" aria-pressed={language === 'ar'} onClick={() => setLanguage('ar')} lang="ar">
              العربية
            </button>
          </div>
        </div>
      </header>

      <main className="sheet alone">
        {refused ? (
          <div className="alone-column">
            <div className="alone-title">
              <h1 className="hand twice">{t('signIn.title')}</h1>
            </div>
            <Beside tone="wrong">{refused === 'activate.noSecret' ? t('activate.noSecret') : tError(refused)}</Beside>
            <a className="act" href={import.meta.env.BASE_URL}>
              {t('signIn.start')}
            </a>
          </div>
        ) : !link ? (
          <div className="alone-column">
            <p className="said">{t('activate.checking')}</p>
            <Wiping rows={3} />
          </div>
        ) : (
          // The title and the journey sit OUTSIDE the branch below, in the same
          // place in the tree either way. That is what lets the third step be
          // drawn rather than simply appear: <Steps> only animates when `at`
          // changes on a component it already had, so moving it inside the
          // branch would remount it and the advance would be silent.
          <div className="alone-column roomy">
            <div className="alone-title">
              <h1 className="hand twice">
                {ready ? t('ready.title', { name: firstName(link.fullName) }) : t('activate.welcome', { name: link.fullName })}
              </h1>
              <p className="said">
                {ready
                  ? t('ready.said')
                  : link.purpose === 'Reset'
                    ? t('activate.reset.said')
                    : t('activate.said')}
              </p>
            </div>

            <Steps
              steps={[t('activate.step.link'), t('activate.step.password'), t('activate.step.ready')]}
              at={ready ? 2 : 1}
            />

            {ready ? (
              <Loop />
            ) : (
              <form id="activate" onSubmit={finish} className="alone-fields">
                <Who link={link} />

                <div className="double-rule" />

                <Write
                  label={t('activate.choosePassword')}
                  type="password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  autoFocus
                  autoComplete="new-password"
                  dir="ltr"
                  hint={t('activate.rules', { length: link.passwordRules.minimumLength })}
                />

                <Write
                  label={t('activate.repeatPassword')}
                  type="password"
                  value={again}
                  onChange={(event) => setAgain(event.target.value)}
                  autoComplete="new-password"
                  dir="ltr"
                  problem={problem ? (problem === 'activate.mismatch' ? t('activate.mismatch') : tError(problem)) : undefined}
                />

                <ChooseLanguage value={language} onChange={setLanguage} />

                <p className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {t('activate.expiresAt', { when: formatDate(link.expiresAtUtc, 'dateTime') })}
                </p>
              </form>
            )}

            {ready ? (
              <>
                <div className="double-rule" />
                <Who link={link} />
              </>
            ) : null}
          </div>
        )}
      </main>

      {/* The same ledge as every other board: along the bottom, one action on it.
          A full navigation rather than a routed one, so the Studio mounts from
          the refresh cookie exactly as it does on every later visit. */}
      {link && !refused ? (
        <footer className="ledge alone-ledge">
          {ready ? (
            <>
              <a className="act" href={`${import.meta.env.BASE_URL}handbook`}>
                {t('ready.handbook')}
              </a>
              <a className="act first" href={`${import.meta.env.BASE_URL}curriculum`}>
                {t('ready.start')}
              </a>
            </>
          ) : (
            <button
              type="submit"
              form="activate"
              className="act first"
              disabled={busy || password === '' || again === ''}
            >
              {busy ? t('signIn.working') : t('activate.start')}
            </button>
          )}
        </footer>
      ) : null}
    </div>
  )
}

/**
 * The one rule everything else follows from, drawn with the four marks the
 * member will meet on every row from here on. Numbered, because the order is
 * the rule: a step cannot be taken out and nothing skips ahead.
 */
function Loop() {
  const { t } = useI18n()

  const steps: [Stroke, string, string][] = [
    ['writing', t('ready.write'), t('ready.writeSaid')],
    ['waiting', t('ready.send'), t('ready.sendSaid')],
    ['written', t('ready.approve'), t('ready.approveSaid')],
    ['live', t('ready.release'), t('ready.releaseSaid')],
  ]

  return (
    <div className="stack tight">
      <p className="engraved">{t('ready.how')}</p>
      <ol className="loop">
        {steps.map(([stroke, name, said]) => (
          <li key={name}>
            <Mark stroke={stroke}>{name}</Mark>
            <p className="said">{said}</p>
          </li>
        ))}
      </ol>
    </div>
  )
}

/** "Malak Hassan" → "Malak". A welcome uses the name somebody is called. */
function firstName(full: string): string {
  return full.trim().split(/\s+/)[0] || full
}

/** Who the link says you are, and what it lets you change — read before anything is asked for. */
function Who({ link }: { link: SetupLinkInfo }) {
  const { t } = useI18n()

  return (
    <div className="stack">
      <div className="pair">
        <div>
          <p className="engraved">{t('activate.youAre')}</p>
          <p style={{ fontSize: 'var(--t-md)' }}>{link.username}</p>
        </div>
        <div>
          <p className="engraved">{t('activate.youMay')}</p>
          <p style={{ fontSize: 'var(--t-md)' }}>{t(`role.${link.studioRole}`)}</p>
        </div>
      </div>

      <p className="said">{t(`role.${link.studioRole}.said`)}</p>

      <div className="stack tight">
        <p className="engraved">{t('activate.yourPart')}</p>
        {link.scope.allNodes ? (
          <Mark stroke="written">{t('activate.everywhere')}</Mark>
        ) : link.scope.nodes.length === 0 ? (
          <Mark stroke="waiting">{t('common.nothingYet')}</Mark>
        ) : (
          <ul className="people">
            {link.scope.nodes.map((node) => (
              <li key={node.id}>
                <Mark stroke={node.exists ? 'written' : 'ended'}>{node.trail.map((title) => title.en).join(' · ')}</Mark>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="stack tight">
        <p className="engraved">{t('activate.yourLanguages')}</p>
        {link.scope.allLanguages ? (
          <Mark stroke="written">{t('activate.everyLanguage')}</Mark>
        ) : (
          <ul className="people">
            {link.scope.languages.map((one) => (
              <li key={one.id}>
                <Mark stroke="written">{one.name}</Mark>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}

function ChooseLanguage({ value, onChange }: { value: InterfaceLanguage; onChange: (next: InterfaceLanguage) => void }) {
  const { t } = useI18n()

  return (
    <div className="field">
      <span className="label">{t('activate.interfaceLanguage')}</span>
      <div className="segments" role="group" aria-label={t('activate.interfaceLanguage')}>
        <button type="button" aria-pressed={value === 'en'} onClick={() => onChange('en')} lang="en">
          English
        </button>
        <button type="button" aria-pressed={value === 'ar'} onClick={() => onChange('ar')} lang="ar">
          العربية
        </button>
      </div>
    </div>
  )
}
