import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
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
  type StudioNode,
} from '../lib/studio'
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

/** What a node of this kind can live under. A grade lives at the top and cannot be moved. */
const livesUnder: Record<NodeKind, NodeKind | null> = {
  grade: null,
  term: 'grade',
  subject: 'term',
  chapter: 'subject',
  lesson: 'chapter',
}

const depthOf: Record<NodeKind, number> = { grade: 1, term: 2, subject: 3, chapter: 4, lesson: 5 }

export function Changes() {
  const { draftId = '' } = useParams()
  const { t } = useI18n()
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
                  className="act first"
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
            ) : (
              <button
                type="button"
                className="act first"
                disabled={busy || !open.can.submit || problems.length > 0}
                onClick={() => void act(() => studio.submit(open.summary.id, revision), t('lesson.submit'))}
              >
                {t('lesson.submit')}
              </button>
            )}
          </>
        ) : null}
      </div>
    </>,
    [open?.summary.id, open?.summary.status, open?.can.review, saving, problems.length, busy, revision, t],
  )

  if (draft.loading) return <Wiping rows={5} />
  if (!open) return <Nothing title={t('errors.draft.notFound')} />

  return (
    <div className="stack loose">
      <div className="stack tight">
        <Trail
          steps={[
            { id: 'root', label: t('curriculum.title') },
            ...trailOf(open.summary.trail, languages).slice(0, -1),
          ]}
          onGo={(id) => navigate(id === 'root' ? '/curriculum' : `/curriculum/${id}`)}
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
      const kind = draft.summary.nodeKind ?? 'lesson'
      return (
        <div className="stack loose">
          <Titles
            titles={made.titles}
            readOnly={readOnly}
            onChange={(titles) => onChange({ ...made, titles })}
          />

          {kind === 'lesson' ? (
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
          kind={draft.summary.nodeKind ?? 'lesson'}
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
  value,
  readOnly,
  onChange,
}: {
  kind: NodeKind
  value: string
  readOnly: boolean
  onChange: (parentId: string) => void
}) {
  const { t } = useI18n()
  const parentKind = livesUnder[kind]
  const [path, setPath] = useState<string[]>([])

  if (!parentKind) return <p className="said">{t('errors.scope.outOfScope')}</p>

  const wanted = depthOf[parentKind]

  return (
    <div className="stack">
      <span className="engraved">{t('curriculum.moveTo')}</span>
      {Array.from({ length: wanted }, (_, level) => (
        <Level
          key={level}
          parentId={level === 0 ? null : (path[level - 1] ?? null)}
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
  parentId,
  value,
  disabled,
  onChange,
}: {
  parentId: string | null
  value: string
  disabled: boolean
  onChange: (id: string) => void
}) {
  const { t } = useI18n()
  const languages = useLanguages()
  const title = useTitle()
  const list = useLoad(() => (disabled ? Promise.resolve([]) : studio.children(parentId, false)), [parentId, disabled])
  const options = list.data ?? []
  const label = options[0] ? t(`curriculum.kind.${options[0].kind}`) : t('curriculum.title')

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
