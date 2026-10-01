import { useResourceList } from '../../lib/resource'
import type { MatchMetricOptionDto, MatchMetricTrust, MatchWinCriterionDto } from '../../types/api'

// ===========================================================================
// Win rules — how a match of a mode is won
//
// A rule is an ordered list of measures: the first decides, each later one
// breaks the ties the earlier ones left. The server owns what each measure
// means and how far it can be trusted; this file owns only how it reads to an
// operator, in plain words.
// ===========================================================================

/** The most measures a rule may have — the server refuses a sixth. */
export const MAX_CRITERIA = 5

const SIGNAL_PREFIX = 'signal:'

/** A count a mini-game reports: lowercase, starts with a letter, then letters, digits or underscores. */
export const SIGNAL_KIND_PATTERN = /^[a-z][a-z0-9_]{0,31}$/

export function signalKindOf(metric: string): string | null {
  return metric.startsWith(SIGNAL_PREFIX) ? metric.slice(SIGNAL_PREFIX.length) : null
}

export function signalMetric(kind: string): string {
  return `${SIGNAL_PREFIX}${kind}`
}

/** `near_miss` → "near miss". */
function humanKind(kind: string): string {
  return kind.replace(/_/g, ' ')
}

function capitalise(text: string): string {
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}`
}

/** What a measure is called when nobody has picked a direction yet — the option label in a picker. */
export function metricName(metric: string): string {
  const kind = signalKindOf(metric)
  if (kind !== null) return kind ? `${capitalise(humanKind(kind))} count` : 'Unnamed count'

  switch (metric) {
    case 'correct_answers':
      return 'Correct answers'
    case 'accuracy':
      return 'Accuracy'
    case 'duration_ms':
      return 'Time played'
    case 'outcome':
      return 'Finished or survived'
    default:
      return metric
  }
}

/** What winning on this measure means, as a phrase: "most correct answers", "fastest finish". */
export function winningPhrase(criterion: MatchWinCriterionDto): string {
  const higher = criterion.order === 'higher'
  const kind = signalKindOf(criterion.metric)

  if (kind !== null) return `${higher ? 'highest' : 'lowest'} ${kind ? humanKind(kind) : 'unnamed'} count`

  switch (criterion.metric) {
    case 'correct_answers':
      return higher ? 'most correct answers' : 'fewest correct answers'
    case 'accuracy':
      return higher ? 'highest accuracy' : 'lowest accuracy'
    case 'duration_ms':
      return higher ? 'longest time alive' : 'fastest finish'
    case 'outcome':
      return higher ? 'finishing or surviving' : 'not finishing'
    default:
      return criterion.metric
  }
}

/** Which way wins, in the words that fit the measure: "More wins" reads wrong for a race. */
export function orderLabel(metric: string, order: 'higher' | 'lower'): string {
  if (metric === 'duration_ms') return order === 'lower' ? 'Faster wins' : 'Longer wins'
  if (metric === 'outcome') return order === 'higher' ? 'Finishing wins' : 'Not finishing wins'
  return order === 'higher' ? 'More wins' : 'Less wins'
}

/**
 * The whole rule as one sentence an operator can read back: "Most correct answers wins. Ties go to
 * the fastest finish." Null for an empty rule.
 */
export function ruleSentence(criteria: MatchWinCriterionDto[]): string | null {
  if (criteria.length === 0) return null

  const [first, ...rest] = criteria
  const lead = winningPhrase(first)
  const opening = `${capitalise(lead)} wins.`

  if (rest.length === 0) return opening

  const ties = rest.map(winningPhrase)
  const joined = ties.length === 1 ? ties[0] : `${ties.slice(0, -1).join(', then ')}, then ${ties[ties.length - 1]}`

  return `${opening} Ties go to ${joined}.`
}

/** How far the server can vouch for a measure, in the words the rule editor uses. */
export const TRUST: Record<MatchMetricTrust, { label: string; tone: 'success' | 'info' | 'warning'; explain: string }> = {
  verified: {
    label: 'Checked by the server',
    tone: 'success',
    explain: 'Graded by the server from the answers themselves. A modified game cannot change it.',
  },
  bounded: {
    label: 'Limited to what is possible',
    tone: 'info',
    explain:
      'Reported by the player’s game, then capped by what the time played allows. Anyone past the cap is placed below every other player.',
  },
  reported: {
    label: 'The player’s own report',
    tone: 'warning',
    explain: 'Reported by the player’s game and not checkable. Fine for fun; never attach real prizes to it.',
  },
}

/** The trust of a measure that is not in the server's list — a count the game reports. */
export function trustOf(metric: string, options: MatchMetricOptionDto[]): MatchMetricTrust {
  return options.find((o) => o.metric === metric)?.trust ?? (signalKindOf(metric) !== null ? 'bounded' : 'reported')
}

/** What a rule may rank on for one game: the fixed measures, and every count the game prices. */
export function useWinMetrics(gameId: string | null) {
  const resource = useResourceList<MatchMetricOptionDto>(
    gameId ? `/api/admin/modes/win-metrics?gameId=${gameId}` : null,
  )

  return { ...resource, metrics: resource.data }
}
