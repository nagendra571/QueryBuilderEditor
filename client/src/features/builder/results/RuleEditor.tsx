import { X } from 'lucide-react'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { ColumnDataType } from '@/types'
import { formatNumber, isComplete, rulesFor, type HighlightRule, type NumberFormat, type RuleColor, type RuleOp } from './grid-format.ts'

const OP_LABEL: Record<RuleOp, string> = {
  gt: 'is greater than',
  lt: 'is less than',
  between: 'is between',
  eq: 'equals',
  is: 'is',
  contains: 'contains',
  empty: 'is empty',
  before: 'is before',
  after: 'is after',
}

const COLORS: { value: RuleColor; label: string; token: string; pct: number }[] = [
  { value: 'red', label: 'Red', token: '--destructive', pct: 20 },
  { value: 'amber', label: 'Amber', token: '--warning', pct: 30 },
  { value: 'green', label: 'Green', token: '--success', pct: 20 },
  { value: 'blue', label: 'Blue', token: '--primary', pct: 20 },
]

// The grid mixes each tint into the cell, so the swatch uses the same mix to show what the cell will look like.
function Swatch({ token, pct }: { token: string; pct: number }) {
  return (
    <span
      aria-hidden
      className="size-4 shrink-0 rounded-[2px] border border-border"
      style={{ background: `color-mix(in oklab, var(${token}) ${pct}%, var(--background))` }}
    />
  )
}

export function RuleEditor({
  rule,
  dataType,
  numberFormat,
  onChange,
  onRemove,
}: {
  rule: HighlightRule
  dataType: ColumnDataType
  numberFormat: NumberFormat | undefined
  onChange: (next: HighlightRule) => void
  onRemove: () => void
}) {
  const isNumber = dataType === 'number'
  const isDate = dataType === 'date' || dataType === 'dateTime'
  const ops = rulesFor(dataType)
  const inputProps = isDate
    ? { type: 'date' as const }
    : isNumber
      ? { type: 'text' as const, inputMode: 'decimal' as const, className: 'font-numeric' }
      : { type: 'text' as const }
  const field = (value: string, set: (v: string) => void, label: string) => {
    const n = Number(value.trim())
    const shown = isNumber && numberFormat?.fraction && value.trim() !== '' && Number.isFinite(n)
    return (
      <span className="flex min-w-0 items-center gap-1.5">
        <Input
          {...inputProps}
          aria-label={label}
          value={value}
          onChange={(e) => set(e.target.value)}
          className={`h-7 min-w-0 flex-1 px-2 text-xs ${inputProps.className ?? ''}`}
        />
        {shown && <span className="font-numeric shrink-0 text-xs text-muted-foreground">= {formatNumber(n, numberFormat)}</span>}
      </span>
    )
  }

  return (
    <div className="space-y-1.5 border border-border p-2">
      <div className="flex items-center gap-1.5">
        <Select value={rule.op} onValueChange={(op) => op && onChange({ ...rule, op: op as RuleOp })}>
          <SelectTrigger size="sm" aria-label="Condition" className="min-w-0 flex-1 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {ops.map((op) => (
              <SelectItem key={op} value={op}>
                {OP_LABEL[op]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={rule.color} onValueChange={(color) => color && onChange({ ...rule, color: color as RuleColor })}>
          <SelectTrigger size="sm" aria-label="Colour" className="w-28 shrink-0 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {COLORS.map((c) => (
              <SelectItem key={c.value} value={c.value}>
                <Swatch token={c.token} pct={c.pct} />
                {c.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <button
          type="button"
          aria-label="Remove rule"
          className="grid size-7 shrink-0 place-items-center text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring"
          onClick={onRemove}
        >
          <X className="size-3.5" />
        </button>
      </div>
      {rule.op !== 'empty' && field(rule.value, (value) => onChange({ ...rule, value }), rule.op === 'between' ? 'From' : 'Value')}
      {rule.op === 'between' && field(rule.value2, (value2) => onChange({ ...rule, value2 }), 'To')}
      {!isComplete(rule) && <p className="text-[11px] text-muted-foreground">Incomplete</p>}
    </div>
  )
}
