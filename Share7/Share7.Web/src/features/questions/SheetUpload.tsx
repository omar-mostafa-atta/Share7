import { motion } from 'motion/react'
import { FileSpreadsheet, Upload, X } from 'lucide-react'
import { useRef, useState } from 'react'
import { Button, Subhead } from '../../components/ui/primitives'
import { Field, Select, Switch } from '../../components/ui/form'
import { toast } from '../../store/toast'
import type { Language } from '../../types/api'

/**
 * The 4-column sheet contract, shown rather than described.
 *
 * Column 2 being the correct answer is the single fact an admin has to get right, and getting it
 * wrong silently publishes a set where every question is mis-keyed — so it is labelled on screen
 * instead of living only in the endpoint's XML docs.
 */
function ColumnLegend() {
  return (
    <div className="s7-columns">
      <div className="s7-column-chip">
        <b>Column 1</b>
        Question
      </div>
      <div className="s7-column-chip is-correct">
        <b>Column 2</b>
        Correct answer
      </div>
      <div className="s7-column-chip">
        <b>Column 3</b>
        Wrong answer
      </div>
      <div className="s7-column-chip">
        <b>Column 4</b>
        Wrong answer
      </div>
    </div>
  )
}

export function SheetUpload({
  languages,
  langId,
  onLangChange,
  busy,
  onUpload,
}: {
  languages: Language[]
  langId: string
  onLangChange: (langId: string) => void
  busy: boolean
  onUpload: (file: File, hasHeaderRow: boolean) => Promise<unknown>
}) {
  const [file, setFile] = useState<File | null>(null)
  const [hasHeaderRow, setHasHeaderRow] = useState(true)
  const [dragOver, setDragOver] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  // The server refuses anything but .xlsx, so it is checked here too — a rejected file should not
  // cost a round-trip and a red toast.
  const accept = (candidate: File | undefined) => {
    if (!candidate) return
    if (!candidate.name.toLowerCase().endsWith('.xlsx')) {
      toast.error('Only .xlsx files are supported', `"${candidate.name}" is not a spreadsheet.`)
      return
    }
    setFile(candidate)
  }

  const submit = async () => {
    if (!file) {
      toast.error('Choose a file', 'Pick the .xlsx sheet to publish.')
      return
    }
    const result = await onUpload(file, hasHeaderRow)
    // Only clear on success — a rejected sheet is usually re-uploaded after a fix, and throwing
    // the selection away means finding the file again.
    if (result) {
      setFile(null)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  return (
    <div>
      <Subhead icon={<FileSpreadsheet size={15} />}>Upload a sheet</Subhead>

      <div
        className={`s7-dropzone ${dragOver ? 'is-over' : ''}`}
        role="button"
        tabIndex={0}
        onClick={() => inputRef.current?.click()}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') inputRef.current?.click()
        }}
        onDragOver={(e) => {
          e.preventDefault()
          setDragOver(true)
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={(e) => {
          e.preventDefault()
          setDragOver(false)
          accept(e.dataTransfer.files[0])
        }}
      >
        <Upload size={22} />
        <span className="s7-dropzone-title">
          {dragOver ? 'Drop the sheet here' : 'Drag an .xlsx here, or click to choose'}
        </span>
        <span className="s7-dropzone-hint">Up to 10 MB. One sheet per language.</span>

        <input
          ref={inputRef}
          type="file"
          accept=".xlsx"
          hidden
          onChange={(e) => accept(e.target.files?.[0])}
        />
      </div>

      {/*
        Entrance only, same reasoning as the error panel: a chip that outlives its selection would
        claim a file is still queued after a successful publish cleared it.
      */}
      {file ? (
          <motion.div
            key="chosen"
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            transition={{ duration: 0.2 }}
            style={{ overflow: 'hidden' }}
          >
            <div className="s7-row" style={{ marginTop: '0.6rem' }}>
              <span className="s7-file-chip">
                <FileSpreadsheet size={13} />
                {file.name}
                <span className="s7-muted">({Math.max(1, Math.round(file.size / 1024))} KB)</span>
              </span>
              <button
                type="button"
                className="s7-qrow-remove"
                aria-label="Clear the chosen file"
                onClick={() => {
                  setFile(null)
                  if (inputRef.current) inputRef.current.value = ''
                }}
              >
                <X size={13} />
              </button>
            </div>
          </motion.div>
        ) : null}

      <ColumnLegend />

      <div className="s7-row" style={{ marginTop: '0.85rem', gap: '1rem', flexWrap: 'wrap' }}>
        <div style={{ minWidth: 180 }}>
          <Field label="Sheet language">
            <Select value={langId} onChange={(e) => onLangChange(e.target.value)}>
              {languages.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name} ({l.code})
                </option>
              ))}
            </Select>
          </Field>
        </div>

        <Switch
          checked={hasHeaderRow}
          onChange={setHasHeaderRow}
          label="First row is a header"
        />
      </div>

      {/*
        "Publish sheet", not "Publish questions". The manual editor below publishes to the same
        endpoint family and had the identical label, so the panel carried two same-named buttons
        for different actions — one of which is disabled until a file is chosen, making a click on
        the wrong one do nothing at all with no explanation.
      */}
      <Button onClick={submit} loading={busy} disabled={!file} style={{ marginTop: '0.85rem' }}>
        {busy ? 'Uploading…' : 'Publish sheet'}
      </Button>

      <div className="s7-hint">
        Validation is all-or-nothing: one bad row rejects the whole sheet and leaves this
        language&rsquo;s current version untouched. A successful upload publishes the{' '}
        <strong>next version</strong> and retires the previous set.
      </div>
    </div>
  )
}
