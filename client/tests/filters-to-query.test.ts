import { test } from 'node:test'
import assert from 'node:assert/strict'
import { planAddToQuery } from '../src/features/builder/results/filters-to-query.ts'
import type { GridFilter } from '../src/features/builder/results/grid-filters.ts'
import type { FilterCondition, FilterGroup, QueryColumn, QueryResultColumnDto } from '../src/types/index.ts'

const col = (over: Partial<QueryColumn> & { columnName: string }): QueryColumn => ({
  tableAlias: 't',
  alias: null,
  dataType: 'text',
  aggregate: 'none',
  orderIndex: 0,
  isVisible: true,
  ...over,
})
const rc = (name: string, dataType: QueryResultColumnDto['dataType']): QueryResultColumnDto => ({ name, dataType })
const group = (operator: 'and' | 'or' = 'and', conditions: FilterCondition[] = []): FilterGroup => ({
  operator,
  conditions,
  groups: [],
})
const cond = (over: Partial<FilterCondition>): FilterCondition => ({
  tableAlias: 't',
  columnName: 'Name',
  dataType: 'text',
  aggregate: 'none',
  operator: 'equals',
  value: 'x',
  isParameterized: false,
  parameterName: null,
  ...over,
})
const values = (column: string, vals: (string | number | boolean | null)[], mode: 'include' | 'exclude' = 'include'): GridFilter => ({
  kind: 'values',
  column,
  mode,
  values: vals,
})
const range = (column: string, min: number | null, max: number | null, includeEmpty = false): GridFilter => ({
  kind: 'range',
  column,
  min,
  max,
  includeEmpty,
})

const nameCols = [col({ columnName: 'Name' })]
const nameRes = [rc('Name', 'text')]
const def = (columns: QueryColumn[], filters = group(), having: FilterGroup | null = null) => ({ columns, filters, having })

test('maps by alias and writes where conditions', () => {
  const plan = planAddToQuery(
    [range('FeePaid', 100, 500)],
    [rc('FeePaid', 'number')],
    [],
    def([col({ columnName: 'Fee', alias: 'FeePaid', dataType: 'number' })]),
  )
  assert.equal(plan.blocked.length, 0)
  assert.equal(plan.add[0].clause, 'where')
  assert.equal(plan.add[0].conditions[0].columnName, 'Fee')
  assert.equal(plan.add[0].conditions[0].aggregate, 'none')
  assert.equal(plan.add[0].conditions[0].isParameterized, false)
  assert.equal(plan.add[0].conditions[0].parameterName, null)
})

test('unmapped and ambiguous columns are blocked', () => {
  const plan = planAddToQuery([values('Gone', ['a'])], [rc('Gone', 'text')], [], def(nameCols))
  assert.equal(plan.add.length, 0)
  assert.equal(plan.blocked[0].reason, "Gone isn't a column of the query any more")
  const dup = planAddToQuery(
    [values('Name', ['a'])],
    nameRes,
    [],
    def([col({ columnName: 'Name' }), col({ columnName: 'Other', alias: 'Name' })]),
  )
  assert.equal(dup.blocked.length, 1)
  const hidden = planAddToQuery([values('Name', ['a'])], nameRes, [], def([col({ columnName: 'Name', isVisible: false })]))
  assert.equal(hidden.blocked.length, 1)
})

test('summary column goes to having with the aggregate result type', () => {
  const plan = planAddToQuery(
    [range('Fee', 500, null)],
    [rc('Fee', 'number')],
    [],
    def([col({ columnName: 'Fee', dataType: 'number', aggregate: 'sum' })]),
  )
  const p = plan.add[0]
  assert.equal(p.clause, 'having')
  assert.equal(p.conditions[0].aggregate, 'sum')
  assert.equal(p.conditions[0].dataType, 'number')
  assert.equal(p.description, 'Sum of Fee at least 500')
  const cnt = planAddToQuery(
    [range('N', 1, null)],
    [rc('N', 'number')],
    [],
    def([col({ columnName: 'N', dataType: 'text', aggregate: 'count' })]),
  )
  assert.equal(cnt.add[0].conditions[0].dataType, 'number')
})

