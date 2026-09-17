import { useState } from 'react'
import { Filter, Play } from 'lucide-react'
import { Button, Card, CardBody, CardHeader } from '../../components/ui/primitives'
import { Field, Input, Select } from '../../components/ui/form'
import { percent, useFunnel } from './data'
import type { DayRange } from './data'
import type { EventCatalogueRowDto } from '../../types/api'

// ===========================================================================
// Funnel
//
// Ordered steps, counted PER USER and IN ORDER within a window of their first
// step. Both of those qualifiers are load-bearing:
//
//   - Per user, because a funnel counted on occurrences lets one child who
//     opened the shop forty times look like forty children, and conversion
//     rates then exceed 100%.
//   - In order, because an unordered funnel is just an intersection of
//     populations — it counts a child who bought yesterday and browsed today as
//     a conversion, and always looks healthier than the journey really is.
//
// This is the one read in the console that opens raw event rows, so it runs on
// a button rather than on every keystroke.
// ===========================================================================

const PRESETS: { label: string; steps: string[] }[] = [
  {
    label: 'Onboarding',
    steps: ['app_launch', 'onboarding_step', 'onboarding_complete', 'session_start'],
  },
  {
    label: 'Shop conversion',
    steps: ['shop_viewed', 'offer_viewed', 'purchase_started', 'purchase_succeeded'],
  },
  {
    label: 'Lesson completion',
    steps: ['lesson_started', 'question_answered', 'lesson_completed'],
  },
  {
    label: 'Matchmaking',
    steps: ['matchmaking_started', 'matchmaking_matched', 'match_finished'],
  },
]

export function FunnelPanel({
  range,
  catalogue,
}: {
  range: DayRange
  catalogue: EventCatalogueRowDto[]
}) {
  const { report, loading, run } = useFunnel(range)

  const [steps, setSteps] = useState<string[]>(PRESETS[1].steps)
  const [windowHours, setWindowHours] = useState(24)

  const setStep = (index: number, value: string) =>
    setSteps((current) => current.map((step, i) => (i === index ? value : step)))

  const addStep = () => setSteps((current) => [...current, ''])

  const removeStep = (index: number) =>
    setSteps((current) => current.filter((_, i) => i !== index))

  const usable = steps.filter((s) => s.trim().length > 0)

  return (
    <Card>
      <CardHeader
        icon={<Filter size={16} />}
        title="Funnel"
        actions={
          <Button
            onClick={() => void run(usable, windowHours)}
            loading={loading}
            disabled={usable.length < 2}
          >
            <Play size={14} /> Run
          </Button>
        }
      />

      <CardBody>
        <div className="s7-funnel-presets">
          {PRESETS.map((preset) => (
            <button
              key={preset.label}
              type="button"
              className="s7-chip"
              onClick={() => setSteps(preset.steps)}
            >
              {preset.label}
            </button>
          ))}
        </div>

        <div className="s7-funnel-steps">
          {steps.map((step, index) => (
            <Field key={index} label={index === 0 ? 'Entry step' : `Step ${index + 1}`}>
              <div className="s7-funnel-step">
                <Select value={step} onChange={(e) => setStep(index, e.target.value)}>
                  <option value="">— choose an event —</option>
                  {catalogue.map((row) => (
                    <option key={row.name} value={row.name}>
                      {row.name}
                    </option>
                  ))}
                </Select>

                {steps.length > 2 ? (
                  <button
                    type="button"
                    className="s7-chip s7-chip-clear"
                    onClick={() => removeStep(index)}
                    aria-label={`Remove step ${index + 1}`}
                  >
                    ×
                  </button>
                ) : null}
              </div>
            </Field>
          ))}
        </div>

        <div className="s7-funnel-controls">
          <Button variant="ghost" onClick={addStep} disabled={steps.length >= 8}>
            Add step
          </Button>

          <Field
            label="Window (hours)"
            hint="How long a user has to complete the whole journey after their first step."
          >
            <Input
              type="number"
              min={1}
              max={720}
              value={windowHours}
              onChange={(e) => setWindowHours(Number(e.target.value) || 24)}
            />
          </Field>
        </div>

        {report ? (
          <div className="s7-funnel-result">
            {report.steps.map((step) => (
              <div key={step.index} className="s7-funnel-bar-row">
                <div className="s7-funnel-bar-label">
                  <strong>{step.name}</strong>
                  <span>{step.users.toLocaleString()} users</span>
                </div>

                <div className="s7-funnel-bar-track">
                  <div
                    className="s7-funnel-bar-fill"
                    style={{ width: `${Math.max(1, step.conversionFromStart * 100)}%` }}
                  />
                </div>

                <div className="s7-funnel-bar-figures">
                  <span title="Share of everyone who reached the entry step">
                    {percent(step.conversionFromStart)}
                  </span>

                  {/* The step-over-step number is the one that shows WHERE the
                      drop is; conversion-from-start only shows that there was
                      one. Both are printed because product people ask for the
                      first and engineers need the second. */}
                  {step.index > 0 ? (
                    <em title="Share of the previous step — where the drop actually happens">
                      {percent(step.conversionFromPrevious)}
                    </em>
                  ) : null}
                </div>
              </div>
            ))}

            <p className="s7-funnel-note">
              Counted per user, in order, within {report.windowHours}h of the entry step.
            </p>
          </div>
        ) : null}
      </CardBody>
    </Card>
  )
}
