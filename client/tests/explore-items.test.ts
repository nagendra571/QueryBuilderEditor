import assert from 'node:assert/strict'
import { test } from 'node:test'
import { buildCartesian, buildPie, buildScatter } from '../src/features/visualizations/chart-data.ts'
import { buildFunnel, buildWaterfall } from '../src/features/visualizations/story-models.ts'
import { buildPivot } from '../src/features/visualizations/pivot-model.ts'
import { pickRow, rowsOf, seriesRowsOf } from '../src/features/visualizations/mark-sources.ts'
import { exploreItems, MAX_EXPLORE_ITEMS } from '../src/features/drill/explore-items.ts'
import type { QueryResultColumnDto, VisualizationOptions } from '../src/types/index.ts'

const opts = (o: Partial<VisualizationOptions>): VisualizationOptions => ({
  version: 1,
  measures: [],
  stacked: false,
  sortX: 'none',
  ...o,
})
const dim = (name: string, dataType: QueryResultColumnDto['dataType'] = 'text'): QueryResultColumnDto => ({
  name,
  dataType,
  aggregate: 'none',
})
const agg = (name: string): QueryResultColumnDto => ({ name, dataType: 'number', aggregate: 'sum' })

test('pickRow: one row identifies the mark; several only when no key column tells them apart', () => {
  const a = { k: 1 }
  const b = { k: 2 }
  assert.equal(pickRow([a]), a)
  assert.equal(pickRow([a, b]), a)
  assert.equal(pickRow([a, b], []), a)
  assert.equal(pickRow([a, b], ['k']), undefined)
  assert.equal(pickRow([]), undefined)
})

test('a sorted cartesian point still finds its own source row', () => {
  const rows = [
    { P: 'b', T: 2 },
    { P: 'a', T: 1 },
  ]
  const m = buildCartesian(opts({ x: 'P', measures: ['T'], sortX: 'asc' }), rows)
  assert.equal(m.points[0].x, 'a')
  assert.equal(seriesRowsOf(m.points[0], 's0')[0], rows[1])
  assert.equal(seriesRowsOf(m.points[1], 's0')[0], rows[0])
})

test('a split cartesian point keeps the row of each series separately', () => {
  const rows = [
    { P: 'a', S: 'x', T: 1 },
    { P: 'a', S: 'y', T: 2 },
  ]
  const m = buildCartesian(opts({ x: 'P', series: 'S', measures: ['T'] }), rows)
  assert.equal(m.points.length, 1)
  assert.equal(seriesRowsOf(m.points[0], 's0')[0], rows[0])
  assert.equal(seriesRowsOf(m.points[0], 's1')[0], rows[1])
})

test('pie, funnel, waterfall and scatter marks carry their rows; the waterfall total has none', () => {
  const rows = [
    { P: 'a', T: 3 },
    { P: 'b', T: 1 },
  ]
  const pie = buildPie(opts({ x: 'P', measures: ['T'], sortX: 'desc' }), rows)
  assert.equal(rowsOf(pie.slices[0])[0], rows[1])
  assert.equal(rowsOf(buildFunnel(opts({ x: 'P', measures: ['T'] }), rows).steps[1])[0], rows[1])
  const wf = buildWaterfall(opts({ x: 'P', measures: ['T'] }), rows)
  assert.equal(rowsOf(wf.bars[0])[0], rows[0])
  assert.deepEqual(rowsOf(wf.bars[2]), [])
  const sc = buildScatter(opts({ x: 'T', measures: ['T'] }), rows)
  assert.equal(rowsOf(sc.groups[0].points[1])[0], rows[1])
})

test('pivot keeps the raw header values, not the shown text', () => {
  const rows = [{ D: '2026-01-02T00:00:00', S: 'on', T: 1 }]
  const m = buildPivot(opts({ pivot: { rows: ['D'], column: 'S', values: ['T'], totals: true } as never }), rows, () => 'sum')
  assert.deepEqual(m.rowHeaders, [['2026-01-02']])
  assert.deepEqual(m.rowRaw, [['2026-01-02T00:00:00']])
  assert.deepEqual(m.columnRaw, ['on'])
})

test('exploreItems lists a bar chart once per point and skips marks that cannot be drilled', () => {
  const columns = [dim('P'), agg('T')]
  const rows = [
    { P: 'a', T: 3 },
    { P: 'b', T: 1 },
  ]
  const { items, more } = exploreItems('bar', opts({ x: 'P', measures: ['T'] }), rows, columns)
  assert.deepEqual(items.map((i) => [i.label, i.value]), [['a', '3'], ['b', '1']])
  assert.equal(more, 0)

  // A key past 2^53 was rounded in transit, so it must not be offered.
  const unsafe = exploreItems('bar', opts({ x: 'P', measures: ['T'] }), [{ P: 'a', T: 1, Id: 2 ** 60 }], [...columns, dim('Id', 'number')], ['Id'])
  assert.equal(unsafe.items.length, 0)
})

