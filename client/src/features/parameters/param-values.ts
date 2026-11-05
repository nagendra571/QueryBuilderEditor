import type { ColumnDataType, FilterCondition, FilterGroup, FilterOperator, ParameterType, QueryDefinition, QueryParameter } from '@/types'

/** Wire format of parameter values, shared with the server (see ParameterBinder). */
export type ParameterValues = Record<string, string | null>

export const RELATIVE_RANGES: { value: string; label: string }[] = [
  { value: 'today', label: 'Today' },
  { value: 'yesterday', label: 'Yesterday' },
  { value: 'this_week', label: 'This week' },
  { value: 'this_month', label: 'This month' },
  { value: 'this_year', label: 'This year' },
  { value: 'last_week', label: 'Last week' },
  { value: 'last_month', label: 'Last month' },
  { value: 'last_year', label: 'Last year' },
  { value: 'last_7_days', label: 'Last 7 days' },
  { value: 'last_14_days', label: 'Last 14 days' },
  { value: 'last_30_days', label: 'Last 30 days' },
  { value: 'last_60_days', label: 'Last 60 days' },
  { value: 'last_90_days', label: 'Last 90 days' },
  { value: 'last_12_months', label: 'Last 12 months' },
]

export const RELATIVE_SINGLES: { value: string; label: string }[] = [
  { value: 'today', label: 'Today' },
  { value: 'yesterday', label: 'Yesterday' },
]

export const RELATIVE_SINGLES_WITH_TIME = [{ value: 'now', label: 'Now' }, ...RELATIVE_SINGLES]

export function relativeLabel(token: string | null | undefined): string | null {
  if (!token) return null
  return [...RELATIVE_RANGES, ...RELATIVE_SINGLES_WITH_TIME].find((t) => t.value === token)?.label ?? null
}

export const TYPE_LABELS: Record<ParameterType, string> = {
  text: 'Text',
  number: 'Number',
  date: 'Date',
  dateTime: 'Date and time',
  dateRange: 'Date range',
  dateTimeRange: 'Date and time range',
  list: 'Dropdown list',
  queryList: 'Dropdown from a query',
  boolean: 'True / false',
}

export function effectiveType(parameter: QueryParameter): ParameterType {
  if (parameter.type) return parameter.type
  switch (parameter.dataType) {
    case 'number':
      return 'number'
    case 'date':
      return 'date'
    case 'dateTime':
      return 'dateTime'
    case 'boolean':
      return 'boolean'
    default:
      return 'text'
  }
}

export function isRangeType(type: ParameterType) {
  return type === 'dateRange' || type === 'dateTimeRange'
}

export function isListType(type: ParameterType) {
  return type === 'list' || type === 'queryList'
}

export function parameterTitle(parameter: QueryParameter) {
  return parameter.label?.trim() || parameter.name
}

const NO_VALUE_OPERATORS: FilterOperator[] = ['isNull', 'isNotNull', 'isTrue', 'isFalse']

/** Mirrors ParameterTypes.AllowedFor on the server: which types a filter can be asked as at run time. */
export function allowedTypesFor(column: ColumnDataType, operator: FilterOperator): ParameterType[] {
  if (NO_VALUE_OPERATORS.includes(operator)) return []
  if (operator === 'between') return column === 'date' || column === 'dateTime' ? ['dateRange', 'dateTimeRange'] : []
  if (operator === 'in' || operator === 'notIn') return column === 'boolean' ? [] : ['list', 'queryList']
  switch (column) {
    case 'number':
      return ['number', 'list', 'queryList']
    case 'date':
      return ['date', 'list', 'queryList']
    case 'dateTime':
      return ['date', 'dateTime', 'list', 'queryList']
    case 'boolean':
      return ['boolean']
    default:
      return ['text', 'list', 'queryList']
  }
}

export function defaultTypeFor(column: ColumnDataType, operator: FilterOperator): ParameterType {
  const allowed = allowedTypesFor(column, operator)
  if (operator === 'in' || operator === 'notIn') return 'list'
  if (operator === 'between') return column === 'dateTime' ? 'dateTimeRange' : 'dateRange'
  if (column === 'dateTime') return 'dateTime'
  return allowed[0] ?? 'text'
}

// ---- value encoding -------------------------------------------------------------------------

export function decodeMulti(value: string | null | undefined): string[] {
  if (!value) return []
  if (value.startsWith('[')) {
    try {
      const parsed: unknown = JSON.parse(value)
      if (Array.isArray(parsed)) return parsed.map((v) => String(v))
    } catch {
      /* a single value that merely starts with '[' */
    }
  }
  return [value]
}

export function encodeMulti(values: string[]): string | null {
  return values.length === 0 ? null : JSON.stringify(values)
}

export function decodeRange(value: string | null | undefined): { start: string; end: string; token: string | null } {
  if (!value) return { start: '', end: '', token: null }
  if (!value.includes('~')) return { start: '', end: '', token: value }
  const [start, end] = value.split('~')
  return { start: start ?? '', end: end ?? '', token: null }
}

