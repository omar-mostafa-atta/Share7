import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useContentLanguages, useLedge } from '../App'
import { Beside, Choose, Counted, Mark, Nothing, Sheet, Wiping, Write, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio, type Framework, type Skill, type SkillImport, type SkillKind, type SkillReviewState } from '../lib/studio'
import { useDoing, useLoad, useSettled } from '../lib/use'
import { SkillState, Tally, saidWrong } from './bits'

// ===========================================================================
// Skills
//
// The outcomes are somebody else's document, and the Studio does not pretend to
// author them: a filled sheet comes in as a set nobody has confirmed yet, and
// the team's work is turning those lines into claims that can be true or false
// about one child.
//
// Nothing here is released. A skill changes what a child's answers MEAN, never
// what the child is asked, so it takes effect at once and leaves a row in the
// permanent record — which is why confirming one is a named act of its own
// rather than a side effect of saving.
// ===========================================================================

export function Skills() {
  const { t } = useI18n()
  const [starting, setStarting] = useState(false)
  const frameworks = useLoad(() => studio.frameworks(), [])
  const say = useSaying()

  useLedge(
    <>
      <span className="engraved">{t('place.skills')}</span>
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => void studio.outcomesTemplate()}>
          {t('skills.template')}
        </button>
        <button type="button" className="act first" onClick={() => setStarting(true)}>
          {t('skills.framework.start')}
        </button>
      </div>
    </>,
    [t],
  )

  const real = (frameworks.data ?? []).filter((one) => !one.isPlaceholders)
  const standIns = (frameworks.data ?? []).filter((one) => one.isPlaceholders)

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('place.skills')}</h1>
        <span className="engraved">{t('skills.said')}</span>
      </div>

      {frameworks.loading ? (
        <Wiping rows={4} />
      ) : (
        <>
          <section className="band">
            <span className="engraved">{t('skills.frameworks')}</span>

            {real.length === 0 ? (
              <Nothing title={t('skills.framework.none')}>{t('skills.framework.noneSaid')}</Nothing>
            ) : (
              <div className="rows">
                {real.map((one) => (
                  <FrameworkRow key={one.id} framework={one} />
                ))}
              </div>
            )}
          </section>

          {standIns.length > 0 ? (
            <section className="band">
              <span className="engraved">{t('skills.framework.placeholders')}</span>
              <p className="said">{t('skills.framework.placeholdersSaid')}</p>
              <div className="rows">
                {standIns.map((one) => (
                  <div className="row" key={one.id}>
                    <span className="row-main">
                      <span className="row-title">{one.name}</span>
                    </span>
                    <span className="row-side">
                      <Counted count={one.skills} name="skills.counted" />
                      {/* A stand-in is work waiting to be done. A count on its
                          own reads as work finished. */}
                      <Mark stroke="waiting">{t('skills.framework.toReplace')}</Mark>
                    </span>
                  </div>
                ))}
              </div>
            </section>
          ) : null}
        </>
      )}

      <StartFramework
        open={starting}
        onClose={() => setStarting(false)}
        onStarted={(made) => {
          setStarting(false)
          say(made.name)
          frameworks.reload()
        }}
      />
    </div>
  )
}

function FrameworkRow({ framework }: { framework: Framework }) {
  const { t } = useI18n()

  // Where it has got to, said as a state rather than left for the reader to
  // work out from two numbers side by side.
  const state =
    framework.skills === 0 ? (
      <Mark stroke="waiting">{t('skills.framework.empty')}</Mark>
    ) : framework.reviewed === framework.skills ? (
      <Mark stroke="written">{t('skills.state.Reviewed')}</Mark>
    ) : (
      <Mark stroke="writing">{t('skills.framework.beingConfirmed')}</Mark>
    )

  return (
    <Link className="row" to={`/skills/${framework.id}`}>
      <span className="row-main">
        <span className="row-title">{framework.name}</span>
        <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
          {framework.frameworkKey} · {framework.versionLabel}
        </span>
      </span>
      <span className="row-side">
        <Counted count={framework.skills} name="skills.counted" />
        <Counted count={framework.reviewed} name="skills.confirmed" />
        {state}
        <span className="row-go" aria-hidden="true">
          ›
        </span>
      </span>
    </Link>
  )
}

