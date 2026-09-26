import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useContentLanguages, useLedge } from '../App'
import { Beside, Counted, Mark, Nothing, Wiping, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio, type Blueprint } from '../lib/studio'
import { useDoing, useLoad } from '../lib/use'
import { Tally, saidWrong } from './bits'

// ===========================================================================
// Papers
//
// A blueprint is a claim about what a real examination contains, so nothing on
// this board invents one. The team reads what exists and sees the two things
// that would make a blueprint unusable: lines naming a stand-in rather than a
// real skill, and lines no question in the bank can satisfy.
//
// Freezing one is a Lead's act and it is final — every coverage figure ever
// computed from a blueprint would quietly change meaning if the blueprint
// could be edited afterwards. A change makes the next version instead.
// ===========================================================================

/** The one reading that matters: could this paper be served, and would it prove anything? */
function Readiness({ blueprint }: { blueprint: Blueprint }) {
  const { t } = useI18n()

  if (blueprint.placeholderLines > 0)
    return <Mark stroke="wrong">{t('exams.onStandIns', { count: blueprint.placeholderLines })}</Mark>

  if (blueprint.unservableLines > 0)
    return <Mark stroke="waiting">{t('exams.unservable', { count: blueprint.unservableLines })}</Mark>

  return <Mark stroke="written">{t('exams.ready')}</Mark>
}

export function Exams() {
  const { t } = useI18n()
  const written = useContentLanguages()[0]

  const blueprints = useLoad(() => studio.blueprints(written?.id), [written?.id])
  const rows = blueprints.data ?? []

  // No primary action. A paper is built over one subject, from that subject's
  // own board, where the team can see whether it is mapped well enough to bear
  // one — starting here would be starting from the wrong end.
  useLedge(
    <>
      <span className="engraved">{t('place.exams')}</span>
    </>,
    [t],
  )

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('place.exams')}</h1>
        <span className="engraved">{t('exams.said')}</span>
      </div>

      {blueprints.loading ? (
        <Wiping rows={4} />
      ) : rows.length === 0 ? (
        <Nothing
          title={t('exams.none')}
          action={
            <Link to="/curriculum" className="act">
              {t('exams.findSubject')}
            </Link>
          }
        >
          {t('exams.noneSaid')}
        </Nothing>
      ) : (
        <div className="rows">
          {rows.map((one) => (
            <Link className="row" key={one.blueprintId} to={`/exams/${one.blueprintId}`}>
              <span className="row-main">
                <span className="row-title">{one.name}</span>
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {one.frameworkName} · {t('exams.version')} <span className="num">{one.versionNumber}</span>
                </span>
              </span>
              <span className="row-side">
                <Readiness blueprint={one} />
                {one.isPublished ? <Mark stroke="written">{t('exams.frozen')}</Mark> : null}
                <span className="row-go" aria-hidden="true">
                  ›
                </span>
              </span>
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// One paper
// ---------------------------------------------------------------------------

export function OneBlueprint() {
  const { blueprintId = '' } = useParams()
  const { t, tError } = useI18n()
  const say = useSaying()
  const written = useContentLanguages()[0]
  const [busy, doing] = useDoing()
  const [problem, setProblem] = useState<string | null>(null)

  const found = useLoad(() => studio.blueprint(blueprintId, written?.id), [blueprintId, written?.id])
  const one = found.data

  const freeze = async () => {
    setProblem(null)
    try {
      await studio.publishBlueprint(blueprintId, written?.id)
      say(t('exams.frozen'))
      found.reload()
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  useLedge(
    <>
      <span className="engraved">{one?.name ?? t('place.exams')}</span>
      <div className="ledge-end">
        {one && !one.isPublished ? (
          <button
            type="button"
            className="act first"
            disabled={busy || one.placeholderLines > 0}
            onClick={() => void doing(freeze)}
          >
            {t('exams.freeze')}
          </button>
        ) : null}
      </div>
    </>,
    [one?.name, one?.isPublished, one?.placeholderLines, busy, t],
  )

  if (found.loading) return <Wiping rows={6} tall />
  // Not 'no paper has been written yet' — papers exist; this particular one
  // does not, and saying the former sends somebody off to build a second copy.
  if (!one)
    return (
      <Nothing
        title={t('exams.gone')}
        action={
          <Link to="/exams" className="act">
            {t('place.exams')}
          </Link>
        }
      >
        {t('exams.goneSaid')}
      </Nothing>
    )

  const lines = one.areas.reduce((all, area) => all + area.lines.length, 0)

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{one.name}</h1>
        <span className="engraved">
          {one.frameworkName} · {t('exams.version')} <span className="num">{one.versionNumber}</span>
        </span>
        {one.isPublished ? <Mark stroke="written">{t('exams.frozen')}</Mark> : null}
      </div>

      {/* Where the paper came from. A structural derivation from Share7's own
          content is a legitimate benchmark and an illegitimate national paper,
          and which one this is has to be on the page.

          Said twice on purpose: the stored note is the permanent record and is
          shown verbatim, in the one language it was written in, because a
          provenance claim that has been paraphrased is not one. The line above
          it says the same thing in the member's own language, so an Arabic
          reader is not handed a single English paragraph in a mirrored board. */}
      {one.blueprintKey.startsWith('share7.benchmark.') ? (
        <Beside tone="live">{t('exams.benchmarkWarning')}</Beside>
      ) : null}
      {one.sourceNote ? (
        <p className="said" lang="en" dir="ltr" style={{ fontSize: 'var(--t-sm)' }}>
          {one.sourceNote}
        </p>
      ) : null}

      <section className="band">
        <span className="engraved">{t('exams.whatItAsks')}</span>

        <Tally
          lines={[
            [t('exams.areas'), one.areas.length],
            [t('exams.lines'), lines],
            [t('exams.onStandInsPlain'), one.placeholderLines],
            [t('exams.unservablePlain'), one.unservableLines],
          ]}
        />

        {one.placeholderLines > 0 ? (
          <Beside tone="wrong">{t('exams.cannotProve')}</Beside>
        ) : one.unservableLines > 0 ? (
          <Beside tone="wrong">{t('exams.cannotServe')}</Beside>
        ) : (
          <Beside>{t('exams.readySaid')}</Beside>
        )}

        {problem ? <Beside tone="wrong">{problem}</Beside> : null}
      </section>

      {one.areas.map((area) => (
        <section className="band" key={area.areaId}>
          <span className="engraved">
            {area.label} · <span className="num">{Math.round(Number(area.normalisedWeight) * 100)}%</span>
          </span>

          <div className="rows">
            {area.lines.map((line) => (
              <div className="row" key={line.lineId}>
                <span className="row-main">
                  <span className="row-title" dir={written?.direction} lang={written?.code}>
                    {line.statement}
                  </span>
                </span>
                <span className="row-side">
                  <Counted count={line.availableItems} name="skills.questions" />
                  {line.isPlaceholder ? (
                    <Mark stroke="wrong">{t('exams.standIn')}</Mark>
                  ) : line.availableItems === 0 ? (
                    <Mark stroke="waiting">{t('exams.noQuestions')}</Mark>
                  ) : (
                    <Mark stroke="written">{t('exams.servable')}</Mark>
                  )}
                </span>
              </div>
            ))}
          </div>
        </section>
      ))}
    </div>
  )
}
