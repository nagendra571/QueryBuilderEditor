import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  allowedBreakdownColumns,
  breakdownChoicesFor,
  breakdownCrumbs,
  clickedCount,
  describeParameterValue,
  drillUrl,
  isChartDrillPath,
  mergeSteps,
  parseDrillSearch,
  popBreakdown,
  pushBreakdown,
  rowSteps,
  stepCrumbs,
} from '../src/features/drill/drill-nav.ts'
import { decodeTrail } from '../src/features/drill/drill-url.ts'

const col = (name: string, aggregate: string | null = null, dataType = 'text') => ({ name, aggregate, dataType }) as never

test('a drill link round-trips steps, filters, context, parameters and the clicked number', () => {
  const url = drillUrl({
    queryId: 'q1',
    vizId: 'v1',
    steps: [{ column: 'Program', value: '20' }, { column: 'Closed', value: null }],
    filters: [{ sourceTileId: 't9', column: 'Site', value: 'A&B' }],
    dashboardId: 'd1',
    tileId: 't1',
    params: { term: '2026', ids: '["a","b"]', empty: '', gone: null },
    n: 140,
  })
  assert.ok(url.startsWith('/queries/q1/charts/v1/drill?'))
  const parsed = parseDrillSearch(new URL(url, 'http://x').searchParams)
  assert.deepEqual(parsed.steps, [{ column: 'Program', value: '20' }, { column: 'Closed', value: null }])
  assert.deepEqual(parsed.filters, [{ sourceTileId: 't9', column: 'Site', value: 'A&B' }])
  assert.equal(parsed.dashboardId, 'd1')
  assert.equal(parsed.tileId, 't1')
  assert.deepEqual(parsed.params, { term: '2026', ids: '["a","b"]' })
  assert.equal(parsed.n, 140)
})

test('a results-grid link has no chart segment and no empty trail', () => {
  assert.equal(drillUrl({ queryId: 'q1', steps: [] }), '/queries/q1/drill')
})

test('a broken s or f is reported as null, never as no steps', () => {
  assert.equal(parseDrillSearch(new URLSearchParams('s=%%%')).steps, null)
  assert.equal(parseDrillSearch(new URLSearchParams('f=abc!')).filters, null)
  assert.deepEqual(parseDrillSearch(new URLSearchParams('')).steps, [])
})

test('a repeated p_ key from the query page becomes one JSON list; n must be digits', () => {
  const parsed = parseDrillSearch(new URLSearchParams('p_x=a&p_x=b&n=-3'))
  assert.equal(parsed.params.x, '["a","b"]')
  assert.equal(parsed.n, null)
})

test('a crumb keeps a shorter trail than the one before it', () => {
  const crumbs = stepCrumbs([{ column: 'A', value: '1' }, { column: 'B', value: null }])
  assert.deepEqual(crumbs, [{ label: 'A = 1', keep: 1 }, { label: 'B is blank', keep: 2 }])
})

test('mergeSteps never repeats a step', () => {
  const a = { column: 'A', value: '1' }
  const b = { column: 'B', value: '2' }
  assert.deepEqual(mergeSteps([a], [a, b]), [a, b])
})

test('breakdown stack pushes and pops to a chosen depth', () => {
  const e1 = { steps: [{ column: 'P', value: '20' }], breakdownColumn: 'Status' }
  const e2 = { steps: [{ column: 'P', value: '20' }, { column: 'Status', value: 'Active' }], breakdownColumn: 'Site' }
  const stack = pushBreakdown(pushBreakdown([], e1), e2)
  assert.deepEqual(popBreakdown(stack, 1), [e1])
  assert.deepEqual(popBreakdown(stack, 0), [])
  assert.equal(popBreakdown(stack, 5), stack)
  assert.deepEqual(breakdownCrumbs('Enrolments', stack), [
    { label: 'Enrolments', keep: 0 },
    { label: 'P = 20 · by Status', keep: 1 },
    { label: 'Status = Active · by Site', keep: 2 },
  ])
})

test('breakdown choices drop trail columns and the column already grouped by', () => {
  const allowed = allowedBreakdownColumns(
    [col('Program'), col('N', 'count', 'number'), col('Status')],
    ['Site', 'Status'],
    ['Enrolled on'],
  )
  assert.deepEqual(allowed, ['Program', 'Status', 'Site', 'Enrolled on'])
  assert.deepEqual(
    breakdownChoicesFor(allowed, [{ column: 'Program', value: '20' }], { steps: [], breakdownColumn: 'Status' }),
    ['Site', 'Enrolled on'],
  )
})

test('the clicked number is only reported for exactly one Count', () => {
  assert.equal(clickedCount([col('P'), col('N', 'count', 'number')], { P: 'x', N: 140 }), 140)
  assert.equal(clickedCount([col('N', 'count'), col('M', 'count')], { N: 1, M: 2 }), null)
  assert.equal(clickedCount([col('S', 'sum')], { S: 5 }), null)
  assert.equal(clickedCount([col('N', 'count')], { N: 2 ** 60 }), null)
})

test('a grid row becomes steps for its plain columns only; unsafe keys give null', () => {
  const cols = [col('P', null, 'number'), col('Status'), col('N', 'count', 'number')]
  assert.deepEqual(rowSteps(cols, { P: 20, Status: null, N: 3 }), [{ column: 'P', value: '20' }, { column: 'Status', value: null }])
  assert.equal(rowSteps(cols, { P: 2 ** 60, Status: 'a', N: 1 }), null)
  assert.equal(rowSteps([col('N', 'count')], { N: 1 }), null)
})

test('only the chart drill route is the open one', () => {
  assert.equal(isChartDrillPath('/queries/a/charts/b/drill'), true)
  assert.equal(isChartDrillPath('/queries/a/drill'), false)
  assert.equal(isChartDrillPath('/queries/a'), false)
})

test('parameter values read plainly', () => {
  assert.equal(describeParameterValue('["a","b"]'), 'a, b')
  assert.equal(describeParameterValue('2026-01-01~2026-02-01'), '2026-01-01 to 2026-02-01')
  assert.equal(describeParameterValue('last_30_days'), 'last 30 days')
})

test('decodeTrail sees what drillUrl wrote', () => {
  const url = drillUrl({ queryId: 'q', steps: [{ column: 'é', value: '😀' }] })
  assert.deepEqual(decodeTrail(new URL(url, 'http://x').searchParams.get('s')!), [{ column: 'é', value: '😀' }])
})
