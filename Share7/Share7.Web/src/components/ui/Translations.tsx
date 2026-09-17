import { useEffect } from 'react'
import { Languages } from 'lucide-react'
import { Input } from './form'
import { useLanguages } from '../../store/languages'

// ===========================================================================
// Translations editor
//
// Games, objectives, products, product kinds, offers and leaderboard boards all
// carry `translations[]`, and all six were about to grow their own copy of this.
//
// The important behaviour is that it renders a row for EVERY content language,
// not only the ones already present. An entity created while only English was
// filled in is invisible to Arabic clients — the API returns the row's `name`
// resolved in the caller's language and there is no fallback — so a missing
// translation has to look missing at authoring time, not at play time.
// ===========================================================================

export interface TranslationRow {
  langId: string
  name: string
  description?: string | null
}

export function TranslationsEditor({
  value,
  onChange,
  withDescription,
  nameLabel = 'Name',
}: {
  value: TranslationRow[]
  onChange: (rows: TranslationRow[]) => void
  withDescription?: boolean
  nameLabel?: string
}) {
  const languages = useLanguages((s) => s.languages)
  const load = useLanguages((s) => s.load)

  useEffect(() => {
    if (!languages.length) void load()
  }, [languages.length, load])

  // Fill in a blank row for any language the entity does not have yet. Done in an
  // effect rather than during render so the parent's form state is the single
  // source of truth — a derived-at-render list would be discarded on submit.
  useEffect(() => {
    if (!languages.length) return

    const missing = languages.filter((lang) => !value.some((row) => row.langId === lang.id))
    if (!missing.length) return

    onChange([
      ...value,
      ...missing.map((lang) => ({ langId: lang.id, name: '', description: '' })),
    ])
    // `onChange` and `value` are recreated by the parent on every render; depending
    // on them here loops. The language list is what actually gates this.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [languages])

  function update(langId: string, patch: Partial<TranslationRow>) {
    onChange(value.map((row) => (row.langId === langId ? { ...row, ...patch } : row)))
  }

  if (!languages.length) {
    return <p className="s7-hint">Loading languages…</p>
  }

  return (
    <div className="s7-translation-rows">
      {languages.map((lang) => {
        const row = value.find((r) => r.langId === lang.id)
        const filled = !!row?.name?.trim()

        return (
          <div key={lang.id} className="s7-field">
            <label className="s7-label">
              <Languages size={13} style={{ verticalAlign: -2, marginInlineEnd: 4 }} />
              {lang.name} — {nameLabel}
              {!filled ? (
                <span className="s7-badge s7-badge-warning" style={{ marginInlineStart: 6 }}>
                  empty
                </span>
              ) : null}
            </label>

            <Input
              value={row?.name ?? ''}
              onChange={(e) => update(lang.id, { name: e.target.value })}
              placeholder={`${nameLabel} in ${lang.name}`}
              // RTL languages need the field itself flipped, or Arabic text renders
              // with its punctuation on the wrong side while being typed.
              dir={lang.code?.startsWith('ar') ? 'rtl' : 'ltr'}
            />

            {withDescription ? (
              <textarea
                className="s7-textarea"
                rows={2}
                value={row?.description ?? ''}
                onChange={(e) => update(lang.id, { description: e.target.value })}
                placeholder={`Description in ${lang.name} (optional)`}
                dir={lang.code?.startsWith('ar') ? 'rtl' : 'ltr'}
                style={{ marginTop: '0.4rem' }}
              />
            ) : null}
          </div>
        )
      })}
    </div>
  )
}

/** Names of the languages still missing a translation, for a pre-submit warning. */
export function untranslated(rows: TranslationRow[], languages: { id: string; name: string }[]): string[] {
  return languages.filter((lang) => !rows.find((r) => r.langId === lang.id)?.name?.trim()).map((l) => l.name)
}
