import { useEffect, useRef } from 'react'
import { MessageSquare, Plus, Trash2, Undo2 } from 'lucide-react'
import { useI18n } from '../i18n/i18n'
import type {
  CommentAnchor,
  ContentLanguage,
  ContentProblem,
  DraftComment,
  DraftItem,
  DraftRendering,
  ItemRole,
  LessonContent,
} from '../lib/studio'

// ===========================================================================
// The written lesson
//
// The whole lesson on one board, the way it would be read aloud: a number, the
// question after it, its three answers indented beneath with the right one
// underlined, and each language under the same number in its own direction.
// No cards, no panels, no containers anywhere — numbering, rules and
// indentation carry the structure, and you edit the words where they read.
//
// Every problem is chalked in the outer margin beside the exact line it
// belongs to, joined to it by a short leader, so nothing is ever hidden behind
// a tab or a tooltip.
// ===========================================================================

const MAX_QUESTION = 1000
const MAX_CHOICE = 500

/** A, B, C — or أ, ب, ج, because the answers are read in the language they are written in. */
const letters: Record<string, string[]> = {
  ar: ['أ', 'ب', 'ج', 'د'],
}

function letter(code: string, index: number) {
  return (letters[code] ?? ['A', 'B', 'C', 'D'])[index] ?? String(index + 1)
}

export function blankRendering(langId: string): DraftRendering {
  return { langId, text: '', choices: ['', '', ''], correctIndex: 0 }
}

export function blankItem(role: ItemRole, order: number, languages: ContentLanguage[]): DraftItem {
  return { itemId: null, role, order, renderings: languages.map((one) => blankRendering(one.id)) }
}

/** Main pool first, each in position order — the order the board is read in. */
export function inOrder(items: DraftItem[], role: ItemRole) {
  return items.filter((item) => item.role === role).sort((a, b) => a.order - b.order)
}

export interface WrittenProps {
  items: DraftItem[]
  languages: ContentLanguage[]
  problems: ContentProblem[]
  comments?: DraftComment[]
  live?: LessonContent | null
  /** Whether the board may say how each line stands against what is live. Only true while a draft is open. */
  standing?: boolean
  readOnly?: boolean
  onChange?: (items: DraftItem[]) => void
  onComment?: (anchor: CommentAnchor) => void
}

export function Written(props: WrittenProps) {
  const { t } = useI18n()
  const main = inOrder(props.items, 'Core')
  const recovery = inOrder(props.items, 'Recovery')

  // What is live but not in the draft any more: still shown, struck through,
  // because a question quietly disappearing is how a lesson loses its history.
  const kept = new Set(props.items.map((item) => item.itemId).filter(Boolean) as string[])

  // While the draft matches what is live, the margin says nothing: 'Unchanged'
  // on every line is noise. The moment one line moves, every line says where it
  // stands, and 'Unchanged — keeps its place' means something again.
  const moved =
    props.standing === true &&
    (props.items.some((item) => !item.itemId || !sameAs(item, props.live?.items.find((one) => one.itemId === item.itemId))) ||
      (props.live?.items ?? []).some((one) => !kept.has(one.itemId)))
  const goneCore = (props.live?.items ?? []).filter((item) => item.role === 'Core' && !kept.has(item.itemId))
  const goneRecovery = (props.live?.items ?? []).filter((item) => item.role === 'Recovery' && !kept.has(item.itemId))

  return (
    <div className="written">
      <Pool
        {...props}
        standing={moved}
        role="Core"
        all={props.items}
        items={main}
        gone={goneCore}
        heading={t('lesson.main')}
        said={t('lesson.mainSaid')}
        first
      />

      <div className="written-divider">
        <h2 style={{ fontSize: 'var(--t-lg)' }}>{t('lesson.recovery')}</h2>
        <p className="said" style={{ fontSize: 'var(--t-sm)' }}>
          {t('lesson.recoverySaid')}
        </p>
      </div>

      <Pool {...props} standing={moved} role="Recovery" all={props.items} items={recovery} gone={goneRecovery} heading="" said="" />
    </div>
  )
}

