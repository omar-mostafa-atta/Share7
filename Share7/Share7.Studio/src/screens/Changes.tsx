import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useContentLanguages, useLanguages, useLedge } from '../App'
import { Beside, Mark, Nothing, Sheet, Trail, Wiping, Write, useSaying, useTelling } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { StudioError } from '../lib/api'
import {
  studio,
  type CommentAnchor,
  type ContentLanguage,
  type ContentProblem,
  type Draft,
  type DraftComment,
  type DraftItem,
  type LessonProposal,
  type MoveProposal,
  type NewNodeProposal,
  type NodeKind,
  type NodeTitle,
  type RecoveryRuleProposal,
  type RenameProposal,
  type ReorderProposal,
  type StudioCurriculum,
  type StudioNode,
} from '../lib/studio'
import { curriculumOfTrail, levelAbove, levelIsPlayable, useCurricula, useCurriculumName, useLevelName } from '../lib/curricula'
import { useDoing, useLoad, useSettled, useTitle, useTrail } from '../lib/use'
import { Comments } from './Comments'
import { DraftState } from './bits'
import { Told, clean } from './Lesson'
import { Written } from './Written'

// ===========================================================================
// A change to the shape of the curriculum
//
// Adding, renaming, moving, reordering, taking out and putting back all go
// through the same board as a lesson's questions: written as a draft, approved
// by a second person, carried by a release. Practice lessons live here too —
// they are reviewed like anything else and never released.
// ===========================================================================

const SAVE_AFTER = 900

/**
 * The level a draft is about. A new node's draft names it; every other draft is about a node that
 * exists, and its trail ends at that node. (Reading only the first used to send every move to the
 * lesson picker, whatever was being moved.)
 */
function levelOf(draft: Draft): NodeKind {
  return draft.summary.nodeKind ?? draft.summary.trail.at(-1)?.kind ?? 'lesson'
}

