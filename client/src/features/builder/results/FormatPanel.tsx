import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useId, useState, useSyncExternalStore } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { cn } from '@/lib/utils'
import type { ColumnDataType, QueryResultColumnDto } from '@/types'
import {
  MAX_RULES,
  defaultDateFormat,
  defaultNumberFormat,
  formatDate,
  formatNumber,
  isFormatted,
  rulesFor,
  type ColumnFormat,
  type DateFormat,
  type DatePattern,
  type HighlightRule,
  type NumberFormat,
  type NumberStyle,
  type ScaleKind,
} from './grid-format.ts'
import { defaultTotal, totalChoices, type TotalFn } from './grid-totals.ts'
import { RuleEditor } from './RuleEditor'
import type { SelectionStore } from './selection-store.ts'
import { TypeBadge } from './TypeBadge'
import type { GridTotals } from './grid-view.ts'

const NUMBER_STYLES: { value: NumberStyle; label: string }[] = [
  { value: 'auto', label: 'As stored' },
  { value: 'number', label: 'Number' },
  { value: 'compact', label: 'Compact (1.2K)' },
  { value: 'currency', label: 'Currency' },
  { value: 'percent', label: 'Percent' },
]
const DECIMALS: (number | null)[] = [null, 0, 1, 2, 3]
const DATE_PATTERNS: { value: DatePattern; label: string }[] = [
  { value: 'auto', label: 'As stored' },
  { value: 'weekday', label: 'Day, date' },
  { value: 'medium', label: 'Day month year' },
  { value: 'short', label: 'Short, your locale' },
  { value: 'iso', label: 'Year-month-day' },
  { value: 'monthYear', label: 'Month year' },
  { value: 'year', label: 'Year' },
]
const TOTAL_NAME: Record<TotalFn, string> = {
  sum: 'Sum',
  avg: 'Average',
  count: 'Count',
  distinct: 'Distinct',
  min: 'Min',
  max: 'Max',
  earliest: 'Earliest',
  latest: 'Latest',
  none: 'None',
}
const SCALES: { value: ScaleKind; label: string; gradient: string }[] = [
  { value: 'blue', label: 'White to blue', gradient: 'linear-gradient(90deg, var(--background), var(--primary))' },
  {
    value: 'redGreen',
    label: 'Red to green',
    gradient: 'linear-gradient(90deg, var(--destructive), var(--background), var(--success))',
  },
]

let ruleCounter = 0
const newRuleId = () => `rule-${++ruleCounter}`

const isBlankSample = (v: unknown) => v === null || v === undefined || v === ''

function Section({ title, onReset, resetDisabled, children }: { title: string; onReset?: () => void; resetDisabled?: boolean; children: React.ReactNode }) {
  return (
    <section className="space-y-2 border-b border-border pb-3">
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{title}</h3>
        {onReset && (
          <button
            type="button"
            disabled={resetDisabled}
            className="text-xs text-primary underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-40 disabled:no-underline"
            onClick={onReset}
          >
            Reset
          </button>
        )}
      </div>
      {children}
    </section>
  )
}

function SwitchRow({ label, checked, onChange }: { label: string; checked: boolean; onChange: (on: boolean) => void }) {
  const id = useId()
  return (
    <div className="flex items-center gap-2 text-xs">
      <Switch id={id} size="sm" checked={checked} onCheckedChange={onChange} />
      <label htmlFor={id} className="min-w-0 flex-1">
        {label}
      </label>
    </div>
  )
}

