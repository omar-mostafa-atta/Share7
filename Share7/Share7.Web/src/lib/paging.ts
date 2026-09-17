import { useCallback, useState } from 'react'

// ===========================================================================
// usePaging
//
// Page state for a list the browser already holds in full — the tables, the
// level curve, the curriculum findings. Lists the server pages (Users, the
// question browser) keep their own page in the query instead, and only share
// the <Pagination> bar.
//
// The subtle part is not the slicing, it is keeping the page valid while the
// list changes underneath it, which is why this is one hook rather than two
// useStates repeated per page.
// ===========================================================================

/** Offered in every pager's "Items Per Page" menu. The first is the default. */
export const PAGE_SIZE_OPTIONS = [10, 25, 50, 100]

export const DEFAULT_PAGE_SIZE = PAGE_SIZE_OPTIONS[0]

export interface Paging {
  /** 1-based, and always within range for the current total. */
  page: number
  pageSize: number
  pageCount: number

  /** Slice bounds for the current page: `rows.slice(start, end)`. */
  start: number
  end: number

  setPage: (page: number) => void

  /** Changes the size and returns to page 1 — "page 7" of a different size is a different set of rows. */
  setPageSize: (size: number) => void
}

export function usePaging(
  total: number,
  {
    resetKey,
    initialPageSize = DEFAULT_PAGE_SIZE,
  }: {
    /**
     * Returns to page 1 whenever this changes. Pass what the list is filtered
     * on — a search typed while on page 4 should show the best matches, not
     * whichever of them happen to fall fourth.
     *
     * Explicit rather than inferred from the list changing, because a row
     * patched in place after a verdict also changes the list, and throwing the
     * admin back to page 1 mid-queue is exactly what must not happen.
     */
    resetKey?: string
    initialPageSize?: number
  } = {},
): Paging {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSizeState] = useState(initialPageSize)
  const [seenResetKey, setSeenResetKey] = useState(resetKey)

  // Adjusted during render rather than in an effect, so there is no frame
  // showing page 4 of the newly filtered list before the reset lands.
  if (resetKey !== seenResetKey) {
    setSeenResetKey(resetKey)
    setPage(1)
  }

  const pageCount = Math.max(1, Math.ceil(total / pageSize))

  // Rows leaving from under the current page — a closed session dropping out of
  // the Live scope, a deleted level — can strand it past the end. Pull back to
  // the last page that exists rather than showing an empty one. Functional, so
  // it composes with a reset queued above in this same render; setting
  // `pageCount` outright would overwrite that reset using the stale page.
  if (page > pageCount) setPage((p) => Math.min(p, pageCount))
  const current = Math.min(page, pageCount)

  const setPageSize = useCallback((size: number) => {
    setPageSizeState(size)
    setPage(1)
  }, [])

  return {
    page: current,
    pageSize,
    pageCount,
    start: (current - 1) * pageSize,
    end: current * pageSize,
    setPage,
    setPageSize,
  }
}
