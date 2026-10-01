import type { ReactNode } from 'react'
import { ArrowDown, ArrowUp, Gauge, Info, Plus, ShieldCheck, Smartphone, TriangleAlert, Trophy, X } from 'lucide-react'
import { Badge, Button } from '../../components/ui/primitives'
import { Input, Select } from '../../components/ui/form'
import {
  MAX_CRITERIA,
  SIGNAL_KIND_PATTERN,
  TRUST,
  metricName,
  orderLabel,
  ruleSentence,
  signalKindOf,
  signalMetric,
  trustOf,
  useWinMetrics,
} from './winRule'
import type { MatchMetricOptionDto, MatchMetricTrust, MatchWinCriterionDto } from '../../types/api'

// ===========================================================================
// How a match is won — the win-rule editor inside a mode's drawer
//
// A rule is an ordered list of measures: the first decides, each later one
// breaks the ties the earlier ones left. Offered only for modes played versus;
// the server re-validates everything and owns what each measure means.
// ===========================================================================

/** The picker's value for "a count this game reports that is not in the list". */
const CUSTOM_COUNT = '__custom'

const TRUST_ICON: Record<MatchMetricTrust, typeof ShieldCheck> = {
  verified: ShieldCheck,
  bounded: Gauge,
  reported: Smartphone,
}

/** What would stop this rule saving, in words, or null. */
export function winRuleError(criteria: MatchWinCriterionDto[]): string | null {
  const metrics = criteria.map((c) => c.metric)

  if (new Set(metrics).size !== metrics.length) return 'Each measure can only be used once.'

  for (const metric of metrics) {
    const kind = signalKindOf(metric)
    if (kind !== null && !SIGNAL_KIND_PATTERN.test(kind))
      return 'A count needs its name as the game reports it: lowercase letters, digits and underscores, starting with a letter — e.g. kill.'
  }

  return null
}

/** A callout inside the rule editor: a full hairline border rather than a coloured side stripe. */
function RuleNote({ tone, children }: { tone?: 'warning'; children: ReactNode }) {
  const Icon = tone === 'warning' ? TriangleAlert : Info

  return (
    <p className={`s7-winrule-note ${tone === 'warning' ? 'is-warning' : ''}`}>
      <Icon size={15} aria-hidden />
      <span>{children}</span>
    </p>
  )
}

/**
 * The ordered measures a match of this mode is decided by. The first decides; each later one only
 * breaks the ties the earlier ones left — which is why the rows read "Decides" and "Then", and why
 * they can be reordered.
 */
export function WinRuleEditor({
  gameId,
  versus,
  savedRule,
  criteria,
  error,
  onChange,
}: {
  gameId: string
  versus: boolean
  savedRule: MatchWinCriterionDto[]
  criteria: MatchWinCriterionDto[]
  error: string | null
  onChange: (criteria: MatchWinCriterionDto[]) => void
}) {
  const { metrics, loading } = useWinMetrics(gameId)

  const sentence = ruleSentence(criteria)
  const trusts = criteria.map((c) => trustOf(c.metric, metrics))
  const weakest: MatchMetricTrust | null = trusts.includes('reported')
    ? 'reported'
    : trusts.includes('bounded')
      ? 'bounded'
      : trusts.length
        ? 'verified'
        : null

  function replace(index: number, next: MatchWinCriterionDto) {
    onChange(criteria.map((c, i) => (i === index ? next : c)))
  }

  function move(index: number, by: -1 | 1) {
    const target = index + by
    if (target < 0 || target >= criteria.length) return

    const next = [...criteria]
    ;[next[index], next[target]] = [next[target], next[index]]
    onChange(next)
  }

  function add() {
    const unused = metrics.find((m) => !criteria.some((c) => c.metric === m.metric))

    onChange([
      ...criteria,
      unused
        ? { metric: unused.metric, order: unused.suggestedOrder }
        : { metric: 'correct_answers', order: 'higher' },
    ])
  }

  return (
    <section className="s7-winrule" aria-labelledby="winrule-title">
      <h3 id="winrule-title" className="s7-subhead">
        <Trophy size={15} /> How a match is won
      </h3>

      {!versus ? (
        criteria.length || savedRule.length ? (
          <RuleNote tone="warning">
            Only a mode played versus has matches to win. Saving without Versus removes this mode’s
            rule{savedRule.length ? ` (“${ruleSentence(savedRule)}”)` : ''}.
          </RuleNote>
        ) : (
          <p className="s7-hint">Turn on Versus to choose how a match of this mode is won.</p>
        )
      ) : (
        <>
          <p className="s7-hint">
            The server places every player from their own results — no game gets to say who won. The
            first measure decides; the rest only break ties.
          </p>

          {sentence ? (
            <p className="s7-winrule-sentence" aria-live="polite">
              {sentence}
            </p>
          ) : (
            <RuleNote>
              No rule yet. Matches of this mode are still recorded and count as played, but nobody is
              placed and nobody wins.
            </RuleNote>
          )}

          {criteria.length ? (
            <ol className="s7-winrule-list">
              {criteria.map((criterion, index) => (
                <WinCriterionRow
                  key={index}
                  index={index}
                  count={criteria.length}
                  criterion={criterion}
                  metrics={metrics}
                  loading={loading}
                  used={criteria.filter((_, i) => i !== index).map((c) => c.metric)}
                  onChange={(next) => replace(index, next)}
                  onMove={(by) => move(index, by)}
                  onRemove={() => onChange(criteria.filter((_, i) => i !== index))}
                />
              ))}
            </ol>
          ) : null}

          {error ? <span className="s7-error">{error}</span> : null}

          {weakest === 'reported' ? (
            <RuleNote tone="warning">
              This rule depends on something only the player’s game reports, which a modified game can
              fake. It is fine for play; do not put real prizes or an event on this mode’s results.
            </RuleNote>
          ) : weakest ? (
            <p className="s7-hint">{TRUST[weakest].explain}</p>
          ) : null}

          {criteria.length < MAX_CRITERIA ? (
            <span>
              <Button variant="ghost" onClick={add} disabled={loading && metrics.length === 0}>
                <Plus size={15} /> {criteria.length ? 'Add a tiebreak' : 'Choose how a match is won'}
              </Button>
            </span>
          ) : (
            <p className="s7-hint">That is the most a rule can hold — five measures.</p>
          )}
        </>
      )}
    </section>
  )
}

