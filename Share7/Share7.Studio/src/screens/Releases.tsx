import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useLanguages, useLedge } from '../App'
import { Mark, Nothing, Sheet, Trail, Wiping, Write, useSaying, useTelling } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { useMe } from '../lib/session'
import { studio, type DiffLine, type ReleaseSummary, type TrailStep } from '../lib/studio'
import { useDoing, useLoad, useTrail } from '../lib/use'
import { ReleaseState } from './bits'

// ===========================================================================
// Releases — the only thing that writes live content
//
// A Lead bundles approved drafts. Before it goes out the board says what would
// stop it and who it reaches, both in plain words. Publishing is one
// transaction: if any part is refused, the whole release is refused and
// nothing it touched changed.
//
// Putting a release back is itself a release, so the version students see goes
// forward and no device can mistake old content for current.
// ===========================================================================

const blockerNames = {
  notApproved: 'blocker.notApproved',
  outOfDate: 'blocker.outOfDate',
  practice: 'blocker.practice',
  closed: 'blocker.closed',
  hasProblems: 'blocker.hasProblems',
  outOfScope: 'blocker.outOfScope',
  needsDraft: 'blocker.needsDraft',
  sameTarget: 'blocker.sameTarget',
} as const

export function Releases() {
  const { releaseId } = useParams()
  return releaseId ? <One releaseId={releaseId} /> : <List />
}

// ---------------------------------------------------------------------------

function List() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const me = useMe()
  const saying = useSaying()
  const [busy, run] = useDoing()
  const [building, setBuilding] = useState(false)
  const [name, setName] = useState('')
  const [notes, setNotes] = useState('')
  const [picked, setPicked] = useState<string[]>([])

  const releases = useLoad(() => studio.releases(), [])
  const approved = useLoad(() => studio.drafts({ status: 'Approved' }), [])
  const ready = approved.data?.drafts ?? []
  const mayRelease = me.studioRole === 'Lead'

  const build = () =>
    run(async () => {
      try {
        const made = await studio.startRelease(name.trim(), notes.trim() || null, picked)
        setBuilding(false)
        setName('')
        setNotes('')
        setPicked([])
        navigate(`/releases/${made.summary.id}`)
      } catch (error) {
        saying(error)
      }
    })

  useLedge(
    <>
      <span className="engraved">{t('release.title')}</span>
      <div className="ledge-end">
        {mayRelease ? (
          <button
            type="button"
            className="act first"
            disabled={busy || ready.length === 0}
            onClick={() => {
              setPicked(ready.map((draft) => draft.id))
              setBuilding(true)
            }}
          >
            {t('release.new')}
          </button>
        ) : null}
      </div>
    </>,
    [mayRelease, busy, ready.length, t],
  )

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('release.title')}</h1>
        <span className="engraved">{t('release.said')}</span>
      </div>

      <section className="band">
        <div className="band-head">
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('release.pick')}</h2>
          {ready.length > 0 ? (
            <span className="quiet">
              <span className="num">{ready.length}</span>
            </span>
          ) : null}
        </div>

        {approved.loading ? (
          <Wiping rows={2} />
        ) : ready.length === 0 ? (
          <Nothing title={t('release.nothingApproved')}>{t('release.nothingApprovedSaid')}</Nothing>
        ) : (
          <div className="rows">
            {ready.map((draft) => (
              <Link key={draft.id} className="row" to={`/drafts/${draft.id}`}>
                <span className="row-main">
                  <span className="row-title">{draft.title}</span>
                  <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                    {t(`kind.${draft.kind}`)}
                  </span>
                </span>
                <span className="row-side">
                  <Mark stroke="written">{t('status.Approved')}</Mark>
                </span>
              </Link>
            ))}
          </div>
        )}
      </section>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('release.sent')}</h2>

        {releases.loading ? (
          <Wiping rows={4} />
        ) : (releases.data?.length ?? 0) === 0 ? (
          <Nothing title={t('release.empty')}>{t('release.emptySaid')}</Nothing>
        ) : (
          <div className="rows">
            {releases.data?.map((release) => (
              <Row key={release.id} release={release} />
            ))}
          </div>
        )}
      </section>

      <Sheet
        open={building}
        onClose={() => setBuilding(false)}
        title={t('release.new')}
        actions={
          <>
            <button type="button" className="act" onClick={() => setBuilding(false)}>
              {t('common.cancel')}
            </button>
            <button type="button" className="act first" disabled={busy || name.trim() === '' || picked.length === 0} onClick={() => void build()}>
              {t('release.new')}
            </button>
          </>
        }
      >
        <div className="stack">
          <Write label={t('release.name')} value={name} onChange={(event) => setName(event.target.value)} autoFocus />
          <Write
            label={t('release.notes')}
            value={notes}
            lines={3}
            onChange={(event) => setNotes(event.target.value)}
            hint={t('common.optional')}
          />

          <div className="stack tight">
            <span className="engraved">{t('release.pick')}</span>
            <p className="beside">{t('release.pickSaid')}</p>
            {ready.map((draft) => (
              <label key={draft.id} className="spread" style={{ gap: 'var(--s3)' }}>
                <input
                  type="checkbox"
                  checked={picked.includes(draft.id)}
                  onChange={(event) =>
                    setPicked(
                      event.target.checked ? [...picked, draft.id] : picked.filter((id) => id !== draft.id),
                    )
                  }
                />
                <span className="grow">{draft.title}</span>
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {t(`kind.${draft.kind}`)}
                </span>
              </label>
            ))}
          </div>
        </div>
      </Sheet>
    </div>
  )
}

