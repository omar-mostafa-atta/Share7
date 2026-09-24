import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useContentLanguages, useLanguages, useLedge } from '../App'
import { Choose, Mark, Nothing, Wiping, Write } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio } from '../lib/studio'
import { useLoad, useSettled, useTrail } from '../lib/use'

// ===========================================================================
// The question bank
//
// Every question the game is serving right now, searched by its words. It is
// the answer to "have we asked this already?" and "where did we word it that
// way?", and each hit opens the lesson it belongs to.
//
// Only live questions are here. Work still in a draft belongs to whoever is
// writing it, and the bank would make it look published.
// ===========================================================================

export function Bank() {
  const { t } = useI18n()
  const languages = useLanguages()
  const content = useContentLanguages()
  const trailOf = useTrail()
  const [words, setWords] = useState('')
  const [langId, setLangId] = useState('')

  const asked = useSettled(words, 400)
  const hits = useLoad(
    () => (asked.trim().length >= 2 ? studio.search(asked.trim(), null, langId || null, 60) : Promise.resolve([])),
    [asked, langId],
  )

  useLedge(
    <>
      <span className="engraved">{t('place.bank')}</span>
      {hits.data ? (
        <span className="quiet">
          <span className="num">{hits.data.length}</span> {t('common.questions')}
        </span>
      ) : null}
    </>,
    [hits.data?.length, t],
  )

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('place.bank')}</h1>
        <span className="engraved">{t('bank.said')}</span>
      </div>

      <div className="spread">
        <div className="grow">
          <Write
            label={t('common.search')}
            value={words}
            autoFocus
            onChange={(event) => setWords(event.target.value)}
            placeholder={t('lesson.questionText')}
          />
        </div>
        <Choose label={t('common.language')} value={langId} onChange={(event) => setLangId(event.target.value)}>
          <option value="">{t('common.all')}</option>
          {content.map((language) => (
            <option key={language.id} value={language.id}>
              {language.name}
            </option>
          ))}
        </Choose>
      </div>

      {asked.trim().length < 2 ? (
        <Nothing title={t('bank.start')}>{t('bank.startSaid')}</Nothing>
      ) : hits.loading ? (
        <Wiping rows={5} />
      ) : (hits.data?.length ?? 0) === 0 ? (
        <Nothing title={t('bank.nothing')}>{t('bank.nothingSaid')}</Nothing>
      ) : (
        <div className="rows">
          {hits.data?.map((hit) => {
            const language = languages.find((one) => one.id === hit.langId)
            const steps = trailOf(hit.trail, languages)
            return (
              <Link key={`${hit.questionId}`} className="row" to={`/lessons/${hit.lessonId}`}>
                <span className="row-main">
                  <span className="row-title" dir={language?.direction} lang={language?.code}>
                    {hit.text}
                  </span>
                  <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                    {steps.map((step) => step.label).join(' · ')}
                  </span>
                </span>
                <span className="row-side">
                  {hit.role === 'Recovery' ? <Mark stroke="live">{t('lesson.recovery')}</Mark> : null}
                  <span className="quiet" style={{ fontSize: 'var(--t-sm)' }}>
                    {language?.name}
                  </span>
                  <span className="num quiet" style={{ fontSize: 'var(--t-sm)' }}>
                    {hit.order}
                  </span>
                </span>
              </Link>
            )
          })}
        </div>
      )}
    </div>
  )
}