function WinCriterionRow({
  index,
  count,
  criterion,
  metrics,
  loading,
  used,
  onChange,
  onMove,
  onRemove,
}: {
  index: number
  count: number
  criterion: MatchWinCriterionDto
  metrics: MatchMetricOptionDto[]
  loading: boolean
  used: string[]
  onChange: (next: MatchWinCriterionDto) => void
  onMove: (by: -1 | 1) => void
  onRemove: () => void
}) {
  const listed = metrics.some((m) => m.metric === criterion.metric)

  // A count the game reports that no valuation lists yet — "kill" in a brand-new game — is typed in.
  const custom = !listed && signalKindOf(criterion.metric) !== null
  const trust = TRUST[trustOf(criterion.metric, metrics)]
  const TrustIcon = TRUST_ICON[trustOf(criterion.metric, metrics)]
  const position = index === 0 ? 'Decides' : 'Then'

  return (
    <li className="s7-winrule-row">
      <span className="s7-winrule-step" aria-hidden>
        {position}
      </span>

      <div className="s7-winrule-pick">
        <Select
          aria-label={`${position}: measure`}
          value={custom ? CUSTOM_COUNT : criterion.metric}
          onChange={(e) => {
            if (e.target.value === CUSTOM_COUNT) {
              onChange({ metric: signalMetric(''), order: 'higher' })
              return
            }

            const option = metrics.find((m) => m.metric === e.target.value)
            onChange({ metric: e.target.value, order: option?.suggestedOrder ?? criterion.order })
          }}
        >
          {loading && !listed && !custom ? <option value={criterion.metric}>Loading…</option> : null}
          {!loading && !listed && !custom ? (
            <option value={criterion.metric}>{metricName(criterion.metric)}</option>
          ) : null}
          {metrics.map((m) => (
            <option key={m.metric} value={m.metric} disabled={used.includes(m.metric)}>
              {metricName(m.metric)}
            </option>
          ))}
          <option value={CUSTOM_COUNT}>Another count…</option>
        </Select>

        {custom ? (
          <Input
            mono
            invalid={!SIGNAL_KIND_PATTERN.test(signalKindOf(criterion.metric) ?? '')}
            aria-label={`${position}: count name`}
            value={signalKindOf(criterion.metric) ?? ''}
            onChange={(e) =>
              onChange({
                ...criterion,
                metric: signalMetric(e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '')),
              })
            }
            placeholder="kill"
          />
        ) : null}

        <Select
          aria-label={`${position}: which way wins`}
          value={criterion.order}
          onChange={(e) => onChange({ ...criterion, order: e.target.value === 'lower' ? 'lower' : 'higher' })}
        >
          <option value="higher">{orderLabel(criterion.metric, 'higher')}</option>
          <option value="lower">{orderLabel(criterion.metric, 'lower')}</option>
        </Select>
      </div>

      <span className="s7-winrule-trust" title={trust.explain}>
        <Badge tone={trust.tone}>
          <TrustIcon size={11} aria-hidden /> {trust.label}
        </Badge>
      </span>

      <span className="s7-winrule-actions">
        <Button
          variant="ghost"
          className="s7-btn-icon"
          aria-label={`Move ${position.toLowerCase()} measure up`}
          title="Move up"
          disabled={index === 0}
          onClick={() => onMove(-1)}
        >
          <ArrowUp size={14} />
        </Button>
        <Button
          variant="ghost"
          className="s7-btn-icon"
          aria-label={`Move ${position.toLowerCase()} measure down`}
          title="Move down"
          disabled={index === count - 1}
          onClick={() => onMove(1)}
        >
          <ArrowDown size={14} />
        </Button>
        <Button
          variant="ghost"
          className="s7-btn-icon"
          aria-label={`Remove this measure`}
          title="Remove"
          onClick={onRemove}
        >
          <X size={14} />
        </Button>
      </span>
    </li>
  )
}
