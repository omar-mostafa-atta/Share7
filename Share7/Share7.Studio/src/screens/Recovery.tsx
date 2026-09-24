import { Link, useNavigate, useParams } from 'react-router-dom'
import { useLedge } from '../App'
import { Beside, Counted, Mark, Nothing, Wiping } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio } from '../lib/studio'
import { useDoing, useLoad } from '../lib/use'
import { Tally, TrailLine } from './bits'

// ===========================================================================
// Second chances
//
// A rule is written at a place in the curriculum and the most specific one
// wins. Nothing is merged: a rule half-inherited from a grade and half from a
// subject is a rule nobody can predict from looking at either.
//
// It changes what every child in its scope is shown, which puts it on the same
// footing as a lesson's questions — proposed in a draft, approved by somebody
// else, released with everything else. So there is no Save on this board. The
// only action is to start the draft.
// ===========================================================================

export function Recovery() {
  const { t } = useI18n()
  const written = useLoad(() => studio.recoveryWritten(), [])

  useLedge(
    <>
      <span className="engraved">{t('recovery.title')}</span>
      <div className="ledge-end">
        <Link to="/curriculum" className="act first">
          {t('recovery.pick')}
        </Link>
      </div>
    </>,
    [t],
  )

  const rules = written.data ?? []

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('recovery.title')}</h1>
        <span className="engraved">{t('recovery.said')}</span>
      </div>

      <section className="band">
        <span className="engraved">{t('recovery.written')}</span>

        {written.loading ? (
          <Wiping rows={4} />
        ) : rules.length === 0 ? (
          <Nothing title={t('recovery.nothingWritten')}>{t('recovery.nothingWrittenSaid')}</Nothing>
        ) : (
          <>
            <p className="said">{t('recovery.writtenSaid')}</p>
            <div className="rows">
              {rules.map((rule) => (
                <Link className="row" key={rule.ruleId} to={`/recovery/${rule.nodeId}`}>
                  <span className="row-main">
                    <span className="row-title">{rule.title}</span>
                    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                      {t(`curriculum.kind.${rule.nodeKind}`)}
                    </span>
                  </span>
                  <span className="row-side">
                    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                      {t('recovery.inWords', {
                        wrong: rule.afterWrongAnswers,
                        serve: rule.questionsToServe,
                      })}
                    </span>
                    <span className="row-go" aria-hidden="true">
                      ›
                    </span>
                  </span>
                </Link>
              ))}
            </div>
          </>
        )}
      </section>
    </div>
  )
}

// ---------------------------------------------------------------------------
// What happens at one place
// ---------------------------------------------------------------------------

export function RecoveryAt() {
  const { nodeId = '' } = useParams()
  const { t } = useI18n()
  const navigate = useNavigate()
  const [busy, doing] = useDoing()

  const here = useLoad(() => studio.recoveryAt(nodeId), [nodeId])
  const at = here.data

  const propose = async () => {
    if (!at) return
    if (at.openDraftId) {
      navigate(`/drafts/${at.openDraftId}`)
      return
    }
    const made = await studio.startDraft({ kind: 'RecoveryRule', nodeId })
    navigate(`/drafts/${made.summary.id}`)
  }

  useLedge(
    <>
      <span className="engraved">{t('recovery.title')}</span>
      <div className="ledge-end">
        {at?.canPropose ? (
          <button type="button" className="act first" disabled={busy} onClick={() => void doing(propose)}>
            {at.openDraftId ? t('recovery.openDraft') : t('recovery.propose')}
          </button>
        ) : null}
      </div>
    </>,
    [at?.canPropose, at?.openDraftId, busy, t],
  )

  if (here.loading) return <Wiping rows={5} tall />
  if (!at) return <Nothing title={t('recovery.nothingWritten')}>{t('recovery.nothingWrittenSaid')}</Nothing>

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{at.title}</h1>
        <TrailLine trail={at.trail} drop={1} />
      </div>

      <section className="band">
        <span className="engraved">{t('recovery.here')}</span>

        {/* Where this was decided is not decoration. A team looking at a lesson
            has to be able to tell "nobody has decided this" from "somebody
            decided this, three levels up", and the numbers alone look the same. */}
        {at.isDefault ? (
          <Mark stroke="waiting">{t('recovery.isDefault')}</Mark>
        ) : at.isOwn ? (
          <Mark stroke="written">{t('recovery.isOwn')}</Mark>
        ) : (
          <Mark stroke="live">{t('recovery.inherited', { where: at.fromNodeTitle ?? '' })}</Mark>
        )}

        <Tally
          lines={[
            [t('recovery.afterWrong'), at.afterWrongAnswers],
            [t('recovery.toServe'), at.questionsToServe],
            [t('recovery.allowRepeats'), at.allowRepeats ? t('common.yes') : t('common.no')],
          ]}
        />

        <Beside>
          {at.isDefault
            ? t('recovery.isDefaultSaid')
            : at.isOwn
              ? t('recovery.afterWrongSaid')
              : t('recovery.inheritedSaid')}
        </Beside>
      </section>

      <section className="band">
        <span className="engraved">{t('recovery.reach')}</span>

        <div className="spread">
          <Counted count={at.lessons} name="recovery.lessons" />
        </div>

        {at.lessonsWithOwnRule > 0 ? (
          <Beside>
            <span className="num">{at.lessonsWithOwnRule}</span> {t('recovery.withOwnRule')}
          </Beside>
        ) : null}

        {at.lessonsWithNoRecoveryQuestions > 0 ? (
          <Beside tone="wrong">
            <span className="num">{at.lessonsWithNoRecoveryQuestions}</span> {t('recovery.withNothing')}
          </Beside>
        ) : null}
      </section>
    </div>
  )
}
