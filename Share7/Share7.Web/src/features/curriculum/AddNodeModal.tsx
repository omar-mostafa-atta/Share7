import { AnimatePresence, motion } from 'motion/react'
import { AlertTriangle, FolderPlus } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Button } from '../../components/ui/primitives'
import { Field, Input } from '../../components/ui/form'
import { Modal } from '../../components/ui/Modal'
import { useLanguages } from '../../store/languages'
import type { Level } from './levels'
import type { CreateCurriculumNodeRequest } from '../../types/api'

export function AddNodeModal({
  level,
  parentName,
  onClose,
  onSubmit,
}: {
  level: Level | null
  parentName: string
  onClose: () => void
  onSubmit: (level: Level, request: CreateCurriculumNodeRequest) => Promise<void>
}) {
  const languages = useLanguages((s) => s.languages)
  const selectedLangId = useLanguages((s) => s.selectedLangId)

  const [names, setNames] = useState<Record<string, string>>({})
  const [order, setOrder] = useState('')
  const [busy, setBusy] = useState(false)
  const [touched, setTouched] = useState(false)

  useEffect(() => {
    if (level) {
      setNames({})
      setOrder('')
      setTouched(false)
    }
  }, [level])

  // A node requires a name in *every* configured language — the server refuses a partial set with
  // details.missingLanguages, so it is worth naming the gap before the round-trip.
  const missing = useMemo(
    () => languages.filter((l) => !(names[l.id] ?? '').trim()).map((l) => l.name),
    [languages, names],
  )

  const submit = async () => {
    setTouched(true)
    if (!level || missing.length || !languages.length) return

    setBusy(true)
    try {
      const request: CreateCurriculumNodeRequest = {
        translations: languages.map((l) => ({ langId: l.id, name: names[l.id].trim() })),
      }

      // Omitted rather than sent as null when blank: the server assigns the next Order itself,
      // and Order is unique per parent so guessing one here would collide.
      if (order.trim() !== '') request.order = Number(order)

      await onSubmit(level, request)
      onClose()
    } catch {
      // Surfaced globally — a duplicate sibling name comes back as a 409 and the dialog stays
      // open with the typed names intact.
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={!!level}
      onClose={onClose}
      icon={<FolderPlus size={17} />}
      title={level ? `Add ${level}${parentName ? ` under ${parentName}` : ''}` : 'Add node'}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} loading={busy}>
            {busy ? 'Adding…' : `Add ${level ?? 'node'}`}
          </Button>
        </>
      }
    >
      <div className="s7-translation-rows">
        {languages.map((l) => (
          <Field key={l.id} label={`${l.name} (${l.code})`}>
            <Input
              value={names[l.id] ?? ''}
              placeholder={`name in ${l.name}`}
              maxLength={200}
              // Arabic typed into an LTR box reads as though the words are reversed. The old
              // console did the same on the code 'ar'.
              dir={l.code === 'ar' ? 'rtl' : undefined}
              autoFocus={l.id === selectedLangId}
              invalid={touched && !(names[l.id] ?? '').trim()}
              onChange={(e) => setNames((prev) => ({ ...prev, [l.id]: e.target.value }))}
            />
          </Field>
        ))}

        <Field
          label="Order (optional)"
          hint="Left blank, the server appends it after the current last sibling."
        >
          <Input
            type="number"
            min={0}
            value={order}
            placeholder="auto"
            onChange={(e) => setOrder(e.target.value)}
          />
        </Field>

        <AnimatePresence initial={false}>
          {touched && missing.length ? (
            <motion.div
              key="missing-languages"
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              exit={{ opacity: 0, height: 0 }}
              transition={{ duration: 0.2 }}
              style={{ overflow: 'hidden' }}
            >
              <div
                className="s7-row"
                style={{
                  padding: '0.6rem 0.7rem',
                  borderRadius: 'var(--s7-radius)',
                  background: 'var(--s7-warning-bg)',
                  color: '#92400e',
                  fontSize: '0.78rem',
                  alignItems: 'flex-start',
                }}
              >
                <AlertTriangle size={15} style={{ flex: '0 0 auto', marginTop: '0.1rem' }} />
                <span>
                  A name is required in every language. Still missing:{' '}
                  <strong>{missing.join(', ')}</strong>.
                </span>
              </div>
            </motion.div>
          ) : null}
        </AnimatePresence>
      </div>
    </Modal>
  )
}