function Pool({
  role,
  items,
  all,
  gone,
  heading,
  said,
  first,
  ...rest
}: Omit<WrittenProps, 'items'> & {
  role: ItemRole
  items: DraftItem[]
  all: DraftItem[]
  gone: LessonContent['items']
  heading: string
  said: string
  first?: boolean
}) {
  const { t } = useI18n()
  const { languages, onChange, readOnly } = rest

  const replace = (item: DraftItem, next: Partial<DraftItem>) =>
    onChange?.(all.map((one) => (one === item ? { ...one, ...next } : one)))

  const add = () => {
    const order = items.length + 1
    onChange?.([...all, blankItem(role, order, languages)])
  }

  const remove = (item: DraftItem) => {
    const left = all.filter((one) => one !== item)
    onChange?.(renumber(left, role))
  }

  const bring = (live: LessonContent['items'][number]) => {
    onChange?.([
      ...all,
      {
        itemId: live.itemId,
        role: live.role,
        order: items.length + 1,
        renderings: live.renderings.map((one) => ({
          langId: one.langId,
          text: one.text,
          choices: one.choices.map((choice) => choice.text),
          correctIndex: one.correctIndex,
        })),
      },
    ])
  }

  const shift = (item: DraftItem, by: -1 | 1) => {
    const here = items.indexOf(item)
    const there = here + by
    if (there < 0 || there >= items.length) return
    const reordered = [...items]
    reordered[here] = items[there]
    reordered[there] = item
    const others = all.filter((one) => one.role !== role)
    onChange?.([...others, ...reordered.map((one, index) => ({ ...one, order: index + 1 }))])
  }

  return (
    <>
      {heading ? (
        <div className="written-divider" style={{ marginTop: 0, paddingTop: 0, border: 'none', boxShadow: 'none' }}>
          <h2 style={{ fontSize: 'var(--t-lg)' }}>{heading}</h2>
          <p className="said" style={{ fontSize: 'var(--t-sm)' }}>
            {said}
          </p>
        </div>
      ) : null}

      {items.map((item, index) => (
        <Line
          key={item.itemId ?? `new-${role}-${index}`}
          {...rest}
          item={item}
          first={first && index === 0}
          canMoveUp={index > 0}
          canMoveDown={index < items.length - 1}
          onEdit={(next) => replace(item, next)}
          onRemove={() => remove(item)}
          onShift={(by) => shift(item, by)}
        />
      ))}

      {gone.map((live) => (
        <GoneLine key={live.itemId} live={live} languages={languages} onBring={() => bring(live)} readOnly={readOnly} />
      ))}

      {readOnly ? null : (
        <div className="written-line" data-first={items.length === 0 && gone.length === 0}>
          <span />
          <button type="button" className="act small" style={{ justifySelf: 'start' }} onClick={add}>
            <Plus size={14} strokeWidth={1.5} aria-hidden />
            {role === 'Recovery' ? t('lesson.addRecovery') : t('lesson.addQuestion')}
          </button>
        </div>
      )}
    </>
  )
}

/** Positions are 1-based within a pool and have to stay that way after a removal. */
function renumber(items: DraftItem[], role: ItemRole): DraftItem[] {
  let order = 0
  return items.map((item) => (item.role === role ? { ...item, order: ++order } : item))
}