export function encodeRange(start: string, end: string): string | null {
  return start || end ? `${start}~${end}` : null
}

export function isBlank(value: string | null | undefined) {
  return !value || value.trim() === '' || value.trim() === '[]'
}

/** What the bar starts from: each parameter's saved default (relative tokens stay tokens). */
export function defaultValues(parameters: QueryParameter[]): ParameterValues {
  const values: ParameterValues = {}
  for (const p of parameters) values[p.name] = p.defaultValue ?? null
  return values
}

/** Required parameters that would have no value if Apply were pressed now. */
export function missingRequired(parameters: QueryParameter[], values: ParameterValues): QueryParameter[] {
  return parameters.filter((p) => p.isRequired && isBlank(values[p.name]) && isBlank(p.defaultValue))
}

export function valuesEqual(parameters: QueryParameter[], a: ParameterValues | null, b: ParameterValues | null) {
  if (!a || !b) return a === b
  return parameters.every((p) => (a[p.name] ?? '') === (b[p.name] ?? ''))
}

/** Only the names the target query declares; the server rejects any other name. */
export function pickKnown(parameters: QueryParameter[], values: ParameterValues | null): ParameterValues {
  const picked: ParameterValues = {}
  if (!values) return picked
  for (const p of parameters) if (p.name in values) picked[p.name] = values[p.name]
  return picked
}

function collectBoundNames(group: FilterGroup | null | undefined, into: Set<string>) {
  if (!group) return
  for (const c of group.conditions) if (c.isParameterized && c.parameterName) into.add(c.parameterName)
  for (const g of group.groups) collectBoundNames(g, into)
}

/**
 * A parameter exists only to feed a filter. One left without a filter would stay in the parameter
 * bar with no way to remove it, because its settings open from the filter's chip, and a required one
 * with no value would block Run. Returns the same object when nothing changed.
 */
export function dropOrphanParameters(definition: QueryDefinition): QueryDefinition {
  const bound = new Set<string>()
  collectBoundNames(definition.filters, bound)
  collectBoundNames(definition.having, bound)
  const parameters = definition.parameters.filter((p) => bound.has(p.name))
  return parameters.length === definition.parameters.length ? definition : { ...definition, parameters }
}

// ---- URL (`p_<name>`) -----------------------------------------------------------------------

export const URL_PREFIX = 'p_'

/** Several values repeat the key (`p_x=a&p_x=b`); a range or token is a single value. */
export function valuesFromSearchParams(parameters: QueryParameter[], search: URLSearchParams): ParameterValues | null {
  const values: ParameterValues = {}
  let found = false
  for (const p of parameters) {
    const all = search.getAll(URL_PREFIX + p.name)
    if (all.length === 0) continue
    found = true
    values[p.name] = all.length > 1 || (isListType(effectiveType(p)) && p.allowMultiple) ? encodeMulti(all) : all[0]
  }
  return found ? { ...defaultValues(parameters), ...values } : null
}

export function writeValuesToSearchParams(
  parameters: QueryParameter[],
  values: ParameterValues,
  search: URLSearchParams,
): URLSearchParams {
  const next = new URLSearchParams(search)
  for (const key of [...next.keys()]) if (key.startsWith(URL_PREFIX)) next.delete(key)
  for (const p of parameters) {
    const value = values[p.name]
    if (isBlank(value)) continue
    // Only differences from the defaults are worth putting in a shareable link.
    if ((p.defaultValue ?? '') === value) continue
    if (isListType(effectiveType(p)) && value!.startsWith('[')) {
      for (const item of decodeMulti(value)) next.append(URL_PREFIX + p.name, item)
    } else {
      next.set(URL_PREFIX + p.name, value!)
    }
  }
  return next
}

// ---- helpers for the builder ----------------------------------------------------------------

/** All run-time conditions of a definition (WHERE and HAVING), for finding what a parameter is bound to. */
export function boundConditions(definition: QueryDefinition, parameterName: string): FilterCondition[] {
  const walk = (group: QueryDefinition['filters'] | null | undefined): FilterCondition[] =>
    group ? [...group.conditions, ...group.groups.flatMap(walk)] : []
  return [...walk(definition.filters), ...walk(definition.having)].filter(
    (c) => c.isParameterized && c.parameterName === parameterName,
  )
}

/** A default for a freshly parameterized filter, translated from the condition's own fixed value. */
export function initialDefault(condition: FilterCondition, type: ParameterType): string | null {
  const value = condition.value
  if (isBlank(value)) return null
  if (isRangeType(type)) {
    try {
      const parsed: unknown = JSON.parse(value!)
      if (Array.isArray(parsed) && parsed.length === 2 && parsed[0] && parsed[1]) return `${parsed[0]}~${parsed[1]}`
    } catch {
      /* not an array */
    }
    return null
  }
  if (isListType(type)) return null
  return value!
}
