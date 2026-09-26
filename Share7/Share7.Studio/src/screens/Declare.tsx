import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowDown, ArrowUp } from 'lucide-react'
import { useContentLanguages, useLedge } from '../App'
import { Beside, Nothing, Trail, Wiping, Write, useSaying, useTelling } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { StudioError } from '../lib/api'
import { useCurricula, useCurriculumName } from '../lib/curricula'
import { useMe } from '../lib/session'
import { studio, type ContentLanguage, type NodeTitle } from '../lib/studio'
import { useDoing } from '../lib/use'

// ===========================================================================
// Declaring a curriculum — its name, and what its levels are called
//
// One board for a new curriculum and for changing an existing one's name or
// levels. A Lead whose part is the whole curriculum does this straight away:
// an empty curriculum the game does not play changes nothing a student sees.
//
// The levels are the whole shape, so they are the board: numbered, ruled, one
// name per language on each line, and the last line is the one students play.
// They are KINDS of level — Grade, Term, Subject — and the board has to make
// that unmistakable, because the first person to use it typed KG1, KG2 and
// Primary 1 into them, taking them for the grades themselves. So a new
// curriculum starts from the shape the team already works in, the heading
// says what goes here and what goes on the next board, and a name that looks
// like one grade rather than a kind is pointed out beside it.
//
// Once something sits at a level, that level and every one above it keep
// their places — they can still be renamed. The levels below the deepest
// occupied one hold nothing yet, so they stay free, and the board says which
// is which beside the line where the fixed ones end.
// ===========================================================================

type Names = Record<string, string>

interface LevelDraft {
  /** Stable while editing, so moving a line up does not hand its text to its neighbour. */
  id: string
  names: Names
}

/** One problem the server named, and where on the board it belongs. */
interface Problem {
  code: string
  field: 'title' | 'level' | 'levels'
  level?: number | null
  langId?: string | null
  max?: number
}

let nextId = 0
const fresh = (names: Names = {}): LevelDraft => ({ id: `level-${nextId++}`, names })

/**
 * The shape the team already works in, in each content language — the Egyptian curriculum's own
 * five levels, named as the Studio names them there. Content, not interface: the English field gets
 * the English names whatever language the Studio is being read in.
 */
const FAMILIAR: Record<string, string[]> = {
  en: ['Grade', 'Term', 'Subject', 'Chapter', 'Lesson'],
  ar: ['صف', 'فصل دراسي', 'مادة', 'وحدة', 'درس'],
}

function familiar(languages: ContentLanguage[]): LevelDraft[] {
  return FAMILIAR.en.map((_, at) =>
    fresh(
      Object.fromEntries(
        languages.filter((language) => FAMILIAR[language.code]).map((language) => [language.id, FAMILIAR[language.code][at]]),
      ),
    ),
  )
}

/**
 * Whether a level's name reads like one of its members rather than the kind: a number (Grade 1,
 * KG1, الصف ١) or an ordinal (First, الأول). A hint only — the server accepts it.
 */
function looksLikeOne(name: string): boolean {
  return (
    /[0-9٠-٩]/.test(name) ||
    /\b(first|second|third|fourth|fifth|sixth|one|two|three)\b/i.test(name) ||
    /(الأول|الأولى|الثاني|الثانية|الثالث|الثالثة|الرابع|الخامس|السادس)/.test(name)
  )
}