test('values map to operators', () => {
  const one = planAddToQuery([values('Name', ['A'])], nameRes, [], def(nameCols)).add[0].conditions
  assert.equal(one.length, 1)
  assert.equal(one[0].operator, 'equals')
  assert.equal(one[0].value, 'A')
  const many = planAddToQuery([values('Name', ['A', 'B'])], nameRes, [], def(nameCols))
  assert.equal(many.add[0].conditions[0].operator, 'in')
  assert.equal(many.add[0].conditions[0].value, 'A,B')
  assert.equal(many.add[0].description, 'Name is A or B')
  const comma = planAddToQuery([values('Name', ['A,1', 'B'])], nameRes, [], def(nameCols))
  assert.equal(comma.add[0].conditions[0].value, '["A,1","B"]')
  const ex = planAddToQuery([values('Name', ['A'], 'exclude')], nameRes, [], def(nameCols))
  assert.equal(ex.add[0].conditions[0].operator, 'notEquals')
  const exMany = planAddToQuery([values('Name', ['A', 'B'], 'exclude')], nameRes, [], def(nameCols))
  assert.equal(exMany.add[0].conditions[0].operator, 'notIn')
  const isNull = planAddToQuery([values('Name', [null])], nameRes, [], def(nameCols)).add[0].conditions[0]
  assert.equal(isNull.operator, 'isNull')
  assert.equal(isNull.value, null)
  const notNull = planAddToQuery([values('Name', [null], 'exclude')], nameRes, [], def(nameCols)).add[0].conditions[0]
  assert.equal(notNull.operator, 'isNotNull')
})

test('booleans are written as true/false', () => {
  const plan = planAddToQuery(
    [values('Active', [true])],
    [rc('Active', 'boolean')],
    [],
    def([col({ columnName: 'Active', dataType: 'boolean' })]),
  )
  assert.equal(plan.add[0].conditions[0].value, 'true')
})

test('empty values mixed with others are blocked, in both modes', () => {
  for (const mode of ['include', 'exclude'] as const) {
    const plan = planAddToQuery([values('Name', ['A', null], mode)], nameRes, [], def(nameCols))
    assert.equal(plan.add.length, 0)
    assert.equal(
      plan.blocked[0].reason,
      "Name: empty values mixed with other values can't be expressed in the query's filters",
    )
  }
})

test('exclude warns when loaded rows hold empties', () => {
  const rows = [{ Name: 'A' }, { Name: null }]
  const w = planAddToQuery([values('Name', ['A'], 'exclude')], nameRes, rows, def(nameCols)).warnings
  assert.ok(w.includes('The query will also leave out empty Name values.'))
  const none = planAddToQuery([values('Name', ['A'], 'exclude')], nameRes, [{ Name: 'A' }], def(nameCols)).warnings
  assert.ok(!none.some((x) => x.includes('leave out empty')))
})

test('number ranges', () => {
  const nc = [col({ columnName: 'Fee', dataType: 'number' })]
  const nr = [rc('Fee', 'number')]
  const both = planAddToQuery([range('Fee', 100, 500)], nr, [], def(nc))
  assert.equal(both.add[0].conditions.length, 1)
  assert.equal(both.add[0].conditions[0].operator, 'between')
  assert.equal(both.add[0].conditions[0].value, '["100","500"]')
  assert.equal(both.add[0].description, 'Fee between 100 and 500')
  const lo = planAddToQuery([range('Fee', 100, null)], nr, [], def(nc)).add[0].conditions
  assert.deepEqual([lo.length, lo[0].operator, lo[0].value], [1, 'greaterOrEqual', '100'])
  const hi = planAddToQuery([range('Fee', null, 7.5)], nr, [], def(nc)).add[0].conditions
  assert.deepEqual([hi[0].operator, hi[0].value], ['lessOrEqual', '7.5'])
})