function Row({ release }: { release: ReleaseSummary }) {
  const { t, formatRelative } = useI18n()

  return (
    <Link className="row" to={`/releases/${release.id}`}>
      <span className="row-main">
        <span className="row-title">{release.title}</span>
        <span className="spread" style={{ gap: 'var(--s3)' }}>
          <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
            {t('release.contains', { count: release.draftCount })}
          </span>
          {release.rollbackOfReleaseId ? (
            <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
              {t('release.rollback')}
            </span>
          ) : null}
        </span>
      </span>
      <span className="row-side">
        {release.rolledBackByReleaseId ? <Mark stroke="ended">{t('release.rolledBackBy')}</Mark> : null}
        <ReleaseState status={release.status} />
        <time className="quiet" style={{ fontSize: 'var(--t-sm)' }} dateTime={release.publishedAtUtc ?? release.createdAtUtc}>
          {formatRelative(release.publishedAtUtc ?? release.createdAtUtc)}
        </time>
      </span>
    </Link>
  )
}

// ---------------------------------------------------------------------------

function One({ releaseId }: { releaseId: string }) {
  const { t, formatDate } = useI18n()
  const navigate = useNavigate()
  const me = useMe()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()
  const [when, setWhen] = useState('')
  const [scheduling, setScheduling] = useState(false)
  const [undoing, setUndoing] = useState(false)
  const [why, setWhy] = useState('')

  const release = useLoad(() => studio.release(releaseId), [releaseId])
  const it = release.data
  const mayRelease = me.studioRole === 'Lead'
  const blocked = (it?.blockers.length ?? 0) > 0

  const act = async (work: () => Promise<unknown>, told?: string) =>
    run(async () => {
      try {
        await work()
        release.reload()
        if (told) say(told)
        return true
      } catch (error) {
        saying(error)
        return false
      }
    })

  useLedge(
    <>
      <span className="engraved">{it?.summary.title ?? t('release.title')}</span>
      {it ? <ReleaseState status={it.summary.status} /> : null}

      <div className="ledge-end">
        {it && mayRelease && it.summary.status === 'Building' ? (
          <>
            <button type="button" className="act" disabled={busy} onClick={() => void act(() => studio.cancelRelease(releaseId))}>
              {t('release.cancel')}
            </button>
            <button type="button" className="act" disabled={busy || blocked} onClick={() => setScheduling(true)}>
              {t('release.schedule')}
            </button>
            <button
              type="button"
              className="act first"
              disabled={busy || blocked}
              onClick={() => void act(() => studio.publish(releaseId), t('release.publish'))}
            >
              {t('release.publish')}
            </button>
          </>
        ) : null}

        {it && mayRelease && it.summary.status === 'Scheduled' ? (
          <button type="button" className="act" disabled={busy} onClick={() => void act(() => studio.cancelRelease(releaseId))}>
            {t('release.cancel')}
          </button>
        ) : null}

        {it && mayRelease && it.summary.status === 'Failed' ? (
          <button
            type="button"
            className="act first"
            disabled={busy}
            onClick={() => void act(() => studio.publish(releaseId), t('release.publish'))}
          >
            {t('release.publish')}
          </button>
        ) : null}

        {it && mayRelease && it.summary.status === 'Published' && !it.summary.rolledBackByReleaseId ? (
          <button type="button" className="act grave" disabled={busy} onClick={() => setUndoing(true)}>
            {t('release.rollback')}
          </button>
        ) : null}
      </div>
    </>,
    [it?.summary.id, it?.summary.status, blocked, busy, mayRelease, t],
  )

  if (release.loading) return <Wiping rows={6} />
  if (!it) return <Nothing title={t('errors.release.notFound')} />

  return (
    <div className="stack loose">
      <div className="stack tight">
        <Trail steps={[{ id: 'root', label: t('release.title') }, { id: it.summary.id, label: it.summary.title }]} onGo={() => navigate('/releases')} />

        <div className="heading">
          <h1>{it.summary.title}</h1>
          <ReleaseState status={it.summary.status} />
        </div>

        {it.summary.notes ? <p className="said">{it.summary.notes}</p> : null}

        <div className="people">
          {it.summary.publishedAtUtc ? (
            <Mark stroke="written">
              {t('release.publishedAt', {
                when: formatDate(it.summary.publishedAtUtc, 'dateTime'),
                name: it.summary.publishedBy?.name ?? '',
              })}
            </Mark>
          ) : null}
          {it.summary.scheduledForUtc ? (
            <Mark stroke="waiting">{t('release.scheduledFor', { when: formatDate(it.summary.scheduledForUtc, 'dateTime') })}</Mark>
          ) : null}
          {it.summary.rolledBackByReleaseId ? <Mark stroke="ended">{t('release.rolledBackBy')}</Mark> : null}
          {it.summary.rollbackOfReleaseId ? (
            <Mark stroke="live">{t('release.isRollbackOf', { name: it.summary.reason ?? '' })}</Mark>
          ) : null}
        </div>

        {it.summary.status === 'Failed' ? (
          <p className="beside" data-tone="wrong">
            {`${t('release.failedSaid')} ${it.summary.failureMessage ?? ''}`}
          </p>
        ) : null}
      </div>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('release.blockers')}</h2>
        {blocked ? (
          <div className="rows">
            {it.blockers.map((blocker, at) => (
              <div className="row" key={`${blocker.draftId}-${at}`}>
                <span className="row-main">
                  <span className="row-title">
                    {it.items.find((item) => item.draftId === blocker.draftId)?.title ?? blocker.draftId}
                  </span>
                </span>
                <span className="row-side">
                  <Mark stroke="wrong">
                    {t(blockerNames[blocker.reason as keyof typeof blockerNames] ?? 'blocker.hasProblems')}
                  </Mark>
                  <Link className="act small" to={`/drafts/${blocker.draftId}`}>
                    {t('common.open')}
                  </Link>
                </span>
              </div>
            ))}
          </div>
        ) : (
          <>
            <Mark stroke="written">{t('release.ready')}</Mark>
            <p className="said">{t('release.readySaid')}</p>
          </>
        )}
      </section>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('release.impact')}</h2>
        <p className="said">{t('release.impactSaid')}</p>
        {it.impact.lines.length === 0 ? (
          <p className="quiet">{t('release.impactNone')}</p>
        ) : (
          <div className="rows">
            {it.impact.lines.map((line, at) => (
              <div className="row" key={`${line.nodeId}-${at}`}>
                <span className="row-main">
                  <TrailOf trail={line.trail} />
                </span>
                <span className="row-side">
                  <Mark stroke="live">{`${line.students} ${t(`impact.${line.kind}`)}`}</Mark>
                </span>
              </div>
            ))}
          </div>
        )}
      </section>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('review.whatChanged')}</h2>
        <div className="stack">
          {it.items.map((item, at) => (
            <div key={`${item.nodeId}-${at}`} className="stack tight">
              <div className="spread" style={{ gap: 'var(--s3)' }}>
                <strong style={{ fontWeight: 500 }}>{item.title}</strong>
                <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                  {t(`kind.${item.kind}`)}
                </span>
                {item.draftId ? (
                  <Link className="act plain small" to={`/drafts/${item.draftId}`}>
                    {t('common.open')}
                  </Link>
                ) : null}
              </div>
              <TrailOf trail={item.trail} />
              <Diff lines={item.diff} />
            </div>
          ))}
        </div>
      </section>

      <Sheet
        open={scheduling}
        onClose={() => setScheduling(false)}
        title={t('release.schedule')}
        actions={
          <>
            <button type="button" className="act" onClick={() => setScheduling(false)}>
              {t('common.cancel')}
            </button>
            <button
              type="button"
              className="act first"
              disabled={busy || when === ''}
              onClick={async () => {
                const done = await act(
                  () => studio.schedule(releaseId, new Date(when).toISOString()),
                  t('release.schedule'),
                )
                if (done) setScheduling(false)
              }}
            >
              {t('release.schedule')}
            </button>
          </>
        }
      >
        <Write
          label={t('release.scheduleAt')}
          type="datetime-local"
          value={when}
          onChange={(event) => setWhen(event.target.value)}
          hint={t('release.scheduleSaid')}
        />
      </Sheet>

      <Sheet
        open={undoing}
        onClose={() => setUndoing(false)}
        title={t('release.rollback')}
        actions={
          <>
            <button type="button" className="act" onClick={() => setUndoing(false)}>
              {t('common.cancel')}
            </button>
            <button
              type="button"
              className="act grave"
              disabled={busy || why.trim() === ''}
              onClick={async () => {
                const done = await act(() => studio.rollback(releaseId, why.trim()), t('release.rollback'))
                if (done) {
                  setUndoing(false)
                  setWhy('')
                }
              }}
            >
              {t('release.rollback')}
            </button>
          </>
        }
      >
        <Write
          label={t('release.rollbackReason')}
          value={why}
          lines={3}
          onChange={(event) => setWhy(event.target.value)}
          hint={t('release.rollbackSaid')}
        />
      </Sheet>
    </div>
  )
}

