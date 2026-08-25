import { useMotionValue, useReducedMotion, useSpring } from 'motion/react'
import { useEffect, useRef } from 'react'

/**
 * A number that counts to its new value instead of snapping.
 *
 * On a balance card this is doing real work rather than decoration: after a grant the figure
 * changes by itself, and a value that animates from the old total to the new one tells the admin
 * *which* balance moved and roughly by how much. A snapped number leaves them re-reading the card
 * to find what changed.
 *
 * The spring drives textContent through a ref rather than component state — a counter that
 * re-rendered on every frame of every card would be the most expensive thing on the page.
 */
export function AnimatedNumber({ value }: { value: number }) {
  const ref = useRef<HTMLSpanElement>(null)
  const reduced = useReducedMotion()

  const source = useMotionValue(value)
  const spring = useSpring(source, { stiffness: 90, damping: 22, restDelta: 0.5 })

  useEffect(() => {
    source.set(value)
  }, [source, value])

  useEffect(() => {
    // Honouring the OS preference means writing the final value once and never subscribing.
    if (reduced) {
      if (ref.current) ref.current.textContent = value.toLocaleString()
      return
    }

    const write = (n: number) => {
      if (ref.current) ref.current.textContent = Math.round(n).toLocaleString()
    }

    const unsubscribe = spring.on('change', write)

    // Two things this guard is for, and neither is cosmetic.
    //
    // Writing spring.get() eagerly here would paint the *pre*-animation figure over the correct
    // one React just rendered — so if no frames follow, the card sits there showing the old
    // balance after a grant that actually succeeded. And frames genuinely may not follow: a
    // backgrounded tab has its rAF loop suspended, so the subscription above never fires at all.
    //
    // React's own render already puts the right number in the DOM, so this only has to guarantee
    // that no stale frame is left showing. The timeout is longer than the spring takes to settle,
    // which makes it a no-op that rewrites the value it already reached in the normal case.
    const settle = window.setTimeout(() => write(value), 1200)

    return () => {
      unsubscribe()
      window.clearTimeout(settle)
    }
  }, [spring, reduced, value])

  // The initial server-rendered-ish text keeps the number present for one frame before the
  // effect attaches, so the card never flashes empty.
  return <span ref={ref}>{value.toLocaleString()}</span>
}
