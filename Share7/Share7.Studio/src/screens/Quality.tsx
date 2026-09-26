import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useContentLanguages, useLedge } from '../App'
import { Beside, Choose, Counted, Mark, Nothing, Sheet, Wiping, Write, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio, type ExclusionReason, type FlaggedQuestion, type ItemQuality } from '../lib/studio'
import { useDoing, useLoad } from '../lib/use'
import { Tally, TrailLine, saidWrong } from './bits'

// ===========================================================================
// What the answers say
//
// The first surface where the evidence pays somebody back, and it pays the
// team rather than a child: which questions are mis-keyed, which wrong answers
// nobody ever picks, which are answered too fast to have been read.
//
// A flag is not a thing to dismiss. It is fixed by rewriting the question —
// which is a draft, a reviewer and a release, exactly like every other change
// to what a child is asked. The only two actions on this board are the ones
// that change what an answer MEANS rather than what the question says, and
// both are a Lead's, at once, with a reason kept beside the numbers.
// ===========================================================================

const flags = [
  'distractor_beats_key',
  'too_hard',
  'too_easy',
  'dead_distractor',
  'answered_too_fast',
  'unmapped',
  'insufficient_data',
] as const

/** A flag is a stroke and its words, never a coloured pill. */
function Flag({ flag }: { flag: string }) {
  const { t } = useI18n()
  const said = t(`quality.flag.${flag}` as 'quality.flag.unmapped')

  // A flag this build has never heard of says nothing rather than showing its key.
  if (said === `quality.flag.${flag}`) return null

  return <Mark stroke={flag === 'insufficient_data' ? 'waiting' : 'wrong'}>{said}</Mark>
}

