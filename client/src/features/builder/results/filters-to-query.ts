import { AGGREGATE_LABELS, aggregateResultDataType } from '../aggregate-functions.ts'
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

function mixedReason(column: string) {
  return `${column}: empty values mixed with other values can't be expressed in the query's filters`
}

function matchAnyReason(clause: 'where' | 'having') {
  const what = clause === 'where' ? 'filters' : 'totals filters'
  return `Your query's ${what} use 'Match any'. Switch to 'Match all' in Build to add these.`
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
      return {
        conditions: [make(include ? 'isNull' : 'isNotNull', null)],
        description: `${label} is ${include ? '' : 'not '}empty`,
      }
    }
    if (real.length === 0) return { conditions: [], description: '' }
    if (!include && hasEmpty) notes.warnings.push(`The query will also leave out empty ${filter.column} values.`)
    if (include && profileKind(dataType) === 'text') notes.text = true
    let cond: FilterCondition
    if (real.length === 1) cond = make(include ? 'equals' : 'notEquals', valueText(real[0]))
    else {
      const texts = real.map(valueText)
      const value = texts.some((t) => t.includes(',')) ? JSON.stringify(texts) : texts.join(',')
      cond = make(include ? 'in' : 'notIn', value)
    }
    return { conditions: [cond], description: `${label} is ${include ? '' : 'not '}${listText(real)}` }
  }

  if (filter.includeEmpty) return { reason: mixedReason(filter.column) }
  const { min, max } = filter
  if (min === null && max === null) return { conditions: [], description: '' }
  if (profileKind(dataType) === 'date') {
    const conditions: FilterCondition[] = []
    if (min !== null) conditions.push(make('greaterOrEqual', isoDay(min)))
    if (max !== null) {
      if (dataType === 'dateTime') conditions.push(make('lessThan', isoDay((Math.floor(max / DAY_MS) + 1) * DAY_MS)))
      else conditions.push(make('lessOrEqual', isoDay(max)))
    }
    let description: string
    if (min !== null && max !== null) description = `${label} between ${isoDay(min)} and ${isoDay(max)}`
    else if (min !== null) description = `${label} from ${isoDay(min)}`
    else description = `${label} up to ${isoDay(max as number)}`
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
    const matches = definition.columns.filter((c) => c.isVisible && (c.alias || c.columnName) === filter.column)
    if (matches.length !== 1) {
      plan.blocked.push({ column: filter.column, reason: `${filter.column} isn't a column of the query any more` })
      continue
    }
    const qc = matches[0]
    const clause: 'where' | 'having' = qc.aggregate !== 'none' ? 'having' : 'where'
    const condType = clause === 'having' ? aggregateResultDataType(qc.dataType, qc.aggregate) : qc.dataType
    const group = clause === 'having' ? definition.having : definition.filters
    if (group && group.operator === 'or' && collect(group).length > 0) {
      plan.blocked.push({ column: filter.column, reason: matchAnyReason(clause) })
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
    const label = clause === 'having' ? `${AGGREGATE_LABELS[qc.aggregate]} of ${filter.column}` : filter.column
    const built = build(filter, resultTypes.get(filter.column) ?? condType, hasEmpty, make, label, notes)
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
