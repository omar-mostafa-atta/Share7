import { useEffect, useMemo, useState } from 'react'
import { motion } from 'motion/react'
import { Gauge, Plus, RefreshCw, RotateCcw, Save, Trash2, TrendingUp } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, IconButton } from '../components/ui/primitives'
import { Note, PageTitle, Pagination } from '../components/ui/bits'
import { Input } from '../components/ui/form'
import { api } from '../lib/client'
import { usePaging } from '../lib/paging'
import { useResourceList } from '../lib/resource'
import { toast } from '../store/toast'
import { listVariants } from '../components/ui/motion'
import type { LevelThresholdDto } from '../types/api'

// ===========================================================================
// Progression — the XP level curve
//
// `PUT /api/admin/progression/levels` replaces the entire curve in one call.
// There is no per-level endpoint, so this page edits a local draft and commits
// it whole, which is why it has an explicit Save rather than saving per field.
//
// The curve is CUMULATIVE: `cumulativeXp` is the total XP required to have
// reached that level, not the amount gained during it. That distinction is the
// single easiest thing to get wrong here, so the table shows both — the stored
// cumulative figure and the derived step — and validates that the sequence
// never decreases.
//
// The table is paged, but the draft is not: edits, validation and Save all work
// on the whole curve, and only the rows drawn are sliced. That is why rows are
// addressed by their index in the draft, never by their position on the page.
// ===========================================================================