export function Quality() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const languages = useContentLanguages()
  const [flag, setFlag] = useState('')
  const [langId, setLangId] = useState('')

  // The board is always read in one language. There is no "all": a question's
  // words live per language, and asking for none is asking for none of them.
  const reading = langId || languages[0]?.id || ''

  // A page at a time. Worst first means the first page is the page worth reading,
  // and sixty rows of it on a phone is a board nobody scrolls to the end of.
  const page = 25
  const [more, setMore] = useState<FlaggedQuestion[]>([])

  const summary = useLoad(() => studio.qualitySummary(), [])
  const found = useLoad(
    () => (reading ? studio.flagged({ langId: reading, flag: flag || undefined, take: page }) : Promise.resolve([])),
    [flag, reading],
  )

  const rows = [...(found.data ?? []), ...more]
  const overall = summary.data
  const written = languages.find((one) => one.id === reading)

  // A register, like the question bank: you read it and then act on one line.
  // There is no single action this board is for, so its ledge carries what it
  // is holding rather than a yellow button that would have to be "Refresh" —
  // which would say the board is for reloading itself.
  useLedge(
    <>
      <span className="engraved">{t('quality.title')}</span>
      {rows.length > 0 ? <Counted count={rows.length} name="quality.flagged" /> : null}
    </>,
    [rows.length, t],
  )

  const showMore = async () =>
    setMore([
      ...more,
      ...(await studio.flagged({ langId: reading, flag: flag || undefined, take: page, skip: rows.length })),
    ])

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('quality.title')}</h1>
        <span className="engraved">{t('quality.said')}</span>
      </div>

      {overall ? (
        <section className="band">
          <span className="engraved">{t('place.quality')}</span>
          <Tally
            lines={[
              [t('quality.summary.items'), overall.questions],
              [t('quality.summary.answered'), overall.answered],
              [t('quality.summary.enough'), overall.enoughToSay],
              [t('quality.summary.unmapped'), overall.unmapped],
              [t('quality.summary.anchors'), overall.anchors],
            ]}
          />
          {overall.unmapped > 0 ? <Beside tone="wrong">{t('quality.unmappedSaid')}</Beside> : null}
        </section>
      ) : null}

      <div className="spread">
        <Choose
          label={t('quality.flag.all')}
          value={flag}
          onChange={(event) => {
            setMore([])
            setFlag(event.target.value)
          }}
        >
          <option value="">{t('quality.flag.all')}</option>
          {flags.map((one) => (
            <option key={one} value={one}>
              {t(`quality.flag.${one}` as 'quality.flag.unmapped')}
            </option>
          ))}
        </Choose>
        <Choose label={t('common.language')} value={reading} onChange={(event) => setLangId(event.target.value)}>
          {languages.map((language) => (
            <option key={language.id} value={language.id}>
              {language.name}
            </option>
          ))}
        </Choose>
      </div>

      {found.loading ? (
        <Wiping rows={6} />
      ) : rows.length === 0 ? (
        // Nothing to offer here, and that is the point: flags come from
        // children's answers, not from anything a member can do. An empty
        // board that reports a good outcome has no action to carry.
        <Nothing title={t('quality.nothing')}>{t('quality.nothingSaid')}</Nothing>
      ) : (
        <div className="rows">
          {rows.map((one) => (
            <button
              type="button"
              className="row"
              key={one.quality.itemId}
              onClick={() => navigate(`/quality/${one.quality.itemId}`)}
            >
              <span className="row-main">
                <span className="row-title" dir={written?.direction} lang={written?.code}>
                  {one.quality.stem ?? t('quality.noWords')}
                </span>
                <TrailLine trail={one.trail} />
              </span>
              <span className="row-side">
                {one.quality.flags.slice(0, 1).map((each) => (
                  <Flag key={each} flag={each} />
                ))}
                <span className="quiet num" style={{ fontSize: 'var(--t-sm)' }}>
                  {one.quality.nTotal}
                </span>
                <span className="row-go" aria-hidden="true">
                  ›
                </span>
              </span>
            </button>
          ))}

          {rows.length >= page && rows.length % page === 0 ? (
            <button type="button" className="act" style={{ justifySelf: 'start' }} onClick={() => void showMore()}>
              {t('activity.more')}
            </button>
          ) : null}
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// One question, and the one way to fix it
// ---------------------------------------------------------------------------

export function Question() {
  const { itemId = '' } = useParams()
  const { t } = useI18n()
  const navigate = useNavigate()
  const say = useSaying()
  const [anchoring, setAnchoring] = useState(false)
  const [excluding, setExcluding] = useState(false)

  const languages = useContentLanguages()
  const written = languages[0]
  const found = useLoad(() => studio.flaggedQuestion(itemId, written?.id), [itemId, written?.id])
  const summary = useLoad(() => studio.qualitySummary(), [])
  const one = found.data
  const quality = one?.quality

  useLedge(
    <>
      <span className="engraved">{t('quality.title')}</span>
      <div className="ledge-end">
        {one?.canFix && quality?.nodeId ? (
          <button
            type="button"
            className="act first"
            onClick={() => navigate(`/lessons/${quality.nodeId}`)}
          >
            {one.openDraftId ? t('quality.openDraft') : t('quality.fix')}
          </button>
        ) : null}
      </div>
    </>,
    [one?.canFix, one?.openDraftId, quality?.nodeId, t],
  )

  if (found.loading) return <Wiping rows={6} tall />
  // Not 'nothing is flagged' — that is a board with no problems on it, which
  // is good news. This is an address that leads nowhere, and it says so and
  // gives the way back.
  if (!quality || !one)
    return (
      <Nothing
        title={t('quality.gone')}
        action={
          <Link to="/quality" className="act">
            {t('place.quality')}
          </Link>
        }
      >
        {t('quality.goneSaid')}
      </Nothing>
    )

  const floor = summary.data?.reportingFloor ?? 0

  return (
    <div className="stack loose">
      <div className="heading">
        <h1 dir={written?.direction} lang={written?.code}>
          {quality.stem ?? t('quality.noWords')}
        </h1>
        <TrailLine trail={one.trail} />
      </div>

      <div className="spread">
        {quality.flags.map((each) => (
          <Flag key={each} flag={each} />
        ))}
        {quality.isAnchor ? <Mark stroke="live">{t('quality.isAnchor')}</Mark> : null}
      </div>

      {!one.canFix ? <Beside tone="wrong">{t('quality.outsideScope')}</Beside> : null}

      <section className="band">
        <span className="engraved">{t('quality.answers')}</span>

        {quality.facility === null ? (
          <Nothing title={t('quality.tooFew')}>{t('quality.tooFewSaid', { floor })}</Nothing>
        ) : (
          <Tally
            lines={[
              [t('quality.answers'), quality.nTotal],
              [t('quality.firstTime'), quality.nFirstEncounter],
              [t('quality.gotItRight'), `${Math.round(quality.facility * 100)}%`],
              ...(quality.meanElapsedMs
                ? ([[t('quality.seconds'), (quality.meanElapsedMs / 1000).toFixed(1)]] as [string, string][])
                : []),
            ]}
          />
        )}
      </section>

      <Choices quality={quality} />

      {/* Both acts are a Lead's, and a Lead's within their own part of the curriculum — the same
          line the board already draws above with `outsideScope`. The server refuses them outside
          it; offering them here anyway would be a board that says "not yours" and then hands you
          the chalk. */}
      {one.canFix ? (
        <>
          <div className="acts">
            <button type="button" className="act" onClick={() => setAnchoring(true)}>
              {quality.isAnchor ? t('quality.unmakeAnchor') : t('quality.makeAnchor')}
            </button>
            <button type="button" className="act grave" onClick={() => setExcluding(true)}>
              {t('quality.exclude')}
            </button>
          </div>
          <Beside>{t('quality.anchorSaid')}</Beside>
        </>
      ) : null}

      <WithReason
        open={anchoring}
        title={quality.isAnchor ? t('quality.unmakeAnchor') : t('quality.makeAnchor')}
        said={t('quality.anchorSaid')}
        onClose={() => setAnchoring(false)}
        onSaid={async (reason) => {
          await studio.setAnchor(quality.itemId, !quality.isAnchor, reason)
          say(quality.isAnchor ? t('quality.unmakeAnchor') : t('quality.makeAnchor'))
          setAnchoring(false)
          found.reload()
        }}
      />

      <WithReason
        open={excluding}
        grave
        title={t('quality.exclude')}
        said={t('quality.excludeSaid')}
        reasons
        onClose={() => setExcluding(false)}
        onSaid={async (note, reason) => {
          const done = await studio.excludeAnswers(quality.itemVersionId, reason ?? 'MisKeyedItem', note)
          say(t('quality.excluded', { count: done.observationsExcluded }))
          setExcluding(false)
          found.reload()
        }}
      />
    </div>
  )
}

/**
 * How often each answer was chosen. The right one is underlined, the way it is
 * in the written lesson — a distractor beating it is then visible without
 * anybody reading a number.
 */
function Choices({ quality }: { quality: ItemQuality }) {
  const { t } = useI18n()
  const written = useContentLanguages()[0]
  if (quality.choices.length === 0) return null

  return (
    <section className="band">
      <span className="engraved">{t('quality.picked')}</span>
      <div className="rows">
        {quality.choices.map((choice) => (
          <div className="row" key={choice.choiceId}>
            <span className="row-main">
              <span
                className="row-title"
                dir={written?.direction}
                lang={written?.code}
                style={choice.isCorrect ? { textDecoration: 'underline' } : undefined}
              >
                {choice.text ?? ''}
              </span>
              {choice.isCorrect ? (
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {t('quality.rightAnswer')}
                </span>
              ) : null}
            </span>
            <span className="row-side">
              <span className="num">{Math.round(choice.share * 100)}%</span>
              <span className="quiet num" style={{ fontSize: 'var(--t-sm)' }}>
                {choice.count}
              </span>
            </span>
          </div>
        ))}
      </div>
    </section>
  )
}

const reasonKeys: ExclusionReason[] = [
  'MisKeyedItem',
  'InvalidatedContract',
  'IntegrityFlag',
  'Misattributed',
  'WithdrawnConsent',
  'Remapped',
]

/**
 * Neither of these acts happens without a sentence. The reason is not a form
 * field: it is the row somebody reads in a year when they ask why the numbers
 * moved, and it is the only part of the act that cannot be reconstructed.
 */
function WithReason({
  open,
  title,
  said,
  grave,
  reasons,
  onClose,
  onSaid,
}: {
  open: boolean
  title: string
  said: string
  grave?: boolean
  reasons?: boolean
  onClose: () => void
  onSaid: (reason: string, kind?: ExclusionReason) => Promise<void>
}) {
  const { t, tError } = useI18n()
  const [words, setWords] = useState('')
  const [kind, setKind] = useState<ExclusionReason>('MisKeyedItem')
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const go = async () => {
    setProblem(null)
    try {
      await onSaid(words.trim(), reasons ? kind : undefined)
      setWords('')
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={title}
      actions={
        <>
          <button type="button" className="act" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className={grave ? 'act grave' : 'act first'}
            disabled={busy || words.trim().length < 10}
            onClick={() => void doing(go)}
          >
            {title}
          </button>
        </>
      }
    >
      <p className="said">{said}</p>

      {reasons ? (
        <Choose
          label={t('quality.excludeReason')}
          value={kind}
          onChange={(event) => setKind(event.target.value as ExclusionReason)}
        >
          {reasonKeys.map((one) => (
            <option key={one} value={one}>
              {t(`quality.reason.${one}` as 'quality.reason.MisKeyedItem')}
            </option>
          ))}
        </Choose>
      ) : null}

      <Write
        label={t('quality.reason')}
        hint={t('quality.reasonSaid')}
        problem={problem}
        lines={3}
        value={words}
        autoFocus
        onChange={(event) => setWords(event.target.value)}
      />
    </Sheet>
  )
}
