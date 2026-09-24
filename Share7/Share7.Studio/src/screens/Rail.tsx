import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Place } from '../App'
import { useI18n } from '../i18n/i18n'
import { studioApi } from '../lib/api'
import { signOut, useMe } from '../lib/session'
import { studio } from '../lib/studio'

// ===========================================================================
// The frame's top edge
//
// The places are chalked along it and the one you are on is underlined — the
// board's own headings are its navigation, so there is no sidebar taking a
// column away from the work.
//
// Two numbers live here, and they are the only counts in the Studio that
// follow you around: how much is waiting for review, and how much happened
// while you were away. Both are read again every minute, quietly.
// ===========================================================================

export function Rail() {
  const { t, language, setLanguage } = useI18n()
  const me = useMe()
  const [waiting, setWaiting] = useState(0)
  const [unread, setUnread] = useState(0)

  useEffect(() => {
    let alive = true

    const count = async () => {
      try {
        const [queue, inbox] = await Promise.all([studio.queue(), studio.notifications(true, 50)])
        if (!alive) return
        setWaiting(queue.filter((item) => item.canReview).length)
        setUnread(inbox.length)
      } catch {
        // A count that cannot be read is left as it was; nothing here is worth a message.
      }
    }

    void count()
    const timer = window.setInterval(count, 60_000)
    return () => {
      alive = false
      window.clearInterval(timer)
    }
  }, [])

  const swap = async (next: 'en' | 'ar') => {
    setLanguage(next)
    try {
      await studioApi.setLanguage(next)
    } catch {
      // Saved on this browser regardless; the profile catches up next time.
    }
  }

  return (
    <header className="rail">
      <Link to="/" className="rail-mark">
        <BoardMark />
        {t('studio.name')}
      </Link>

      <nav className="places" aria-label={t('studio.name')}>
        <Place to="/">{t('place.home')}</Place>
        <Place to="/curriculum">{t('place.curriculum')}</Place>
        <Place to="/bank">{t('place.bank')}</Place>
        <Place to="/skills">{t('place.skills')}</Place>
        <Place to="/quality">{t('place.quality')}</Place>
        <Place to="/recovery">{t('place.recovery')}</Place>
        <Place to="/exams">{t('place.exams')}</Place>
        <Place to="/reviews" count={waiting}>
          {t('place.reviews')}
        </Place>
        <Place to="/releases">{t('place.releases')}</Place>
        <Place to="/activity" count={unread}>
          {t('place.activity')}
        </Place>
        <Place to="/handbook">{t('place.handbook')}</Place>
      </nav>

      <div className="rail-end">
        <div className="segments" role="group" aria-label={t('common.language')}>
          <button type="button" aria-pressed={language === 'en'} onClick={() => void swap('en')} lang="en">
            English
          </button>
          <button type="button" aria-pressed={language === 'ar'} onClick={() => void swap('ar')} lang="ar">
            العربية
          </button>
        </div>

        <Link to="/account" className="act plain small rail-name" title={t('place.settings')}>
          {me.fullName}
        </Link>

        <button type="button" className="act plain small" onClick={() => void signOut()}>
          {t('common.signOut')}
        </button>
      </div>
    </header>
  )
}

/** The board itself, small: a green field, an aluminium frame, three chalked lines. */
function BoardMark() {
  return (
    <svg width="20" height="20" viewBox="0 0 32 32" aria-hidden focusable="false">
      <rect x="1.5" y="3.5" width="29" height="25" rx="1.5" fill="none" stroke="var(--frame)" strokeWidth="1.5" />
      <path d="M7 12h18M7 18h12" stroke="var(--ink)" strokeWidth="1.8" strokeLinecap="round" opacity="0.9" />
      <path d="M7 24h7" stroke="var(--go)" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  )
}