export function Progression() {
  // Returns `{ levels: [...] }`, not a bare array — useResourceList unwraps either.
  const { data: saved, loading, refreshing, reload } = useResourceList<LevelThresholdDto>(
    '/api/admin/progression/levels',
  )

  const [draft, setDraft] = useState<LevelThresholdDto[]>([])
  const [saving, setSaving] = useState(false)

  // Seed the draft from the server, and re-seed whenever a reload brings new
  // values. Guarded on the saved reference rather than on length: a reload that
  // returns the same number of levels with different XP must still refresh the
  // draft, or Save would write back stale numbers.
  useEffect(() => {
    setDraft(saved.map((row) => ({ ...row })))
  }, [saved])

  const dirty = useMemo(() => {
    if (draft.length !== saved.length) return true
    return draft.some((row, i) => row.level !== saved[i]?.level || row.cumulativeXp !== saved[i]?.cumulativeXp)
  }, [draft, saved])

  // A curve that goes backwards would make a player lose a level by earning XP.
  // The API rejects it; catching it here says which row is at fault.
  const problems = useMemo(() => {
    const found: { index: number; message: string }[] = []

    draft.forEach((row, index) => {
      if (index === 0) return
      const previous = draft[index - 1]

      if (row.level <= previous.level) {
        found.push({ index, message: `Level ${row.level} does not come after ${previous.level}.` })
      }

      if (row.cumulativeXp < previous.cumulativeXp) {
        found.push({
          index,
          message: `Level ${row.level} requires less total XP than level ${previous.level}.`,
        })
      }
    })

    return found
  }, [draft])

  const maxXp = draft.length ? Math.max(...draft.map((r) => r.cumulativeXp), 1) : 1

  const paging = usePaging(draft.length)
  const pageRows = draft.slice(paging.start, paging.end)

  /** The page a draft row is drawn on. A problem may sit on a page the admin is not looking at. */
  function pageOf(index: number) {
    return Math.floor(index / paging.pageSize) + 1
  }

  function patch(index: number, next: Partial<LevelThresholdDto>) {
    setDraft((rows) => rows.map((row, i) => (i === index ? { ...row, ...next } : row)))
  }

  function addLevel() {
    // A new level goes on the end, which on a long curve is a page the admin may
    // not be on. Follow it there, or the click appears to do nothing.
    paging.setPage(pageOf(draft.length))

    setDraft((rows) => {
      const last = rows[rows.length - 1]

      // A sensible next row rather than a blank one: continue the level count and
      // repeat the previous step, which is almost always closer than zero.
      const step = rows.length >= 2 ? last.cumulativeXp - rows[rows.length - 2].cumulativeXp : 100

      return [
        ...rows,
        {
          level: (last?.level ?? 0) + 1,
          cumulativeXp: (last?.cumulativeXp ?? 0) + Math.max(1, step),
        },
      ]
    })
  }

  async function save() {
    setSaving(true)
    try {
      await api.put('/api/admin/progression/levels', { levels: draft })
      toast.success('Level curve replaced', `${draft.length} levels are now in effect.`)
      await reload()
    } finally {
      setSaving(false)
    }
  }

  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Gauge size={22} />}
        title="Progression"
        subtitle="The XP level curve. Thresholds are cumulative — the total XP needed to have reached a level, not the amount earned during it."
        actions={
          <>
            {dirty ? (
              <Button variant="ghost" onClick={() => setDraft(saved.map((r) => ({ ...r })))}>
                <RotateCcw size={15} /> Discard
              </Button>
            ) : null}
            <Button loading={saving} disabled={!dirty || !!problems.length} onClick={save}>
              <Save size={15} /> Replace curve
            </Button>
          </>
        }
      />

      {dirty ? (
        <motion.div variants={listVariants} style={{ marginBottom: '1rem' }}>
          <Note tone={problems.length ? 'danger' : 'warning'}>
            {problems.length ? (
              <>
                <strong>The curve is not valid yet.</strong>
                <ul style={{ margin: '0.4rem 0 0', paddingInlineStart: '1.1rem' }}>
                  {problems.slice(0, 4).map((p, i) => (
                    <li key={i}>
                      {p.message}{' '}
                      {pageOf(p.index) !== paging.page ? (
                        <button
                          type="button"
                          className="s7-link-btn"
                          onClick={() => paging.setPage(pageOf(p.index))}
                        >
                          Show on page {pageOf(p.index)}
                        </button>
                      ) : null}
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <>
                Unsaved changes. <strong>Replace curve</strong> writes all {draft.length} levels at
                once — this endpoint has no per-level update, so whatever is in this table becomes
                the whole curve.
              </>
            )}
          </Note>
        </motion.div>
      ) : null}

      <Card>
        <CardHeader
          icon={<TrendingUp size={16} />}
          title={`${draft.length} level${draft.length === 1 ? '' : 's'}`}
          actions={
            <IconButton label="Reload from server" busy={refreshing} onClick={() => void reload()}>
              <RefreshCw size={15} />
            </IconButton>
          }
        />
        <CardBody>
          {loading ? (
            <p className="s7-hint">Loading curve…</p>
          ) : !draft.length ? (
            <div className="s7-stack">
              <Note>
                No curve is defined, so every player sits at level 1 no matter how much XP they
                earn. Add levels below and replace the curve to switch progression on.
              </Note>
              <Button onClick={addLevel}>
                <Plus size={15} /> Add the first level
              </Button>
            </div>
          ) : (
            <>
              <div className="s7-dt-wrap">
                <table className="s7-dt">
                  <thead>
                    <tr>
                      <th style={{ width: '5rem' }}>Level</th>
                      <th className="s7-num" style={{ width: '11rem' }}>
                        Cumulative XP
                      </th>
                      <th className="s7-num" style={{ width: '9rem' }}>
                        Step
                      </th>
                      <th>Shape</th>
                      <th style={{ width: '3rem' }} />
                    </tr>
                  </thead>
                  <tbody>
                    {pageRows.map((row, offset) => {
                      // The first row of page 2 still steps from the last row of
                      // page 1, so the neighbour comes from the draft, not the page.
                      const index = paging.start + offset
                      const previous = index > 0 ? draft[index - 1] : null
                      const step = previous ? row.cumulativeXp - previous.cumulativeXp : row.cumulativeXp
                      const invalid = previous ? row.cumulativeXp < previous.cumulativeXp : false
                      const outOfOrder = previous ? row.level <= previous.level : false

                      return (
                        <tr key={index}>
                          <td>
                            <Input
                              type="number"
                              min={1}
                              invalid={outOfOrder}
                              value={row.level}
                              onChange={(e) => patch(index, { level: Number(e.target.value) || 1 })}
                              style={{ width: '4.5rem' }}
                            />
                          </td>
                          <td className="s7-num">
                            <Input
                              type="number"
                              min={0}
                              invalid={invalid}
                              value={row.cumulativeXp}
                              onChange={(e) =>
                                patch(index, { cumulativeXp: Number(e.target.value) || 0 })
                              }
                              style={{ width: '10rem', textAlign: 'right' }}
                            />
                          </td>
                          <td className="s7-num">
                            {invalid ? (
                              <Badge tone="danger">{step.toLocaleString()}</Badge>
                            ) : (
                              <span className="s7-muted">+{step.toLocaleString()}</span>
                            )}
                          </td>
                          <td>
                            {/* A bar per row is the whole curve at a glance: a
                                healthy progression is a smooth ramp, and a
                                mistyped digit shows up as a step in the wall. */}
                            <span className="s7-meter" style={{ minWidth: '6rem' }}>
                              <span style={{ width: `${(row.cumulativeXp / maxXp) * 100}%` }} />
                            </span>
                          </td>
                          <td>
                            <button
                              type="button"
                              className="s7-btn s7-btn-ghost s7-btn-icon"
                              aria-label={`Remove level ${row.level}`}
                              onClick={() => setDraft((rows) => rows.filter((_, i) => i !== index))}
                            >
                              <Trash2 size={14} />
                            </button>
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>

              <Pagination
                page={paging.page}
                pageSize={paging.pageSize}
                total={draft.length}
                onPageChange={paging.setPage}
                onPageSizeChange={paging.setPageSize}
                noun="levels"
              />

              <div className="s7-bar" style={{ marginTop: '0.9rem', marginBottom: 0 }}>
                <Button variant="ghost" onClick={addLevel}>
                  <Plus size={15} /> Add level
                </Button>
                <span className="s7-spacer s7-hint">
                  Top level reaches {draft[draft.length - 1]?.cumulativeXp.toLocaleString()} XP
                </span>
              </div>
            </>
          )}
        </CardBody>
      </Card>
    </motion.div>
  )
}