export function Declare() {
  const { curriculumId } = useParams()
  const editing = !!curriculumId
  const { t } = useI18n()
  const navigate = useNavigate()
  const me = useMe()
  const saying = useSaying()
  const { say } = useTelling()
  const languages = useContentLanguages()
  const { list, loading, reload } = useCurricula()
  const curriculumName = useCurriculumName()
  const [busy, run] = useDoing()

  const existing = editing ? list.find((one) => one.id === curriculumId) : undefined
  const locked = existing?.levelsLocked ?? false
  // How many levels from the top keep their places. A new curriculum has nothing in it yet.
  const fixed = existing?.fixedLevels ?? 0

  const [titles, setTitles] = useState<Names>({})
  const [levels, setLevels] = useState<LevelDraft[]>([])
  const [problems, setProblems] = useState<Problem[]>([])
  const [ready, setReady] = useState(false)

  // Editing starts from what is there; a new curriculum from the familiar shape. Either once the
  // content languages it is written in have arrived.
  useEffect(() => {
    if (ready) return
    if (editing) {
      if (!existing) return
      setTitles(Object.fromEntries(existing.titles.map((one) => [one.langId, one.title])))
      setLevels(existing.levels.map((level) => fresh(Object.fromEntries(level.names.map((one) => [one.langId, one.title])))))
      setReady(true)
    } else if (languages.length > 0) {
      setLevels(familiar(languages))
      setReady(true)
    }
  }, [editing, existing, languages, ready])

  const mayDeclare = me.studioRole === 'Lead' && me.scope.allNodes
  const leave = () => navigate(editing ? `/curriculum/of/${curriculumId}` : '/curriculum')

  const toTitles = (names: Names): NodeTitle[] =>
    languages.filter((language) => (names[language.id] ?? '').trim() !== '').map((language) => ({ langId: language.id, title: names[language.id].trim() }))

  const submit = (event?: FormEvent) => {
    event?.preventDefault()
    return run(async () => {
      setProblems([])
      const body = { titles: toTitles(titles), levels: levels.map((level) => ({ names: toTitles(level.names) })) }

      try {
        const saved = editing
          ? await studio.updateCurriculum(curriculumId!, body)
          : await studio.declareCurriculum(body)

        reload()
        say(editing ? t('common.saved') : t('curricula.created'))
        navigate(`/curriculum/of/${saved.id}`)
      } catch (error) {
        const found = error instanceof StudioError ? (error.details?.problems as Problem[] | undefined) : undefined
        if (found?.length) setProblems(found)
        else saying(error)
      }
    })
  }

  useLedge(
    <>
      <span className="engraved">{editing ? curriculumName(existing) : t('curricula.new.title')}</span>
      <div className="ledge-end">
        <button type="button" className="act plain small" onClick={leave}>
          {t('common.cancel')}
        </button>
        {mayDeclare ? (
          <button type="submit" form="declare" className="act first" disabled={busy || !ready}>
            {editing ? t('common.save') : t('curricula.declare')}
          </button>
        ) : null}
      </div>
    </>,
    [busy, ready, editing, existing?.id, mayDeclare, t],
  )

  if ((editing && loading) || (!ready && mayDeclare && (!editing || existing))) return <Wiping rows={6} />

  if (editing && (!existing || existing.isServed)) {
    return (
      <Nothing
        title={t(existing?.isServed ? 'errors.curriculum.served' : 'errors.curriculum.notFound')}
        action={
          <Link to="/curriculum" className="act">
            {t('curricula.title')}
          </Link>
        }
      />
    )
  }

  if (!mayDeclare) {
    return (
      <Nothing
        title={t('curricula.onlyLeads')}
        action={
          <Link to="/curriculum" className="act">
            {t('curricula.title')}
          </Link>
        }
      >
        {t('curricula.onlyLeadsSaid')}
      </Nothing>
    )
  }

  const problemFor = (field: Problem['field'], langId: string | null, level?: number) => {
    const found = problems.find(
      (one) =>
        one.field === field &&
        (field !== 'level' || one.level === level) &&
        (one.langId == null || one.langId === langId),
    )
    return found ? said(found, languages, t) : undefined
  }

  const levelsProblem = problems.find((one) => one.field === 'levels')

  // A free line moves among the free lines only: never up into the ones that keep their places.
  const move = (at: number, by: -1 | 1) => {
    const to = at + by
    if (to < fixed || to >= levels.length) return
    const next = [...levels]
    ;[next[at], next[to]] = [next[to], next[at]]
    setLevels(next)
  }

  return (
    <form id="declare" onSubmit={submit} className="stack loose">
      <div className="stack tight">
        <Trail
          steps={[
            { id: 'root', label: t('curricula.title') },
            ...(editing ? [{ id: 'curriculum', label: curriculumName(existing) }] : []),
          ]}
          linkAll
          onGo={(id) => navigate(id === 'root' ? '/curriculum' : `/curriculum/of/${curriculumId}`)}
        />

        <div className="heading">
          <h1>{editing ? t('curricula.edit.title') : t('curricula.new.title')}</h1>
        </div>
        <p className="said">{editing ? t('curricula.edit.said') : t('curricula.new.said')}</p>
      </div>

      <section className="band">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('curricula.name')}</h2>
        <div className="pair">
          {languages.map((language) => (
            <Write
              key={language.id}
              label={language.name}
              value={titles[language.id] ?? ''}
              onChange={(event) => setTitles({ ...titles, [language.id]: event.target.value })}
              dir={language.direction}
              lang={language.code}
              maxLength={200}
              autoFocus={!editing && language === languages[0]}
              problem={problemFor('title', language.id)}
            />
          ))}
        </div>
      </section>

      <section className="band">
        <div className="stack tight">
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('curricula.shape')}</h2>
          <p className="said">{t('curricula.shapeSaid')}</p>
        </div>

        {locked ? <Beside>{t('curricula.locked')}</Beside> : null}
        {levelsProblem ? <Beside tone="wrong">{said(levelsProblem, languages, t)}</Beside> : null}

        <ol className="loop levels">
          {levels.map((level, at) => {
            const played = at === levels.length - 1
            const keeps = at < fixed
            const oneNotKind = Object.values(level.names).some((name) => looksLikeOne(name.trim()))

            return (
              <li key={level.id}>
                <div className="stack tight">
                  <div className="pair">
                    {languages.map((language) => (
                      <Write
                        key={language.id}
                        label={language.name}
                        value={level.names[language.id] ?? ''}
                        onChange={(event) => {
                          const next = [...levels]
                          next[at] = { ...level, names: { ...level.names, [language.id]: event.target.value } }
                          setLevels(next)
                        }}
                        dir={language.direction}
                        lang={language.code}
                        maxLength={64}
                        aria-label={t('curricula.levelField', { n: at + 1, language: language.name })}
                        placeholder={placeholder(at, levels.length, language, t)}
                        problem={problemFor('level', language.id, at + 1)}
                      />
                    ))}
                  </div>

                  {/* The mistake this board was rebuilt to prevent, caught where it is made. */}
                  {oneNotKind ? <Beside>{t('curricula.oneNotKind')}</Beside> : null}

                  {/* Where the fixed levels end, said once, on the last of them. */}
                  {keeps && at === fixed - 1 && !locked ? <Beside>{t('curricula.fixedHere')}</Beside> : null}

                  {/* The one fact about the last line that nothing else on the board says. */}
                  {played ? <Beside tone="live">{t('curricula.playedHere')}</Beside> : null}

                  {keeps || locked ? null : (
                    <div className="acts">
                      <button
                        type="button"
                        className="act icon"
                        disabled={at <= fixed}
                        onClick={() => move(at, -1)}
                        aria-label={t('lesson.moveUp')}
                      >
                        <ArrowUp size={14} strokeWidth={1.5} aria-hidden />
                      </button>
                      <button
                        type="button"
                        className="act icon"
                        disabled={at === levels.length - 1}
                        onClick={() => move(at, 1)}
                        aria-label={t('lesson.moveDown')}
                      >
                        <ArrowDown size={14} strokeWidth={1.5} aria-hidden />
                      </button>
                      <button
                        type="button"
                        className="act plain small"
                        disabled={levels.length === 1 || levels.length - 1 <= fixed}
                        onClick={() => setLevels(levels.filter((one) => one.id !== level.id))}
                      >
                        {t('curricula.removeLevel')}
                      </button>
                    </div>
                  )}
                </div>
              </li>
            )
          })}
        </ol>

        {locked ? null : (
          <button
            type="button"
            className="act small"
            style={{ justifySelf: 'start' }}
            disabled={levels.length >= 8}
            onClick={() => setLevels([...levels, fresh()])}
          >
            {t('curricula.addLevel')}
          </button>
        )}
      </section>
    </form>
  )
}

