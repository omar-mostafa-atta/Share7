import { motion } from 'motion/react'
import { Activity, BookOpen, Coins, Radio, RefreshCw, Rocket, Trophy, Users } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Card, CardBody, CardHeader, IconButton, SkeletonRows } from '../components/ui/primitives'
import { Stat, StatRow } from '../components/ui/Stat'
import { Meter, Note } from '../components/ui/bits'
import { NAV_ENTRIES } from '../lib/nav'
import { useResource } from '../lib/resource'
import { formatDateTime } from '../lib/time'
import { useAuth } from '../store/auth'
import { listVariants } from '../components/ui/motion'
import type { AdminOverviewDto } from '../types/api'

// ===========================================================================
// Command Center — the console's landing page
//
// Answers three questions in order of urgency:
//   1. Is anything waiting for a human?  (flags, live sessions)
//   2. Is the content playable?          (lessons vs lessons with questions)
//   3. How big is the platform?          (catalogue totals)
//
// Backed by GET /api/admin/overview, which this work added. Everything here was
// previously reachable only by fetching whole lists and measuring them.
// ===========================================================================

const EMPTY: AdminOverviewDto = {
  users: 0,
  usersAddedLast7Days: 0,
  games: 0,
  activeGames: 0,
  grades: 0,
  lessons: 0,
  lessonsWithQuestions: 0,
  questions: 0,
  currencies: 0,
  offers: 0,
  activeOffers: 0,
  products: 0,
  objectives: 0,
  activeObjectives: 0,
  rewardRules: 0,
  enabledRewardRules: 0,
  signalValuations: 0,
  boards: 0,
  openCycles: 0,
  liveSessions: 0,
  flaggedRuns: 0,
  flaggedResults: 0,
  runsLast24Hours: 0,
  serverTimeUtc: '',
}

