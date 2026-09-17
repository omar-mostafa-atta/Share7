import { create } from 'zustand'

export type ToastKind = 'danger' | 'success' | 'warning' | 'info'

export interface Toast {
  id: number
  kind: ToastKind
  title: string
  detail?: string
}

interface ToastState {
  toasts: Toast[]
  push: (kind: ToastKind, title: string, detail?: string) => number
  dismiss: (id: number) => void
}

let nextId = 1

/** Matches the old console: failures linger for 8s, confirmations are shorter. */
const TTL: Record<ToastKind, number> = {
  danger: 8000,
  warning: 8000,
  success: 4000,
  info: 5000,
}

export const useToasts = create<ToastState>((set, get) => ({
  toasts: [],

  push: (kind, title, detail) => {
    const id = nextId++
    set((s) => ({ toasts: [...s.toasts, { id, kind, title, detail }] }))
    window.setTimeout(() => get().dismiss(id), TTL[kind])
    return id
  },

  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}))

/** Imperative helpers for use outside components. */
export const toast = {
  error: (title: string, detail?: string) => useToasts.getState().push('danger', title, detail),
  success: (title: string, detail?: string) => useToasts.getState().push('success', title, detail),
  warn: (title: string, detail?: string) => useToasts.getState().push('warning', title, detail),
  info: (title: string, detail?: string) => useToasts.getState().push('info', title, detail),
}
