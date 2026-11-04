import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { test } from 'node:test'
import { MAX_FUNNEL_STEPS, MAX_WATERFALL_BARS, buildFunnel, buildTrend, buildWaterfall } from '../src/features/visualizations/story-models.ts'
import type { VisualizationOptions } from '../src/types/index.ts'

const opts = (patch: Partial<VisualizationOptions> = {}): VisualizationOptions => ({
  version: 1,
  x: 'Step',
  measures: ['N'],
  stacked: false,
  sortX: 'none',
  ...patch,
})
const rowsOf = (...pairs: [string, unknown][]) => pairs.map(([Step, N]) => ({ Step, N }))

test('funnel percentages: 100, 50, 25', () => {
  const m = buildFunnel(opts(), rowsOf(['A', 100], ['B', 50], ['C', 25]))
  assert.deepEqual(m.steps.map((s) => s.pctOfFirst), [100, 50, 25])
  assert.deepEqual(m.steps.map((s) => s.pctOfPrevious), [null, 50, 50])
  assert.equal(m.skipped, 0)
})

test('funnel zero denominators give null, never Infinity', () => {
  const zeroFirst = buildFunnel(opts(), rowsOf(['A', 0], ['B', 5]))
  assert.equal(zeroFirst.steps[1].pctOfFirst, null)
  assert.equal(zeroFirst.steps[1].pctOfPrevious, null)
  const zeroPrev = buildFunnel(opts(), rowsOf(['A', 10], ['B', 0], ['C', 4]))
  assert.equal(zeroPrev.steps[1].pctOfPrevious, 0)
  assert.equal(zeroPrev.steps[2].pctOfPrevious, null)
  assert.equal(zeroPrev.steps[2].pctOfFirst, 40)
})

test('funnel single step and missing values', () => {
  const one = buildFunnel(opts(), rowsOf(['A', 7]))
  assert.deepEqual(one.steps, [{ name: 'A', value: 7, pctOfFirst: 100, pctOfPrevious: null }])
  const gap = buildFunnel(opts(), rowsOf(['A', 10], ['B', null], ['C', 5], ['D', 'x']))
  assert.equal(gap.steps[1].value, null)
  assert.equal(gap.steps[1].pctOfFirst, null)
  assert.equal(gap.steps[2].pctOfPrevious, null)
  assert.equal(gap.skipped, 1)
  assert.equal(gap.steps[3].value, null)
})

test('funnel keeps query order unless sorted', () => {
  const rows = rowsOf(['B', 2], ['A', 1], ['C', 3])
  assert.deepEqual(buildFunnel(opts(), rows).steps.map((s) => s.name), ['B', 'A', 'C'])
  assert.deepEqual(buildFunnel(opts({ sortX: 'asc' }), rows).steps.map((s) => s.name), ['A', 'B', 'C'])
  assert.deepEqual(buildFunnel(opts({ sortX: 'desc' }), rows).steps.map((s) => s.name), ['C', 'B', 'A'])
})

test('funnel does not merge duplicate step names', () => {
  const m = buildFunnel(opts(), rowsOf(['A', 10], ['A', 5]))
  assert.equal(m.steps.length, 2)
})

test('waterfall +10 -3 +5 ends in a total of 12 and chains the bases', () => {
  const m = buildWaterfall(opts(), rowsOf(['a', 10], ['b', -3], ['c', 5]))
  assert.deepEqual(m.bars, [
    { name: 'a', base: 0, size: 10, kind: 'up', value: 10 },
    { name: 'b', base: 7, size: 3, kind: 'down', value: -3 },
    { name: 'c', base: 7, size: 5, kind: 'up', value: 5 },
    { name: 'Total', base: 0, size: 12, kind: 'total', value: 12 },
  ])
  assert.equal(m.skipped, 0)
})

test('waterfall bar crossing zero keeps size non-negative and base at the low end', () => {
  const m = buildWaterfall(opts(), rowsOf(['a', 5], ['b', -8]))
  assert.deepEqual(m.bars[1], { name: 'b', base: -3, size: 8, kind: 'down', value: -8 })
  assert.deepEqual(m.bars[2], { name: 'Total', base: -3, size: 3, kind: 'total', value: -3 })
})

