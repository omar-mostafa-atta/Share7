import {
  Activity,
  BarChart3,
  CalendarRange,
  Database,
  GraduationCap,
  Building2,
  History,
  Boxes,
  Layers,
  Coins,
  Compass,
  Gamepad2,
  Gauge,
  Gift,
  LayoutDashboard,
  Radio,
  Rocket,
  ShieldCheck,
  ShoppingBag,
  Sparkles,
  Tag,
  Trophy,
  Users,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { can, type Permission } from './access'

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
//
// `NAV` below is the Admin Console's, which is the only one there is since the
// Content Portal closed at cutover. The helpers at the bottom still take a list
// rather than reading this one, because that is what a second audience would
// arrive through — see lib/portals.ts.
// ===========================================================================

export interface NavEntry {
  to: string
  label: string
  icon: LucideIcon
  blurb: string

  /** Shown only to people who hold it, over and above the portal's own. Nobody is offered a page the API refuses. */
  permission?: Permission
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
    // Authoring left this console at cutover (plan P6): the curriculum tree, the questions in it,
    // answer quality and the learning skills are built in the Content Studio, where a second person
    // reviews everything before a Lead releases it. What is left here is measurement and the people
    // being measured — questions this console is still the only place to ask.
    //
    // The old addresses are not listed but still answer; see routes/Moved.tsx.
    section: 'Content',
    items: [
      {
        to: '/organizations',
        label: 'Organizations',
        icon: Building2,
        blurb:
          'Schools, districts and tutoring centres: cohorts, rosters, teacher and admin roles, guardian links, curriculum overlays, assignments, and exactly how much of a learner an organization can see',
      },
      {
        to: '/exams',
        label: 'Examinations',
        icon: GraduationCap,
        blurb:
          'Blueprints, exam specifications, coverage thresholds and how far each exam is from a calibration that could predict an outcome',
      },
      {
        to: '/games',
        label: 'Games',
        icon: Gamepad2,
        blurb: 'Mini-game catalogue, player counts, lobby and matchmaking flags',
      },
      {
        to: '/modes',
        label: 'Modes & Worlds',
        icon: Layers,
        blurb:
          'Rule-sets a game offers, what each counts for, payout profiles, and how a world is unlocked or bought',
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
        to: '/live-events',
        label: 'Live events',
        icon: Trophy,
        blurb:
          'Competitions, their ladders, entry rules and prize tables — in-game or real-world — and the prize claim queue',
      },
      {
        to: '/progression',
        label: 'Progression',
        icon: Gauge,
        blurb: 'The XP level curve — cumulative thresholds per level',
      },
      {
        to: '/guidance',
        label: 'Guidance',
        icon: Compass,
        blurb:
          'Remote guidance CMS — walkthroughs, onboarding tours, drafts, version publishing and emergency kill-switch',
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
        blurb: 'Look up an account, add one of any role, inspect its profile, grant entitlements, delete',
      },
      {
        to: '/team',
        label: 'Team & Access',
        icon: ShieldCheck,
        blurb:
          'The content team: who is in it, their Studio role and scope, suspend, reset access, close; the audit log; staff sign-in security',
        permission: 'team.manage',
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

export type FlatNavEntry = NavEntry & { section: string }

/** A nav list without the entries `roles` may not open, and without sections left empty by that. */
export function navFor(nav: NavGroup[], roles: readonly string[]): NavGroup[] {
  return nav
    .map((group) => ({ ...group, items: group.items.filter((item) => !item.permission || can(roles, item.permission)) }))
    .filter((group) => group.items.length > 0)
}

/** A nav list flattened, for the command palette and for resolving a path to its label. */
export function flattenNav(nav: NavGroup[]): FlatNavEntry[] {
  return nav.flatMap((group) => group.items.map((item) => ({ ...item, section: group.section })))
}

/** The Admin Console's entries, flattened. */
export const NAV_ENTRIES: FlatNavEntry[] = flattenNav(NAV)

/**
 * The entry in `nav` matching a pathname, or null.
 *
 * Longest-prefix rather than exact, so `/leaderboards/flagged` still titles the
 * page "Leaderboards". The root entry is excluded from prefix matching or it
 * would match everything.
 */
export function entryForPath(pathname: string, nav: NavGroup[] = NAV): FlatNavEntry | null {
  let best: FlatNavEntry | null = null

  for (const entry of flattenNav(nav)) {
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