function Segmented({ label, options, value, onChange }: { label: string; options: { value: number | null; text: string }[]; value: number | null; onChange: (v: number | null) => void }) {
  return (
    <div className="flex items-center justify-between gap-2 text-xs">
      <span id={`${label}-seg`}>{label}</span>
      <div role="group" aria-labelledby={`${label}-seg`} className="flex">
        {options.map((o) => (
          <button
            key={String(o.value)}
            type="button"
            aria-pressed={value === o.value}
            className={cn(
              'h-7 min-w-8 border border-border px-2 -ml-px first:ml-0 focus-visible:z-10 focus-visible:outline-2 focus-visible:outline-ring',
              value === o.value ? 'bg-primary text-primary-foreground' : 'hover:bg-muted',
            )}
            onClick={() => onChange(o.value)}
          >
            {o.text}
          </button>
        ))}
      </div>
    </div>
  )
}

function numberSample(sample: unknown): number | null {
  return typeof sample === 'number' && Number.isFinite(sample) ? sample : null
}

export function FormatPanel({
  column,
  format,
  onChange,
  totals,
  onTotalChange,
  sample,
  onPrev,
  onNext,
}: {
  column: QueryResultColumnDto
  format: ColumnFormat | undefined
  onChange: (next: ColumnFormat | null) => void
  totals: GridTotals
  onTotalChange: (fn: TotalFn) => void
  /** The active cell's value, or the first non-empty value in the column. */
  sample: unknown
  onPrev?: () => void
  onNext?: () => void
}) {
  const type: ColumnDataType = column.dataType
  const isNumber = type === 'number'
  const isDate = type === 'date' || type === 'dateTime'
  const hasRules = rulesFor(type).length > 0
  const current: ColumnFormat = format ?? { rules: [] }
  const update = (patch: Partial<ColumnFormat>) => {
    const next = { ...current, ...patch }
    onChange(isFormatted(next) ? next : null)
  }
  const nf: NumberFormat = current.number ?? defaultNumberFormat
  const df: DateFormat = current.date ?? defaultDateFormat
  const setNumber = (patch: Partial<NumberFormat>) => update({ number: { ...nf, ...patch } })
  const setDate = (patch: Partial<DateFormat>) => update({ date: { ...df, ...patch } })

  // The box holds what is being typed; anything that changes the applied code from outside (Reset, Clear, another
  // column, choosing Currency again) puts it back in step, which also clears a stale invalid-code error.
  const syncKey = `${column.name}|${nf.currency}|${nf.style === 'currency'}`
  const [currency, setCurrency] = useState({ key: syncKey, draft: nf.currency })
  if (currency.key !== syncKey) setCurrency({ key: syncKey, draft: nf.currency })
  const currencyDraft = currency.key === syncKey ? currency.draft : nf.currency
  const setCurrencyDraft = (draft: string) => setCurrency({ key: syncKey, draft })
  const currencyBad = !/^[A-Za-z]{3}$/.test(currencyDraft.trim())

  const n = numberSample(sample)
  const looksLike = n === null ? null : formatNumber(n, nf)
  const dateType = type === 'dateTime' ? 'dateTime' : 'date'
  const dateText = (pattern: DatePattern, showTime = df.showTime) =>
    isBlankSample(sample) ? null : formatDate(sample, dateType, { pattern, showTime })

  const rules = current.rules
  const setRules = (next: HighlightRule[]) => update({ rules: next })
  const addRule = () =>
    setRules([...rules, { id: newRuleId(), op: rulesFor(type)[0], value: '', value2: '', color: 'red' }])

  const choices = totalChoices(column)
  const fn = totals.fns[column.name] ?? defaultTotal(column)
  const totalSection = (
    <Section title="Total" onReset={() => onTotalChange(defaultTotal(column))} resetDisabled={fn === defaultTotal(column)}>
      {totals.on ? (
        <Select value={fn} onValueChange={(v) => v && onTotalChange(v as TotalFn)}>
          <SelectTrigger size="sm" aria-label={`Total for ${column.name}`} className="w-full text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {choices.map((c) => (
              <SelectItem key={c} value={c}>
                {TOTAL_NAME[c]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      ) : (
        <p className="text-xs text-muted-foreground">Turn on Σ Totals to show totals</p>
      )}
    </Section>
  )

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex shrink-0 items-center gap-1.5 border-b border-border px-3 py-2">
        <TypeBadge dataType={type} />
        <span className="min-w-0 truncate text-sm font-semibold">{column.name}</span>
        <span className="ml-auto flex shrink-0">
          <Button variant="ghost" size="icon" className="size-7" aria-label="Previous column" disabled={!onPrev} onClick={onPrev}>
            <ChevronLeft className="size-4" />
          </Button>
          <Button variant="ghost" size="icon" className="size-7" aria-label="Next column" disabled={!onNext} onClick={onNext}>
            <ChevronRight className="size-4" />
          </Button>
        </span>
      </div>

      <div className="min-h-0 flex-1 space-y-3 overflow-y-auto px-3 py-3">
        {type === 'boolean' && <p className="text-xs text-muted-foreground">Nothing to format for Yes/No columns</p>}

        {isNumber && (
          <Section title="Number format" onReset={() => update({ number: undefined })} resetDisabled={!current.number}>
            <div className="flex items-center gap-2">
              <Select value={nf.style} onValueChange={(v) => v && setNumber({ style: v as NumberStyle })}>
                <SelectTrigger size="sm" aria-label="Style" className="min-w-0 flex-1 text-xs">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {NUMBER_STYLES.map((s) => (
                    <SelectItem key={s.value} value={s.value}>
                      {s.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {nf.style === 'currency' && (
                <Input
                  aria-label="Currency code"
                  aria-invalid={currencyBad}
                  maxLength={3}
                  value={currencyDraft}
                  className="h-7 w-16 px-2 text-xs uppercase"
                  onChange={(e) => {
                    const v = e.target.value
                    setCurrencyDraft(v)
                    if (/^[A-Za-z]{3}$/.test(v.trim())) setNumber({ currency: v.trim().toUpperCase() })
                  }}
                />
              )}
            </div>
            {nf.style === 'currency' && currencyBad && (
              <p role="alert" className="text-xs text-destructive">
                Use a three-letter code, like USD
              </p>
            )}
            {nf.style !== 'auto' && (
              <>
                <Segmented
                  label="Decimals"
                  options={DECIMALS.map((d) => ({ value: d, text: d === null ? 'Auto' : String(d) }))}
                  value={nf.decimals}
                  onChange={(decimals) => setNumber({ decimals })}
                />
                <SwitchRow label="Thousands separator" checked={nf.thousands} onChange={(thousands) => setNumber({ thousands })} />
                {nf.style === 'percent' && (
                  <SwitchRow
                    label="Values are fractions (0.42 = 42%)"
                    checked={nf.fraction}
                    onChange={(fraction) => setNumber({ fraction })}
                  />
                )}
              </>
            )}
            {looksLike !== null && (
              <p className="text-xs text-muted-foreground">
                Looks like <b className="font-numeric font-semibold text-foreground">{looksLike}</b>
              </p>
            )}
          </Section>
        )}

        {isDate && (
          <Section title="Date format" onReset={() => update({ date: undefined })} resetDisabled={!current.date}>
            <Select value={df.pattern} onValueChange={(v) => v && setDate({ pattern: v as DatePattern })}>
              <SelectTrigger size="sm" aria-label="Date format" className="w-full text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DATE_PATTERNS.map((p) => (
                  <SelectItem key={p.value} value={p.value}>
                    {(p.value !== 'auto' && dateText(p.value, false)) || p.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {type === 'dateTime' && df.pattern !== 'auto' && (
              <SwitchRow label="Show time" checked={df.showTime} onChange={(showTime) => setDate({ showTime })} />
            )}
          </Section>
        )}

        {isNumber && (
          <Section
            title="Bars and colour"
            onReset={() => update({ bars: false, scale: null })}
            resetDisabled={!current.bars && !current.scale}
          >
            <SwitchRow label="Bars in cells" checked={!!current.bars} onChange={(bars) => update({ bars })} />
            <SwitchRow
              label="Colour scale"
              checked={!!current.scale}
              onChange={(on) => update({ scale: on ? 'blue' : null })}
            />
            <div role="group" aria-label="Colour scale" className={cn('flex gap-2', !current.scale && 'opacity-50')}>
              {SCALES.map((s) => (
                <button
                  key={s.value}
                  type="button"
                  disabled={!current.scale}
                  aria-label={s.label}
                  aria-pressed={current.scale === s.value}
                  className={cn(
                    'h-5 flex-1 border focus-visible:outline-2 focus-visible:outline-ring',
                    current.scale === s.value ? 'border-primary ring-1 ring-primary' : 'border-border',
                  )}
                  style={{ background: s.gradient }}
                  onClick={() => update({ scale: s.value })}
                />
              ))}
            </div>
          </Section>
        )}

        {hasRules && (
          <Section title="Highlight rules" onReset={() => setRules([])} resetDisabled={rules.length === 0}>
            <p className="text-xs text-muted-foreground">First match wins</p>
            {rules.map((rule, i) => (
              <RuleEditor
                key={rule.id}
                rule={rule}
                position={i + 1}
                dataType={type}
                numberFormat={current.number}
                onChange={(next) => setRules(rules.map((r) => (r.id === rule.id ? next : r)))}
                onRemove={() => setRules(rules.filter((r) => r.id !== rule.id))}
              />
            ))}
            <Button
              variant="outline"
              size="sm"
              className="w-full"
              disabled={rules.length >= MAX_RULES}
              onClick={addRule}
            >
              + Add rule
            </Button>
            {rules.length >= MAX_RULES && <p className="text-xs text-muted-foreground">Up to {MAX_RULES} rules per column</p>}
          </Section>
        )}

        {totalSection}

        <Button variant="outline" size="sm" className="w-full" disabled={!format} onClick={() => onChange(null)}>
          Clear formatting for this column
        </Button>
      </div>
    </div>
  )
}

const HINT = <p className="p-3 text-xs text-muted-foreground">Select a cell to format its column.</p>

/** Follows the grid's active column, like the Profile tab. */
export function FormatTab({
  store,
  columns,
  rows,
  formats,
  totals,
  onChange,
  onTotalChange,
  onMove,
}: {
  store: SelectionStore
  /** The result's columns: the store can hold the previous run's for a frame. */
  columns: QueryResultColumnDto[]
  rows: Record<string, unknown>[]
  formats: Record<string, ColumnFormat>
  totals: GridTotals
  onChange: (column: string, next: ColumnFormat | null) => void
  onTotalChange: (column: string, fn: TotalFn) => void
  onMove: (pos: { row: number; col: number }) => void
}) {
  const snap = useSyncExternalStore(store.subscribe, store.get)
  const { range } = snap
  const column = range ? snap.columns[range.focus.col] : undefined
  if (!range || !column || !columns.includes(column)) return HINT
  const cell = snap.rows[range.focus.row]?.[column.name]
  // A number column can hold text (very long numerics), which has no number to preview.
  const usable = column.dataType === 'number' ? (v: unknown) => typeof v === 'number' && Number.isFinite(v) : (v: unknown) => !isBlankSample(v)
  const sample = usable(cell) ? cell : rows.find((r) => usable(r[column.name]))?.[column.name]
  const { row, col } = range.focus
  return (
    <FormatPanel
      column={column}
      format={formats[column.name]}
      onChange={(next) => onChange(column.name, next)}
      totals={totals}
      onTotalChange={(fn) => onTotalChange(column.name, fn)}
      sample={sample}
      onPrev={col > 0 ? () => onMove({ row, col: col - 1 }) : undefined}
      onNext={col < snap.columns.length - 1 ? () => onMove({ row, col: col + 1 }) : undefined}
    />
  )
}
