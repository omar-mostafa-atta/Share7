import { motion } from 'motion/react'
import { useState } from 'react'
import {
  Activity,
  AlertTriangle,
  BarChart3,
  Clock,
  Coins,
  RefreshCw,
  TrendingUp,
  UserPlus,
  Users,
} from 'lucide-react'
import { Card, CardBody, CardHeader, IconButton, Badge } from '../components/ui/primitives'
import { Stat, StatRow } from '../components/ui/Stat'
import { Note } from '../components/ui/bits'
import { PageTitle } from '../components/ui/bits'
import { listVariants } from '../components/ui/motion'
import {
  DEFAULT_RANGE,
  RANGE_PRESETS,
  compact,
  duration,
  percent,
  useAnalyticsOverview,
  useEconomy,
  useEventCatalogue,
  useTimeseries,
} from '../features/analytics/data'
import type { DayRange } from '../features/analytics/data'
import { TrendChart } from '../features/analytics/TrendChart'
import { FunnelPanel } from '../features/analytics/FunnelPanel'

// ===========================================================================
// Analytics — the overview
//
// Answers, in order of how often somebody asks:
//   1. How many people are here, and is that going up?   (DAU/WAU/MAU, trend)
//   2. Do they come back?                                (D1/D7/D30)
//   3. Is the pipeline actually reporting?               (projector health)
//   4. Where do they drop out?                           (funnel)
//   5. Is the economy inflating?                         (sources vs sinks)
//
// Question 3 is on this page rather than hidden in an ops corner for one
// reason: a stalled projector looks EXACTLY like a collapse in engagement —
// every number goes flat at once — and without the lag figure beside them the
// obvious reading is the wrong one.
// ===========================================================================

