import { useEffect, useState } from 'react'
import { motion } from 'motion/react'
import { ChevronLeft, ChevronRight, Languages, Search, Sparkles } from 'lucide-react'
import { Badge, Button, Card, CardBody, CardHeader, EmptyState, SkeletonRows } from '../../components/ui/primitives'
import { SearchBox, Segmented } from '../../components/ui/bits'
import { Switch } from '../../components/ui/form'
import { listVariants } from '../../components/ui/motion'
import { useQuestionSearch } from './insight'
import type { QuestionPoolFilter } from '../../types/api'

// ===========================================================================
// Question browser
//
// Every question under a node, flat.
//
// The tree answers "what is under this?", which is the wrong question most of
// the time. What an author wants is "show me every question in this term", or
// "every recovery question in this grade", or "everything that only exists in
// English" — a set that spans lessons and cannot be assembled by clicking
// through them one at a time.
//
// Scope comes from whatever the tree has selected, so the two views stay in
// step without a second node picker to keep in sync with the first.
// ===========================================================================

export function QuestionBrowser({
  scopeLevel,
  scopeId,
  scopeName,
  onOpenLesson,
}: {
  scopeLevel: string | null
  scopeId: string | null
  scopeName: string
  onOpenLesson: (lessonId: string, path: string[]) => void
}) {
  const [pool, setPool] = useState<QuestionPoolFilter>('All')
  const [search, setSearch] = useState('')
  const [onlyUnpaired, setOnlyUnpaired] = useState(false)
  const [page, setPage] = useState(1)

  // Any change to what is being asked invalidates which page you were on. Without this, narrowing a
  // search while on page 7 shows an empty list that looks like "no results".
  useEffect(() => {
    setPage(1)
  }, [scopeId, scopeLevel, pool, search, onlyUnpaired])

  const { result, loading } = useQuestionSearch(
    { scopeLevel, scopeId, pool, search, onlyUnpaired, page },
    true,
  )

  const lastPage = result ? Math.max(1, Math.ceil(result.total / result.pageSize)) : 1

  return (
    <Card>
      <CardHeader
        icon={<Search size={16} />}
        title={scopeId ? `Questions in ${scopeName}` : 'Questions across the curriculum'}
        actions={
          result ? (
            <span className="s7-inline">
              <Badge tone="info">{result.total} questions</Badge>
              <Badge tone="muted">{result.lessonCount} lessons</Badge>
            </span>
          ) : null
        }
      />

      <CardBody>
        <div className="s7-bar">
          <SearchBox
            value={search}
            onChange={setSearch}
            placeholder="Search question text or any answer, either language…"
          />

          <Segmented
            layoutId="browser-pool"
            value={pool}
            onChange={setPool}
            options={[
              { value: 'All', label: 'All' },
              { value: 'Main', label: 'Main' },
              { value: 'Recovery', label: 'Recovery' },
            ]}
          />

          <Switch
            checked={onlyUnpaired}
            onChange={setOnlyUnpaired}
            label="Only one-language"
          />
        </div>

        {loading && !result ? (
          <SkeletonRows rows={5} />
        ) : !result || result.items.length === 0 ? (
          <EmptyState icon={<Sparkles size={20} />}>
            {onlyUnpaired
              ? 'Every question here exists in both languages.'
              : search
                ? 'Nothing matches that.'
                : 'No questions published under this node yet.'}
          </EmptyState>
        ) : (
          <motion.div variants={listVariants} initial="hidden" animate="visible" className="s7-stack">
            {result.items.map((item) => (
              <div
                key={`${item.lessonId}-${item.isRecovery}-${item.rowNumber}`}
                className="s7-panel"
              >
                <div className="s7-bar">
                  <span className="s7-hint" style={{ minWidth: 0 }}>
                    {item.path.join(' › ')}
                  </span>

                  <span className="s7-inline" style={{ marginInlineStart: 'auto' }}>
                    <Badge tone="muted">row {item.rowNumber}</Badge>
                    {item.isRecovery ? <Badge tone="success">recovery</Badge> : null}
                    {item.isUnpaired ? (
                      <Badge tone="warning">
                        <Languages size={12} /> one language
                      </Badge>
                    ) : null}
                    <Button variant="ghost" onClick={() => onOpenLesson(item.lessonId, item.path)}>
                      Edit
                    </Button>
                  </span>
                </div>

                <div className="s7-form-grid-2">
                  <div>
                    <div>{item.questionEn || <span className="s7-muted">— no English —</span>}</div>
                    {item.correctEn ? <span className="s7-muted">✓ {item.correctEn}</span> : null}
                  </div>
                  <div dir="rtl">
                    <div>{item.questionAr || <span className="s7-muted">— لا يوجد نص عربي —</span>}</div>
                    {item.correctAr ? <span className="s7-muted">✓ {item.correctAr}</span> : null}
                  </div>
                </div>
              </div>
            ))}
          </motion.div>
        )}

        {result && result.total > result.pageSize ? (
          <div className="s7-bar" style={{ marginTop: '0.75rem' }}>
            <Button variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              <ChevronLeft size={15} /> Previous
            </Button>
            <span className="s7-muted">
              Page {result.page} of {lastPage}
            </span>
            <Button
              variant="ghost"
              disabled={page >= lastPage}
              onClick={() => setPage((p) => p + 1)}
            >
              Next <ChevronRight size={15} />
            </Button>
          </div>
        ) : null}
      </CardBody>
    </Card>
  )
}