export function Overview() {
  const username = useAuth((s) => s.username)
  const { data, loading, refreshing, reload } = useResource<AdminOverviewDto>(
    '/api/admin/overview',
    EMPTY,
  )

  const needsReview = data.flaggedRuns + data.flaggedResults
  const authoringGap = data.lessons - data.lessonsWithQuestions

  // Quick-launch tiles, minus the page you are already on.
  const tiles = NAV_ENTRIES.filter((entry) => entry.to !== '/')

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <motion.section className="s7-hero">
        <h1>
          {greeting()}, {username || 'Admin'}.
        </h1>
        <p>
          Everything the platform runs on, in one place — curriculum and games, the currencies and
          rules that pay players, and the operational surfaces where a human still has to decide.
        </p>

        <div className="s7-hero-row">
          <div className="s7-hero-stat">
            <strong>{loading ? '—' : data.users.toLocaleString()}</strong>
            <span>accounts</span>
          </div>
          <div className="s7-hero-stat">
            <strong>{loading ? '—' : data.runsLast24Hours.toLocaleString()}</strong>
            <span>runs in 24h</span>
          </div>
          <div className="s7-hero-stat">
            <strong>{loading ? '—' : data.liveSessions.toLocaleString()}</strong>
            <span>live sessions</span>
          </div>
          <div className="s7-hero-stat">
            <strong>{loading ? '—' : needsReview.toLocaleString()}</strong>
            <span>awaiting review</span>
          </div>
        </div>
      </motion.section>

      {/* Only shown when there is something to do. A permanent banner reading
          "0 items need attention" trains people to stop reading banners. */}
      {!loading && needsReview > 0 ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1.35rem' }}>
          <Note tone="warning">
            <strong>{needsReview.toLocaleString()}</strong> item{needsReview === 1 ? '' : 's'} flagged
            for review — {data.flaggedRuns.toLocaleString()} run
            {data.flaggedRuns === 1 ? '' : 's'} and {data.flaggedResults.toLocaleString()} leaderboard
            result{data.flaggedResults === 1 ? '' : 's'}. Until a human rules on them, the results stay
            out of the boards. <Link to="/runs">Review runs</Link> ·{' '}
            <Link to="/leaderboards">Review results</Link>
          </Note>
        </motion.div>
      ) : null}

      <StatRow>
        <Stat
          icon={<Users size={13} />}
          label="Accounts"
          value={data.users}
          sub={`${data.usersAddedLast7Days.toLocaleString()} joined this week`}
          tone="brand"
        />
        <Stat
          icon={<Rocket size={13} />}
          label="Runs · 24h"
          value={data.runsLast24Hours}
          sub={`${data.flaggedRuns.toLocaleString()} flagged, unreviewed`}
          tone={data.flaggedRuns ? 'warning' : 'success'}
        />
        <Stat
          icon={<Radio size={13} />}
          label="Live sessions"
          value={data.liveSessions}
          sub="Creating, created, starting or running"
          tone="cool"
        />
        <Stat
          icon={<Trophy size={13} />}
          label="Open cycles"
          value={data.openCycles}
          sub={`across ${data.boards.toLocaleString()} board${data.boards === 1 ? '' : 's'}`}
          tone="info"
        />
      </StatRow>

      <div className="s7-split">
        <Card>
          <CardHeader
            icon={<BookOpen size={16} />}
            title="Content readiness"
            actions={<IconButton label="Refresh" busy={refreshing} onClick={() => void reload()}><RefreshCw size={15} /></IconButton>}
          />
          <CardBody>
            {loading ? (
              <SkeletonRows rows={3} />
            ) : (
              <div className="s7-stack">
                <div>
                  <div className="s7-inline" style={{ justifyContent: 'space-between' }}>
                    <span className="s7-label" style={{ margin: 0 }}>
                      Lessons with a published question set
                    </span>
                  </div>
                  <Meter value={data.lessonsWithQuestions} max={data.lessons} />
                </div>

                {/* The single most actionable number on this page: a lesson with
                    no questions cannot be played, and nothing else surfaces it. */}
                {authoringGap > 0 ? (
                  <Note tone={authoringGap > data.lessonsWithQuestions ? 'warning' : undefined}>
                    <strong>{authoringGap.toLocaleString()}</strong> lesson
                    {authoringGap === 1 ? ' has' : 's have'} no questions in any language, so
                    {authoringGap === 1 ? ' it is' : ' they are'} unplayable.{' '}
                    <Link to="/curriculum">Open the curriculum</Link>
                  </Note>
                ) : (
                  <Note>Every lesson has at least one published question set.</Note>
                )}

                <dl className="s7-dl">
                  <dt>Grades</dt>
                  <dd>{data.grades.toLocaleString()}</dd>
                  <dt>Lessons</dt>
                  <dd>{data.lessons.toLocaleString()}</dd>
                  <dt>Questions</dt>
                  <dd>{data.questions.toLocaleString()}</dd>
                  <dt>Games</dt>
                  <dd>
                    {data.activeGames.toLocaleString()} active of {data.games.toLocaleString()}
                  </dd>
                </dl>
              </div>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader icon={<Coins size={16} />} title="Economy" />
          <CardBody>
            {loading ? (
              <SkeletonRows rows={3} />
            ) : (
              <dl className="s7-dl">
                <dt>Currencies</dt>
                <dd>{data.currencies.toLocaleString()}</dd>
                <dt>Signal valuations</dt>
                <dd>
                  {data.signalValuations.toLocaleString()}{' '}
                  <span className="s7-muted">— what a pickup pays</span>
                </dd>
                <dt>Reward rules</dt>
                <dd>
                  {data.enabledRewardRules.toLocaleString()} enabled of{' '}
                  {data.rewardRules.toLocaleString()}
                </dd>
                <dt>Products</dt>
                <dd>{data.products.toLocaleString()}</dd>
                <dt>Offers</dt>
                <dd>
                  {data.activeOffers.toLocaleString()} on sale of {data.offers.toLocaleString()}
                  {data.offers > data.activeOffers ? (
                    <span className="s7-muted">
                      {' '}
                      — the rest are disabled or past their expiry
                    </span>
                  ) : null}
                </dd>
                <dt>Objectives</dt>
                <dd>
                  {data.activeObjectives.toLocaleString()} live of {data.objectives.toLocaleString()}
                </dd>
              </dl>
            )}
          </CardBody>
        </Card>
      </div>

      <Card className="s7-col-12">
        <CardHeader icon={<Activity size={16} />} title="Jump to" />
        <CardBody>
          <div className="s7-tiles">
            {tiles.map((entry) => (
              <Link key={entry.to} to={entry.to} className="s7-tile">
                <span className="s7-tile-glyph" aria-hidden>
                  <entry.icon size={17} />
                </span>
                <span style={{ minWidth: 0 }}>
                  <strong>{entry.label}</strong>
                  <span>{entry.blurb}</span>
                </span>
              </Link>
            ))}
          </div>
        </CardBody>
      </Card>

      {data.serverTimeUtc ? (
        <p className="s7-hint" style={{ marginTop: '1rem', textAlign: 'center' }}>
          Server time {formatDateTime(data.serverTimeUtc)} — figures are counted at the moment of
          the request, not cached.
        </p>
      ) : null}
    </motion.div>
  )
}

/** Local-clock greeting. Cosmetic, and deliberately not from the server: it is
 *  about the person reading the screen, not about the platform. */
function greeting(): string {
  const hour = new Date().getHours()
  if (hour < 5) return 'Still up'
  if (hour < 12) return 'Good morning'
  if (hour < 18) return 'Good afternoon'
  return 'Good evening'
}
