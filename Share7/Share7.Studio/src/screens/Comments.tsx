import { useState } from 'react'
import { Check, CornerDownRight } from 'lucide-react'
import { Mark, Nothing, useSaying } from '../board/pieces'
import { useI18n } from '../i18n/i18n'
import { studio, type CommentAnchor, type DraftComment } from '../lib/studio'
import { useDoing } from '../lib/use'

// ===========================================================================
// What people said about a draft
//
// A comment can be pinned to a question, a language or a field, and one that
// is pinned also appears in the margin beside the thing it is about. Threads
// are one level deep: a remark and the replies to it, which is as much as
// "the Arabic in question 4 reads oddly" ever needs.
// ===========================================================================

export function Comments({
  draftId,
  comments,
  anchor,
  onChanged,
  onClearAnchor,
}: {
  draftId: string
  comments: DraftComment[]
  anchor?: CommentAnchor | null
  onChanged: () => void
  onClearAnchor?: () => void
}) {
  const { t } = useI18n()
  const saying = useSaying()
  const [busy, run] = useDoing()
  const [body, setBody] = useState('')
  const [replyTo, setReplyTo] = useState<string | null>(null)

  const top = comments.filter((one) => one.parentCommentId === null)
  const repliesTo = (id: string) => comments.filter((one) => one.parentCommentId === id)

  const send = () =>
    run(async () => {
      const text = body.trim()
      if (!text) return
      try {
        await studio.comment(draftId, text, replyTo ? null : (anchor ?? null), replyTo)
        setBody('')
        setReplyTo(null)
        onClearAnchor?.()
        onChanged()
      } catch (error) {
        saying(error)
      }
    })

  const settle = (id: string) =>
    run(async () => {
      try {
        await studio.resolveComment(id)
        onChanged()
      } catch (error) {
        saying(error)
      }
    })

  return (
    <div className="stack">
      {top.length === 0 ? (
        <Nothing title={t('review.noComments')} />
      ) : (
        <div className="stack">
          {top.map((comment) => (
            <div key={comment.id} className="stack tight">
              <One comment={comment} onSettle={() => void settle(comment.id)} onReply={() => setReplyTo(comment.id)} busy={busy} />
              {repliesTo(comment.id).map((reply) => (
                <div key={reply.id} style={{ paddingInlineStart: 'var(--s5)' }}>
                  <One comment={reply} busy={busy} />
                </div>
              ))}
            </div>
          ))}
        </div>
      )}

      <div className="stack tight">
        {anchor && !replyTo ? (
          <p className="beside" data-tone="live">
            {t('review.commentOn', { n: anchor.order ?? 0 })}{' '}
            {onClearAnchor ? (
              <button type="button" className="act plain small" onClick={onClearAnchor}>
                {t('common.cancel')}
              </button>
            ) : null}
          </p>
        ) : null}

        {replyTo ? (
          <p className="beside" data-tone="live">
            <CornerDownRight size={13} strokeWidth={1.5} aria-hidden /> {t('review.reply')}{' '}
            <button type="button" className="act plain small" onClick={() => setReplyTo(null)}>
              {t('common.cancel')}
            </button>
          </p>
        ) : null}

        <textarea
          className="write"
          rows={2}
          value={body}
          placeholder={t('review.commentPlaceholder')}
          aria-label={t('review.comment')}
          onChange={(event) => setBody(event.target.value)}
        />

        <button
          type="button"
          className="act small"
          style={{ justifySelf: 'start' }}
          disabled={busy || body.trim() === ''}
          onClick={() => void send()}
        >
          {t('review.comment')}
        </button>
      </div>
    </div>
  )
}

function One({
  comment,
  onSettle,
  onReply,
  busy,
}: {
  comment: DraftComment
  onSettle?: () => void
  onReply?: () => void
  busy: boolean
}) {
  const { t, formatRelative } = useI18n()

  return (
    <div className="stack tight">
      <div className="spread" style={{ gap: 'var(--s3)' }}>
        <strong style={{ fontWeight: 500 }}>{comment.author.name}</strong>
        <time className="quiet" style={{ fontSize: 'var(--t-sm)' }} dateTime={comment.createdAtUtc}>
          {formatRelative(comment.createdAtUtc)}
        </time>
        {comment.anchor?.order ? (
          <Mark stroke="live">{t('review.commentOn', { n: comment.anchor.order })}</Mark>
        ) : null}
        {comment.resolvedAtUtc ? (
          <Mark stroke="ended">{t('review.resolved', { name: comment.resolvedBy?.name ?? '' })}</Mark>
        ) : null}
      </div>

      <p className="said">{comment.body}</p>

      {comment.resolvedAtUtc ? null : (
        <div className="acts">
          {onReply ? (
            <button type="button" className="act plain small" onClick={onReply} disabled={busy}>
              {t('review.reply')}
            </button>
          ) : null}
          {onSettle ? (
            <button type="button" className="act plain small" onClick={onSettle} disabled={busy}>
              <Check size={13} strokeWidth={1.5} aria-hidden />
              {t('review.resolve')}
            </button>
          ) : null}
        </div>
      )}
    </div>
  )
}