/** A hint of the shape for a line left blank: "Subject" on top, "Lesson" at the bottom. */
function placeholder(at: number, count: number, language: ContentLanguage, t: ReturnType<typeof useI18n>['t']) {
  const arabic = language.code === 'ar'
  if (at === 0) return arabic ? t('curricula.example.topAr') : t('curricula.example.top')
  if (at === count - 1) return arabic ? t('curricula.example.playedAr') : t('curricula.example.played')
  return ''
}

/** One server problem as one sentence, in the member's language. */
function said(problem: Problem, languages: ContentLanguage[], t: ReturnType<typeof useI18n>['t']) {
  const language = languages.find((one) => one.id === problem.langId)?.name ?? ''
  switch (problem.code) {
    case 'missing':
      return language ? t('curricula.problem.missingIn', { language }) : t('curricula.problem.missing')
    case 'tooLong':
      return t('curricula.problem.tooLong', { max: problem.max ?? 0 })
    case 'taken':
      return t('curricula.problem.taken')
    case 'noLevels':
      return t('curricula.problem.noLevels')
    case 'tooManyLevels':
      return t('curricula.problem.tooManyLevels', { max: problem.max ?? 8 })
    case 'repeated':
      return t('curricula.problem.repeated')
    default:
      return t('errors.curriculum.invalid')
  }
}
