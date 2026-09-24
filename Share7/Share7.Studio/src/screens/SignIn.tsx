import { useState, type FormEvent } from 'react'
import { Beside, Steps, Write } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { StudioError, studioApi, type SignInOutcome } from '../lib/api'
import { acceptSession } from '../lib/session'

// ===========================================================================
// The sign-in board — the first thing anybody sees of the Studio
//
// The date in the top corner, the name of the place centred and underlined
// twice, one column with the only two things that matter in it, and Start on
// the ledge: the one coloured thing on the whole board.
//
// The second step, when a member has 2-step on, is the same board with the
// journey chalked across the top, so it reads as the next line of the same
// board rather than a different screen.
// ===========================================================================

export function SignIn({ reason }: { reason: 'none' | 'ended' | 'signed-out' }) {
  const { t, tError, language, setLanguage, formatDate } = useI18n()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [challenge, setChallenge] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [backup, setBackup] = useState(false)
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)

  const arrive = async (outcome: SignInOutcome) => {
    if (outcome.status === 'two-step-required') {
      setChallenge(outcome.challenge)
      setCode('')
      setProblem(null)
      return
    }
    await acceptSession(outcome)
  }

  const refused = (error: unknown) => {
    setProblem(error instanceof StudioError ? error.messageKey : 'errors.unexpected')
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setProblem(null)
    try {
      if (challenge) {
        await arrive(await studioApi.twoStep(challenge, backup ? null : code.trim(), backup ? code.trim() : null))
      } else {
        await arrive(await studioApi.signIn(username.trim(), password))
      }
    } catch (error) {
      refused(error)
      // A challenge that has run out sends the member back to the start of the board.
      if (error instanceof StudioError && error.code === 'STUDIO_CHALLENGE_EXPIRED') {
        setChallenge(null)
        setPassword('')
      }
    } finally {
      setBusy(false)
    }
  }

  const ready = challenge ? code.trim().length > 3 : username.trim() !== '' && password !== ''

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
        <form id="sign-in" onSubmit={submit} className="alone-column">
          <div className="alone-title">
            <h1 className="hand twice">{t('signIn.title')}</h1>
            <p className="said">{t('signIn.said')}</p>
          </div>

          {challenge ? (
            <Steps steps={[t('activate.step.password'), t('signIn.twoStep.title'), t('activate.step.ready')]} at={1} />
          ) : null}

          {!challenge && reason !== 'none' ? (
            <Beside>{reason === 'ended' ? t('signIn.endedElsewhere') : t('signIn.endedHere')}</Beside>
          ) : null}

          {challenge ? (
            <>
              <p className="said">{backup ? t('signIn.twoStep.backupSaid') : t('signIn.twoStep.said')}</p>
              <Write
                label={backup ? t('signIn.twoStep.backupCode') : t('signIn.twoStep.code')}
                value={code}
                onChange={(event) => setCode(event.target.value)}
                autoFocus
                autoComplete="one-time-code"
                inputMode={backup ? 'text' : 'numeric'}
                spellCheck={false}
                dir="ltr"
                problem={problem ? tError(problem) : undefined}
              />
              <button
                type="button"
                className="act plain small"
                onClick={() => {
                  setBackup(!backup)
                  setCode('')
                  setProblem(null)
                }}
              >
                {backup ? t('signIn.twoStep.usePhone') : t('signIn.twoStep.useBackup')}
              </button>
            </>
          ) : (
            <>
              <Write
                label={t('signIn.username')}
                value={username}
                onChange={(event) => setUsername(event.target.value)}
                autoFocus
                autoComplete="username"
                spellCheck={false}
                dir="ltr"
              />
              <Write
                label={t('signIn.password')}
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="current-password"
                dir="ltr"
                problem={problem ? tError(problem) : undefined}
                hint={t('signIn.noAccount')}
              />
            </>
          )}

        </form>
      </main>

      {/* The ledge is the board's ledge here too: the full width of the frame,
          along the bottom, carrying the one action. The button belongs to the
          form above it by name, so Enter in a field still submits. */}
      <footer className="ledge alone-ledge">
        <button type="submit" form="sign-in" className="act first" disabled={busy || !ready}>
          {busy ? t('signIn.working') : challenge ? t('signIn.twoStep.continue') : t('signIn.start')}
        </button>
      </footer>
    </div>
  )
}
