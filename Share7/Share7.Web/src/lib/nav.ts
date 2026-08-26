import {
  Activity,
  BarChart3,
  CalendarRange,
  Database,
  History,
  Boxes,
  Coins,
  Gamepad2,
  Gauge,
  Gift,
  LayoutDashboard,
  Network,
  Radio,
  Rocket,
  ShoppingBag,
  Sparkles,
  Tag,
  Trophy,
  Users,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

// ===========================================================================
// Navigation registry
//
// One list, consumed by the sidebar and the command palette. They disagreed in
// the vanilla console — nav.js listed seven pages, the sign-in redirect knew
// about an eighth — and the only reliable fix is for there to be one array.
//
// `blurb` is not decoration: it is what the command palette matches against,
// so searching "cheat" finds Runs and "xp" finds both Progression and Signal
// Valuations without anyone maintaining a keyword list.
// ===========================================================================

export interface NavEntry {
  to: string
  label: string
  icon: LucideIcon
  blurb: string
}

export interface NavGroup {
  section: string
  items: NavEntry[]
}

export const NAV: NavGroup[] = [
  {
    section: 'Overview',
    items: [
      {
        to: '/',
        label: 'Command Center',
        icon: LayoutDashboard,
        blurb: 'Platform health, live sessions, flags awaiting review, catalogue totals',
      },
    ],
  },
  {
    section: 'Analytics',
    items: [
      {
        to: '/analytics',
        label: 'Analytics',
        icon: Activity,
        blurb: 'DAU, WAU, MAU, stickiness, sessions, funnels, conversion, economy inflation',
      },
      {
        to: '/retention',
        label: 'Retention',
        icon: CalendarRange,
        blurb: 'D1 D7 D30 cohort triangle, churn, comeback rate, install cohorts',
      },
      {
        to: '/events',
        label: 'Events',
        icon: Database,
        blurb: 'Event registry, volumes, sampling, retention, unregistered names awaiting review',
      },
      {
        to: '/trace',
        label: 'User trace',
        icon: History,
        blurb: 'Everything one player did — grants, rewards, purchases, runs, screens, in order',
      },
    ],
  },
  {
    section: 'Content',
    items: [
      {
        to: '/curriculum',
        label: 'Curriculum',
        icon: Network,
        blurb: 'Grades, terms, subjects, chapters, lessons and their question pools',
      },
      {
        to: '/games',
        label: 'Games',
        icon: Gamepad2,
        blurb: 'Mini-game catalogue, player counts, lobby and matchmaking flags',
      },
    ],
  },
  {
    section: 'Engagement',
    items: [
      {
        to: '/objectives',
        label: 'Objectives',
        icon: Trophy,
        blurb: 'Daily, weekly and achievement goals — metric, target, cycle, rewards',
      },
      {
        to: '/leaderboards',
        label: 'Leaderboards',
        icon: BarChart3,
        blurb: 'Boards, cycles, metric bounds and flagged results awaiting a verdict',
      },
      {
        to: '/progression',
        label: 'Progression',
        icon: Gauge,
        blurb: 'The XP level curve — cumulative thresholds per level',
      },
    ],
  },
  {
    section: 'Economy',
    items: [
      {
        to: '/currencies',
        label: 'Currencies',
        icon: Coins,
        blurb: 'Soft and hard currencies, daily earn caps, balances and manual grants',
      },
      {
        to: '/signals',
        label: 'Signal Valuations',
        icon: Sparkles,
        blurb: 'What a pickup pays: unit value, per-run, per-day and per-second ceilings, XP',
      },
      {
        to: '/rewards',
        label: 'Reward Rules',
        icon: Gift,
        blurb: 'Event-driven payouts, repeat policy, cooldowns and daily limits',
      },
      {
        to: '/shop',
        label: 'Shop',
        icon: ShoppingBag,
        blurb: 'Products, product kinds and the grants a product hands over',
      },
      {
        to: '/offers',
        label: 'Offers',
        icon: Tag,
        blurb: 'Priced bundles, availability windows, purchase limits and badges',
      },
    ],
  },
  {
    section: 'Operations',
    items: [
      {
        to: '/runs',
        label: 'Runs',
        icon: Rocket,
        blurb: 'Flagged runs, collected signals, payout breakdown and cheat review',
      },
      {
        to: '/multiplayer',
        label: 'Multiplayer',
        icon: Radio,
        blurb: 'Live sessions, player counts, heartbeats and forced close',
      },
      {
        to: '/users',
        label: 'Users',
        icon: Users,
        blurb: 'Look up an account, inspect its profile, grant entitlements, delete',
      },
      {
        to: '/catalogue',
        label: 'Product Kinds',
        icon: Boxes,
        blurb: 'The kinds a product can be, and how many products use each',
      },
    ],
  },
]

/** Flattened, for the command palette and for resolving a path to its label. */
export const NAV_ENTRIES: (NavEntry & { section: string })[] = NAV.flatMap((group) =>
  group.items.map((item) => ({ ...item, section: group.section })),
)

/**
 * The entry matching a pathname, or null.
 *
 * Longest-prefix rather than exact, so `/leaderboards/flagged` still titles the
 * page "Leaderboards". The root entry is excluded from prefix matching or it
 * would match everything.
 */
export function entryForPath(pathname: string): (NavEntry & { section: string }) | null {
  let best: (NavEntry & { section: string }) | null = null

  for (const entry of NAV_ENTRIES) {
    if (entry.to === '/') {
      if (pathname === '/') best = entry
      continue
    }

    if (pathname === entry.to || pathname.startsWith(`${entry.to}/`)) {
      if (!best || entry.to.length > best.to.length) best = entry
    }
  }

  return best
}