function TrailOf({ trail }: { trail: TrailStep[] }) {
  const languages = useLanguages()
  const trailOf = useTrail()
  const steps = trailOf(trail, languages)

  return (
    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
      {steps.map((step) => step.label).join(' · ')}
    </span>
  )
}

/** What one change replaces, line by line: what was, and what will be. */
function Diff({ lines }: { lines: DiffLine[] }) {
  const { t } = useI18n()
  const languages = useLanguages()

  if (lines.length === 0) return <p className="quiet">{t('review.noChanges')}</p>

  return (
    <div className="stack tight">
      {lines.slice(0, 40).map((line, at) => {
        const language = languages.find((one) => one.id === line.langId)
        return (
          <div className="against" key={at}>
            <div className="against-side">
              <span className="engraved">
                {`${line.order ? `${line.order}. ` : ''}${language?.name ?? ''} ${t(
                  line.change === 'added'
                    ? 'lesson.added'
                    : line.change === 'removed'
                      ? 'lesson.removed'
                      : 'lesson.changed',
                )}`}
              </span>
              {line.before ? <span className="was">{line.before}</span> : null}
            </div>
            <div className="against-side">
              {line.after ? <span className="now">{line.after}</span> : null}
            </div>
          </div>
        )
      })}
      {lines.length > 40 ? <p className="quiet">{t('common.andMore', { count: lines.length - 40 })}</p> : null}
    </div>
  )
}
