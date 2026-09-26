import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useContentLanguages, useLanguages, useLedge } from '../App'
import { Mark, Nothing, Sheet, Trail, Wiping, useSaying, useTelling } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { curriculumOfTrail, useCurricula, useCurriculumName } from '../lib/curricula'
import { StudioError } from '../lib/api'
import {
  studio,
  type CommentAnchor,
  type ContentProblem,
  type Draft,
  type DraftItem,
  type ImportTrial,
  type LessonContent,
  type LessonProposal,
} from '../lib/studio'
import { useDoing, useLoad, useSettled, useTitle, useTrail } from '../lib/use'
import { Comments } from './Comments'
import { DraftState } from './bits'
import { Written } from './Written'

// ===========================================================================
// A lesson's board
//
// What students have now, and what the team is proposing instead, written out
// as one lesson. The team shares one draft: two people on the same lesson edit
// the same board, saved as it is typed, and a save that lands on somebody
// else's newer version says so rather than quietly winning.
//
// Nothing on this board reaches a student. A second person approves it and a
// Lead releases it; until then the lesson the game serves is untouched.
// ===========================================================================

const SAVE_AFTER = 900
const PRESENCE_EVERY = 45_000

export function Lesson() {
  const { lessonId = '' } = useParams()
  const { t, formatRelative } = useI18n()
  const navigate = useNavigate()
  const languages = useLanguages()
  const content = useContentLanguages()
  const title = useTitle()
  const trailOf = useTrail()
  const { list } = useCurricula()
  const curriculumName = useCurriculumName()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()

  const workspace = useLoad(() => studio.lesson(lessonId), [lessonId])
  const draftId = workspace.data?.openDraft?.id ?? null
  const draft = useLoad<Draft | null>(() => (draftId ? studio.draft(draftId) : Promise.resolve(null)), [draftId])
  const comments = useLoad(() => (draftId ? studio.comments(draftId) : Promise.resolve([])), [draftId])

  const live = workspace.data?.live ?? null
  const open = draft.data

  const [items, setItems] = useState<DraftItem[]>([])
  const [revision, setRevision] = useState(0)
  const [problems, setProblems] = useState<ContentProblem[]>([])
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [alsoHere, setAlsoHere] = useState<string[]>([])
  const [anchor, setAnchor] = useState<CommentAnchor | null>(null)
  const [trial, setTrial] = useState<ImportTrial | null>(null)
  const [sendBack, setSendBack] = useState(false)
  const [note, setNote] = useState('')
  const dirty = useRef(false)

  // The draft as it arrives becomes the board; from then on the board is the
  // truth until a save answers, or somebody else's save makes us reload.
  useEffect(() => {
    if (!open) {
      setItems(live ? fromLive(live) : [])
      setRevision(0)
      setProblems([])
      dirty.current = false
      return
    }

    setItems(((open.proposal as LessonProposal | null)?.items ?? []).map(clean))
    setRevision(open.summary.revision)
    setProblems(open.problems)
    setSavedAt(open.summary.updatedAtUtc)
    setAlsoHere(open.alsoHere.map((person) => person.name))
    dirty.current = false
  }, [open, live])

  const settled = useSettled(items, SAVE_AFTER)
  const mayEdit = open?.can.edit ?? false

  // Autosave. The revision we loaded goes up with it: if somebody else saved in
  // between, the server refuses and we take their version rather than flattening it.
  useEffect(() => {
    if (!open || !mayEdit || !dirty.current) return

    let alive = true
    setSaving(true)

    studio
      .saveDraft(open.summary.id, revision, { items: settled } satisfies LessonProposal)
      .then((saved) => {
        if (!alive) return
        setRevision(saved.summary.revision)
        setProblems(saved.problems)
        setSavedAt(saved.summary.updatedAtUtc)
        dirty.current = false
      })
      .catch((error) => {
        if (!alive) return
        saying(error)
        if (error instanceof StudioError && error.code === 'DRAFT_REVISION_MOVED') draft.reload()
      })
      .finally(() => {
        if (alive) setSaving(false)
      })

    return () => {
      alive = false
    }
    // Only a settled change saves; everything else is read at the moment it runs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settled])

  // "I have this open" — a heartbeat, not a lock, answering with who else does.
  useEffect(() => {
    if (!draftId) return
    let alive = true

    const beat = () =>
      studio
        .presence(draftId)
        .then((people) => {
          if (alive) setAlsoHere(people.map((person) => person.name))
        })
        .catch(() => {
          // Presence is a courtesy; its failure is never worth a message.
        })

    void beat()
    const timer = window.setInterval(beat, PRESENCE_EVERY)
    return () => {
      alive = false
      window.clearInterval(timer)
    }
  }, [draftId])

  const change = (next: DraftItem[]) => {
    dirty.current = true
    setItems(next)
  }

  const act = async (work: () => Promise<unknown>, told?: string) =>
    run(async () => {
      try {
        await work()
        draft.reload()
        workspace.reload()
        if (told) say(told)
        return true
      } catch (error) {
        saying(error)
        return false
      }
    })

  // What goes live is what is on the board, so a change still on its way to the server holds it.
  const pending = saving || dirty.current
  const lead = open?.can.releaseNow ?? false

  const startWriting = () =>
    run(async () => {
      try {
        await studio.startDraft({ kind: 'LessonContent', nodeId: lessonId })
        workspace.reload()
      } catch (error) {
        saying(error)
      }
    })

  useLedge(
    <>
      <span className="engraved">
        {open
          ? saving
            ? t('common.saving')
            : savedAt
              ? t('common.savedAt', { time: formatRelative(savedAt) })
              : t('lesson.draft')
          : t('lesson.live')}
      </span>

      {open && problems.length > 0 ? (
        <Mark stroke="wrong">{t('lesson.checksCount', { count: problems.length })}</Mark>
      ) : null}
      {open && problems.length === 0 ? <Mark stroke="written">{t('lesson.checksClear')}</Mark> : null}
      {alsoHere.length > 0 ? (
        <Mark stroke="live">
          {t(alsoHere.length > 1 ? 'lesson.alsoHerePlural' : 'lesson.alsoHere', { names: alsoHere.join(', ') })}
        </Mark>
      ) : null}

      <div className="ledge-end">
        {!open ? (
          <button type="button" className="act first" onClick={() => void startWriting()} disabled={busy}>
            {t('lesson.startDraft')}
          </button>
        ) : (
          <>
            <button type="button" className="act" disabled={busy} onClick={() => void act(() => studio.check(open.summary.id))}>
              {t('lesson.checks')}
            </button>

            {open.can.review ? (
              <>
                <button type="button" className="act" disabled={busy} onClick={() => setSendBack(true)}>
                  {t('review.requestChanges')}
                </button>
                <button
                  type="button"
                  className={lead ? 'act' : 'act first'}
                  disabled={busy}
                  onClick={() => void act(() => studio.approve(open.summary.id, open.summary.revision), t('review.approve'))}
                >
                  {t('review.approve')}
                </button>
              </>
            ) : open.summary.status === 'InReview' ? (
              <button
                type="button"
                className="act"
                disabled={busy}
                onClick={() => void act(() => studio.withdraw(open.summary.id, open.summary.revision))}
              >
                {t('lesson.withdraw')}
              </button>
            ) : lead && open.summary.status === 'Approved' ? null : (
              <button
                type="button"
                className={lead ? 'act' : 'act first'}
                disabled={busy || !open.can.submit || problems.length > 0}
                onClick={() => void act(() => studio.submit(open.summary.id, revision), t('lesson.submit'))}
              >
                {t('lesson.submit')}
              </button>
            )}

            {/* A Lead needs nobody else: their one action is to put it live. */}
            {lead ? (
              <button
                type="button"
                className="act first"
                disabled={busy || pending || (open.summary.status !== 'Approved' && problems.length > 0)}
                onClick={() => void act(() => studio.releaseNow(open.summary.id, revision), t('lesson.releasedNow'))}
              >
                {t('lesson.releaseNow')}
              </button>
            ) : null}
          </>
        )}
      </div>
    </>,
    [open?.summary.id, open?.summary.status, open?.can.review, lead, pending, saving, savedAt, problems.length, alsoHere.join(), busy, revision, t],
  )

  if (workspace.loading) return <Wiping rows={6} tall />
  if (!workspace.data)
    return (
      <Nothing
        title={t('errors.node.notFound')}
        action={
          <Link to="/curriculum" className="act">
            {t('place.curriculum')}
          </Link>
        }
      />
    )

  const lesson = workspace.data.lesson
  const curriculum = list.find((one) => one.id === lesson.curriculumId) ?? curriculumOfTrail(list, workspace.data.trail)

  // The list, the curriculum by name, then the way down — without a declared curriculum's root,
  // which the second step already names, and without the lesson, which the heading below says.
  const wholeTrail = trailOf(workspace.data.trail.filter((step) => step.kind !== 'curriculum'), languages).slice(0, -1)

  return (
    <div className="stack loose">
      <div className="stack tight">
        <Trail
          steps={[
            { id: 'root', label: t('curricula.title') },
            { id: 'curriculum', label: curriculumName(curriculum) },
            ...wholeTrail,
          ]}
          linkAll
          onGo={(id) =>
            navigate(
              id === 'root' ? '/curriculum' : id === 'curriculum' ? `/curriculum/of/${curriculum?.id ?? ''}` : `/curriculum/${id}`,
            )
          }
        />

        <div className="heading">
          <h1>{title(lesson.titles, languages)}</h1>
          {open ? <DraftState status={open.summary.status} /> : <Mark stroke="written">{t('lesson.live')}</Mark>}
          {open?.summary.isPractice ? <Mark stroke="live">{t('lesson.practice')}</Mark> : null}
        </div>

        <LiveVersions live={live} />
      </div>

      {open?.summary.isOutOfDate ? (
        <section className="band">
          <Mark stroke="wrong">{t('lesson.outOfDate')}</Mark>
          <p className="said">{t('lesson.outOfDateSaid')}</p>
          <button
            type="button"
            className="act"
            style={{ justifySelf: 'start' }}
            disabled={busy}
            onClick={() =>
              void act(
                () => studio.bringUpToDate(open.summary.id, revision, { items } satisfies LessonProposal),
                t('lesson.bringUpToDate'),
              )
            }
          >
            {t('lesson.bringUpToDate')}
          </button>
        </section>
      ) : null}

      {!open && !draft.loading ? (
        <section className="band">
          <Nothing
            title={t('lesson.noDraft')}
            action={
              <button type="button" className="act" onClick={() => void startWriting()} disabled={busy}>
                {t('lesson.startDraft')}
              </button>
            }
          >
            {t('lesson.noDraftSaid')}
          </Nothing>
        </section>
      ) : null}

      {draft.loading ? (
        <Wiping rows={5} tall />
      ) : (
        <Written
          items={items}
          languages={content}
          problems={problems}
          comments={comments.data ?? []}
          live={live}
          standing={Boolean(open)}
          readOnly={!mayEdit}
          onChange={mayEdit ? change : undefined}
          onComment={open ? setAnchor : undefined}
        />
      )}

      {open ? (
        <>
          <Excel
            lessonId={lessonId}
            draftId={open.summary.id}
            revision={revision}
            mayEdit={mayEdit}
            onImported={() => draft.reload()}
            onTrial={setTrial}
          />

          <section className="band">
            <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('review.comments')}</h2>
            <Comments
              draftId={open.summary.id}
              comments={comments.data ?? []}
              anchor={anchor}
              onChanged={() => comments.reload()}
              onClearAnchor={() => setAnchor(null)}
            />
          </section>

          <Told draft={open} />

          {open.can.discard ? (
            <section className="band">
              <p className="said">{t('lesson.discardSaid')}</p>
              <button
                type="button"
                className="act grave"
                style={{ justifySelf: 'start' }}
                disabled={busy}
                onClick={() => void act(() => studio.discard(open.summary.id), t('common.done'))}
              >
                {t('lesson.discard')}
              </button>
            </section>
          ) : null}
        </>
      ) : null}

      <Sheet
        open={trial !== null}
        onClose={() => setTrial(null)}
        title={t('lesson.trial')}
        actions={
          <button type="button" className="act" onClick={() => setTrial(null)}>
            {t('common.close')}
          </button>
        }
      >
        {trial ? <TrialReport trial={trial} /> : null}
      </Sheet>

      <Sheet
        open={sendBack}
        onClose={() => setSendBack(false)}
        title={t('review.requestChanges')}
        actions={
          <>
            <button type="button" className="act" onClick={() => setSendBack(false)}>
              {t('common.cancel')}
            </button>
            <button
              type="button"
              className="act first"
              disabled={busy || note.trim() === '' || !open}
              onClick={async () => {
                if (!open) return
                const done = await act(
                  () => studio.requestChanges(open.summary.id, open.summary.revision, note.trim()),
                  t('review.requestChanges'),
                )
                if (done) {
                  setSendBack(false)
                  setNote('')
                }
              }}
            >
              {t('review.requestChanges')}
            </button>
          </>
        }
      >
        <div className="field">
          <label htmlFor="send-back-note">{t('review.note')}</label>
          <textarea
            id="send-back-note"
            className="write"
            rows={4}
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />
          <p className="beside">{t('review.noteRequired')}</p>
        </div>
      </Sheet>
    </div>
  )
}

/** What the game is serving right now, per pool and language. */
function LiveVersions({ live }: { live: LessonContent | null }) {
  const { t } = useI18n()
  const languages = useLanguages()

  if (!live || live.sets.length === 0) return null

  return (
    <div className="people">
      {live.sets.map((set) => {
        const language = languages.find((one) => one.id === set.langId)
        const pool = set.role === 'Recovery' ? ` · ${t('lesson.recovery')}` : ''
        const count = `${set.itemCount} ${t(set.itemCount === 1 ? 'common.question' : 'common.questions')}`
        return (
          <Mark key={`${set.role}-${set.langId}`} stroke="live">
            {`${language?.name ?? ''}${pool} · ${t('lesson.version', { n: set.version })} · ${count}`}
          </Mark>
        )
      })}
    </div>
  )
}

/** Verdicts and who wrote it: the record a reviewer needs before they decide. */
export function Told({ draft }: { draft: Draft }) {
  const { t, formatRelative } = useI18n()

  if (draft.reviews.length === 0 && draft.contributors.length === 0) return null

  return (
    <section className="band">
      <div className="stack tight">
        <span className="engraved">{t('review.contributors')}</span>
        <p className="people">{draft.contributors.map((person) => person.name).join(' · ')}</p>
      </div>

      {draft.reviews.map((review) => (
        <div key={review.id} className="spread" style={{ gap: 'var(--s3)' }}>
          <Mark stroke={review.verdict === 'Approved' ? (review.isCurrent ? 'written' : 'ended') : 'wrong'}>
            {review.verdict === 'Approved'
              ? t('review.approvedBy', { name: review.reviewer.name })
              : t('review.sentBackBy', { name: review.reviewer.name })}
          </Mark>
          {!review.isCurrent ? <span className="quiet">{t('review.approvalVoid')}</span> : null}
          {review.note ? <span className="said">{review.note}</span> : null}
          <time className="quiet" style={{ fontSize: 'var(--t-sm)' }} dateTime={review.createdAtUtc}>
            {formatRelative(review.createdAtUtc)}
          </time>
        </div>
      ))}
    </section>
  )
}

/** Excel, kept as a first-class way in and out. */
function Excel({
  lessonId,
  draftId,
  revision,
  mayEdit,
  onImported,
  onTrial,
}: {
  lessonId: string
  draftId: string
  revision: number
  mayEdit: boolean
  onImported: () => void
  onTrial: (trial: ImportTrial) => void
}) {
  const { t } = useI18n()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()
  const bringIn = useRef<HTMLInputElement>(null)
  const tryFirst = useRef<HTMLInputElement>(null)

  return (
    <section className="band">
      <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('lesson.excel')}</h2>
      <p className="said">{t('lesson.importSaid')}</p>

      <div className="acts">
        {mayEdit ? (
          <>
            <button type="button" className="act small" disabled={busy} onClick={() => bringIn.current?.click()}>
              {t('lesson.import')}
            </button>
            <input
              ref={bringIn}
              type="file"
              accept=".xlsx"
              hidden
              onChange={(event) => {
                const file = event.target.files?.[0]
                event.target.value = ''
                if (!file) return
                void run(async () => {
                  try {
                    await studio.importIntoDraft(draftId, revision, file)
                    onImported()
                    say(t('common.saved'))
                  } catch (error) {
                    saying(error)
                  }
                })
              }}
            />
          </>
        ) : null}

        <button type="button" className="act small" disabled={busy} onClick={() => tryFirst.current?.click()}>
          {t('lesson.trial')}
        </button>
        <input
          ref={tryFirst}
          type="file"
          accept=".xlsx"
          hidden
          onChange={(event) => {
            const file = event.target.files?.[0]
            event.target.value = ''
            if (!file) return
            void run(async () => {
              try {
                onTrial(await studio.trialSheet(file))
              } catch (error) {
                saying(error)
              }
            })
          }}
        />

        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => void run(async () => studio.lessonSheet(lessonId, false).catch(saying))}
        >
          {`${t('lesson.export')} — ${t('lesson.exportLive')}`}
        </button>

        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => void run(async () => studio.lessonSheet(lessonId, true).catch(saying))}
        >
          {`${t('lesson.export')} — ${t('lesson.exportDraft')}`}
        </button>

        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => void run(async () => studio.blankSheet().catch(saying))}
        >
          {t('lesson.template')}
        </button>
      </div>
    </section>
  )
}

