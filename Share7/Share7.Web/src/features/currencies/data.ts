// ===========================================================================
// Currencies — data access
//
// Ports wwwroot/js/currencies.js. Endpoint paths and payloads are unchanged;
// what is new is that update() is wired up at all — PUT /api/currencies/{id}
// has always existed and the vanilla console never called it, so renaming or
// retiring a currency meant editing the database by hand.
// ===========================================================================

import { useCallback, useEffect, useState } from 'react'
import { api } from '../../lib/client'
import { toast } from '../../store/toast'
import type {
  AdminGrantCurrencyRequest,
  BalanceDto,
  BalancesResponse,
  CreateCurrencyRequest,
  CurrenciesResponse,
  CurrencyDto,
  UpdateCurrencyRequest,
  WalletMutationResult,
} from '../../types/api'

// ---------------------------------------------------------------------------
// Currency catalogue
// ---------------------------------------------------------------------------

export function useCurrencies() {
  const [currencies, setCurrencies] = useState<CurrencyDto[]>([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(async (isRefresh = false) => {
    if (isRefresh) setRefreshing(true)
    try {
      const data = await api.get<CurrenciesResponse>('/api/currencies')
      setCurrencies(data.currencies ?? [])
    } catch {
      // Already surfaced by the global error handler.
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const create = useCallback(
    async (request: CreateCurrencyRequest) => {
      await api.post<CurrencyDto>('/api/currencies', request)
      toast.success('Currency created', `"${request.key}" is now available.`)
      await load(true)
    },
    [load],
  )

  const update = useCallback(async (currencyId: string, request: UpdateCurrencyRequest) => {
    const updated = await api.put<CurrencyDto>(`/api/currencies/${currencyId}`, request)

    // Replace in place rather than refetching the list: the response is the updated row, and a
    // full reload would remount every table row and replay the entrance animation.
    setCurrencies((rows) => rows.map((c) => (c.currencyId === currencyId ? updated : c)))

    toast.success(
      request.enabled ? 'Currency updated' : 'Currency retired',
      request.enabled
        ? `"${updated.key}" saved.`
        : `"${updated.key}" no longer accepts credits or debits. Balances and history are intact.`,
    )

    return updated
  }, [])

  return { currencies, loading, refreshing, reload: () => load(true), create, update }
}

// ---------------------------------------------------------------------------
// Balances
// ---------------------------------------------------------------------------

export function useBalances() {
  const [balances, setBalances] = useState<BalanceDto[]>([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const load = useCallback(async (isRefresh = false) => {
    if (isRefresh) setRefreshing(true)
    try {
      const data = await api.get<BalancesResponse>('/api/commerce/balances')
      setBalances(data.balances ?? [])
    } catch {
      // Already surfaced by the global error handler.
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const grant = useCallback(async (request: AdminGrantCurrencyRequest) => {
    const result = await api.post<WalletMutationResult>('/api/currencies/grant', request)

    // The response carries the new absolute balance, so this upserts instead of refetching. The
    // upsert matters as much as the saved round-trip: a currency the account has never held is
    // absent from GET /balances rather than reported as zero, so the first grant of one has to
    // add the row.
    setBalances((rows) => {
      const index = rows.findIndex((b) => b.currency === result.currency)
      const row: BalanceDto = { currency: result.currency, amount: result.amount }
      if (index === -1) return [...rows, row]

      const next = [...rows]
      next[index] = row
      return next
    })

    const delta = request.amount
    toast.success(
      'Balance updated',
      `${delta > 0 ? '+' : ''}${delta.toLocaleString()} ${result.currency} — new total ${result.amount.toLocaleString()}.`,
    )

    return result
  }, [])

  return { balances, loading, refreshing, reload: () => load(true), grant }
}