test('date ranges', () => {
  const dc = [col({ columnName: 'When', dataType: 'dateTime' })]
  const min = Date.UTC(2025, 0, 1)
  const dt = planAddToQuery([range('When', min, Date.UTC(2025, 2, 31) + 86_399_999)], [rc('When', 'dateTime')], [], def(dc))
  const c = dt.add[0].conditions
  assert.deepEqual(
    c.map((x) => [x.operator, x.value]),
    [
      ['greaterOrEqual', '2025-01-01'],
      ['lessThan', '2025-04-01'],
    ],
  )
  const d = planAddToQuery(
    [range('When', min, Date.UTC(2025, 2, 31))],
    [rc('When', 'date')],
    [],
    def([col({ columnName: 'When', dataType: 'date' })]),
  )
  assert.deepEqual(
    d.add[0].conditions.map((x) => [x.operator, x.value]),
    [
      ['greaterOrEqual', '2025-01-01'],
      ['lessOrEqual', '2025-03-31'],
    ],
  )
  assert.equal(d.add[0].description, 'When between 2025-01-01 and 2025-03-31')
})

test('a range that includes empty values is blocked', () => {
  const plan = planAddToQuery(
    [range('Fee', 1, 2, true)],
    [rc('Fee', 'number')],
    [],
    def([col({ columnName: 'Fee', dataType: 'number' })]),
  )
  assert.equal(plan.add.length, 0)
  assert.equal(plan.blocked[0].reason, "Fee: empty values mixed with other values can't be expressed in the query's filters")
})

test("'Match any' blocks, but only a non-empty group", () => {
  const existing = group('or', [cond({ columnName: 'Other' })])
  const plan = planAddToQuery([values('Name', ['A'])], nameRes, [], def(nameCols, existing))
  assert.equal(plan.add.length, 0)
  assert.equal(
    plan.blocked[0].reason,
    "Your query's filters use 'Match any'. Switch to 'Match all' in Build to add these.",
  )
  const empty = planAddToQuery([values('Name', ['A'])], nameRes, [], def(nameCols, group('or')))
  assert.equal(empty.add.length, 1)
  const sumCols = [col({ columnName: 'Fee', dataType: 'number', aggregate: 'sum' })]
  const having = planAddToQuery(
    [range('Fee', 1, null)],
    [rc('Fee', 'number')],
    [],
    def(sumCols, group(), group('or', [cond({ aggregate: 'sum', columnName: 'Fee' })])),
  )
  assert.equal(
    having.blocked[0].reason,
    "Your query's totals filters use 'Match any'. Switch to 'Match all' in Build to add these.",
  )
})

test('overlap with an existing condition is warned about', () => {
  const existing = group('and', [cond({ columnName: 'Name', operator: 'notEquals' })])
  const plan = planAddToQuery([values('Name', ['A'])], nameRes, [], def(nameCols, existing))
  assert.ok(plan.warnings.includes('The query already filters Name; both conditions will apply.'))
  const other = planAddToQuery([values('Name', ['A'])], nameRes, [], def(nameCols, group('and', [cond({ columnName: 'Z' })])))
  assert.ok(!other.warnings.some((w) => w.includes('already filters')))
})

test('the case note appears once for two text filters', () => {
  const two = [col({ columnName: 'Name' }), col({ columnName: 'City' })]
  const plan = planAddToQuery(
    [values('Name', ['A']), values('City', ['X', 'Y'])],
    [rc('Name', 'text'), rc('City', 'text')],
    [],
    def(two),
  )
  assert.equal(plan.warnings.filter((w) => w === 'Matching in the query may ignore upper/lower case.').length, 1)
  const ex = planAddToQuery([values('Name', ['A'], 'exclude')], nameRes, [], def(nameCols))
  assert.ok(!ex.warnings.some((w) => w.includes('upper/lower')))
})
