import { useNavigate } from 'react-router-dom'
import { useLedge } from '../App'
import { Nothing, Wiping } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio } from '../lib/studio'
import { useLoad } from '../lib/use'
import { DraftRow } from './bits'

// ===========================================================================
// Reviews — everything waiting for a second pair of eyes
//
// One queue, oldest first, so nothing is quietly left at the bottom. Work you
// cannot review is still shown, with the reason beside it: you wrote part of
// it, it is outside your part of the curriculum, or it changes a language you
// do not work in. Hiding it would make the queue look shorter than it is.
// ===========================================================================

const because: Record<string, 'review.cannot.ownWork' | 'review.cannot.role' | 'review.cannot.node' | 'review.cannot.languages' | 'review.cannot.status'> = {
  ownWork: 'review.cannot.ownWork',
  role: 'review.cannot.role',
  node: 'review.cannot.node',
  languages: 'review.cannot.languages',
  status: 'review.cannot.status',
}

export function Reviews() {
  const { t, formatRelative } = useI18n()
  const navigate = useNavigate()
  const queue = useLoad(() => studio.queue(), [])

  const items = queue.data ?? []
  const yours = items.filter((item) => item.canReview)
  const others = items.filter((item) => !item.canReview)

  useLedge(
    <>
      <span className="engraved">{t('review.title')}</span>
      {yours.length > 0 ? (
        <span className="quiet">
          <span className="num">{yours.length}</span> {t('common.waiting')}
        </span>
      ) : null}
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => queue.reload()}>
          {t('common.refresh')}
        </button>
        <button type="button" className="act" onClick={() => navigate('/curriculum')}>
          {t('home.startWriting')}
        </button>
      </div>
    </>,
    [yours.length, t],
  )

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('review.title')}</h1>
        <span className="engraved">{t('review.said')}</span>
      </div>

      {queue.loading ? (
        <Wiping rows={4} />
      ) : items.length === 0 ? (
        <Nothing title={t('review.queueEmpty')}>{t('review.queueEmptySaid')}</Nothing>
      ) : (
        <>
          <section className="band">
            <div className="band-head">
              <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('home.waitingOnYou')}</h2>
            </div>

            {yours.length === 0 ? (
              <Nothing>{t('home.nothingWaitingSaid')}</Nothing>
            ) : (
              <div className="rows">
                {yours.map((item) => (
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

          {others.length > 0 ? (
            <section className="band">
              <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('review.forOthers')}</h2>
              <div className="rows">
                {others.map((item) => (
                  <DraftRow
                    key={item.draft.id}
                    draft={item.draft}
                    aside={
                      <span className="beside" style={{ fontSize: 'var(--t-sm)' }}>
                        {t(because[item.cannotReviewBecause ?? 'status'] ?? 'review.cannot.status')}
                      </span>
                    }
                  />
                ))}
              </div>
            </section>
          ) : null}
        </>
      )}
    </div>
  )
}
