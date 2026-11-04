import { format, parseISO } from 'date-fns'
import { CalendarIcon, CalendarClock, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Calendar } from '@/components/ui/calendar'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import {
  decodeRange,
  encodeRange,
  relativeLabel,
  RELATIVE_RANGES,
  RELATIVE_SINGLES,
  RELATIVE_SINGLES_WITH_TIME,
} from '@/features/parameters/param-values'
import { cn } from '@/lib/utils'

interface ControlProps {
  id: string
  label: string
  value: string | null
  onChange: (value: string | null) => void
}

function isoDate(value: string | null | undefined) {
  if (!value || !/^\d{4}-\d{2}-\d{2}/.test(value)) return undefined
  return parseISO(value.slice(0, 10))
}

function RelativeMenu({
  label,
  tokens,
  onPick,
}: {
  label: string
  tokens: { value: string; label: string }[]
  onPick: (token: string) => void
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="icon" className="size-7 shrink-0" aria-label={`${label}: relative date`} title="Relative date">
          <CalendarClock className="size-3.5" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="max-h-72 overflow-y-auto">
        {tokens.map((t) => (
          <DropdownMenuItem key={t.value} onSelect={() => onPick(t.value)} className="text-xs">
            {t.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function DatePickerButton({
  id,
  label,
  value,
  placeholder,
  onChange,
  className,
}: {
  id?: string
  label: string
  value: string | null
  placeholder: string
  onChange: (value: string | null) => void
  className?: string
}) {
  const token = relativeLabel(value)
  const selected = isoDate(value)
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          id={id}
          variant="outline"
          aria-label={label}
          className={cn('h-7 w-36 justify-start text-xs font-normal', !value && 'text-muted-foreground', className)}
        >
          <CalendarIcon className="mr-1.5 size-3.5 shrink-0" />
          <span className="truncate">{token ?? (selected ? format(selected, 'MMM d, yyyy') : placeholder)}</span>
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align="start">
        <Calendar mode="single" selected={selected} onSelect={(date) => onChange(date ? format(date, 'yyyy-MM-dd') : null)} autoFocus />
      </PopoverContent>
    </Popover>
  )
}

export function DateControl({ id, label, value, onChange }: ControlProps) {
  return (
    <div className="flex items-center gap-1.5">
      <DatePickerButton id={id} label={label} value={value} placeholder="Pick a date" onChange={onChange} />
      <RelativeMenu label={label} tokens={RELATIVE_SINGLES} onPick={onChange} />
      {value && <ClearButton label={label} onClear={() => onChange(null)} />}
    </div>
  )
}

export function DateTimeControl({ id, label, value, onChange }: ControlProps) {
  const token = relativeLabel(value)
  return (
    <div className="flex items-center gap-1.5">
      {token ? (
        <span id={id} className="inline-flex h-7 items-center rounded-md bg-query/10 px-2 text-xs font-medium text-query">
          {token}
        </span>
      ) : (
        <Input
          id={id}
          type="datetime-local"
          aria-label={label}
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value || null)}
          className="h-7 w-48 text-xs"
        />
      )}
      <RelativeMenu label={label} tokens={RELATIVE_SINGLES_WITH_TIME} onPick={onChange} />
      {value && <ClearButton label={label} onClear={() => onChange(null)} />}
    </div>
  )
}

export function DateRangeControl({ id, label, value, onChange, withTime }: ControlProps & { withTime: boolean }) {
  const { start, end, token } = decodeRange(value)
  const tokenLabel = token ? relativeLabel(token) : null

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {tokenLabel ? (
        <span id={id} className="inline-flex h-7 items-center rounded-md bg-query/10 px-2 text-xs font-medium text-query">
          {tokenLabel}
        </span>
      ) : withTime ? (
        <>
          <Input
            id={id}
            type="datetime-local"
            aria-label={`${label}: start`}
            value={start}
            onChange={(e) => onChange(encodeRange(e.target.value, end))}
            className="h-7 w-44 text-xs"
          />
          <span className="text-xs text-muted-foreground">to</span>
          <Input
            type="datetime-local"
            aria-label={`${label}: end`}
            value={end}
            onChange={(e) => onChange(encodeRange(start, e.target.value))}
            className="h-7 w-44 text-xs"
          />
        </>
      ) : (
        <>
          <DatePickerButton
            id={id}
            label={`${label}: start`}
            value={start || null}
            placeholder="Start date"
            onChange={(v) => onChange(encodeRange(v ?? '', end))}
          />
          <span className="text-xs text-muted-foreground">to</span>
          <DatePickerButton
            label={`${label}: end`}
            value={end || null}
            placeholder="End date"
            onChange={(v) => onChange(encodeRange(start, v ?? ''))}
          />
        </>
      )}
      <RelativeMenu label={label} tokens={RELATIVE_RANGES} onPick={onChange} />
      {value && <ClearButton label={label} onClear={() => onChange(null)} />}
    </div>
  )
}

function ClearButton({ label, onClear }: { label: string; onClear: () => void }) {
  return (
    <Button variant="ghost" size="icon" className="size-6 shrink-0" aria-label={`Clear ${label}`} onClick={onClear}>
      <X className="size-3" />
    </Button>
  )
}