test('waterfall skips missing amounts and counts them', () => {
  const m = buildWaterfall(opts(), rowsOf(['a', 10], ['b', null], ['c', 'x'], ['d', 2]))
  assert.equal(m.bars.length, 3)
  assert.equal(m.skipped, 2)
  assert.equal(m.bars[2].value, 12)
})

test('waterfall keeps a real Total category apart from the final total and honors sort', () => {
  const m = buildWaterfall(opts({ sortX: 'desc' }), rowsOf(['b', 2], ['Total', 1]))
  assert.deepEqual(m.bars.map((b) => [b.name, b.kind]), [['Total', 'up'], ['b', 'up'], ['Total', 'total']])
})

const trendOpts = () => opts({ x: 'Day', measures: ['N'], sortX: 'none' })
const dayRows = (...pairs: [unknown, unknown][]) => pairs.map(([Day, N]) => ({ Day, N }))

test('trend sorts by X and uses the latest value (AC-C5)', () => {
  const m = buildTrend(trendOpts(), dayRows(['2024-03-01', 130], ['2024-01-01', 100], ['2024-02-01', 110]))
  assert.deepEqual(m.points.map((p) => p.x), ['2024-01-01', '2024-02-01', '2024-03-01'])
  assert.equal(m.latest, 130)
  assert.equal(m.latestLabel, '2024-03-01')
  assert.equal(m.previous, 110)
  assert.equal(m.previousLabel, '2024-02-01')
  assert.equal(m.change, 20)
  assert.ok(Math.abs((m.changePct ?? 0) - (20 / 110) * 100) < 1e-9)
})

test('trend falls back to the last non-empty value and names its row', () => {
  const m = buildTrend(trendOpts(), dayRows(['2024-01-01', 100], ['2024-02-01', 120], ['2024-03-01', null]))
  assert.equal(m.latest, 120)
  assert.equal(m.latestLabel, '2024-02-01')
  assert.equal(m.previousLabel, '2024-01-01')
  assert.equal(m.points[2].y, null)
})

test('trend change percent is null when the previous value is 0', () => {
  const m = buildTrend(trendOpts(), dayRows([1, 0], [2, 5]))
  assert.equal(m.change, 5)
  assert.equal(m.changePct, null)
})

test('trend with one row or no values has no comparison', () => {
  const one = buildTrend(trendOpts(), dayRows(['2024-01-01', 7]))
  assert.equal(one.latest, 7)
  assert.equal(one.previous, null)
  assert.equal(one.previousLabel, null)
  assert.equal(one.change, null)
  assert.equal(one.changePct, null)
  const none = buildTrend(trendOpts(), dayRows(['2024-01-01', null], ['2024-01-02', null]))
  assert.equal(none.latest, null)
  assert.equal(buildTrend(trendOpts(), []).points.length, 0)
})

test('trend skips and counts rows without an X, and unusable values', () => {
  const m = buildTrend(trendOpts(), dayRows([null, 5], ['2024-01-01', 'x'], ['2024-01-02', 3], ['', 1]))
  assert.equal(m.points.length, 2)
  assert.equal(m.skipped, 3)
  assert.equal(m.latest, 3)
})

test('trend keeps date labels YYYY-MM-DD and sorts date-times chronologically', () => {
  const m = buildTrend(
    trendOpts(),
    dayRows(['2024-01-10T00:00:00', 3], ['2024-01-02T09:30:00', 2], ['2024-01-02T00:00:00', 1], ['2024-01-09T00:00:00', 9]),
  )
  assert.deepEqual(m.points.map((p) => p.x), ['2024-01-02', '2024-01-02T09:30:00', '2024-01-09', '2024-01-10'])
  assert.equal(m.latestLabel, '2024-01-10')
})

test('trend sorts numeric X numerically', () => {
  const m = buildTrend(trendOpts(), dayRows([10, 1], [9, 2], [100, 3]))
  assert.deepEqual(m.points.map((p) => p.x), ['9', '10', '100'])
})