export function Analytics() {
  const [range, setRange] = useState<DayRange>(DEFAULT_RANGE)

  const overview = useAnalyticsOverview(range)
  const activeUsers = useTimeseries('active_users', range)
  const newUsers = useTimeseries('new_users', range)
  const sessions = useTimeseries('session_start', range)
  const catalogue = useEventCatalogue(range)
  const economy = useEconomy(range)

  const data = overview.data
  const stale = data.projectionLagSeconds > 300

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<BarChart3 size={20} />}
        title="Analytics"
        subtitle="Who is playing, whether they come back, and where they stop."
        actions={
          <div className="s7-range">
            {RANGE_PRESETS.map((preset) => (
              <button
                key={preset.label}
                type="button"
                className="s7-chip"
                onClick={() => setRange(preset.range())}
              >
                {preset.label}
              </button>
            ))}

            <IconButton
              label="Refresh"
              busy={overview.refreshing}
              onClick={() => void overview.reload()}
            >
              <RefreshCw size={16} />
            </IconButton>
          </div>
        }
      />

      {/* The pipeline's own health, above the numbers it produces — because if
          this is wrong, everything below it is wrong in a way that looks
          plausible. */}
      {stale ? (
        <Note tone="warning">
          <AlertTriangle size={14} /> The projector is {duration(data.projectionLagSeconds)} behind
          with {compact(data.pendingEvents)} event(s) pending. Figures below are missing whatever has
          not been folded yet — a flat line here right now is not necessarily a flat line in reality.
        </Note>
      ) : null}

      <StatRow>
        <Stat
          icon={<Users size={15} />}
          label="Daily active"
          value={data.dau}
          sub={`${compact(data.wau)} weekly · ${compact(data.mau)} monthly`}
          tone="brand"
        />
        <Stat
          icon={<Activity size={15} />}
          label="Stickiness"
          value={percent(data.stickiness)}
          sub="DAU ÷ MAU — how much of the month shows up on a given day"
          tone="cool"
        />
        <Stat
          icon={<UserPlus size={15} />}
          label="New accounts"
          value={data.newUsers}
          sub={`in the selected range`}
          tone="success"
        />
        <Stat
          icon={<Clock size={15} />}
          label="Avg session"
          value={duration(data.averageSessionSeconds)}
          sub={`${data.sessionsPerActiveUser.toFixed(2)} sessions per active user`}
          tone="info"
        />
      </StatRow>

      {/* Retention headlines. Each is null until its cohorts have matured, and
          NULL RENDERS AS "not yet" — never as 0%. A D30 built from cohorts
          eleven days old is not a low number, it is a meaningless one, and
          showing it as zero is how a team spends a week fixing nothing. */}
      <StatRow>
        <RetentionStat label="D1" value={data.d1} cohorts={data.d1CohortCount} tone="success" />
        <RetentionStat label="D7" value={data.d7} cohorts={data.d7CohortCount} tone="brand" />
        <RetentionStat label="D30" value={data.d30} cohorts={data.d30CohortCount} tone="cool" />
        <Stat
          icon={<TrendingUp size={15} />}
          label="Events"
          value={compact(data.totalEvents)}
          sub={`${duration(data.totalPlaySeconds)} of play recorded`}
          tone="warning"
        />
      </StatRow>

      <Card>
        <CardHeader icon={<Users size={16} />} title="Active and new users" />
        <CardBody>
          <TrendChart
            from={range.from}
            to={range.to}
            series={[
              {
                key: 'active',
                label: 'Active users',
                tone: 'brand',
                points: activeUsers.data.series[0]?.points ?? [],
              },
              {
                key: 'new',
                label: 'New accounts',
                tone: 'success',
                points: newUsers.data.series[0]?.points ?? [],
              },
            ]}
          />
        </CardBody>
      </Card>

      <Card>
        <CardHeader
          icon={<Activity size={16} />}
          title="Sessions"
          actions={
            data.platforms.length > 0 ? (
              <div className="s7-inline-badges">
                {data.platforms.map((platform) => (
                  <Badge key={platform.key} tone="muted">
                    {platform.key} {percent(platform.share, 0)}
                  </Badge>
                ))}
              </div>
            ) : null
          }
        />
        <CardBody>
          <TrendChart
            from={range.from}
            to={range.to}
            series={[
              {
                key: 'sessions',
                label: 'Sessions',
                tone: 'cool',
                points: sessions.data.series[0]?.points ?? [],
              },
            ]}
          />
        </CardBody>
      </Card>

      <FunnelPanel range={range} catalogue={catalogue.data.events} />

      <Card>
        <CardHeader icon={<Coins size={16} />} title="Economy — sources and sinks" />
        <CardBody>
          {economy.data.currencies.length === 0 ? (
            <div className="s7-chart-empty">No currency moved in this range.</div>
          ) : (
            <div className="s7-economy">
              {economy.data.currencies.map((currency) => (
                <div key={currency.currencyId} className="s7-economy-row">
                  <div className="s7-economy-head">
                    <strong>{currency.code}</strong>

                    {/* Sustained positive net is inflation: minting faster than
                        removing, so every price in the shop quietly gets
                        cheaper. Flagged rather than merely displayed. */}
                    <Badge tone={currency.net > 0 ? 'warning' : 'success'}>
                      net {currency.net > 0 ? '+' : ''}
                      {compact(currency.net)}
                    </Badge>
                  </div>

                  <div className="s7-economy-bars">
                    <div className="s7-economy-bar">
                      <span>sourced</span>
                      <div className="s7-economy-track">
                        <div
                          className="s7-economy-fill s7-economy-in"
                          style={{
                            width: `${barWidth(currency.sourced, currency.sourced, currency.sunk)}%`,
                          }}
                        />
                      </div>
                      <em>{compact(currency.sourced)}</em>
                    </div>

                    <div className="s7-economy-bar">
                      <span>sunk</span>
                      <div className="s7-economy-track">
                        <div
                          className="s7-economy-fill s7-economy-out"
                          style={{
                            width: `${barWidth(currency.sunk, currency.sourced, currency.sunk)}%`,
                          }}
                        />
                      </div>
                      <em>{compact(currency.sunk)}</em>
                    </div>
                  </div>

                  <div className="s7-economy-split">
                    <div>
                      <h4>From</h4>
                      {currency.sources.slice(0, 5).map((source) => (
                        <div key={source.key} className="s7-economy-line">
                          <span>{source.key}</span>
                          <em>{percent(source.share, 0)}</em>
                        </div>
                      ))}
                    </div>

                    <div>
                      <h4>To</h4>
                      {currency.sinks.length === 0 ? (
                        <div className="s7-economy-line s7-muted">nothing spent</div>
                      ) : (
                        currency.sinks.slice(0, 5).map((sink) => (
                          <div key={sink.key} className="s7-economy-line">
                            <span>{sink.key}</span>
                            <em>{percent(sink.share, 0)}</em>
                          </div>
                        ))
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}

          <p className="s7-funnel-note">
            Read from the currency ledger, not from telemetry — the ledger is the authoritative
            record of every grant and spend, and a second count assembled from client events would
            eventually disagree with it.
          </p>
        </CardBody>
      </Card>
    </motion.div>
  )
}

function barWidth(value: number, a: number, b: number): number {
  const max = Math.max(a, b, 1)
  return Math.max(1, (value / max) * 100)
}

/**
 * A retention headline.
 *
 * Renders "not yet" for null and prints how many cohorts it averaged, so a
 * number built from three cohorts reads as thin rather than as fact.
 */
function RetentionStat({
  label,
  value,
  cohorts,
  tone,
}: {
  label: string
  value: number | null
  cohorts: number
  tone: 'brand' | 'success' | 'cool'
}) {
  return (
    <Stat
      icon={<Users size={15} />}
      label={`${label} retention`}
      value={value === null ? 'not yet' : percent(value)}
      sub={
        value === null
          ? 'no cohort has aged this far in range'
          : `weighted across ${cohorts} cohort(s)`
      }
      tone={tone}
    />
  )
}
