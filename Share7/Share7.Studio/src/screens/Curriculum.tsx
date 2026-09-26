import { useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { useLanguages, useLedge } from '../App'
import { Beside, Mark, Nothing, Trail, Wiping, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import {
  curriculumOfTrail,
  levelAbove,
  levelIsEditable,
  levelUnder,
  useCurricula,
  useCurriculumName,
  useCurriculumOf,
  useLevelName,
} from '../lib/curricula'
import { useMe } from '../lib/session'
import { studio, type DraftKind, type DraftSummary, type NodeKind, type StudioCurriculum, type StudioNode } from '../lib/studio'
import { useDoing, useLoad, useTitle, useTrail } from '../lib/use'
import { DraftState, LessonCounts } from './bits'

// ===========================================================================
// The curriculum — everything the Studio holds, one level at a time
//
// Three boards, one shape. The top is the list of curricula: the one the game
// plays, and any declared in the Studio that it does not play yet. One of them
// opened is its top level. A node opened is what is under it.
//
// A level is a list of what is on it, each line saying what it is, what state
// it is in, and (for the level students play) what it has in every language.
// Changing the shape of a curriculum — adding, renaming, moving, reordering,
// taking out, putting back — is always a draft, the same as changing a
// lesson's questions: a second person approves it, or a Lead releases it
// themselves. What is proposed and not released yet is listed with what is
// there, marked with its state, so a level never looks empty to the person
// who has just added to it. Every level is called what its own curriculum calls it.
// ===========================================================================

/** Declaring a curriculum is a Lead's, and only one whose part is the whole curriculum. */
function useMayDeclare() {
  const me = useMe()
  return me.studioRole === 'Lead' && me.scope.allNodes
}

// ---------------------------------------------------------------------------
// The list of curricula
// ---------------------------------------------------------------------------

export function Curricula() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const { list, loading } = useCurricula()
  const mayDeclare = useMayDeclare()

  useLedge(
    <>
      <span className="engraved">{t('curricula.title')}</span>
      <div className="ledge-end">
        {mayDeclare ? (
          <button type="button" className="act first" onClick={() => navigate('/curriculum/new')}>
            {t('curricula.add')}
          </button>
        ) : null}
      </div>
    </>,
    [mayDeclare, t],
  )

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('curricula.title')}</h1>
        <span className="engraved">{t('curricula.said')}</span>
      </div>

      {loading ? (
        <Wiping rows={3} />
      ) : (
        <div className="rows">
          {list.map((one) => (
            <CurriculumLine key={one.id} curriculum={one} />
          ))}
        </div>
      )}

      {!loading && list.every((one) => one.isServed) ? (
        <Nothing>{mayDeclare ? t('curricula.onlyOneSaid') : t('curricula.onlyOneOthers')}</Nothing>
      ) : null}
    </div>
  )
}

function CurriculumLine({ curriculum }: { curriculum: StudioCurriculum }) {
  const { t } = useI18n()
  const name = useCurriculumName()

  return (
    <Link to={`/curriculum/of/${curriculum.id}`} className="row">
      <span className="row-main">
        <span className="row-title">{name(curriculum)}</span>
        <Levels curriculum={curriculum} />
      </span>
      <span className="row-side">
        {curriculum.isServed ? (
          <Mark stroke="live">{t('curricula.played')}</Mark>
        ) : (
          <Mark stroke="waiting">{t('curricula.notPlayed')}</Mark>
        )}
        <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
          <span className="num">{curriculum.playableCount}</span> {t('curricula.toPlay')}
        </span>
        <ChevronRight size={16} strokeWidth={1.5} aria-hidden className="quiet row-go" />
      </span>
    </Link>
  )
}

/** Its levels, top down, dot-separated like a trail — the shape at a glance. */
function Levels({ curriculum }: { curriculum: StudioCurriculum }) {
  const levelName = useLevelName()

  return (
    <span className="trail" aria-label={curriculum.levels.map((level) => levelName(level.key, curriculum)).join(', ')}>
      {curriculum.levels.map((level, at) => (
        <span key={level.key} style={{ display: 'contents' }}>
          {at > 0 ? (
            <span className="sep" aria-hidden>
              ·
            </span>
          ) : null}
          <span>{levelName(level.key, curriculum)}</span>
        </span>
      ))}
    </span>
  )
}

// ---------------------------------------------------------------------------
// One curriculum: its top level
// ---------------------------------------------------------------------------