test('trend order does not depend on the machine time zone', () => {
  const code = `
    import { buildTrend } from './src/features/visualizations/story-models.ts'
    const rows = ['2024-01-02T09:30:00', '2024-01-02T00:00:00', '2024-01-02T09:30:00+10:00', '2024-01-01', '2024-01-02T10:00:00Z', '2024-01-02']
      .map((Day, i) => ({ Day, N: i }))
    const m = buildTrend({ version: 1, x: 'Day', measures: ['N'], stacked: false, sortX: 'none' }, rows)
    console.log(JSON.stringify(m.points.map((p) => p.y)))
  `
  // UTC instants: 01-01=#3, 01-02 00:00 (#1, #5), 09:30+10 = 01-01 23:30Z (#2), 09:30 (#0), 10:00Z (#4)
  const expected = [3, 2, 1, 5, 0, 4]
  for (const TZ of ['America/Los_Angeles', 'Pacific/Auckland', 'UTC', 'Asia/Kolkata']) {
    const out = spawnSync(process.execPath, ['--input-type=module', '-e', code], { env: { ...process.env, TZ }, encoding: 'utf8', cwd: process.cwd() })
    assert.equal(out.status, 0, out.stderr)
    assert.deepEqual(JSON.parse(out.stdout), expected, `TZ=${TZ}`)
  }
})

const manyRows = (n: number, value: unknown = 1) => Array.from({ length: n }, (_, i) => ({ Step: `s${i}`, N: value }))

test('funnel over 20 steps carries the server message; 20 is fine', () => {
  assert.equal(buildFunnel(opts(), manyRows(MAX_FUNNEL_STEPS)).problem, null)
  assert.equal(buildFunnel(opts(), manyRows(25)).problem, 'Too many steps for a funnel (max 20).')
})

test('funnel with a negative value carries the server message, after the step limit', () => {
  assert.equal(buildFunnel(opts(), rowsOf(['A', 10], ['B', -1])).problem, 'A funnel cannot show negative values.')
  assert.equal(buildFunnel(opts(), manyRows(21, -1)).problem, 'Too many steps for a funnel (max 20).')
  assert.equal(buildFunnel(opts(), rowsOf(['A', 10], ['B', null], ['C', 'x'])).problem, null)
})

test('waterfall over 30 rows carries the server message; 30 is fine; negatives are allowed', () => {
  assert.equal(buildWaterfall(opts(), manyRows(MAX_WATERFALL_BARS)).problem, null)
  assert.equal(
    buildWaterfall(opts(), manyRows(31)).problem,
    'Too many bars for a waterfall (max 30); add filters or totals.',
  )
  assert.equal(buildWaterfall(opts(), rowsOf(['a', -4], ['b', 2])).problem, null)
})

test('funnel with a missing first step has no percent of first', () => {
  const m = buildFunnel(opts(), rowsOf(['A', null], ['B', 5]))
  assert.equal(m.steps[0].value, null)
  assert.equal(m.steps[1].pctOfFirst, null)
  assert.equal(m.steps[1].pctOfPrevious, null)
})

test('funnel of all zeros gives no percentages', () => {
  const m = buildFunnel(opts(), rowsOf(['A', 0], ['B', 0]))
  assert.deepEqual(m.steps.map((s) => s.pctOfFirst), [null, null])
  assert.deepEqual(m.steps.map((s) => s.pctOfPrevious), [null, null])
})

test('waterfall +5 -8 +10 ends in a total of 7', () => {
  const m = buildWaterfall(opts(), rowsOf(['a', 5], ['b', -8], ['c', 10]))
  assert.deepEqual(m.bars.map((b) => [b.base, b.size, b.kind]), [[0, 5, 'up'], [-3, 8, 'down'], [-3, 10, 'up'], [0, 7, 'total']])
  assert.equal(m.bars[3].value, 7)
})

test('waterfall of only missing values has no bars and counts every skip', () => {
  const m = buildWaterfall(opts(), rowsOf(['a', null], ['b', 'x'], ['c', null]))
  assert.equal(m.bars.length, 0)
  assert.equal(m.skipped, 3)
})

test('a column named constructor that the rows lack reads as missing, not as the inherited member', () => {
  const o = opts({ x: 'constructor', measures: ['constructor'] })
  const funnel = buildFunnel(o, rowsOf(['A', 1]))
  assert.equal(funnel.steps[0].value, null)
  assert.equal(funnel.steps[0].name, '(blank)')
  const waterfall = buildWaterfall(o, rowsOf(['A', 1]))
  assert.equal(waterfall.bars.length, 0)
})
