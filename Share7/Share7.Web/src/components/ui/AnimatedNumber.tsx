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

    // The deadline, and it is not cosmetic.
    //
    // This spring is allowed to own the text only for as long as it is actually moving. Where it
    // is not — a hidden document freezes the timeline, and the subscription then emits the
    // *starting* value and nothing after it — every frame it does emit paints a stale figure over
    // the correct one React already rendered. That is a card reading 0 next to a table listing
    // one, which is the version of this bug that reached a screenshot.
    //
    // So the deadline unsubscribes first and writes the true value second. After it, nothing can
    // put a wrong number back. It is longer than the spring takes to settle, so in the normal
    // case it rewrites the value the animation already arrived at and changes nothing.
    const settle = window.setTimeout(() => {
      unsubscribe()
      write(value)
    }, 1200)

    return () => {
      unsubscribe()
      window.clearTimeout(settle)
    }
  }, [spring, reduced, value])

  // The initial server-rendered-ish text keeps the number present for one frame before the
  // effect attaches, so the card never flashes empty.
  return <span ref={ref}>{value.toLocaleString()}</span>
}
