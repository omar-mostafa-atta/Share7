// ===========================================================================
// Shared motion variants
//
// Kept in one place so pages animate consistently instead of each component
// inventing its own timing. Durations lean on the CSS tokens: --s7-duration
// (180ms) for state changes, --s7-duration-lg (420ms) for entrances.
//
// Every transform here is opacity/translate/scale only — all compositor
// properties, so nothing in this file triggers layout. MotionConfig in App.tsx
// disables the lot when the OS asks for reduced motion.
// ===========================================================================

import type { Transition, Variants } from 'motion/react'

export const easeOutExpo = [0.16, 1, 0.3, 1] as const
export const easeSpring = [0.34, 1.56, 0.64, 1] as const

export const springSoft: Transition = { type: 'spring', stiffness: 380, damping: 30 }
export const springSnappy: Transition = { type: 'spring', stiffness: 560, damping: 34 }

/** A page fading up into place. Applied by AppShell to every route. */
export const pageVariants: Variants = {
  hidden: { opacity: 0, y: 10 },
  visible: {
    opacity: 1,
    y: 0,
    transition: { duration: 0.42, ease: easeOutExpo, staggerChildren: 0.06, delayChildren: 0.04 },
  },
  exit: { opacity: 0, y: -6, transition: { duration: 0.16, ease: 'easeIn' } },
}

/** A child of a staggered container — cards, form sections, table rows. */
export const riseVariants: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.42, ease: easeOutExpo } },
  exit: { opacity: 0, y: -8, transition: { duration: 0.16, ease: 'easeIn' } },
}

/** A list container that staggers its children in. */
export const listVariants: Variants = {
  hidden: {},
  visible: { transition: { staggerChildren: 0.035 } },
}

/**
 * A row appearing in or leaving a table. Rows collapse their height on exit so the ones below
 * slide up rather than jumping — the reason exit animates height as well as opacity.
 */
export const rowVariants: Variants = {
  hidden: { opacity: 0, x: -8 },
  visible: { opacity: 1, x: 0, transition: { duration: 0.28, ease: easeOutExpo } },
  exit: { opacity: 0, x: 12, transition: { duration: 0.18, ease: 'easeIn' } },
}

/** Modal panel: a small scale-up with a spring, so it lands rather than stopping. */
export const modalVariants: Variants = {
  hidden: { opacity: 0, scale: 0.94, y: 12 },
  visible: { opacity: 1, scale: 1, y: 0, transition: springSoft },
  exit: { opacity: 0, scale: 0.97, y: 6, transition: { duration: 0.14, ease: 'easeIn' } },
}

export const scrimVariants: Variants = {
  hidden: { opacity: 0 },
  visible: { opacity: 1, pointerEvents: 'auto', transition: { duration: 0.2 } },

  // `pointerEvents: none` on the way out, and it is not cosmetic. The scrim covers the whole
  // viewport, so for as long as it is mounted it swallows every click on the page — and it stays
  // mounted until its exit animation completes. Anything that stalls that animation (a
  // backgrounded tab whose rAF loop is suspended is the easy way to see it) would otherwise leave
  // an invisible sheet over a page that looks perfectly usable. Non-animatable values are applied
  // immediately rather than tweened, so this takes effect the moment the exit starts.
  exit: { opacity: 0, pointerEvents: 'none', transition: { duration: 0.16 } },
}

/** Toast sliding in from the right edge. */
export const toastVariants: Variants = {
  hidden: { opacity: 0, x: 40, scale: 0.96 },
  visible: { opacity: 1, x: 0, scale: 1, transition: springSnappy },
  exit: { opacity: 0, x: 40, scale: 0.96, transition: { duration: 0.18, ease: 'easeIn' } },
}

/** Press feedback shared by every button. */
export const tapScale = { scale: 0.97 }
export const hoverLift = { y: -1 }