/** A sheet read and reported row by row. Nothing was saved. */
function TrialReport({ trial }: { trial: ImportTrial }) {
  const { t } = useI18n()
  const languages = useLanguages()

  return (
    <div className="stack">
      <p className="said">{t('lesson.trialSaid')}</p>

      <div className="people">
        <span className="quiet">
          <span className="num">{trial.mainCount}</span> {t('lesson.main')}
        </span>
        <span className="quiet">
          <span className="num">{trial.recoveryCount}</span> {t('lesson.recovery')}
        </span>
        {trial.languages.map((id) => (
          <Mark key={id} stroke="live">
            {languages.find((one) => one.id === id)?.name ?? id}
          </Mark>
        ))}
      </div>

      {trial.problems.length === 0 ? (
        <Mark stroke="written">{t('lesson.checksClear')}</Mark>
      ) : (
        <table>
          <thead>
            <tr>
              <th>#</th>
              <th>{t('lesson.checks')}</th>
            </tr>
          </thead>
          <tbody>
            {trial.problems.map((problem, at) => (
              <tr key={at}>
                <td className="num">{`${problem.row}${problem.column ?? ''}`}</td>
                <td style={{ color: 'var(--bad-ink)' }}>{problem.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------

export function fromLive(live: LessonContent): DraftItem[] {
  return live.items.map((item) => ({
    itemId: item.itemId,
    role: item.role,
    order: item.order,
    renderings: item.renderings.map((rendering) => ({
      langId: rendering.langId,
      text: rendering.text,
      choices: rendering.choices.map((choice) => choice.text),
      correctIndex: rendering.correctIndex,
    })),
  }))
}

/** The server may send more than the board writes back; only these fields are ours. */
export function clean(item: DraftItem): DraftItem {
  return {
    itemId: item.itemId ?? null,
    sourceKeyHint: item.sourceKeyHint ?? null,
    role: item.role,
    order: item.order,
    renderings: (item.renderings ?? []).map((rendering) => ({
      langId: rendering.langId,
      text: rendering.text ?? '',
      choices: [0, 1, 2].map((slot) => rendering.choices?.[slot] ?? ''),
      correctIndex: rendering.correctIndex ?? 0,
    })),
  }
}
