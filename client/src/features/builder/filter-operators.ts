import type { ColumnDataType, FilterCondition, FilterOperator } from '@/types'

export const OPERATOR_LABELS: Record<FilterOperator, string> = {
  equals: 'Equals',
  notEquals: 'Not equals',
  greaterThan: 'Greater than',
  greaterOrEqual: 'Greater or equal',
  lessThan: 'Less than',
  lessOrEqual: 'Less or equal',
  contains: 'Contains',
  notContains: 'Does not contain',
  startsWith: 'Starts with',
  endsWith: 'Ends with',
  in: 'Is any of',
  notIn: 'Is none of',
  between: 'Between',
  isNull: 'Is empty',
  isNotNull: 'Is not empty',
  isTrue: 'Is true',
  isFalse: 'Is false',
}

const TEXT_OPERATORS: FilterOperator[] = ['equals', 'notEquals', 'contains', 'notContains', 'startsWith', 'endsWith', 'isNull', 'isNotNull']
const NUMBER_OPERATORS: FilterOperator[] = ['equals', 'notEquals', 'greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual', 'between', 'isNull', 'isNotNull']
const DATE_OPERATORS: FilterOperator[] = ['equals', 'notEquals', 'greaterThan', 'greaterOrEqual', 'lessThan', 'lessOrEqual', 'between', 'isNull', 'isNotNull']
const BOOLEAN_OPERATORS: FilterOperator[] = ['isTrue', 'isFalse']
const GUID_OPERATORS: FilterOperator[] = ['equals', 'notEquals', 'isNull', 'isNotNull']

export function operatorsForDataType(dataType: ColumnDataType): FilterOperator[] {
  switch (dataType) {
    case 'number':
      return NUMBER_OPERATORS
    case 'date':
    case 'dateTime':
      return DATE_OPERATORS
    case 'boolean':
      return BOOLEAN_OPERATORS
    case 'guid':
      return GUID_OPERATORS
    default:
      return TEXT_OPERATORS
  }
}

export function operatorNeedsValue(operator: FilterOperator): boolean {
  return !['isNull', 'isNotNull', 'isTrue', 'isFalse'].includes(operator)
}

export function operatorNeedsTwoValues(operator: FilterOperator): boolean {
  return operator === 'between'
}

/**
 * A condition is safe to leave as-is (and safe to let the user add another one after) once it
 * either needs no value (Is empty / Is true, etc.), is set to prompt at run time, or has every
 * value field it needs actually filled in. Used to gate "Add filter" and to flag stray blanks.
 */
export function isFilterConditionComplete(condition: FilterCondition): boolean {
  if (!condition.columnName) return false
  if (condition.isParameterized) return true
  if (!operatorNeedsValue(condition.operator)) return true

  if (operatorNeedsTwoValues(condition.operator)) {
    const [lo, hi] = parseValuePair(condition.value)
    return lo.trim() !== '' && hi.trim() !== ''
  }

  return !!condition.value && condition.value.trim() !== ''
}

function parseValuePair(value: string | null | undefined): [string, string] {
  if (!value) return ['', '']
  try {
    const parsed = JSON.parse(value)
    return [parsed[0] ?? '', parsed[1] ?? '']
  } catch {
    return ['', '']
  }
}