function StartFramework({
  open,
  onClose,
  onStarted,
}: {
  open: boolean
  onClose: () => void
  onStarted: (made: Framework) => void
}) {
  const { t, tError } = useI18n()
  const [name, setName] = useState('')
  const [key, setKey] = useState('')
  const [version, setVersion] = useState(String(new Date().getFullYear()))
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const start = async () => {
    setProblem(null)
    try {
      onStarted(await studio.startFramework(key.trim(), name.trim(), version.trim()))
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={t('skills.framework.start')}
      actions={
        <>
          <button type="button" className="act" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="act first"
            disabled={busy || name.trim().length < 2 || key.trim().length < 3}
            onClick={() => void doing(start)}
          >
            {t('skills.framework.start')}
          </button>
        </>
      }
    >
      <Write
        label={t('skills.framework.name')}
        hint={t('skills.framework.nameSaid')}
        value={name}
        autoFocus
        onChange={(event) => setName(event.target.value)}
      />
      <Write
        label={t('skills.framework.key')}
        hint={t('skills.framework.keySaid')}
        value={key}
        onChange={(event) => setKey(event.target.value)}
      />
      <Write
        label={t('skills.framework.version')}
        hint={t('skills.framework.versionSaid')}
        problem={problem}
        value={version}
        onChange={(event) => setVersion(event.target.value)}
      />
    </Sheet>
  )
}

// ---------------------------------------------------------------------------
// One curriculum's outcomes
// ---------------------------------------------------------------------------

export function FrameworkSkills() {
  const { frameworkId = '' } = useParams()
  const { t } = useI18n()
  const languages = useContentLanguages()
  const [words, setWords] = useState('')
  const [state, setState] = useState('')
  const [importing, setImporting] = useState(false)
  const [open, setOpen] = useState<Skill | null>(null)

  const asked = useSettled(words, 400)
  const frameworks = useLoad(() => studio.frameworks(), [])
  const skills = useLoad(
    () => studio.skills(frameworkId, { search: asked.trim() || undefined, reviewState: state || undefined, take: 200 }),
    [frameworkId, asked, state],
  )

  const framework = (frameworks.data ?? []).find((one) => one.id === frameworkId)

  useLedge(
    <>
      <span className="engraved">{framework?.name ?? t('skills.frameworks')}</span>
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => void studio.outcomesTemplate()}>
          {t('skills.template')}
        </button>
        <button type="button" className="act first" onClick={() => setImporting(true)}>
          {t('skills.import')}
        </button>
      </div>
    </>,
    [framework?.name, t],
  )

  const rows = skills.data ?? []
  const first = languages[0]

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{framework?.name ?? t('skills.frameworks')}</h1>
        <span className="engraved">{framework ? `${framework.frameworkKey} · ${framework.versionLabel}` : ''}</span>
      </div>

      <div className="spread">
        <div className="grow">
          <Write label={t('skills.search')} value={words} autoFocus onChange={(event) => setWords(event.target.value)} />
        </div>
        <Choose label={t('skills.state.all')} value={state} onChange={(event) => setState(event.target.value)}>
          <option value="">{t('skills.state.all')}</option>
          <option value="Unreviewed">{t('skills.state.Unreviewed')}</option>
          <option value="Reviewed">{t('skills.state.Reviewed')}</option>
          <option value="Deprecated">{t('skills.state.Deprecated')}</option>
        </Choose>
      </div>

      {skills.loading ? (
        <Wiping rows={6} />
      ) : rows.length === 0 ? (
        <Nothing title={asked.trim() ? t('skills.nothing') : t('skills.framework.none')}>
          {asked.trim() ? t('skills.nothingSaid') : t('skills.framework.noneSaid')}
        </Nothing>
      ) : (
        <div className="rows">
          {rows.map((skill) => (
            <button type="button" className="row" key={skill.id} onClick={() => setOpen(skill)}>
              <span className="row-main">
                <span className="row-title" dir={first?.direction} lang={first?.code}>
                  {(first && skill.statements[first.id]) ?? Object.values(skill.statements)[0] ?? skill.targetKey}
                </span>
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {skill.targetKey} · {t(`skills.kind.${skill.targetKindKey}`)}
                  {skill.isEdited ? ` · ${t('skills.edited')}` : ''}
                </span>
              </span>
              <span className="row-side">
                <Counted count={skill.questions} name="skills.questions" />
                <SkillState state={skill.reviewState} />
              </span>
            </button>
          ))}
        </div>
      )}

      <BringIn
        open={importing}
        frameworkId={frameworkId}
        onClose={() => setImporting(false)}
        onDone={() => {
          setImporting(false)
          skills.reload()
          frameworks.reload()
        }}
      />

      {open ? (
        <OneSkill
          skill={open}
          onClose={() => setOpen(null)}
          onChanged={() => {
            setOpen(null)
            skills.reload()
            frameworks.reload()
          }}
        />
      ) : null}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Bringing in a filled sheet — read what it would do first, every time
// ---------------------------------------------------------------------------

function BringIn({
  open,
  frameworkId,
  onClose,
  onDone,
}: {
  open: boolean
  frameworkId: string
  onClose: () => void
  onDone: () => void
}) {
  const { t, tError } = useI18n()
  const say = useSaying()
  const [file, setFile] = useState<File | null>(null)
  const [trial, setTrial] = useState<SkillImport | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const read = async (chosen: File) => {
    setProblem(null)
    setTrial(null)
    setFile(chosen)
    try {
      setTrial(await studio.importOutcomes(frameworkId, chosen, true))
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  const bringIn = async () => {
    if (!file) return
    const done = await studio.importOutcomes(frameworkId, file, false)
    say(done.added + done.updated === 0 ? t('skills.import.nothing') : t('skills.import.done'))
    onDone()
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={t('skills.import')}
      actions={
        <>
          <button type="button" className="act" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="act first"
            disabled={busy || trial === null || trial.problems.length > 0}
            onClick={() => void doing(bringIn)}
          >
            {t('skills.import.doIt')}
          </button>
        </>
      }
    >
      <div className="field">
        <span className="label">{t('skills.import.choose')}</span>
        <input
          type="file"
          accept=".xlsx"
          className="write"
          onChange={(event) => {
            const chosen = event.target.files?.[0]
            if (chosen) void read(chosen)
          }}
        />
        {problem ? <Beside tone="wrong">{problem}</Beside> : <Beside>{t('skills.import.chooseSaid')}</Beside>}
      </div>

      {file && !trial && !problem ? <p className="said">{t('skills.import.trying')}</p> : null}

      {trial ? (
        <section className="band">
          <span className="engraved">{t('skills.import.would')}</span>

          <Tally
            lines={[
              [t('skills.import.rows'), trial.rowsRead],
              [t('skills.import.added'), trial.added],
              [t('skills.import.updated'), trial.updated],
              [t('skills.import.leftAlone'), trial.leftAlone],
            ]}
          />

          {trial.leftAlone > 0 ? <Beside>{t('skills.import.leftAloneSaid')}</Beside> : null}

          {trial.problems.length > 0 ? (
            <>
              <Mark stroke="wrong">{t('skills.import.problems')}</Mark>
              <Beside tone="wrong">{t('skills.import.problemsSaid')}</Beside>
              <div className="rows">
                {trial.problems.map((one, index) => (
                  <div className="row" key={`${one.row}-${one.code}-${index}`}>
                    <span className="row-main">
                      <span className="row-title">{one.message}</span>
                    </span>
                    {one.row > 0 ? (
                      <span className="row-side">
                        <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                          {t('skills.import.row')} <span className="num">{one.row}</span>
                        </span>
                      </span>
                    ) : null}
                  </div>
                ))}
              </div>
            </>
          ) : null}
        </section>
      ) : null}
    </Sheet>
  )
}

// ---------------------------------------------------------------------------
// One outcome: reword it, say what kind it is, confirm it
// ---------------------------------------------------------------------------

function OneSkill({ skill, onClose, onChanged }: { skill: Skill; onClose: () => void; onChanged: () => void }) {
  const { t, tError } = useI18n()
  const say = useSaying()
  const languages = useContentLanguages()
  const [statements, setStatements] = useState<Record<string, string>>(skill.statements)
  const [kind, setKind] = useState<SkillKind>(skill.targetKindKey)
  const [band, setBand] = useState(skill.difficultyBand === null ? '' : String(skill.difficultyBand))
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const save = async () => {
    setProblem(null)
    try {
      await studio.editSkill(skill.id, {
        statements,
        targetKindKey: kind,
        difficultyBand: band === '' ? null : Number(band),
      })
      say(t('common.saved'))
      onChanged()
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  const setState = async (state: SkillReviewState) => {
    setProblem(null)
    try {
      await studio.setSkillState(skill.id, state)
      say(t(`skills.state.${state}`))
      onChanged()
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  return (
    <Sheet
      open
      onClose={onClose}
      title={skill.targetKey}
      actions={
        <>
          {skill.reviewState === 'Reviewed' ? (
            <button type="button" className="act" disabled={busy} onClick={() => void doing(() => setState('Unreviewed'))}>
              {t('skills.unconfirm')}
            </button>
          ) : (
            <button type="button" className="act" disabled={busy} onClick={() => void doing(() => setState('Reviewed'))}>
              {t('skills.confirm')}
            </button>
          )}
          <button type="button" className="act first" disabled={busy} onClick={() => void doing(save)}>
            {t('common.save')}
          </button>
        </>
      }
    >
      <div className="spread">
        <SkillState state={skill.reviewState} />
        <Counted count={skill.questions} name="skills.questions" />
        {skill.replaced > 0 ? <Counted count={skill.replaced} name="skills.replaced" /> : null}
      </div>

      {skill.questions === 0 ? <Beside>{t('skills.noQuestions')}</Beside> : null}
      {skill.isEdited ? <Beside>{t('skills.editedSaid')}</Beside> : null}

      {languages.map((language, index) => (
        <Write
          key={language.id}
          label={`${t('skills.statement')} — ${language.name}`}
          hint={index === 0 ? t('skills.statementSaid') : undefined}
          lines={2}
          dir={language.direction as 'ltr' | 'rtl'}
          lang={language.code}
          value={statements[language.id] ?? ''}
          onChange={(event) => setStatements({ ...statements, [language.id]: event.target.value })}
        />
      ))}

      <div className="spread">
        <Choose
          label={t('skills.kind.skill')}
          value={kind}
          onChange={(event) => setKind(event.target.value as SkillKind)}
        >
          <option value="skill">{t('skills.kind.skill')}</option>
          <option value="concept">{t('skills.kind.concept')}</option>
          <option value="procedure">{t('skills.kind.procedure')}</option>
        </Choose>
        <Choose label={t('skills.band')} value={band} onChange={(event) => setBand(event.target.value)}>
          <option value="">{t('skills.bandNone')}</option>
          {[1, 2, 3, 4, 5, 6, 7, 8, 9].map((one) => (
            <option key={one} value={one}>
              {one}
            </option>
          ))}
        </Choose>
      </div>
      {problem ? <Beside tone="wrong">{problem}</Beside> : <Beside>{t('skills.bandSaid')}</Beside>}
    </Sheet>
  )
}