test('exploreItems: counter has one item, a non-aggregated table none, and the list is capped', () => {
  const counter = exploreItems('counter', opts({ counter: { value: 'T' } as never }), [{ T: 5 }], [agg('T')])
  assert.equal(counter.items.length, 1)
  assert.equal(counter.items[0].value, '5')

  assert.equal(exploreItems('table', opts({ table: { columns: ['P'] } as never }), [{ P: 'a' }], [dim('P')]).items.length, 0)

  const many = Array.from({ length: MAX_EXPLORE_ITEMS + 5 }, (_, i) => ({ P: `p${i}`, T: i }))
  const capped = exploreItems('bar', opts({ x: 'P', measures: ['T'] }), many, [dim('P'), agg('T')])
  assert.equal(capped.items.length, MAX_EXPLORE_ITEMS)
  assert.equal(capped.more, 5)
})

test('exploreItems offers nothing where the renderer shows a message instead of the chart', () => {
  const cols = [dim('P'), agg('T')]
  const none = (type: Parameters<typeof exploreItems>[0], o: Partial<VisualizationOptions>, rows: Record<string, unknown>[], columns = cols) =>
    assert.equal(exploreItems(type, opts(o), rows, columns).items.length, 0)

  const many = Array.from({ length: 13 }, (_, i) => ({ P: `p${i}`, T: i + 1 }))
  none('pie', { x: 'P', measures: ['T'] }, many)
  none('pie', { x: 'P', measures: ['T'] }, [{ P: 'a', T: -1 }, { P: 'b', T: 2 }])
  assert.equal(exploreItems('pie', opts({ x: 'P', measures: ['T'] }), many.slice(0, 3), cols).items.length, 3)

  none('scatter', { x: 'T', measures: ['T'], label: 'P', size: 'T' }, [{ P: 'a', T: -1 }])
  none('funnel', { x: 'P', measures: ['T'] }, [{ P: 'a', T: -1 }])
  none('waterfall', { x: 'P', measures: ['T'] }, Array.from({ length: 31 }, (_, i) => ({ P: `p${i}`, T: 1 })))

  const wide = Array.from({ length: 51 }, (_, i) => ({ P: 'r', C: `c${i}`, T: 1 }))
  none('pivot', { pivot: { rows: ['P'], column: 'C', values: ['T'], totals: true } as never }, wide, [dim('P'), dim('C'), agg('T')])

  none('bar', { x: 'P', measures: ['T'] }, [])
})

import { pointCenters, nearestPointIndex, rowAtX, tagSeriesRows } from '../src/features/visualizations/mark-sources.ts'

test('rowAtX resolves a single series and refuses an ambiguous X', () => {
  const a = { id: 1 }
  const b = { id: 2 }
  const one = tagSeriesRows({}, { s1: [a] })
  assert.equal(rowAtX(one, ['s1']), a)
  const two = tagSeriesRows({}, { s1: [a], s2: [b] })
  assert.equal(rowAtX(two, ['s1', 's2']), undefined)
  const same = tagSeriesRows({}, { s1: [a], s2: [a] })
  assert.equal(rowAtX(same, ['s1', 's2']), a)
  assert.equal(rowAtX({}, ['s1']), undefined)
  assert.equal(rowAtX(one, []), undefined)
})

test('nearestPointIndex maps a click x to the nearest point with no prior hover', () => {
  const xs = [10, 50, 90, undefined, 170]
  assert.equal(nearestPointIndex(xs, 12, 0, 200), 0)
  assert.equal(nearestPointIndex(xs, 71, 0, 200), 2)
  assert.equal(nearestPointIndex(xs, 140, 0, 200), 4)
  assert.equal(nearestPointIndex(xs, 5, 20, 200), undefined)
  assert.equal(nearestPointIndex(xs, 210, 0, 200), undefined)
  assert.equal(nearestPointIndex([], 10, 0, 200), undefined)
})

test('pointCenters uses band centres so a click right of the centre still maps to its band', () => {
  const lefts: Record<string, number> = { a: 0, b: 100, c: 200 }
  const band = Object.assign((v: string) => lefts[v], { bandwidth: () => 100 })
  const xs = pointCenters(band as never, ['a', 'b', 'c'])
  assert.deepEqual(xs, [50, 150, 250])
  assert.equal(nearestPointIndex(xs, 140, 0, 300), 1)
  assert.equal(nearestPointIndex(xs, 195, 0, 300), 1)
  const point = (v: string) => ({ a: 10, b: 60 })[v as 'a' | 'b']
  assert.deepEqual(pointCenters(point as never, ['a', 'b', 'z']), [10, 60, undefined])
})
