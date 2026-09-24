import { useEffect } from 'react'

/**
 * Marks `<html>` with `data-motion="ready"` the first time the document is actually visible.
 *
 * ## Why this exists
 *
 * Motion will not run an entrance animation while `document.visibilityState === 'hidden'`: the
 * document timeline is frozen at zero, so no animation is ever created. Every element that mounts
 * with `initial="hidden"` therefore sits at its hidden state — which for cards, stat tiles and page
 * titles is `opacity: 0`.
 *
 * The result is a console that renders **nothing at all** in a tab that loaded while hidden. That
 * is not an edge case: a background tab, a restored session, a collapsed preview pane and a link
 * opened with ctrl-click all load hidden, and the page the user eventually switches to is blank.
 *
 * ## Why the fix is a stylesheet rule rather than different variants
 *
 * The obvious repair — give every `hidden` variant a non-zero opacity — trades a blank page for a
 * dim one and has to be remembered at every call site forever. The rule in `console.css` instead
 * holds the animated elements at their *resting* state until this attribute appears, so the
 * failure mode is "no entrance animation", which nobody can see, rather than "no content".
 *
 * Content must never depend on an animation having run. That is the whole of it.
 */
export function useDocumentHasBeenVisible(): void {
  useEffect(() => {
    // Decided once, at mount, and never revisited.
    //
    // Re-arming on `visibilitychange` was the first attempt and it is wrong: Motion does not catch
    // up when a document it mounted under becomes visible — the elements were already mounted at
    // their hidden state and no animation was ever created for them — so releasing the override
    // later empties the page instead of animating it. A page load that began hidden forfeits its
    // entrance for good, which is the right way round: choreography is the thing worth losing.
    if (document.visibilityState === 'visible') {
      document.documentElement.dataset.motion = 'ready'
    }
  }, [])
}