export function CurriculumTop() {
  const { curriculumId = '' } = useParams()
  const { t } = useI18n()
  const navigate = useNavigate()
  const saying = useSaying()
  const { list, loading: listLoading } = useCurricula()
  const name = useCurriculumName()
  const levelName = useLevelName()
  const [retired, setRetired] = useState(false)
  const [busy, run] = useDoing()

  const me = useMe()
  const curriculum = list.find((one) => one.id === curriculumId)
  const rootId = curriculum?.rootNodeId ?? null
  const proposed = useProposed(curriculum?.isServed ? null : rootId)

  // The served curriculum's top is its grades; a declared one's is whatever hangs from its root.
  const top = useLoad(
    () =>
      !curriculum
        ? Promise.resolve([] as StudioNode[])
        : curriculum.isServed
          ? studio.children(null, retired)
          : studio.node(rootId!).then((detail) => detail.children),
    [curriculum?.id, rootId, retired],
  )

  const next = levelUnder(curriculum, curriculum?.isServed ? null : 'curriculum')
  const mayAdd = !!curriculum && !curriculum.isServed && curriculum.inScope && next !== null

  const add = () =>
    run(async () => {
      try {
        const draft = await studio.startDraft({ kind: 'NewNode', parentNodeId: rootId, nodeKind: next })
        navigate(`/drafts/${draft.summary.id}`)
      } catch (error) {
        saying(error)
      }
    })

  useLedge(
    <>
      <span className="engraved">{name(curriculum)}</span>
      <div className="ledge-end">
        <button type="button" className="act plain small" onClick={() => setRetired(!retired)}>
          {retired ? t('curriculum.hideRetired') : t('curriculum.showRetired')}
        </button>
        {mayAdd ? (
          <button type="button" className="act first" disabled={busy} onClick={() => void add()}>
            {t('curriculum.add')} — {levelName(next, curriculum)}
          </button>
        ) : null}
      </div>
    </>,
    [curriculum?.id, curriculum?.inScope, retired, busy, next, t],
  )

  if (listLoading) return <Wiping rows={5} />

  if (!curriculum) {
    return (
      <Nothing
        title={t('errors.curriculum.notFound')}
        action={
          <Link to="/curriculum" className="act">
            {t('curricula.title')}
          </Link>
        }
      />
    )
  }

  const shown = retired ? (top.data ?? []) : (top.data ?? []).filter((node) => !node.isRetired)

  return (
    <div className="stack loose">
      <div className="stack tight">
        <Trail steps={[{ id: 'root', label: t('curricula.title') }]} onGo={() => navigate('/curriculum')} linkAll />

        <div className="heading">
          <h1>{name(curriculum)}</h1>
          {curriculum.isServed ? (
            <Mark stroke="live">{t('curricula.played')}</Mark>
          ) : (
            <Mark stroke="waiting">{t('curricula.notPlayed')}</Mark>
          )}
        </div>

        <div className="spread" style={{ gap: 'var(--s4)', alignItems: 'baseline' }}>
          <span className="engraved">{t('curricula.levels')}</span>
          <Levels curriculum={curriculum} />
          {curriculum.canManage ? (
            <Link to={`/curriculum/of/${curriculum.id}/edit`} className="act plain small">
              {t('curricula.change')}
            </Link>
          ) : null}
        </div>

        {/* What this curriculum is to a student, said once, beside it. */}
        {curriculum.isServed ? (
          <Beside>{t('curricula.servedSaid')}</Beside>
        ) : (
          <Beside tone="live">{t('curricula.notPlayedSaid')}</Beside>
        )}

        {!curriculum.isServed && !curriculum.inScope ? <Beside>{t('curricula.notYours')}</Beside> : null}
      </div>

      {top.loading ? (
        <Wiping rows={5} />
      ) : shown.length === 0 && proposed.length === 0 ? (
        <Nothing
          title={t('curricula.emptyTop', { level: levelName(next, curriculum) })}
          action={
            mayAdd ? (
              <button type="button" className="act" disabled={busy} onClick={() => void add()}>
                {t('curriculum.add')} — {levelName(next, curriculum)}
              </button>
            ) : undefined
          }
        >
          {t(me.studioRole === 'Lead' ? 'curricula.emptyTopSaidLead' : 'curricula.emptyTopSaid')}
        </Nothing>
      ) : (
        <div className="rows">
          {shown.map((child) => (
            <Line key={child.id} node={child} />
          ))}
          {proposed.map((draft) => (
            <ProposedLine key={draft.id} draft={draft} curriculum={curriculum} />
          ))}
        </div>
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------
// One node, and what is under it
// ---------------------------------------------------------------------------

export function Curriculum() {
  const { nodeId = '' } = useParams()
  const { t } = useI18n()
  const navigate = useNavigate()
  const saying = useSaying()
  const languages = useLanguages()
  const title = useTitle()
  const trailOf = useTrail()
  const levelName = useLevelName()
  const curriculumName = useCurriculumName()
  const me = useMe()
  const { list } = useCurricula()
  const [retired, setRetired] = useState(false)
  const [busy, run] = useDoing()

  const here = useLoad(() => studio.node(nodeId), [nodeId])
  const node = here.data?.node ?? null
  const curriculum = useCurriculumOf(node?.curriculumId) ?? curriculumOfTrail(list, here.data?.trail)

  const list_ = here.data?.children ?? []
  const shown = retired ? list_ : list_.filter((child) => !child.isRetired)
  const proposed = useProposed(nodeId)
  const next = node ? levelUnder(curriculum, node.kind) : null

  const start = (kind: DraftKind, body: Parameters<typeof studio.startDraft>[0]) =>
    run(async () => {
      try {
        const draft = await studio.startDraft({ ...body, kind })
        navigate(`/drafts/${draft.summary.id}`)
      } catch (error) {
        saying(error)
      }
    })

  const mayAdd = !!node && node.inScope && !node.isRetired && next !== null

  useLedge(
    <>
      <span className="engraved">{node ? title(node.titles, languages) : ''}</span>
      <div className="ledge-end">
        <button type="button" className="act plain small" onClick={() => setRetired(!retired)}>
          {retired ? t('curriculum.hideRetired') : t('curriculum.showRetired')}
        </button>
        {mayAdd ? (
          <button
            type="button"
            className="act first"
            disabled={busy}
            onClick={() => void start('NewNode', { kind: 'NewNode', parentNodeId: nodeId, nodeKind: next })}
          >
            {t('curriculum.add')} — {levelName(next, curriculum)}
          </button>
        ) : null}
      </div>
    </>,
    [node?.id, retired, busy, mayAdd, next, curriculum?.id, t],
  )

  // A declared curriculum's root is its name, not a place: it opens as the curriculum.
  if (node?.kind === 'curriculum') return <Navigate to={`/curriculum/of/${node.curriculumId}`} replace />

  if (here.loading) return <Wiping rows={5} />

  if (!node) {
    return (
      <Nothing
        title={t('errors.node.notFound')}
        action={
          <Link to="/curriculum" className="act">
            {t('curricula.title')}
          </Link>
        }
      />
    )
  }

  // The trail: the list, the curriculum, then the way down — without the root, which the second
  // step already names, and without this node, which the heading does.
  const steps = [
    { id: 'root', label: t('curricula.title') },
    { id: 'curriculum', label: curriculumName(curriculum) },
    ...trailOf((here.data?.trail ?? []).filter((step) => step.kind !== 'curriculum'), languages).slice(0, -1),
  ]

  return (
    <div className="stack loose">
      <div className="stack tight">
        <Trail
          steps={steps}
          linkAll
          onGo={(id) =>
            navigate(id === 'root' ? '/curriculum' : id === 'curriculum' ? `/curriculum/of/${node.curriculumId}` : `/curriculum/${id}`)
          }
        />

        <div className="heading">
          <h1>{title(node.titles, languages)}</h1>
          <span className="engraved">{levelName(node.kind, curriculum)}</span>
          {node.isRetired ? <Mark stroke="ended">{t('curriculum.retire')}</Mark> : null}
        </div>

        {!node.inScope ? <p className="beside">{t('curriculum.notYours')}</p> : null}

        {/* The two boards that are about this place rather than about the tree. Both read the
            curriculum the game plays, so a declared one does not offer them yet. */}
        {curriculum?.isServed !== false ? (
          <div className="spread" style={{ gap: 'var(--s4)' }}>
            <Link to={`/recovery/${node.id}`} className="act plain small">
              {t('place.recovery')}
            </Link>
            {node.kind === 'subject' ? (
              <Link to={`/mapping/${node.id}`} className="act plain small">
                {t('mapping.title')}
              </Link>
            ) : null}
          </div>
        ) : null}
      </div>

      <NodeActions
        node={node}
        curriculum={curriculum}
        busy={busy}
        onStart={(kind, body) => void start(kind, body)}
        childCount={shown.length}
      />

      {shown.length === 0 && proposed.length === 0 ? (
        next ? (
          <Nothing title={t('curricula.emptyUnder', { level: levelName(next, curriculum) })}>
            {t(me.studioRole === 'Lead' ? 'curricula.emptyTopSaidLead' : 'curriculum.emptySaid')}
          </Nothing>
        ) : null
      ) : (
        <div className="rows">
          {shown.map((child) => (
            <Line key={child.id} node={child} />
          ))}
          {proposed.map((draft) => (
            <ProposedLine key={draft.id} draft={draft} curriculum={curriculum} />
          ))}
        </div>
      )}
    </div>
  )
}

function Line({ node }: { node: StudioNode }) {
  const { t } = useI18n()
  const languages = useLanguages()
  const title = useTitle()
  const levelName = useLevelName()
  const curriculum = useCurriculumOf(node.curriculumId)
  const to = node.isPlayable ? `/lessons/${node.id}` : `/curriculum/${node.id}`

  return (
    <Link to={to} className="row">
      <span className="row-main">
        <span className="row-title">{title(node.titles, languages)}</span>
        <span className="spread" style={{ gap: 'var(--s3)' }}>
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            {levelName(node.kind, curriculum)}
          </span>
          {!node.isPlayable && node.childCount > 0 ? (
            <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
              <span className="num">{node.childCount}</span>
            </span>
          ) : null}
        </span>
      </span>

      <span className="row-side">
        <LessonCounts node={node} />
        {node.openDrafts.length > 0 ? <Mark stroke="waiting">{t('curriculum.openDraft')}</Mark> : null}
        {node.isRetired ? <Mark stroke="ended">{t('curriculum.retire')}</Mark> : null}
        {!node.inScope ? (
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>{t('blocker.outOfScope')}</span>
        ) : null}
        <ChevronRight size={16} strokeWidth={1.5} aria-hidden className="quiet row-go" />
      </span>
    </Link>
  )
}

/**
 * New parts proposed straight under a parent and not released yet. Without them a member who has
 * just added a grade looks at an empty level and concludes it did not work.
 */
function useProposed(parentId: string | null) {
  const drafts = useLoad(
    () => (parentId ? studio.drafts({ underNodeId: parentId, pageSize: 100 }) : Promise.resolve(null)),
    [parentId],
  )
  return (drafts.data?.drafts ?? []).filter(
    (draft) => draft.kind === 'NewNode' && draft.parentNodeId === parentId && !draft.isPractice,
  )
}

/** One of them: a line among the others at its level, its state said by its mark, opening its draft. */
function ProposedLine({ draft, curriculum }: { draft: DraftSummary; curriculum: StudioCurriculum | undefined }) {
  const { t } = useI18n()
  const levelName = useLevelName()

  return (
    <Link to={`/drafts/${draft.id}`} className="row">
      <span className="row-main">
        <span className="row-title">{draft.title}</span>
        <span className="spread" style={{ gap: 'var(--s3)' }}>
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            {levelName(draft.nodeKind, curriculum)}
          </span>
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            {t('curriculum.proposedNew')}
          </span>
        </span>
      </span>

      <span className="row-side">
        <DraftState status={draft.status} />
        <ChevronRight size={16} strokeWidth={1.5} aria-hidden className="quiet row-go" />
      </span>
    </Link>
  )
}

/**
 * What can be done to this part of the curriculum. Every one of them starts a
 * draft — the Studio has no way of changing the tree without one, though a
 * Lead can release theirs straight away — and only what the curriculum allows
 * is offered: the served curriculum's grades
 * can have their terms put in order and nothing else, and a declared
 * curriculum's top level has nowhere else to move to.
 */
function NodeActions({
  node,
  curriculum,
  busy,
  childCount,
  onStart,
}: {
  node: StudioNode
  curriculum: StudioCurriculum | undefined
  busy: boolean
  childCount: number
  onStart: (kind: DraftKind, body: Parameters<typeof studio.startDraft>[0]) => void
}) {
  const { t } = useI18n()

  if (!node.inScope) return null

  const editable = levelIsEditable(curriculum, node.kind as NodeKind)
  const movable = editable && levelAbove(curriculum, node.kind as NodeKind).depth > 0
  const reorderable = !node.isPlayable && !node.isRetired && childCount > 1

  if (!editable && !reorderable) return null

  return (
    <div className="acts">
      {editable && !node.isRetired ? (
        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => onStart('Rename', { kind: 'Rename', nodeId: node.id })}
        >
          {t('curriculum.rename')}
        </button>
      ) : null}

      {movable && !node.isRetired ? (
        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => onStart('Move', { kind: 'Move', nodeId: node.id })}
        >
          {t('curriculum.move')}
        </button>
      ) : null}

      {reorderable ? (
        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => onStart('Reorder', { kind: 'Reorder', nodeId: node.id })}
        >
          {t('curriculum.reorder')}
        </button>
      ) : null}

      {!editable ? null : node.isRetired ? (
        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => onStart('Restore', { kind: 'Restore', nodeId: node.id })}
        >
          {t('curriculum.restore')}
        </button>
      ) : (
        <button
          type="button"
          className="act small grave"
          disabled={busy}
          onClick={() => onStart('Retire', { kind: 'Retire', nodeId: node.id })}
        >
          {t('curriculum.retire')}
        </button>
      )}
    </div>
  )
}
