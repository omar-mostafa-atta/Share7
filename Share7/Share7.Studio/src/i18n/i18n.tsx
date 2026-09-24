import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import type { InterfaceLanguage } from '../lib/api'
import { ar } from './ar'
import { en, type MessageKey } from './en'

// ===========================================================================
// English and Arabic
//
// Every string on screen comes from en.ts or ar.ts. ar.ts is typed against
// en.ts, so a key added in one and forgotten in the other is a compile error,
// not a blank label in production. The interface language sets <html lang> and
// <html dir>; layout uses logical properties throughout, so the whole Studio
// mirrors while each piece of content keeps its own direction.
// ===========================================================================

const dictionaries: Record<InterfaceLanguage, Record<MessageKey, string>> = { en, ar }

/**
 * Every time the API sends is UTC, but .NET writes an unspecified-kind DateTime
 * with no zone on the end — and a browser reads a bare "2026-09-22T20:45:00" as
 * local time. In Cairo that is a three-hour lie on every "saved just now". A
 * string with no zone and no offset is therefore read as the UTC it is.
 */
function asDate(value: string | Date): Date {
  if (value instanceof Date) return value
  const naive = /^\d{4}-\d{2}-\d{2}T[\d:.]+$/.test(value)
  return new Date(naive ? `${value}Z` : value)
}

const STORAGE_KEY = 'share7-studio.language'

type Vars = Record<string, string | number>

interface I18n {
  language: InterfaceLanguage
  dir: 'ltr' | 'rtl'
  setLanguage: (language: InterfaceLanguage) => void
  t: (key: MessageKey, vars?: Vars) => string

  /** For a server messageKey, which may be one this build does not know. */
  tError: (messageKey: string) => string

  formatDate: (iso: string | Date, style?: 'date' | 'dateTime' | 'time') => string
  formatRelative: (iso: string) => string
}

const I18nContext = createContext<I18n | null>(null)

function initialLanguage(): InterfaceLanguage {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored === 'en' || stored === 'ar') return stored
  } catch {
    // Storage blocked; fall back to the browser's language.
  }

  return navigator.language.toLowerCase().startsWith('ar') ? 'ar' : 'en'
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<InterfaceLanguage>(initialLanguage)
  const dir = language === 'ar' ? 'rtl' : 'ltr'

  useEffect(() => {
    document.documentElement.lang = language
    document.documentElement.dir = dir
  }, [language, dir])

  const setLanguage = useCallback((next: InterfaceLanguage) => {
    setLanguageState(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // A convenience only; the profile keeps the real preference once signed in.
    }
  }, [])

  const value = useMemo<I18n>(() => {
    const dictionary = dictionaries[language]
    const locale = language === 'ar' ? 'ar-EG' : 'en-GB'

    const t = (key: MessageKey, vars?: Vars) => {
      let text = dictionary[key] ?? en[key] ?? key
      if (vars) for (const [name, value] of Object.entries(vars)) text = text.replaceAll(`{${name}}`, String(value))
      return text
    }

    return {
      language,
      dir,
      setLanguage,
      t,
      tError: (messageKey) => (messageKey in dictionary ? dictionary[messageKey as MessageKey] : dictionary['errors.unexpected']),
      formatDate: (iso, style = 'date') => {
        const date = asDate(iso)
        const options: Intl.DateTimeFormatOptions =
          style === 'time'
            ? { hour: 'numeric', minute: '2-digit' }
            : style === 'dateTime'
              ? { day: 'numeric', month: 'long', hour: 'numeric', minute: '2-digit' }
              : { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }
        return new Intl.DateTimeFormat(locale, options).format(date)
      },
      formatRelative: (iso) => {
        const seconds = (asDate(iso).getTime() - Date.now()) / 1000
        const rtf = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
        const abs = Math.abs(seconds)
        if (abs < 60) return rtf.format(Math.round(seconds), 'second')
        if (abs < 3600) return rtf.format(Math.round(seconds / 60), 'minute')
        if (abs < 86400) return rtf.format(Math.round(seconds / 3600), 'hour')
        return rtf.format(Math.round(seconds / 86400), 'day')
      },
    }
  }, [language, dir, setLanguage])

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

export function useI18n(): I18n {
  const i18n = useContext(I18nContext)
  if (!i18n) throw new Error('useI18n() outside <I18nProvider>')
  return i18n
}