function Line({
  item,
  first,
  languages,
  problems,
  comments,
  live,
  standing: showStanding,
  readOnly,
  canMoveUp,
  canMoveDown,
  onEdit,
  onRemove,
  onShift,
  onComment,
}: Omit<WrittenProps, 'items' | 'onChange'> & {
  item: DraftItem
  first?: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  onEdit: (next: Partial<DraftItem>) => void
  onRemove: () => void
  onShift: (by: -1 | 1) => void
}) {
  const { t } = useI18n()

  const mine = problems.filter(
    (problem) => problem.role === item.role && (problem.order === null || problem.order === item.order),
  )

  const pinned = (comments ?? []).filter(
    (comment) =>
      !comment.resolvedAtUtc &&
      (comment.anchor?.itemId === item.itemId ||
        (comment.anchor?.role === item.role && comment.anchor?.order === item.order)),
  )

  const was = live?.items.find((one) => one.itemId === item.itemId)
  const stands = !item.itemId ? 'added' : was && sameAs(item, was) ? 'kept' : 'changed'

  const rendering = (langId: string) => item.renderings.find((one) => one.langId === langId)

  const edit = (langId: string, next: Partial<DraftRendering>) => {
    const has = rendering(langId)
    const renderings = has
      ? item.renderings.map((one) => (one.langId === langId ? { ...one, ...next } : one))
      : [...item.renderings, { ...blankRendering(langId), ...next }]
    onEdit({ renderings })
  }

  return (
    <>
      <div className="written-line" data-first={first}>
        <span className="written-no" aria-hidden>
          {item.order}.
        </span>

        <div className="written-body">
          {languages.map((language) => {
            const said = rendering(language.id)
            const inLanguage = mine.filter((problem) => problem.langId === null || problem.langId === language.id)

            return (
              <div className="written-lang" key={language.id} dir={language.direction} lang={language.code}>
                <span className="written-langname">{language.name}</span>

                <Grown
                  className="written-q"
                  value={said?.text ?? ''}
                  disabled={readOnly}
                  aria-label={`${t('lesson.questionText')} — ${language.name}`}
                  aria-invalid={inLanguage.some((problem) => problem.field === 'question') || undefined}
                  maxLength={MAX_QUESTION}
                  placeholder={readOnly ? '' : t('lesson.questionText')}
                  onChange={(text) => edit(language.id, { text })}
                />

                <div className="written-answers">
                  {[0, 1, 2].map((slot) => {
                    const right = (said?.correctIndex ?? 0) === slot
                    const broken = inLanguage.some((problem) => problem.field === `choice${slot + 1}`)

                    return (
                      <div className="written-answer" key={slot} data-right={right}>
                        <button
                          type="button"
                          className="written-pick"
                          disabled={readOnly}
                          aria-pressed={right}
                          title={t('lesson.markCorrect')}
                          onClick={() => edit(language.id, { correctIndex: slot })}
                        >
                          {letter(language.code, slot)}
                        </button>
                        <input
                          className="written-a"
                          value={said?.choices[slot] ?? ''}
                          disabled={readOnly}
                          maxLength={MAX_CHOICE}
                          aria-label={`${t('lesson.answer', { n: slot + 1 })} — ${language.name}`}
                          aria-invalid={broken || undefined}
                          placeholder={readOnly ? '' : t('lesson.answer', { n: slot + 1 })}
                          onChange={(event) => {
                            const choices = [0, 1, 2].map((one) =>
                              one === slot ? event.target.value : (said?.choices[one] ?? ''),
                            )
                            edit(language.id, { choices })
                          }}
                        />
                      </div>
                    )
                  })}
                </div>
              </div>
            )
          })}
        </div>
      </div>

      <div className="written-margin" data-first={first}>
        {!showStanding ? null : stands === 'kept' ? (
          <p className="written-note">{t('lesson.kept')}</p>
        ) : stands === 'added' ? (
          <p className="written-note">{t('lesson.added')}</p>
        ) : (
          <p className="written-note">{t('lesson.changed')}</p>
        )}

        {mine.map((problem, at) => (
          <p className="written-note" data-tone="wrong" key={`${problem.code}-${at}`}>
            {sayProblem(t, problem, languages)}
          </p>
        ))}

        {pinned.map((comment) => (
          <p className="written-note" data-tone="live" key={comment.id}>
            <strong>{comment.author.name}</strong> — {comment.body}
          </p>
        ))}

        {readOnly ? null : (
          <div className="written-line-acts">
            {onComment ? (
              <button
                type="button"
                className="act icon"
                title={t('review.commentOn', { n: item.order })}
                onClick={() => onComment({ itemId: item.itemId, role: item.role, order: item.order })}
              >
                <MessageSquare size={15} strokeWidth={1.5} aria-hidden />
              </button>
            ) : null}
            <button
              type="button"
              className="act icon"
              disabled={!canMoveUp}
              title={t('lesson.moveUp')}
              onClick={() => onShift(-1)}
            >
              ↑
            </button>
            <button
              type="button"
              className="act icon"
              disabled={!canMoveDown}
              title={t('lesson.moveDown')}
              onClick={() => onShift(1)}
            >
              ↓
            </button>
            <button type="button" className="act icon" title={t('lesson.removeQuestion')} onClick={onRemove}>
              <Trash2 size={15} strokeWidth={1.5} aria-hidden />
            </button>
          </div>
        )}
      </div>
    </>
  )
}

