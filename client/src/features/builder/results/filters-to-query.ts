import { aggregateResultDataType } from '../aggregate-functions.ts'
import { profileKind } from './column-profile.ts'
import type { FilterValue, GridFilter } from './grid-filters.ts'
import type { ColumnDataType, FilterCondition, FilterGroup, FilterOperator, QueryColumn, QueryResultColumnDto } from '@/types'

export interface PlannedFilter {
  column: string
  clause: 'where' | 'having'
  conditions: FilterCondition[]
  description: string
}

export interface AddToQueryPlan {
  add: PlannedFilter[]
  blocked: { column: string; reason: string }[]
  warnings: string[]
}

const DAY_MS = 86_400_000

const isEmptyCell = (v: unknown) => v === null || v === undefined || v === ''
const isoDay = (ms: number) => new Date(ms).toISOString().slice(0, 10)
const valueText = (v: Exclude<FilterValue, null>) => String(v)
const mod = (t: number) => ((t % DAY_MS) + DAY_MS) % DAY_MS
const aliasOf = (c: QueryColumn) => (c.alias?.trim() ? c.alias : c.columnName)
// The server trims and drops empties when it splits a comma list, so those values need the JSON form.
const needsJsonList = (texts: string[]) =>
  texts.some((t) => t.includes(',') || t !== t.trim() || t.trim() === '' || t.startsWith('['))

function mixedReason(column: string) {
  return `${column}: empty values mixed with other values can't be expressed in the query's filters`
}

function matchAnyReason(column: string, clause: 'where' | 'having') {
  const what = clause === 'where' ? 'filters' : 'totals filters'
  return `${column}: your query's ${what} use 'Match any'. Switch to 'Match all' in Build to add these.`
}

function collect(group: FilterGroup | null | undefined, into: FilterCondition[] = []): FilterCondition[] {
  if (!group) return into
  into.push(...group.conditions)
  for (const g of group.groups ?? []) collect(g, into)
  return into
}

function listText(values: Exclude<FilterValue, null>[]): string {
  const parts = values.map(valueText)
  return parts.length <= 1 ? (parts[0] ?? '') : parts.join(' or ')
}

function build(
  filter: GridFilter,
  dataType: ColumnDataType,
  hasEmpty: boolean,
  hasBlank: boolean,
  make: (operator: FilterOperator, value: string | null) => FilterCondition,
  label: string,
  notes: { text: boolean; warnings: string[] },
): { conditions: FilterCondition[]; description: string } | { reason: string } {
  if (filter.kind === 'values') {
    const nulls = filter.values.filter((v) => v === null).length
    const real = filter.values.filter((v): v is Exclude<FilterValue, null> => v !== null)
    if (nulls > 0 && real.length > 0) return { reason: mixedReason(filter.column) }
    const include = filter.mode === 'include'
    if (nulls > 0) {
      if (hasBlank) {
        notes.warnings.push(
          `The query treats blank text and empty values differently: blank ${filter.column} values will be ${include ? 'left out' : 'kept'}.`,
        )
      }
      return {
        conditions: [make(include ? 'isNull' : 'isNotNull', null)],
        description: `${label} is ${include ? '' : 'not '}empty`,
      }
    }
    if (real.length === 0) return { conditions: [], description: '' }
    const isBoolean = profileKind(dataType) === 'boolean'
    const hasTrue = real.includes(true)
    const hasFalse = real.includes(false)
    // 'is not null' (both values excluded) keeps empties, so the warning would be wrong there.
    if (!include && hasEmpty && !(isBoolean && hasTrue && hasFalse)) {
      notes.warnings.push(`The query will also leave out empty ${filter.column} values.`)
    }
    if (isBoolean) {
      let operator: FilterOperator
      if (hasTrue && hasFalse) operator = include ? 'isNotNull' : 'isNull'
      else if (hasTrue) operator = include ? 'isTrue' : 'isFalse'
      else operator = include ? 'isFalse' : 'isTrue'
      const names = [hasTrue ? 'Yes' : null, hasFalse ? 'No' : null].filter(Boolean).join(' or ')
      return { conditions: [make(operator, null)], description: `${label} is ${include ? '' : 'not '}${names}` }
    }
    if (profileKind(dataType) === 'text') notes.text = true
    let cond: FilterCondition
    if (real.length === 1) cond = make(include ? 'equals' : 'notEquals', valueText(real[0]))
    else {
      const texts = real.map(valueText)
      const value = needsJsonList(texts) ? JSON.stringify(texts) : texts.join(',')
      cond = make(include ? 'in' : 'notIn', value)
    }
    const shown = include ? listText(real) : real.map(valueText).join(', ')
    return { conditions: [cond], description: `${label} is ${include ? '' : 'not '}${shown}` }
  }

  if (filter.includeEmpty) return { reason: mixedReason(filter.column) }
  const { min, max } = filter
  if (min === null && max === null) return { conditions: [], description: '' }
  if (profileKind(dataType) === 'date') {
    const conditions: FilterCondition[] = []
    const midnight = (ms: number) => mod(ms) === 0
    const exact = (ms: number) => {
      if (midnight(ms)) return isoDay(ms)
      const iso = new Date(ms).toISOString()
      return ms % 1000 === 0 ? iso.slice(0, 19) : iso.slice(0, 23)
    }
    if (min !== null) conditions.push(make('greaterOrEqual', exact(min)))
    if (max !== null) {
      if (mod(max) === DAY_MS - 1) {
        conditions.push(make('lessThan', isoDay((Math.floor(max / DAY_MS) + 1) * DAY_MS)))
      } else conditions.push(make('lessOrEqual', exact(max)))
    }
    const text = (ms: number) => (midnight(ms) || mod(ms) === DAY_MS - 1 ? isoDay(ms) : exact(ms).replace('T', ' '))
    let description: string
    if (min !== null && max !== null) description = `${label} between ${text(min)} and ${text(max)}`
    else if (min !== null) description = `${label} from ${text(min)}`
    else description = `${label} up to ${text(max as number)}`
    return { conditions, description }
  }
  if (min !== null && max !== null) {
    return {
      conditions: [make('between', JSON.stringify([String(min), String(max)]))],
      description: `${label} between ${min} and ${max}`,
    }
  }
  if (min !== null) return { conditions: [make('greaterOrEqual', String(min))], description: `${label} at least ${min}` }
  return { conditions: [make('lessOrEqual', String(max))], description: `${label} at most ${max}` }
}

