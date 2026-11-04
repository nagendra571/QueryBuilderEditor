import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { DateControl, DateRangeControl, DateTimeControl } from '@/features/parameters/controls/DateControls'
import { ChoiceControl, QueryChoiceControl } from '@/features/parameters/controls/ListControls'
import { effectiveType, parameterTitle } from '@/features/parameters/param-values'
import type { OptionsSource } from '@/hooks/useParameterOptions'
import type { QueryParameter } from '@/types'

/**
 * One input for one parameter. The caller renders the visible <Label htmlFor={id}> — every control
 * puts `id` on its first focusable element and also carries an aria-label, so it is operable and
 * announced by keyboard and screen reader alike.
 */
export function ParameterControl({
  parameter,
  id,
  value,
  onChange,
  optionsSource,
}: {
  parameter: QueryParameter
  id: string
  value: string | null
  onChange: (value: string | null) => void
  /** Where a query-list control loads its choices from. */
  optionsSource: OptionsSource | null
}) {
  const type = effectiveType(parameter)
  const label = parameterTitle(parameter)

  switch (type) {
    case 'number':
      return (
        <Input
          id={id}
          type="number"
          aria-label={label}
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
          className="h-7 w-32 text-xs tabular-nums"
          placeholder="0"
        />
      )
    case 'date':
      return <DateControl id={id} label={label} value={value} onChange={onChange} />
    case 'dateTime':
      return <DateTimeControl id={id} label={label} value={value} onChange={onChange} />
    case 'dateRange':
      return <DateRangeControl id={id} label={label} value={value} onChange={onChange} withTime={false} />
    case 'dateTimeRange':
      return <DateRangeControl id={id} label={label} value={value} onChange={onChange} withTime />
    case 'list':
      return (
        <ChoiceControl
          id={id}
          label={label}
          value={value}
          onChange={onChange}
          options={(parameter.options ?? []).map((o) => ({ label: o, value: o }))}
          multiple={!!parameter.allowMultiple}
        />
      )
    case 'queryList':
      return optionsSource ? (
        <QueryChoiceControl
          id={id}
          label={label}
          value={value}
          onChange={onChange}
          multiple={!!parameter.allowMultiple}
          source={optionsSource}
        />
      ) : (
        <span className="text-xs text-muted-foreground">Save the query to load these choices.</span>
      )
    case 'boolean':
      return (
        <Select value={value ?? '__none__'} onValueChange={(v) => v !== '' && onChange(v === '__none__' ? null : v)}>
          <SelectTrigger id={id} size="sm" aria-label={label} className="h-7 w-32 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="__none__" className="text-xs text-muted-foreground">
              None
            </SelectItem>
            <SelectItem value="true" className="text-xs">
              True
            </SelectItem>
            <SelectItem value="false" className="text-xs">
              False
            </SelectItem>
          </SelectContent>
        </Select>
      )
    default:
      return (
        <Input
          id={id}
          aria-label={label}
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value === '' ? null : e.target.value)}
          className="h-7 w-48 text-xs"
          placeholder="Value"
        />
      )
  }
}
