import { createContext } from 'react'
import { isBlank, isListType, effectiveType, parameterTitle, type ParameterValues } from '@/features/parameters/param-values'
import type { DashboardParameterDto, ParameterMappingDto, ParameterType, QueryParameter } from '@/types'

/** The values a viewer has applied in the dashboard's parameter bar, by dashboard parameter name. */
export const DashboardValuesContext = createContext<ParameterValues>({})

export const MAX_DASHBOARD_PARAMETERS = 20

export function dashboardParameterTitle(p: DashboardParameterDto) {
  return p.title?.trim() || p.name
}

/** Lets the Phase A controls (built around a query parameter) render a dashboard parameter. */
export function toQueryParameter(p: DashboardParameterDto): QueryParameter {
  return {
    name: p.name,
    label: dashboardParameterTitle(p),
    dataType: 'text',
    type: p.type,
    defaultValue: p.value ?? null,
    isRequired: false,
    options: p.options ?? [],
    optionsQueryId: p.optionsQueryId ?? null,
    optionsLabelColumn: p.optionsLabelColumn ?? null,
    optionsValueColumn: p.optionsValueColumn ?? null,
    allowMultiple: p.allowMultiple,
  }
}

/** Mirrors DashboardParameterValidator.Family on the server. */
function family(type: ParameterType): number {
  switch (type) {
    case 'text':
      return 0
    case 'number':
      return 1
    case 'date':
    case 'dateTime':
      return 2
    case 'dateRange':
    case 'dateTimeRange':
      return 3
    case 'list':
    case 'queryList':
      return 4
    default:
      return 5
  }
}

/** A dashboard parameter can feed a query parameter of the same kind of value; several values only to one that takes several. */
export function isCompatible(source: DashboardParameterDto, target: QueryParameter): boolean {
  const targetType = effectiveType(target)
  if (source.allowMultiple && !(target.allowMultiple && isListType(targetType))) return false
  return family(source.type) === family(targetType)
}

export function uniqueName(base: string, taken: string[]): string {
  const lower = new Set(taken.map((t) => t.toLowerCase()))
  if (!lower.has(base.toLowerCase())) return base
  for (let i = 2; ; i++) {
    const candidate = `${base}_${i}`
    if (!lower.has(candidate.toLowerCase())) return candidate
  }
}

/** A new dashboard parameter modelled on a query parameter: same type, choices and default. */
export function parameterFromQuery(query: QueryParameter, taken: string[]): DashboardParameterDto {
  return {
    name: uniqueName(query.name, taken),
    title: parameterTitle(query),
    type: effectiveType(query),
    options: query.options ?? [],
    optionsQueryId: query.optionsQueryId ?? null,
    optionsLabelColumn: query.optionsLabelColumn ?? null,
    optionsValueColumn: query.optionsValueColumn ?? null,
    allowMultiple: !!query.allowMultiple,
    value: query.defaultValue ?? null,
  }
}

export type Missing = { parameter: QueryParameter; mapping: ParameterMappingDto | undefined }

/**
 * The values a tile sends with its render call: dashboard, then tile input, then fixed, then the
 * query's own default (which the server applies when nothing is sent). Blank values are left out so
 * that default applies. Whatever is sent is validated again by the server.
 */
export function resolveTileValues(
  parameters: QueryParameter[],
  mappings: Record<string, ParameterMappingDto> | null | undefined,
  dashboardValues: ParameterValues,
  tileValues: ParameterValues,
  promptValues: ParameterValues | null,
): { values: ParameterValues; missing: Missing[] } {
  const values: ParameterValues = {}
  const missing: Missing[] = []
  for (const parameter of parameters) {
    const mapping = mappings?.[parameter.name]
    let value: string | null | undefined
    switch (mapping?.source) {
      case 'dashboard':
        value = mapping.dashboardParameter ? dashboardValues[mapping.dashboardParameter] : null
        break
      case 'tile':
        value = tileValues[parameter.name] ?? parameter.defaultValue
        break
      case 'fixed':
        value = mapping.value
        break
      default:
        value = promptValues?.[parameter.name]
    }
    if (!isBlank(value)) values[parameter.name] = value!
    else if (parameter.isRequired && isBlank(parameter.defaultValue)) missing.push({ parameter, mapping })
  }
  return { values, missing }
}

export function mappedBy(
  mappings: Record<string, ParameterMappingDto> | null | undefined,
  source: ParameterMappingDto['source'],
): string[] {
  return Object.entries(mappings ?? {})
    .filter(([, m]) => m.source === source)
    .map(([name]) => name)
}