export function planAddToQuery(
  filters: GridFilter[],
  resultColumns: QueryResultColumnDto[],
  rows: Record<string, unknown>[],
  definition: { columns: QueryColumn[]; filters: FilterGroup; having?: FilterGroup | null },
): AddToQueryPlan {
  const plan: AddToQueryPlan = { add: [], blocked: [], warnings: [] }
  const notes = { text: false, warnings: [] as string[] }
  const resultTypes = new Map(resultColumns.map((c) => [c.name, c.dataType]))

  for (const filter of filters) {
    const matches = definition.columns.filter((c) => c.isVisible && aliasOf(c) === filter.column)
    if (matches.length !== 1) {
      plan.blocked.push({ column: filter.column, reason: `${filter.column} isn't a column of the query any more` })
      continue
    }
    const qc = matches[0]
    const clause: 'where' | 'having' = qc.aggregate !== 'none' ? 'having' : 'where'
    const condType = clause === 'having' ? aggregateResultDataType(qc.dataType, qc.aggregate) : qc.dataType
    const group = clause === 'having' ? definition.having : definition.filters
    if (group && group.operator === 'or' && collect(group).length > 0) {
      plan.blocked.push({ column: filter.column, reason: matchAnyReason(filter.column, clause) })
      continue
    }

    const make = (operator: FilterOperator, value: string | null): FilterCondition => ({
      tableAlias: qc.tableAlias,
      columnName: qc.columnName,
      dataType: condType,
      aggregate: clause === 'having' ? qc.aggregate : 'none',
      operator,
      value,
      isParameterized: false,
      parameterName: null,
    })
    const hasEmpty = rows.some((r) => isEmptyCell(r[filter.column]))
    const hasBlank = rows.some((r) => r[filter.column] === '')
    const resultType = resultTypes.get(filter.column) ?? condType
    const label = filter.column
    const built = build(filter, resultType, hasEmpty, hasBlank, make, label, notes)
    if ('reason' in built) {
      plan.blocked.push({ column: filter.column, reason: built.reason })
      continue
    }
    if (built.conditions.length === 0) continue

    const overlaps = collect(group).some(
      (c) => c.tableAlias === qc.tableAlias && c.columnName === qc.columnName && c.aggregate === (clause === 'having' ? qc.aggregate : 'none'),
    )
    if (overlaps) notes.warnings.push(`The query already filters ${filter.column}; both conditions will apply.`)
    plan.add.push({ column: filter.column, clause, conditions: built.conditions, description: built.description })
  }

  const warnings = [...notes.warnings]
  if (notes.text) warnings.push('Matching in the query may ignore upper/lower case.')
  plan.warnings = [...new Set(warnings)]
  return plan
}
