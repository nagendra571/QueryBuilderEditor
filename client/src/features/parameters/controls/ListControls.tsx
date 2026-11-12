import { AlertCircle, ChevronsUpDown, Loader2, Lock, X } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Command, CommandEmpty, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { decodeMulti, encodeMulti } from '@/features/parameters/param-values'
import { useParameterOptions, type OptionsSource } from '@/hooks/useParameterOptions'
import { ApiError } from '@/lib/api-client'
import { cn } from '@/lib/utils'
import type { ParameterOption } from '@/types'

const NONE = '__none__'

interface ChoiceProps {
  id: string
  label: string
  value: string | null
  onChange: (value: string | null) => void
  options: ParameterOption[]
  multiple: boolean
  disabled?: boolean
  placeholder?: string
}

export function ChoiceControl({ id, label, value, onChange, options, multiple, disabled, placeholder }: ChoiceProps) {
  if (multiple) {
    return (
      <MultiSelect
        id={id}
        label={label}
        options={options}
        selected={decodeMulti(value)}
        disabled={disabled}
        onChange={(values) => onChange(encodeMulti(values))}
      />
    )
  }

  return (
    <Select
      value={value ?? NONE}
      // Radix Select inside a form can report '' while its items aren't mounted; that is never a real choice.
      onValueChange={(v) => {
        if (v === '') return
        onChange(v === NONE ? null : v)
      }}
      disabled={disabled}
    >
      <SelectTrigger id={id} size="sm" aria-label={label} className="h-7 w-48 text-xs">
        <SelectValue placeholder={placeholder ?? 'Select…'} />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={NONE} className="text-xs text-muted-foreground">
          None
        </SelectItem>
        {options.map((o) => (
          <SelectItem key={o.value} value={o.value} className="text-xs">
            {o.label}
          </SelectItem>
        ))}
        {/* A saved value that is no longer offered stays visible rather than silently vanishing. */}
        {value && !options.some((o) => o.value === value) && (
          <SelectItem value={value} className="text-xs">
            {value}
          </SelectItem>
        )}
      </SelectContent>
    </Select>
  )
}

function MultiSelect({
  id,
  label,
  options,
  selected,
  onChange,
  disabled,
}: {
  id: string
  label: string
  options: ParameterOption[]
  selected: string[]
  onChange: (values: string[]) => void
  disabled?: boolean
}) {
  const [open, setOpen] = useState(false)
  const labelFor = (value: string) => options.find((o) => o.value === value)?.label ?? value
  const summary =
    selected.length === 0
      ? 'Select…'
      : selected.length <= 2
        ? selected.map(labelFor).join(', ')
        : `${selected.length} selected`

  function toggle(value: string) {
    onChange(selected.includes(value) ? selected.filter((v) => v !== value) : [...selected, value])
  }

  return (
    <div className="flex items-center gap-1">
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            id={id}
            variant="outline"
            role="combobox"
            aria-expanded={open}
            aria-label={`${label}: ${selected.length === 0 ? 'none selected' : selected.length + ' selected'}`}
            disabled={disabled}
            className={cn('h-7 w-48 justify-between text-xs font-normal', selected.length === 0 && 'text-muted-foreground')}
          >
            <span className="min-w-0 flex-1 truncate text-left">{summary}</span>
            <ChevronsUpDown className="ml-1 size-3.5 shrink-0 opacity-50" />
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-64 p-0" align="start">
          <Command>
            <CommandInput placeholder="Search…" />
            <CommandList>
              <CommandEmpty>Nothing matches.</CommandEmpty>
              {options.map((o) => (
                <CommandItem key={o.value} value={`${o.label} ${o.value}`} onSelect={() => toggle(o.value)} className="gap-2 text-xs">
                  <Checkbox checked={selected.includes(o.value)} tabIndex={-1} aria-hidden className="pointer-events-none" />
                  <span className="min-w-0 flex-1 truncate">{o.label}</span>
                  {o.label !== o.value && <span className="font-numeric text-[11px] text-muted-foreground">{o.value}</span>}
                </CommandItem>
              ))}
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {selected.length > 0 && (
        <Button variant="ghost" size="icon" className="size-6 shrink-0" aria-label={`Clear ${label}`} onClick={() => onChange([])}>
          <X className="size-3" />
        </Button>
      )}
    </div>
  )
}

/** A dropdown whose choices the server computes for the signed-in user from another saved query. */
export function QueryChoiceControl({
  source,
  ...props
}: Omit<ChoiceProps, 'options' | 'disabled' | 'placeholder'> & { source: OptionsSource }) {
  const { data, isLoading, error } = useParameterOptions(source)

  if (isLoading) {
    return (
      <span className="inline-flex h-7 items-center gap-1.5 text-xs text-muted-foreground" role="status">
        <Loader2 className="size-3.5 animate-spin" /> Loading choices…
      </span>
    )
  }

  if (error) {
    const forbidden = error instanceof ApiError && error.status === 403
    return (
      <span className="inline-flex min-h-7 items-center gap-1.5 text-xs text-destructive" role="alert">
        {forbidden ? <Lock className="size-3.5 shrink-0" /> : <AlertCircle className="size-3.5 shrink-0" />}
        {forbidden
          ? 'You do not have access to the query that supplies these choices.'
          : error instanceof ApiError
            ? error.message
            : 'Could not load the choices.'}
      </span>
    )
  }

  return <ChoiceControl {...props} options={data ?? []} />
}