/** A live question the draft no longer carries: struck through, and put back with one press. */
function GoneLine({
  live,
  languages,
  onBring,
  readOnly,
}: {
  live: LessonContent['items'][number]
  languages: ContentLanguage[]
  onBring: () => void
  readOnly?: boolean
}) {
  const { t } = useI18n()

  return (
    <>
      <div className="written-line" data-gone="true">
        <span className="written-no" aria-hidden>
          —
        </span>
        <div className="written-body">
          {languages.map((language) => {
            const said = live.renderings.find((one) => one.langId === language.id)
            if (!said) return null
            return (
              <div className="written-lang" key={language.id} dir={language.direction} lang={language.code}>
                <span className="written-langname">{language.name}</span>
                <p className="was">{said.text}</p>
              </div>
            )
          })}
        </div>
      </div>
      <div className="written-margin">
        <p className="written-note" data-tone="wrong">
          {t('lesson.removed')}
        </p>
        {readOnly ? null : (
          <button type="button" className="act small" style={{ justifySelf: 'start' }} onClick={onBring}>
            <Undo2 size={14} strokeWidth={1.5} aria-hidden />
            {t('curriculum.restore')}
          </button>
        )}
      </div>
    </>
  )
}

/** A question grows with what is written in it: no scrollbar inside a line of a lesson. */
function Grown({
  value,
  onChange,
  ...rest
}: Omit<React.TextareaHTMLAttributes<HTMLTextAreaElement>, 'onChange' | 'value'> & {
  value: string
  onChange: (next: string) => void
}) {
  const ref = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    const box = ref.current
    if (!box) return
    box.style.height = 'auto'
    box.style.height = `${box.scrollHeight}px`
  }, [value])

  return <textarea ref={ref} rows={1} value={value} onChange={(event) => onChange(event.target.value)} {...rest} />
}

/** Has anything the game would send actually changed? */
function sameAs(item: DraftItem, live: LessonContent['items'][number] | undefined): boolean {
  if (!live) return false
  return item.renderings.every((rendering) => {
    const was = live.renderings.find((one) => one.langId === rendering.langId)
    if (!was) return false
    return (
      was.text === rendering.text &&
      was.correctIndex === rendering.correctIndex &&
      was.choices.length === rendering.choices.length &&
      was.choices.every((choice, at) => choice.text === rendering.choices[at])
    )
  })
}

/** The server names the problem with a code; the Studio says it in the member's language. */
export function sayProblem(
  t: ReturnType<typeof useI18n>['t'],
  problem: ContentProblem,
  languages: ContentLanguage[],
): string {
  const language = languages.find((one) => one.id === problem.langId)?.name ?? ''
  const key = `problem.${problem.code}` as 'problem.unknown'
  const said = t(key, { language })
  return said === `problem.${problem.code}` ? t('problem.unknown') : said
}
