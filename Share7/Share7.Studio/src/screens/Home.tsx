import { useNavigate } from 'react-router-dom'
import { useLedge } from '../App'
import { Mark, Nothing, Wiping, useSaying, useTelling } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { useMe } from '../lib/session'
import { studio } from '../lib/studio'
import { useDoing, useLoad } from '../lib/use'
import { DraftRow, Notified, TrailLine } from './bits'

// ===========================================================================
// Home — the board as you find it
//
// Four bands, in the order a member cares about them: what is waiting on you,
// what you are writing, what you were given, and what happened while you were
// away. A band with nothing in it teaches what would put something there
// rather than saying "nothing here".
// ===========================================================================

export function Home() {
  const { t, formatRelative, formatDate } = useI18n()
  const me = useMe()
  const navigate = useNavigate()
  const saying = useSaying()
  const { say } = useTelling()
  const [busy, run] = useDoing()

  const queue = useLoad(() => studio.queue(), [])
  const mine = useLoad(() => studio.drafts({ mine: true, includePractice: true }), [])
  const given = useLoad(() => studio.assignments(true, false), [])
  const inbox = useLoad(() => studio.notifications(false, 20), [])

  const startPractice = () =>
    run(async () => {
      try {
        const draft = await studio.startDraft({ kind: 'LessonContent', isPractice: true })
        navigate(`/drafts/${draft.summary.id}`)
      } catch (error) {
        saying(error)
      }
    })

  useLedge(
    <>
      <span className="engraved">{t('home.today')}</span>
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => void startPractice()} disabled={busy}>
          {t('lesson.startPractice')}
        </button>
        <button type="button" className="act first" onClick={() => navigate('/curriculum')}>
          {t('home.startWriting')}
        </button>
      </div>
    </>,
    [busy, t],
  )

  const reviewable = (queue.data ?? []).filter((item) => item.canReview)
  const unread = (inbox.data ?? []).filter((one) => !one.isRead)

  const markAllRead = () =>
    run(async () => {
      try {
        await studio.markRead(null)
        inbox.reload()
        say(t('common.done'))
      } catch (error) {
        saying(error)
      }
    })

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('home.greeting', { name: me.fullName })}</h1>
        <time className="engraved" dateTime={new Date().toISOString().slice(0, 10)}>
          {formatDate(new Date())}
        </time>
      </div>

      <section className="band">
        <div className="band-head">
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('home.waitingOnYou')}</h2>
          {reviewable.length > 0 ? (
            <span className="quiet">
              <span className="num">{reviewable.length}</span> {t('common.waiting')}
            </span>
          ) : null}
        </div>

        {queue.loading ? (
          <Wiping rows={2} />
        ) : reviewable.length === 0 ? (
          <Nothing>{t('home.nothingWaitingSaid')}</Nothing>
        ) : (
          <div className="rows">
            {reviewable.slice(0, 6).map((item) => (
              <DraftRow
                key={item.draft.id}
                draft={item.draft}
                aside={
                  <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                    {t('review.waitingFor', { duration: formatRelative(item.waitingSince) })}
                  </span>
                }
              />
            ))}
          </div>
        )}
      </section>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('home.yourDrafts')}</h2>

        {mine.loading ? (
          <Wiping rows={2} />
        ) : (mine.data?.drafts.length ?? 0) === 0 ? (
          <Nothing
            title={t('home.noDrafts')}
            action={
              <button type="button" className="act" onClick={() => navigate('/curriculum')}>
                {t('home.startWriting')}
              </button>
            }
          >
            {t('home.noDraftsSaid')}
          </Nothing>
        ) : (
          <div className="rows">
            {mine.data?.drafts.map((draft) => (
              <DraftRow key={draft.id} draft={draft} />
            ))}
          </div>
        )}
      </section>

      {(given.data?.length ?? 0) > 0 ? (
        <section className="band">
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('home.yourAssignments')}</h2>
          <div className="rows">
            {given.data?.map((one) => {
              const late = one.dueOn !== null && new Date(one.dueOn) < new Date()
              return (
                <a key={one.id} className="row" href={`/lessons/${one.nodeId}`}>
                  <span className="row-main">
                    <span className="row-title">{one.note ?? t('curriculum.kind.lesson')}</span>
                    <TrailLine trail={one.trail} />
                  </span>
                  <span className="row-side">
                    {one.dueOn ? (
                      <Mark stroke={late ? 'wrong' : 'waiting'}>
                        {late ? t('home.overdue') : t('home.dueOn', { when: formatDate(one.dueOn) })}
                      </Mark>
                    ) : null}
                    <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                      {t('common.by', { name: one.assignedBy.name })}
                    </span>
                  </span>
                </a>
              )
            })}
          </div>
        </section>
      ) : null}

      <section className="band">
        <div className="band-head">
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('home.inbox')}</h2>
          {unread.length > 0 ? (
            <button type="button" className="act plain small" onClick={() => void markAllRead()} disabled={busy}>
              {t('home.markAllRead')}
            </button>
          ) : null}
        </div>

        {inbox.loading ? (
          <Wiping rows={3} />
        ) : (inbox.data?.length ?? 0) === 0 ? (
          <p className="quiet">{t('common.nothingYet')}</p>
        ) : (
          <div className="rows">
            {inbox.data?.map((one) => (
              <div key={one.id} className="row">
                <span className="row-main">
                  <span className="row-title" style={{ color: one.isRead ? 'var(--ink-2)' : undefined }}>
                    <Notified kind={one.kind} actor={one.actor?.name} />
                  </span>
                  {one.title ? <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>{one.title}</span> : null}
                </span>
                <span className="row-side">
                  {one.isRead ? null : <Mark stroke="waiting">{t('common.waiting')}</Mark>}
                  <time className="quiet" style={{ fontSize: 'var(--t-sm)' }} dateTime={one.createdAtUtc}>
                    {formatRelative(one.createdAtUtc)}
                  </time>
                </span>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}
