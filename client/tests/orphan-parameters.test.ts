import { test } from 'node:test'
import assert from 'node:assert/strict'
import { dropOrphanParameters } from '../src/features/parameters/param-values.ts'
import type { FilterCondition, FilterGroup, QueryDefinition, QueryParameter } from '../src/types/index.ts'

const parameter = (name: string): QueryParameter => ({
  name,
  label: name,
  dataType: 'text',
  type: 'text',
  defaultValue: null,
  isRequired: true,
  options: [],
  allowMultiple: false,
})

const condition = (parameterName?: string): FilterCondition => ({
  tableAlias: 'e',
  columnName: 'Status',
  dataType: 'text',
  aggregate: 'none',
  operator: 'equals',
  value: '',
  isParameterized: !!parameterName,
  parameterName,
})

const group = (conditions: FilterCondition[], groups: FilterGroup[] = []): FilterGroup => ({ operator: 'and', conditions, groups })

const definition = (parameters: QueryParameter[], filters: FilterGroup, having?: FilterGroup | null) =>
  ({ filters, having, parameters }) as unknown as QueryDefinition

const names = (d: QueryDefinition) => d.parameters.map((p) => p.name)

test('a parameter whose filter is gone is dropped', () => {
  const result = dropOrphanParameters(definition([parameter('Status')], group([])))
  assert.deepEqual(names(result), [])
})

test('a parameter still bound in WHERE is kept', () => {
  const result = dropOrphanParameters(definition([parameter('Status'), parameter('Gone')], group([condition('Status')])))
  assert.deepEqual(names(result), ['Status'])
})

test('a parameter still bound in HAVING is kept', () => {
  const result = dropOrphanParameters(definition([parameter('Total')], group([]), group([condition('Total')])))
  assert.deepEqual(names(result), ['Total'])
})

test('a parameter bound in a nested group is kept', () => {
  const result = dropOrphanParameters(definition([parameter('Deep')], group([], [group([], [group([condition('Deep')])])])))
  assert.deepEqual(names(result), ['Deep'])
})

test('a parameter shared by two conditions survives removing one of them', () => {
  const result = dropOrphanParameters(definition([parameter('Shared')], group([condition('Shared')])))
  assert.deepEqual(names(result), ['Shared'])
})

test('a condition that is not parameterized does not keep a parameter alive through a stale name', () => {
  const stale = { ...condition('Old'), isParameterized: false }
  const result = dropOrphanParameters(definition([parameter('Old')], group([stale])))
  assert.deepEqual(names(result), [])
})

test('nothing to drop returns the same object, so a store update changes nothing', () => {
  const input = definition([parameter('Status')], group([condition('Status')]))
  assert.equal(dropOrphanParameters(input), input)
})

test('other parts of the definition are left alone', () => {
  const filters = group([condition()])
  const result = dropOrphanParameters(definition([parameter('Gone')], filters, null))
  assert.equal(result.filters, filters)
  assert.equal(result.having, null)
})

test('a definition with no having at all works', () => {
  const result = dropOrphanParameters(definition([parameter('Status')], group([condition('Status')]), undefined))
  assert.deepEqual(names(result), ['Status'])
})
