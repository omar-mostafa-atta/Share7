import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { useLanguages, useLedge } from '../App'
import { Mark, Nothing, Trail, Wiping, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { useMe } from '../lib/session'
import { studio, type DraftKind, type NodeKind, type StudioNode } from '../lib/studio'
import { useDoing, useLoad, useTitle, useTrail } from '../lib/use'
import { LessonCounts } from './bits'

// ===========================================================================
// The curriculum — everything the game teaches, one level at a time
//
// A level is a list of what is on it, each line saying what it is, what state
// it is in, and (for a lesson) what it has in every language. Changing the
// shape of the curriculum — adding, renaming, moving, reordering, taking out,
// putting back — is always a draft, the same as changing a lesson's questions,
// so the same second person approves it and the same release carries it.
// ===========================================================================

/** What a level below this kind is called. Nothing lives under a lesson. */
const under: Record<NodeKind, NodeKind | null> = {
  grade: 'term',
  term: 'subject',
  subject: 'chapter',
  chapter: 'lesson',
  lesson: null,
}

export function Curriculum() {
  const { nodeId } = useParams()
  const { t } = useI18n()
  const navigate = useNavigate()
  const saying = useSaying()
  const me = useMe()
  const languages = useLanguages()
  const title = useTitle()
  const trailOf = useTrail()
  const [retired, setRetired] = useState(false)
  const [busy, run] = useDoing()

  const here = useLoad(() => (nodeId ? studio.node(nodeId) : Promise.resolve(null)), [nodeId])
  const children = useLoad(
    () => (nodeId ? Promise.resolve(null) : studio.children(null, retired)),
    [nodeId, retired],
  )

  // A node's own read carries its children; the grades come from the list call.
  const list = nodeId ? (here.data?.children ?? []) : (children.data ?? [])
  const shown = retired ? list : list.filter((child) => !child.isRetired)
  const loading = nodeId ? here.loading : children.loading
  const node = here.data?.node ?? null
  const next = node ? under[node.kind] : 'grade'

  const start = (kind: DraftKind, body: Parameters<typeof studio.startDraft>[0]) =>
    run(async () => {
      try {
        const draft = await studio.startDraft({ ...body, kind })
        navigate(`/drafts/${draft.summary.id}`)
      } catch (error) {
        saying(error)
      }
    })

  const mayChange = node ? node.inScope : me.scope.allNodes

  useLedge(
    <>
      <span className="engraved">{node ? title(node.titles, languages) : t('curriculum.grades')}</span>
      <div className="ledge-end">
        <button type="button" className="act plain small" onClick={() => setRetired(!retired)}>
          {retired ? t('curriculum.hideRetired') : t('curriculum.showRetired')}
        </button>
        {next && mayChange ? (
          <button
            type="button"
            className="act first"
            disabled={busy}
            onClick={() =>
              void start('NewNode', { kind: 'NewNode', parentNodeId: nodeId ?? null, nodeKind: next })
            }
          >
            {t('curriculum.add')} — {t(`curriculum.kind.${next}`)}
          </button>
        ) : null}
      </div>
    </>,
    [node?.id, retired, busy, mayChange, next, t],
  )

  return (
    <div className="stack loose">
      <div className="stack tight">
        {node ? (
          <Trail
            steps={[{ id: 'root', label: t('curriculum.title') }, ...trailOf(here.data?.trail, languages).slice(0, -1)]}
            onGo={(id) => navigate(id === 'root' ? '/curriculum' : `/curriculum/${id}`)}
          />
        ) : null}

        <div className="heading">
          <h1>{node ? title(node.titles, languages) : t('curriculum.title')}</h1>
          <span className="engraved">
            {node ? t(`curriculum.kind.${node.kind}`) : t('curriculum.said')}
          </span>
          {node?.isRetired ? <Mark stroke="ended">{t('curriculum.retire')}</Mark> : null}
        </div>

        {node && !node.inScope ? <p className="beside">{t('curriculum.notYours')}</p> : null}

        {/* The two boards that are about this place rather than about the tree.
            They belong here, beside the place they are about, rather than as two
            more names along the top that nobody would connect to anything. */}
        {node ? (
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

      {node ? (
        <NodeActions
          node={node}
          busy={busy}
          onStart={(kind, body) => void start(kind, body)}
          childCount={shown.length}
        />
      ) : null}

      {loading ? (
        <Wiping rows={5} />
      ) : shown.length === 0 ? (
        <Nothing title={t('curriculum.empty')}>{t('curriculum.emptySaid')}</Nothing>
      ) : (
        <div className="rows">
          {shown.map((child) => (
            <Line key={child.id} node={child} />
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
  const to = node.kind === 'lesson' ? `/lessons/${node.id}` : `/curriculum/${node.id}`

  return (
    <Link to={to} className="row">
      <span className="row-main">
        <span className="row-title">{title(node.titles, languages)}</span>
        <span className="spread" style={{ gap: 'var(--s3)' }}>
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            {t(`curriculum.kind.${node.kind}`)}
          </span>
          {node.kind !== 'lesson' && node.childCount > 0 ? (
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
 * What can be done to this part of the curriculum. Every one of them starts a
 * draft — the Studio has no way of changing the tree that skips review.
 */
function NodeActions({
  node,
  busy,
  childCount,
  onStart,
}: {
  node: StudioNode
  busy: boolean
  childCount: number
  onStart: (kind: DraftKind, body: Parameters<typeof studio.startDraft>[0]) => void
}) {
  const { t } = useI18n()

  if (!node.inScope) return null

  return (
    <div className="acts">
      <button
        type="button"
        className="act small"
        disabled={busy}
        onClick={() => onStart('Rename', { kind: 'Rename', nodeId: node.id })}
      >
        {t('curriculum.rename')}
      </button>

      <button
        type="button"
        className="act small"
        disabled={busy}
        onClick={() => onStart('Move', { kind: 'Move', nodeId: node.id })}
      >
        {t('curriculum.move')}
      </button>

      {childCount > 1 ? (
        <button
          type="button"
          className="act small"
          disabled={busy}
          onClick={() => onStart('Reorder', { kind: 'Reorder', nodeId: node.id })}
        >
          {t('curriculum.reorder')}
        </button>
      ) : null}

      {node.isRetired ? (
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
