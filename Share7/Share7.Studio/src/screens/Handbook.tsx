import type { ReactNode } from 'react'
import { useLedge } from '../App'
import { useI18n } from '../i18n/i18n'
import { handbook } from './handbook-text'

// ===========================================================================
// The handbook
//
// Read mode inside an Operate app: set for reading, one column, generous
// measure, and a contents list that jumps rather than a sidebar that follows.
// It prints cleanly, because somebody will want it on the desk on their first
// morning.
// ===========================================================================

export function Handbook() {
  const { t, language } = useI18n()
  const chapters = handbook[language]

  useLedge(
    <>
      <span className="engraved">{t('handbook.title')}</span>
      <div className="ledge-end">
        <button type="button" className="act" onClick={() => window.print()}>
          {t('handbook.print')}
        </button>
      </div>
    </>,
    [t],
  )

  return (
    <div className="stack loose">
      <div className="heading">
        <h1>{t('handbook.title')}</h1>
        <span className="engraved">{t('handbook.said')}</span>
      </div>

      <nav className="stack tight" aria-label={t('handbook.contents')}>
        <span className="engraved">{t('handbook.contents')}</span>
        <ol className="contents">
          {chapters.map((chapter, at) => (
            <li key={chapter.id}>
              <a href={`#${chapter.id}`}>
                <span className="num quiet">{at + 1}.</span> {chapter.title}
              </a>
            </li>
          ))}
        </ol>
      </nav>

      <article className="reading">
        {chapters.map((chapter) => (
          <section key={chapter.id} id={chapter.id} className="stack">
            <h2>{chapter.title}</h2>
            {chapter.paragraphs.map((paragraph, at) => (
              <p key={at}>{strong(paragraph)}</p>
            ))}
            {chapter.points ? (
              <ul>
                {chapter.points.map((point, at) => (
                  <li key={at}>{strong(point)}</li>
                ))}
              </ul>
            ) : null}
          </section>
        ))}
      </article>
    </div>
  )
}

/** **Like this** is the only mark the handbook's text uses. */
function strong(text: string): ReactNode {
  return text.split(/\*\*(.+?)\*\*/g).map((piece, at) => (at % 2 === 1 ? <strong key={at}>{piece}</strong> : piece))
}
