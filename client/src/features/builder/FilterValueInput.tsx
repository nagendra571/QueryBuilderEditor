import { format, parseISO } from 'date-fns'
import { CalendarIcon } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Calendar } from '@/components/ui/calendar'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { operatorNeedsTwoValues, operatorNeedsValue } from '@/features/builder/filter-operators'
import { cn } from '@/lib/utils'
import type { ColumnDataType, FilterOperator } from '@/types'

interface Props {
  dataType: ColumnDataType
  operator: FilterOperator
  value: string | null | undefined
  onChange: (value: string) => void
}

export function FilterValueInput({ dataType, operator, value, onChange }: Props) {
  if (!operatorNeedsValue(operator)) return null

  if (operatorNeedsTwoValues(operator)) {
    const [lo, hi] = parseArray(value)
    return (
      <div className="flex items-center gap-1.5">
        <SingleValueInput dataType={dataType} value={lo} onChange={(v) => onChange(JSON.stringify([v, hi]))} />
        <span className="text-xs text-muted-foreground">and</span>
        <SingleValueInput dataType={dataType} value={hi} onChange={(v) => onChange(JSON.stringify([lo, v]))} />
      </div>
    )
  }

  return <SingleValueInput dataType={dataType} value={value ?? ''} onChange={onChange} />
}

function parseArray(value: string | null | undefined): [string, string] {
  if (!value) return ['', '']
  try {
    const parsed = JSON.parse(value)
    return [parsed[0] ?? '', parsed[1] ?? '']
  } catch {
    return ['', '']
  }
}

function SingleValueInput({
  dataType,
  value,
  onChange,
}: {
  dataType: ColumnDataType
  value: string
  onChange: (value: string) => void
}) {
  if (dataType === 'date' || dataType === 'dateTime') {
    const selected = value ? parseISO(value) : undefined
    return (
      <Popover>
        <PopoverTrigger asChild>
          <Button
            variant="outline"
            className={cn('h-7 w-36 justify-start text-xs font-normal', !value && 'text-muted-foreground')}
          >
            <CalendarIcon className="mr-1.5 size-3.5" />
            {selected ? format(selected, 'MMM d, yyyy') : 'Pick a date'}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-auto p-0" align="start">
          <Calendar
            mode="single"
            selected={selected}
            onSelect={(date) => date && onChange(format(date, 'yyyy-MM-dd'))}
            autoFocus
          />
        </PopoverContent>
      </Popover>
    )
  }

  if (dataType === 'number') {
    return (
      <Input
        type="number"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-7 w-28 text-xs tabular-nums"
        placeholder="0"
      />
    )
  }

  return <Input value={value} onChange={(e) => onChange(e.target.value)} className="h-7 flex-1 text-xs" placeholder="Value" />
}