export function Changes() {
  const { draftId = '' } = useParams()
  const { t } = useI18n()
  const { list: curricula, reload: reloadCurricula } = useCurricula()
  const curriculumName = useCurriculumName()
  const navigate = useNavigate()
  const languages = useLanguages()
  const content = useContentLanguages()
  const trailOf = useTrail()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()

  const draft = useLoad(() => studio.draft(draftId), [draftId])
  const comments = useLoad(() => studio.comments(draftId), [draftId])
  const open = draft.data

  const [proposal, setProposal] = useState<unknown>(null)
  const [revision, setRevision] = useState(0)
  const [problems, setProblems] = useState<ContentProblem[]>([])
  const [saving, setSaving] = useState(false)
  const [anchor, setAnchor] = useState<CommentAnchor | null>(null)
  const [sendBack, setSendBack] = useState(false)
  const [note, setNote] = useState('')
  const dirty = useRef(false)

  useEffect(() => {
    if (!open) return
    setProposal(open.proposal)
    setRevision(open.summary.revision)
    setProblems(open.problems)
    dirty.current = false
  }, [open])

  const settled = useSettled(proposal, SAVE_AFTER)
  const mayEdit = open?.can.edit ?? false

  useEffect(() => {
    if (!open || !mayEdit || !dirty.current || settled === null) return

    let alive = true
    setSaving(true)

    studio
      .saveDraft(open.summary.id, revision, settled)
      .then((saved) => {
        if (!alive) return
        setRevision(saved.summary.revision)
        setProblems(saved.problems)
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
    // Only a settled change saves.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settled])

  const change = (next: unknown) => {
    dirty.current = true
    setProposal(next)
  }

  const act = async (work: () => Promise<unknown>, told?: string) =>
    run(async () => {
      try {
        await work()
        draft.reload()
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

  // A Lead's change goes live in one step. A new part of the curriculum is then shown where it now
  // sits, among the others at its level, so the next one can be added straight after it.
  const releaseNow = (current: Draft) =>
    run(async () => {
      try {
        await studio.releaseNow(current.summary.id, revision)
        if (current.summary.kind === 'NewNode') {
          reloadCurricula()
          say(t('curriculum.addedNow', { title: current.summary.title }))
          const parent = current.summary.trail.at(-1)
          const curriculum = curriculumOfTrail(curricula, current.summary.trail)
          navigate(
            !parent || parent.kind === 'curriculum' ? `/curriculum/of/${curriculum?.id ?? ''}` : `/curriculum/${parent.id}`,
          )
        } else {
          draft.reload()
          say(t('lesson.releasedNow'))
        }
      } catch (error) {
        saying(error)
        draft.reload()
      }
    })

  useLedge(
    <>
      <span className="engraved">{open ? t(`kind.${open.summary.kind}`) : ''}</span>
      {saving ? <span className="quiet">{t('common.saving')}</span> : null}
      {open && problems.length > 0 ? (
        <Mark stroke="wrong">{t('lesson.checksCount', { count: problems.length })}</Mark>
      ) : null}

      <div className="ledge-end">
        {open ? (
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

            {lead ? (
              <button
                type="button"
                className="act first"
                disabled={busy || pending || (open.summary.status !== 'Approved' && problems.length > 0)}
                onClick={() => void releaseNow(open)}
              >
                {t('lesson.releaseNow')}
              </button>
            ) : null}
          </>
        ) : null}
      </div>
    </>,
    [open?.summary.id, open?.summary.status, open?.can.review, lead, pending, saving, problems.length, busy, revision, t],
  )

  if (draft.loading) return <Wiping rows={5} />
  if (!open)
    return (
      <Nothing
        title={t('errors.draft.notFound')}
        action={
          <Link to="/" className="act">
            {t('place.home')}
          </Link>
        }
      />
    )

  const draftCurriculum = curriculumOfTrail(curricula, open.summary.trail)

  return (
    <div className="stack loose">
      <div className="stack tight">
        <Trail
          steps={[
            { id: 'root', label: t('curricula.title') },
            { id: 'curriculum', label: curriculumName(draftCurriculum) },
            ...trailOf(
              open.summary.trail.filter((step) => step.kind !== 'curriculum'),
              languages,
            ).slice(0, open.summary.kind === 'NewNode' ? undefined : -1),
          ]}
          linkAll
          onGo={(id) =>
            navigate(
              id === 'root' ? '/curriculum' : id === 'curriculum' ? `/curriculum/of/${draftCurriculum?.id ?? ''}` : `/curriculum/${id}`,
            )
          }
        />

        <div className="heading">
          <h1>{open.summary.title}</h1>
          <span className="engraved">{t(`kind.${open.summary.kind}`)}</span>
          <DraftState status={open.summary.status} />
          {open.summary.isPractice ? <Mark stroke="live">{t('lesson.practice')}</Mark> : null}
        </div>
      </div>

      {open.summary.isOutOfDate ? (
        <section className="band">
          <Mark stroke="wrong">{t('lesson.outOfDate')}</Mark>
          <p className="said">{t('lesson.outOfDateSaid')}</p>
          <button
            type="button"
            className="act"
            style={{ justifySelf: 'start' }}
            disabled={busy}
            onClick={() => void act(() => studio.bringUpToDate(open.summary.id, revision), t('lesson.bringUpToDate'))}
          >
            {t('lesson.bringUpToDate')}
          </button>
        </section>
      ) : null}

      <section className="band">
        <Editor
          draft={open}
          proposal={proposal}
          problems={problems}
          readOnly={!mayEdit}
          onChange={change}
          content={content}
          comments={comments.data ?? []}
          onComment={setAnchor}
        />
      </section>

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
            onClick={async () => {
              const done = await act(() => studio.discard(open.summary.id), t('common.done'))
              if (done) navigate('/')
            }}
          >
            {t('lesson.discard')}
          </button>
        </section>
      ) : null}

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
              disabled={busy || note.trim() === ''}
              onClick={async () => {
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
          <label htmlFor="changes-note">{t('review.note')}</label>
          <textarea
            id="changes-note"
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

// ---------------------------------------------------------------------------
// One editor per kind of change
// ---------------------------------------------------------------------------

function Editor({
  draft,
  proposal,
  problems,
  readOnly,
  onChange,
  content,
  comments,
  onComment,
}: {
  draft: Draft
  proposal: unknown
  problems: ContentProblem[]
  readOnly: boolean
  onChange: (next: unknown) => void
  content: ContentLanguage[]
  comments: DraftComment[]
  onComment: (anchor: CommentAnchor) => void
}) {
  const { t } = useI18n()
  const { list } = useCurricula()
  const curriculum = curriculumOfTrail(list, draft.summary.trail)

  switch (draft.summary.kind) {
    case 'LessonContent': {
      const items = ((proposal as LessonProposal | null)?.items ?? []).map(clean)
      return (
        <Written
          items={items}
          languages={content}
          problems={problems}
          comments={comments}
          standing
          readOnly={readOnly}
          onChange={readOnly ? undefined : (next: DraftItem[]) => onChange({ items: next } satisfies LessonProposal)}
          onComment={onComment}
        />
      )
    }

    case 'NewNode': {
      const made = (proposal as NewNodeProposal | null) ?? { titles: [], position: null, items: null }
      const kind = levelOf(draft)
      return (
        <div className="stack loose">
          <Titles
            titles={made.titles}
            readOnly={readOnly}
            onChange={(titles) => onChange({ ...made, titles })}
          />

          {levelIsPlayable(curriculum, kind) ? (
            <div className="stack">
              <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('lesson.main')}</h2>
              <p className="said">{t('lesson.mainSaid')}</p>
              <Written
                items={(made.items ?? []).map(clean)}
                languages={content}
                problems={problems}
                comments={comments}
                readOnly={readOnly}
                onChange={readOnly ? undefined : (items: DraftItem[]) => onChange({ ...made, items })}
                onComment={onComment}
              />
            </div>
          ) : null}
        </div>
      )
    }

    case 'Rename': {
      const renamed = (proposal as RenameProposal | null) ?? { titles: [] }
      return <Titles titles={renamed.titles} readOnly={readOnly} onChange={(titles) => onChange({ titles })} />
    }

    case 'Move': {
      const moved = (proposal as MoveProposal | null) ?? { newParentId: '', position: null }
      return (
        <MoveTo
          kind={levelOf(draft)}
          curriculum={curriculum}
          value={moved.newParentId}
          readOnly={readOnly}
          onChange={(newParentId) => onChange({ ...moved, newParentId })}
        />
      )
    }

    case 'Reorder': {
      const ordered = (proposal as ReorderProposal | null) ?? { orderedChildIds: [] }
      return (
        <Reorder
          parentId={draft.summary.nodeId ?? ''}
          order={ordered.orderedChildIds}
          readOnly={readOnly}
          onChange={(orderedChildIds) => onChange({ orderedChildIds })}
        />
      )
    }

    case 'Retire':
      return (
        <div className="stack">
          <Mark stroke="ended">{t('curriculum.retire')}</Mark>
          <p className="said">{t('curriculum.retireSaid')}</p>
        </div>
      )

    case 'Restore':
      return (
        <div className="stack">
          <Mark stroke="written">{t('curriculum.restore')}</Mark>
          <p className="said">{t('curriculum.retireSaid')}</p>
        </div>
      )

    case 'RecoveryRule': {
      const rule = (proposal as RecoveryRuleProposal | null) ?? {
        afterWrongAnswers: 2,
        questionsToServe: 3,
        allowRepeats: false,
        clear: false,
      }
      return <RecoveryRuleEditor rule={rule} readOnly={readOnly} onChange={onChange} />
    }
  }
}

/**
 * Three numbers and a switch, and a fourth choice that is not any of them: take
 * the rule away so whatever covers this place from above applies again. That is
 * a different thing from a rule that happens to match its parent's numbers, and
 * the only way for the team to say it.
 */
function RecoveryRuleEditor({
  rule,
  readOnly,
  onChange,
}: {
  rule: RecoveryRuleProposal
  readOnly: boolean
  onChange: (next: unknown) => void
}) {
  const { t } = useI18n()
  const set = (part: Partial<RecoveryRuleProposal>) => onChange({ ...rule, ...part })

  return (
    <div className="stack">
      {rule.clear ? (
        <>
          <Mark stroke="ended">{t('recovery.clearing')}</Mark>
          <p className="said">{t('recovery.clearSaid')}</p>
        </>
      ) : (
        <>
          <Write
            label={t('recovery.afterWrong')}
            hint={t('recovery.afterWrongSaid')}
            type="number"
            min={1}
            max={20}
            inputMode="numeric"
            disabled={readOnly}
            value={rule.afterWrongAnswers}
            onChange={(event) => set({ afterWrongAnswers: Number(event.target.value) })}
          />
          <Write
            label={t('recovery.toServe')}
            hint={t('recovery.toServeSaid')}
            type="number"
            min={1}
            max={20}
            inputMode="numeric"
            disabled={readOnly}
            value={rule.questionsToServe}
            onChange={(event) => set({ questionsToServe: Number(event.target.value) })}
          />
          <label className="field" style={{ flexDirection: 'row', alignItems: 'baseline', gap: 'var(--s3)' }}>
            <input
              type="checkbox"
              checked={rule.allowRepeats}
              disabled={readOnly}
              onChange={(event) => set({ allowRepeats: event.target.checked })}
            />
            <span className="label">{t('recovery.allowRepeats')}</span>
          </label>
          <Beside>{t('recovery.allowRepeatsSaid')}</Beside>
        </>
      )}

      {readOnly ? null : (
        <button type="button" className={rule.clear ? 'act' : 'act grave'} onClick={() => set({ clear: !rule.clear })}>
          {rule.clear ? t('common.cancel') : t('recovery.clear')}
        </button>
      )}
    </div>
  )
}

/** A name in every language, each written in its own direction. */
function Titles({
  titles,
  readOnly,
  onChange,
}: {
  titles: NodeTitle[]
  readOnly: boolean
  onChange: (titles: NodeTitle[]) => void
}) {
  const { t } = useI18n()
  const content = useContentLanguages()

  return (
    <div className="stack">
      <span className="engraved">{t('curriculum.newTitles')}</span>
      {content.map((language) => {
        const has = titles.find((one) => one.langId === language.id)
        return (
          <div key={language.id} dir={language.direction} lang={language.code}>
            <Write
              label={language.name}
              value={has?.title ?? ''}
              disabled={readOnly}
              onChange={(event) => {
                const next = has
                  ? titles.map((one) => (one.langId === language.id ? { ...one, title: event.target.value } : one))
                  : [...titles, { langId: language.id, title: event.target.value }]
                onChange(next)
              }}
            />
          </div>
        )
      })}
    </div>
  )
}

/** Where it goes: the curriculum walked down one level at a time. */
function MoveTo({
  kind,
  curriculum,
  value,
  readOnly,
  onChange,
}: {
  kind: NodeKind
  curriculum: StudioCurriculum | undefined
  value: string
  readOnly: boolean
  onChange: (parentId: string) => void
}) {
  const { t } = useI18n()
  const above = levelAbove(curriculum, kind)
  const [path, setPath] = useState<string[]>([])

  // Nothing to move to: the served grades are fixed, and a declared curriculum's top level has only
  // its one root above it — a new place there is a new order, not a move.
  if (!above.kind || above.depth === 0) return <p className="said">{t('curriculum.cannotMove')}</p>

  const wanted = above.depth

  // The walk starts at the curriculum's own top: the grades, or what hangs from a declared root.
  const top = curriculum && !curriculum.isServed ? curriculum.rootNodeId : null

  return (
    <div className="stack">
      <span className="engraved">{t('curriculum.moveTo')}</span>
      {Array.from({ length: wanted }, (_, level) => (
        <Level
          key={level}
          curriculum={curriculum}
          parentId={level === 0 ? top : (path[level - 1] ?? null)}
          disabled={readOnly || (level > 0 && !path[level - 1])}
          value={path[level] ?? ''}
          onChange={(id) => {
            const next = [...path.slice(0, level), id]
            setPath(next)
            if (next.length === wanted && id) onChange(id)
          }}
        />
      ))}
      {value ? <Mark stroke="written">{t('common.done')}</Mark> : <Mark stroke="waiting">{t('common.waiting')}</Mark>}
    </div>
  )
}

function Level({
  curriculum,
  parentId,
  value,
  disabled,
  onChange,
}: {
  curriculum: StudioCurriculum | undefined
  parentId: string | null
  value: string
  disabled: boolean
  onChange: (id: string) => void
}) {
  const { t } = useI18n()
  const languages = useLanguages()
  const title = useTitle()
  const levelName = useLevelName()
  const list = useLoad(() => (disabled ? Promise.resolve([]) : studio.children(parentId, false)), [parentId, disabled])
  const options = list.data ?? []
  const label = options[0] ? levelName(options[0].kind, curriculum) : t('curriculum.title')

  return (
    <div className="field">
      <label>{label}</label>
      <select
        className="write"
        value={value}
        disabled={disabled || list.loading}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{t('common.none')}</option>
        {options.map((node) => (
          <option key={node.id} value={node.id}>
            {title(node.titles, languages)}
          </option>
        ))}
      </select>
    </div>
  )
}

/** A new order for a parent's live children. */
function Reorder({
  parentId,
  order,
  readOnly,
  onChange,
}: {
  parentId: string
  order: string[]
  readOnly: boolean
  onChange: (order: string[]) => void
}) {
  const { t } = useI18n()
  const languages = useLanguages()
  const title = useTitle()
  const detail = useLoad(() => studio.node(parentId), [parentId])
  const children = detail.data?.children ?? []

  const named = (id: string): StudioNode | undefined => children.find((child) => child.id === id)
  const list = order.length > 0 ? order : children.map((child) => child.id)

  const shift = (at: number, by: -1 | 1) => {
    const to = at + by
    if (to < 0 || to >= list.length) return
    const next = [...list]
    next[at] = list[to]
    next[to] = list[at]
    onChange(next)
  }

  if (detail.loading) return <Wiping rows={4} />

  return (
    <div className="stack">
      <p className="said">{t('curriculum.reorderSaid')}</p>
      <div className="rows">
        {list.map((id, at) => (
          <div className="row" key={id}>
            <span className="row-main">
              <span className="row-title">
                <span className="num quiet">{at + 1}.</span> {title(named(id)?.titles, languages)}
              </span>
            </span>
            <span className="row-side">
              <button
                type="button"
                className="act icon"
                disabled={readOnly || at === 0}
                title={t('lesson.moveUp')}
                onClick={() => shift(at, -1)}
              >
                ↑
              </button>
              <button
                type="button"
                className="act icon"
                disabled={readOnly || at === list.length - 1}
                title={t('lesson.moveDown')}
                onClick={() => shift(at, 1)}
              >
                ↓
              </button>
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}
