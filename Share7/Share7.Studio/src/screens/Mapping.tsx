import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useContentLanguages, useLedge } from '../App'
import { Beside, Choose, Counted, Mark, Nothing, Sheet, Wiping, Write, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import {
  studio,
  type ItemSkillLine,
  type QuestionToMap,
  type Skill,
  type SkillKind,
  type StandIn,
} from '../lib/studio'
import { useDoing, useLoad, useSettled } from '../lib/use'
import { SkillState, Tally, saidWrong } from './bits'

// ===========================================================================
// What the questions measure
//
// One subject at a time, because that is the only way it can be done: a
// specialist who knows mathematics maps mathematics. The list opens on the
// questions mapped to nothing, then the ones still on the stand-in their
// lesson was minted with, then the ones that are done — so it is a list that
// gets shorter rather than one nobody starts.
//
// Two ways to finish a stand-in. Where the official document already has the
// claim, the questions move onto it and nothing is written; where it does not,
// the team mints the claim itself. Either way every answer a child has already
// given is re-read against the real claim it was always about.
// ===========================================================================

export function Mapping() {
  const { subjectNodeId = '' } = useParams()
  const { t } = useI18n()
  const languages = useContentLanguages()
  const written = languages[0]
  const [state, setState] = useState('')
  const [open, setOpen] = useState<QuestionToMap | null>(null)
  const [replacing, setReplacing] = useState(false)
  const [building, setBuilding] = useState(false)

  const progress = useLoad(() => studio.subjectProgress(subjectNodeId, written?.id), [subjectNodeId, written?.id])
  const questions = useLoad(
    () => studio.questionsToMap(subjectNodeId, { langId: written?.id, state: state || undefined, take: 50 }),
    [subjectNodeId, written?.id, state],
  )
  const standIns = useLoad(() => studio.standIns(subjectNodeId, written?.id), [subjectNodeId, written?.id])

  const done = progress.data
  const rows = questions.data ?? []
  const left = (done?.onPlaceholders ?? 0) + (done?.unmapped ?? 0)

  useLedge(
    <>
      <span className="engraved">{done?.subjectTitle ?? t('mapping.title')}</span>
      {/* Two, not three. Getting back to the curriculum is a place along the
          top of the frame, not an act, and a ledge carrying navigation beside
          its one yellow action is a ledge that has stopped meaning anything. */}
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => setBuilding(true)}>
          {t('exams.buildPaper')}
        </button>
        {(standIns.data?.length ?? 0) > 0 ? (
          <button type="button" className="act first" onClick={() => setReplacing(true)}>
            {t('mapping.replace')}
          </button>
        ) : null}
      </div>
    </>,
    [done?.subjectTitle, standIns.data?.length, t],
  )

  const reload = () => {
    progress.reload()
    questions.reload()
    standIns.reload()
  }

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{done?.subjectTitle || t('mapping.title')}</h1>
        <span className="engraved">{t('mapping.said')}</span>
      </div>

      {progress.loading ? (
        <Wiping rows={3} />
      ) : !done ? (
        <Nothing title={t('mapping.none')}>{t('mapping.noneSaid')}</Nothing>
      ) : (
        <section className="band">
          <span className="engraved">{t('mapping.subject')}</span>

          <Tally
            lines={[
              [t('recovery.lessons'), done.lessons],
              [t('skills.questions'), done.questions],
              [t('mapping.progress'), done.mappedToSkills],
              [t('mapping.onPlaceholders'), done.onPlaceholders],
              [t('mapping.unmapped'), done.unmapped],
            ]}
          />

          {/* The gate, said in words rather than as a bar: a subject is mapped
              or it is not, and "83% mapped" is a number that hides the work. */}
          {done.isComplete ? (
            <Mark stroke="written">{t('mapping.complete')}</Mark>
          ) : (
            <Mark stroke="writing">{t('mapping.left', { count: left })}</Mark>
          )}
        </section>
      )}

      <div className="spread">
        <Choose label={t('mapping.show')} value={state} onChange={(event) => setState(event.target.value)}>
          <option value="">{t('common.all')}</option>
          <option value="unmapped">{t('mapping.unmapped')}</option>
          <option value="standIn">{t('mapping.onPlaceholders')}</option>
          <option value="mapped">{t('mapping.progress')}</option>
        </Choose>
      </div>

      {questions.loading ? (
        <Wiping rows={6} />
      ) : rows.length === 0 ? (
        <Nothing title={t('mapping.none')}>{t('mapping.noneSaid')}</Nothing>
      ) : (
        <div className="rows">
          {rows.map((one) => (
            <button type="button" className="row" key={one.itemId} onClick={() => setOpen(one)}>
              <span className="row-main">
                <span className="row-title" dir={one.text ? written?.direction : undefined} lang={one.text ? written?.code : undefined}>
                  {one.text || t('quality.noWords')}
                </span>
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {one.lessonTitle}
                  {one.role === 'Recovery' ? ` · ${t('lesson.recovery')}` : ''}
                </span>
              </span>
              <span className="row-side">
                {one.primaryTargetId === null ? (
                  <Mark stroke="wrong">{t('mapping.unmapped')}</Mark>
                ) : one.onStandIn ? (
                  <Mark stroke="waiting">{t('mapping.onPlaceholders')}</Mark>
                ) : (
                  <Mark stroke="written">{t('mapping.progress')}</Mark>
                )}
              </span>
            </button>
          ))}
        </div>
      )}

      {open ? (
        <MapOne
          question={open}
          onClose={() => setOpen(null)}
          onMapped={() => {
            setOpen(null)
            reload()
          }}
        />
      ) : null}

      <Replace
        open={replacing}
        standIns={standIns.data ?? []}
        onClose={() => setReplacing(false)}
        onDone={() => {
          setReplacing(false)
          reload()
        }}
      />

      <BuildPaper
        open={building}
        subjectNodeId={subjectNodeId}
        standInLines={done?.onPlaceholders ?? 0}
        onClose={() => setBuilding(false)}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// What one question is mainly about
// ---------------------------------------------------------------------------

function MapOne({
  question,
  onClose,
  onMapped,
}: {
  question: QuestionToMap
  onClose: () => void
  onMapped: () => void
}) {
  const { t, tError } = useI18n()
  const say = useSaying()
  const languages = useContentLanguages()
  const written = languages[0]
  const [words, setWords] = useState('')
  const [picked, setPicked] = useState<Skill | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const asked = useSettled(words, 400)
  const frameworks = useLoad(() => studio.frameworks(), [])
  const real = (frameworks.data ?? []).filter((one) => !one.isPlaceholders)

  const hits = useLoad(
    () =>
      asked.trim().length >= 2 && real.length > 0
        ? studio.skills(real[0].id, { langId: written?.id, search: asked.trim(), take: 25 })
        : Promise.resolve([]),
    [asked, real.length, written?.id],
  )

  const save = async () => {
    if (!picked) return
    setProblem(null)
    try {
      const lines: ItemSkillLine[] = [{ targetId: picked.id, emphasis: 1, isPrimary: true }]
      await studio.mapQuestion(question.itemId, lines)
      say(t('mapping.saved'))
      onMapped()
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  return (
    <Sheet
      open
      onClose={onClose}
      title={t('mapping.pick')}
      actions={
        <>
          <button type="button" className="act" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="act first" disabled={busy || !picked} onClick={() => void doing(save)}>
            {t('mapping.save')}
          </button>
        </>
      }
    >
      <p className="said" dir={question.text ? written?.direction : undefined} lang={question.text ? written?.code : undefined}>
        {question.text || t('quality.noWords')}
      </p>

      {question.primaryStatement ? (
        <Beside>
          {t('mapping.nowMeasures')}: {question.primaryStatement}
        </Beside>
      ) : (
        <Beside tone="wrong">{t('mapping.unmapped')}</Beside>
      )}

      {real.length === 0 ? (
        <Nothing title={t('skills.framework.none')}>{t('skills.framework.noneSaid')}</Nothing>
      ) : (
        <>
          <Write
            label={t('skills.search')}
            hint={t('mapping.pickSaid')}
            value={words}
            autoFocus
            onChange={(event) => {
              setPicked(null)
              setWords(event.target.value)
            }}
          />

          {hits.loading ? (
            <Wiping rows={3} />
          ) : (hits.data?.length ?? 0) === 0 ? (
            asked.trim().length >= 2 ? (
              <Nothing title={t('skills.nothing')}>{t('skills.nothingSaid')}</Nothing>
            ) : null
          ) : (
            <div className="rows">
              {hits.data?.map((skill) => (
                <button
                  type="button"
                  className="row"
                  key={skill.id}
                  aria-pressed={picked?.id === skill.id}
                  onClick={() => setPicked(skill)}
                >
                  <span className="row-main">
                    <span className="row-title" dir={written?.direction} lang={written?.code}>
                      {(written && skill.statements[written.id]) ?? Object.values(skill.statements)[0] ?? skill.targetKey}
                    </span>
                    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                      {skill.targetKey}
                    </span>
                  </span>
                  <span className="row-side">
                    <SkillState state={skill.reviewState} />
                  </span>
                </button>
              ))}
            </div>
          )}
        </>
      )}

      {problem ? <Beside tone="wrong">{problem}</Beside> : null}
    </Sheet>
  )
}

// ---------------------------------------------------------------------------
// A paper over this subject
// ---------------------------------------------------------------------------

/**
 * Share7's own benchmark, and it says so on its own face. A structural
 * derivation from the platform's own content is a legitimate benchmark and an
 * illegitimate national paper; the warning is not a disclaimer, it is the
 * difference between the two.
 */
function BuildPaper({
  open,
  subjectNodeId,
  standInLines,
  onClose,
}: {
  open: boolean
  subjectNodeId: string
  standInLines: number
  onClose: () => void
}) {
  const { t, tError } = useI18n()
  const say = useSaying()
  const navigate = useNavigate()
  const languages = useContentLanguages()
  const [label, setLabel] = useState(String(new Date().getFullYear()))
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const build = async () => {
    setProblem(null)
    try {
      const report = await studio.buildBenchmark(subjectNodeId, label.trim(), languages[0]?.id)
      say(t('exams.built', { areas: report.areas, lines: report.lines }))
      onClose()
      navigate(`/exams/${report.blueprintId}`)
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={t('exams.benchmark')}
      actions={
        <>
          <button type="button" className="act" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="act first" disabled={busy} onClick={() => void doing(build)}>
            {t('exams.benchmark')}
          </button>
        </>
      }
    >
      <p className="said">{t('exams.benchmarkSaid')}</p>
      <Beside tone="live">{t('exams.benchmarkWarning')}</Beside>

      {standInLines > 0 ? <Beside tone="wrong">{t('exams.cannotProve')}</Beside> : null}

      <Write
        label={t('skills.framework.version')}
        hint={t('skills.framework.versionSaid')}
        problem={problem}
        value={label}
        autoFocus
        onChange={(event) => setLabel(event.target.value)}
      />
    </Sheet>
  )
}

// ---------------------------------------------------------------------------
// Replacing a stand-in
// ---------------------------------------------------------------------------

function Replace({
  open,
  standIns,
  onClose,
  onDone,
}: {
  open: boolean
  standIns: StandIn[]
  onClose: () => void
  onDone: () => void
}) {
  const { t, tError } = useI18n()
  const say = useSaying()
  const languages = useContentLanguages()
  const [chosen, setChosen] = useState<string[]>([])
  const [mint, setMint] = useState(false)
  const [key, setKey] = useState('')
  const [statements, setStatements] = useState<Record<string, string>>({})
  const [kind, setKind] = useState<SkillKind>('skill')
  const [words, setWords] = useState('')
  const [picked, setPicked] = useState<Skill | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, doing] = useDoing()

  const asked = useSettled(words, 400)
  const frameworks = useLoad(() => studio.frameworks(), [])
  const real = (frameworks.data ?? []).filter((one) => !one.isPlaceholders)

  const hits = useLoad(
    () =>
      !mint && asked.trim().length >= 2 && real.length > 0
        ? studio.skills(real[0].id, { langId: languages[0]?.id, search: asked.trim(), take: 25 })
        : Promise.resolve([]),
    [asked, mint, real.length, languages],
  )

  const go = async () => {
    setProblem(null)
    try {
      const report = await studio.promote({
        targetKey: mint ? key.trim() : '',
        statements: mint ? statements : {},
        targetKindKey: kind,
        replacesTargetIds: chosen,
        useExistingTargetId: mint ? null : (picked?.id ?? null),
      })
      say(t('mapping.replaced', { questions: report.questionsMoved, answers: report.answersRebuilt }))
      setChosen([])
      setPicked(null)
      onDone()
    } catch (error) {
      setProblem(saidWrong(tError, error))
    }
  }

  const ready = chosen.length > 0 && (mint ? key.trim().length >= 2 : picked !== null)

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={t('mapping.replace')}
      actions={
        <>
          <button type="button" className="act" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="button" className="act first" disabled={busy || !ready} onClick={() => void doing(go)}>
            {t('mapping.replace')}
          </button>
        </>
      }
    >
      <p className="said">{t('mapping.replaceSaid')}</p>

      <section className="band">
        <span className="engraved">{t('mapping.standIns')}</span>
        <div className="rows">
          {standIns.slice(0, 40).map((one) => (
            <button
              type="button"
              className="row"
              key={one.targetId}
              aria-pressed={chosen.includes(one.targetId)}
              onClick={() =>
                setChosen(
                  chosen.includes(one.targetId)
                    ? chosen.filter((id) => id !== one.targetId)
                    : [...chosen, one.targetId],
                )
              }
            >
              <span className="row-main">
                <span className="row-title">{one.nodeTitle ?? one.statement}</span>
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {one.statement}
                </span>
              </span>
              <span className="row-side">
                <Counted count={one.questions} name="skills.questions" />
                <Counted count={one.answers} name="quality.answers" />
                {one.isReplaced ? (
                  <Mark stroke="ended">{t('mapping.alreadyReplaced')}</Mark>
                ) : (
                  <Mark stroke="waiting">{t('skills.framework.toReplace')}</Mark>
                )}
              </span>
            </button>
          ))}
        </div>
      </section>

      <div className="segments" role="group">
        <button type="button" aria-pressed={!mint} onClick={() => setMint(false)}>
          {t('mapping.useImported')}
        </button>
        <button type="button" aria-pressed={mint} onClick={() => setMint(true)}>
          {t('mapping.mintNew')}
        </button>
      </div>

      {mint ? (
        <>
          <Beside>{t('mapping.mintSaid')}</Beside>
          <Write
            label={t('skills.framework.key')}
            hint={t('mapping.keySaid')}
            value={key}
            onChange={(event) => setKey(event.target.value)}
          />
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
          <Choose label={t('skills.kind.skill')} value={kind} onChange={(event) => setKind(event.target.value as SkillKind)}>
            <option value="skill">{t('skills.kind.skill')}</option>
            <option value="concept">{t('skills.kind.concept')}</option>
            <option value="procedure">{t('skills.kind.procedure')}</option>
          </Choose>
        </>
      ) : (
        <>
          <Beside>{t('mapping.useImportedSaid')}</Beside>
          <Write
            label={t('skills.search')}
            value={words}
            onChange={(event) => {
              setPicked(null)
              setWords(event.target.value)
            }}
          />
          {hits.loading ? (
            <Wiping rows={3} />
          ) : (
            <div className="rows">
              {hits.data?.map((skill) => (
                <button
                  type="button"
                  className="row"
                  key={skill.id}
                  aria-pressed={picked?.id === skill.id}
                  onClick={() => setPicked(skill)}
                >
                  <span className="row-main">
                    <span className="row-title">
                      {(languages[0] && skill.statements[languages[0].id]) ??
                        Object.values(skill.statements)[0] ??
                        skill.targetKey}
                    </span>
                    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                      {skill.targetKey}
                    </span>
                  </span>
                  <span className="row-side">
                    <SkillState state={skill.reviewState} />
                  </span>
                </button>
              ))}
            </div>
          )}
        </>
      )}

      {problem ? <Beside tone="wrong">{problem}</Beside> : null}
    </Sheet>
  )
}
