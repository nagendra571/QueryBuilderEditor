import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { test } from 'node:test'
import {
  buildModel,
  buildOptions,
  buildScatter,
  canSplit,
  compareBlankLast,
  describeChart,
  formatScatterX,
  cellValue,
  defaultTableColumns,
  duplicateXCount,
  hasStackGap,
  isNumberColumn,
  measureLimit,
  negativeCategoryCount,
  optionsProblem,
  pageRows,
  scatterHasTime,
  TABLE_PAGE_SIZE,
  referencedColumns,
  toPercentStack,
  typeNoun,
  type CartesianModel,
  type ChartRoles,
  type OutputColumn,
} from '../src/features/visualizations/chart-data.ts'
import { formatPlainNumber, percentTick, truncateLabel } from '../src/features/visualizations/format.ts'
import { TYPE_GROUPS } from '../src/features/visualizations/chart-types.ts'
import type { VisualizationOptions } from '../src/types/index.ts'

const series = [
  { key: 's0', label: 'A' },
  { key: 's1', label: 'B' },
]

const base: VisualizationOptions = { version: 1, x: 'Region', measures: ['Sales'], series: null, stacked: false, sortX: 'none' }
const opts = (patch: Partial<VisualizationOptions>): VisualizationOptions => ({ ...base, ...patch })

const col = (name: string, dataType: OutputColumn['dataType'], aggregate: OutputColumn['aggregate'] = 'none'): OutputColumn => ({
  name,
  dataType,
  isAggregate: aggregate !== 'none',
  aggregate,
})
const columns = [
  col('Region', 'text'),
  col('Day', 'date'),
  col('Sales', 'number', 'sum'),
  col('Cost', 'number', 'sum'),
  col('Profit', 'number', 'sum'),
]

test('toPercentStack divides each category by its total', () => {
  const out = toPercentStack([{ x: 'EU', s0: 30, s1: 70 }], series)
  assert.equal(out[0].s0, 30)
  assert.equal(out[0].s1, 70)
  assert.equal(out[0].x, 'EU')
})

test('toPercentStack leaves a zero-total category empty with no NaN', () => {
  const out = toPercentStack([{ x: 'EU', s0: 0, s1: 0 }], series)
  assert.equal(out[0].s0, null)
  assert.equal(out[0].s1, null)
})

test('toPercentStack keeps a missing value missing and divides by the non-null total', () => {
  const out = toPercentStack([{ x: 'EU', s0: null, s1: 50 }], series)
  assert.equal(out[0].s0, null)
  assert.equal(out[0].s1, 100)
})

test('duplicateXCount counts rows that share an X', () => {
  const rows = [{ Region: 'EU' }, { Region: 'EU' }, { Region: 'US' }]
  assert.equal(duplicateXCount(opts({}), rows), 2)
  assert.equal(duplicateXCount(opts({ series: 'Channel' }), rows), 0)
  assert.equal(duplicateXCount(opts({}), [{ Region: 'EU' }, { Region: 'US' }]), 0)
})

test('hasStackGap is true only when a stacked series is missing at some point', () => {
  const model = (points: CartesianModel['points']): CartesianModel => ({ kind: 'cartesian', series, points, skipped: 0 })
  assert.equal(hasStackGap(model([{ x: 'a', s0: 1, s1: null }])), true)
  assert.equal(hasStackGap(model([{ x: 'a', s0: 1, s1: 2 }])), false)
})

test('optionsProblem does not throw on leftover roles and reports them', () => {
  const scatter = optionsProblem('scatter', opts({ x: 'Region' }), columns)
  assert.equal(typeof scatter, 'string')
  const combo = optionsProblem('combo', opts({ measures: ['Sales', 'Cost'], lineMeasures: ['Gone'] }), columns)
  assert.equal(typeof combo, 'string')
})

test('optionsProblem accepts valid shapes', () => {
  assert.equal(optionsProblem('bar', opts({}), columns), null)
  assert.equal(optionsProblem('combo', opts({ measures: ['Sales', 'Cost'], lineMeasures: ['Cost'] }), columns), null)
  assert.equal(optionsProblem('scatter', opts({ x: 'Day' }), columns), null)
  assert.equal(optionsProblem('area', opts({ stacked: true, percent: true }), columns), null)
})

test('optionsProblem mirrors the server rules', () => {
  assert.match(optionsProblem('bar', opts({ percent: true }), columns)!, /stacked/i)
  assert.ok(optionsProblem('line', opts({ stacked: true }), columns))
  assert.ok(optionsProblem('combo', opts({ measures: ['Sales', 'Cost'], lineMeasures: [] }), columns))
  assert.ok(optionsProblem('combo', opts({ measures: ['Sales', 'Cost'], lineMeasures: ['Sales', 'Cost'] }), columns))
  assert.ok(optionsProblem('combo', opts({ measures: ['Sales', 'Cost'], lineMeasures: ['Cost'], series: 'Region' }), columns))
  assert.ok(optionsProblem('funnel', opts({ measures: ['Sales', 'Cost'] }), columns))
  assert.ok(optionsProblem('waterfall', opts({ series: 'Region' }), columns))
  assert.ok(optionsProblem('trend', opts({ x: 'Region' }), columns))
  assert.equal(optionsProblem('trend', opts({ x: 'Day' }), columns), null)
  assert.ok(optionsProblem('scatter', opts({ x: 'Day', size: 'Region' }), columns))
  assert.ok(optionsProblem('bar', opts({ measures: ['Sales', 'Sales'] }), columns))
  assert.ok(optionsProblem('bar', opts({ measures: ['Sales', 'Cost', 'Profit', 'Sales', 'Cost', 'Profit'] }), columns))
  assert.ok(optionsProblem('pie', opts({ series: 'Region' }), columns))
  assert.ok(optionsProblem('bar', opts({ format: { style: 'number', decimals: 9 } }), columns))
  assert.ok(optionsProblem('bar', opts({ format: { style: 'currency', currency: 'usd' } }), columns))
})

test('optionsProblem covers table and pivot', () => {
  const table = (cols: string[]) => opts({ x: null, measures: [], table: { columns: cols } })
  assert.ok(optionsProblem('table', table([]), columns))
  assert.ok(optionsProblem('table', table(['Region', 'Region']), columns))
  assert.equal(optionsProblem('table', table(['Region', 'Sales']), columns), null)
  const pivot = (p: Partial<NonNullable<VisualizationOptions['pivot']>>) =>
    opts({ x: null, measures: [], pivot: { rows: ['Region'], values: ['Sales'], totals: true, shade: false, ...p } })
  assert.equal(optionsProblem('pivot', pivot({}), columns), null)
  assert.ok(optionsProblem('pivot', pivot({ rows: [] }), columns))
  assert.ok(optionsProblem('pivot', pivot({ column: 'Region' }), columns))
  assert.ok(optionsProblem('pivot', pivot({ values: ['Region'] }), columns))
})

test('referencedColumns follows the server per type', () => {
  const o = opts({
    series: 'Channel',
    measures: ['Sales', 'Cost'],
    lineMeasures: ['Cost', 'Extra'],
    size: 'Size',
    label: 'Label',
  })
  assert.deepEqual(referencedColumns('bar', o), ['Region', 'Channel', 'Sales', 'Cost'])
  assert.deepEqual(referencedColumns('combo', o), ['Region', 'Channel', 'Sales', 'Cost', 'Extra'])
  assert.deepEqual(referencedColumns('scatter', o), ['Region', 'Channel', 'Sales', 'Cost', 'Size', 'Label'])
  assert.deepEqual(referencedColumns('line', o), ['Region', 'Channel', 'Sales', 'Cost'])
  assert.deepEqual(
    referencedColumns('counter', opts({ counter: { value: 'Sales', target: 'Cost', decimals: 0 } })),
    ['Sales', 'Cost'],
  )
  assert.deepEqual(referencedColumns('table', opts({ table: { columns: ['A', 'B', 'A'] } })), ['A', 'B'])
  assert.deepEqual(
    referencedColumns('pivot', opts({ pivot: { rows: ['R1', 'R2'], column: 'C', values: ['V'], totals: true, shade: false } })),
    ['R1', 'R2', 'C', 'V'],
  )
})

test('TYPE_GROUPS lists each built type once', () => {
  const all = TYPE_GROUPS.flatMap((g) => g.types)
  assert.equal(new Set(all).size, all.length)
  assert.deepEqual(TYPE_GROUPS.map((g) => g.label), ['Compare', 'Trend', 'Part of whole', 'Relationship', 'Table', 'Single number'])
  assert.deepEqual(TYPE_GROUPS.find((g) => g.label === 'Table')?.types, ['table', 'pivot'])
  assert.deepEqual(TYPE_GROUPS.find((g) => g.label === 'Trend')?.types, ['line', 'area', 'trend'])
  assert.deepEqual(TYPE_GROUPS.find((g) => g.label === 'Part of whole')?.types, ['pie', 'funnel'])
  assert.deepEqual(TYPE_GROUPS.find((g) => g.label === 'Compare')?.types, ['bar', 'horizontalBar', 'combo', 'waterfall'])
  for (const t of ['bar', 'horizontalBar', 'combo', 'line', 'area', 'pie', 'counter', 'pivot', 'funnel', 'waterfall'] as const) assert.ok(all.includes(t))
})

test('buildModel table keeps the chosen column order and every row', () => {
  const rows = [{ A: 1, B: 'x', C: null }, { A: 2, B: 'y', C: 3 }]
  const model = buildModel('table', opts({ table: { columns: ['C', 'A'] } }), rows)
  assert.deepEqual(model, { kind: 'table', columns: ['C', 'A'], rows })
  assert.deepEqual(buildModel('table', opts({}), rows), { kind: 'table', columns: [], rows })
})

test('pageRows pages by 25 and clamps a page past the end', () => {
  const rows = Array.from({ length: 26 }, (_, i) => ({ i }))
  assert.equal(TABLE_PAGE_SIZE, 25)
  const first = pageRows(rows, 0, TABLE_PAGE_SIZE)
  assert.equal(first.rows.length, 25)
  assert.equal(first.pageCount, 2)
  const last = pageRows(rows, 1, TABLE_PAGE_SIZE)
  assert.deepEqual(last.rows, [{ i: 25 }])
  const past = pageRows(rows, 9, TABLE_PAGE_SIZE)
  assert.equal(past.page, 1)
  assert.deepEqual(past.rows, [{ i: 25 }])
  const negative = pageRows(rows, -3, TABLE_PAGE_SIZE)
  assert.equal(negative.page, 0)
  const empty = pageRows([], 4, TABLE_PAGE_SIZE)
  assert.equal(empty.pageCount, 1)
  assert.equal(empty.page, 0)
})

test('defaultTableColumns takes visible output columns up to the cap of 30', () => {
  const many = Array.from({ length: 35 }, (_, i) => col(`C${i}`, 'text'))
  const picked = defaultTableColumns(many)
  assert.equal(picked.length, 30)
  assert.equal(picked[0], 'C0')
  assert.deepEqual(defaultTableColumns(columns), ['Region', 'Day', 'Sales', 'Cost', 'Profit'])
})

test('isNumberColumn trusts the declared type, else checks the values', () => {
  const rows = [{ A: 1, B: '1', C: null }, { A: 2.5, B: '2', C: null }]
  assert.equal(isNumberColumn('B', [{ name: 'B', dataType: 'number' }], rows), true)
  assert.equal(isNumberColumn('A', [{ name: 'A', dataType: 'text' }], rows), false)
  assert.equal(isNumberColumn('A', undefined, rows), true)
  assert.equal(isNumberColumn('B', undefined, rows), false)
  assert.equal(isNumberColumn('C', undefined, rows), false)
  assert.equal(isNumberColumn('A', [{ name: 'Z', dataType: 'number' }], rows), true)
})

test('prototype-named columns absent from a row read as missing', () => {
  const rows = [{ A: 1 }]
  assert.equal(cellValue(rows[0], 'constructor'), undefined)
  assert.equal(cellValue(rows[0], '__proto__'), undefined)
  assert.equal(cellValue(rows[0], 'A'), 1)
  assert.equal(isNumberColumn('toString', undefined, rows), false)
  assert.equal(isNumberColumn('constructor', undefined, rows), false)
})

const scatterOpts = (patch: Partial<VisualizationOptions> = {}) =>
  opts({ x: 'Day', measures: ['Sales'], ...patch })

test('buildScatter turns ISO date X into UTC timestamps and flags xIsDate', () => {
  const m = buildScatter(scatterOpts(), [
    { Day: '2024-03-05', Sales: 10 },
    { Day: '2024-03-06T00:00:00', Sales: 12 },
    { Day: '2024-03-07T10:30:00Z', Sales: 14 },
    { Day: '2024-03-08T10:30:00+02:00', Sales: 16 },
  ])
  assert.equal(m.xIsDate, true)
  const xs = m.groups[0].points.map((p) => p.x)
  assert.deepEqual(xs, [
    Date.UTC(2024, 2, 5),
    Date.UTC(2024, 2, 6),
    Date.UTC(2024, 2, 7, 10, 30),
    Date.UTC(2024, 2, 8, 8, 30),
  ])
})

test('buildScatter reads the same calendar day whatever the machine time zone', () => {
  const code = `
    import { buildScatter } from ${JSON.stringify(new URL('../src/features/visualizations/chart-data.ts', import.meta.url).href)}
    const m = buildScatter({ version: 1, x: 'D', measures: ['Y'], series: null, stacked: false, sortX: 'none' },
      [{ D: '2024-03-10', Y: 1 }, { D: '2024-03-10T00:00:00', Y: 2 }, { D: '2024-11-03', Y: 3 }])
    console.log(JSON.stringify(m.groups[0].points.map((p) => p.x)))
  `
  const expected = [Date.UTC(2024, 2, 10), Date.UTC(2024, 2, 10), Date.UTC(2024, 10, 3)]
  for (const TZ of ['America/Los_Angeles', 'Pacific/Auckland', 'UTC', 'Asia/Kolkata']) {
    const out = spawnSync(process.execPath, ['--input-type=module', '-e', code], { env: { ...process.env, TZ }, encoding: 'utf8' })
    assert.equal(out.status, 0, out.stderr)
    assert.deepEqual(JSON.parse(out.stdout), expected, `TZ=${TZ}`)
  }
  const d = new Date(Date.UTC(2024, 2, 10))
  assert.equal(d.getUTCDate(), 10)
})

test('buildScatter rejects an impossible calendar date', () => {
  const m = buildScatter(scatterOpts(), [{ Day: '2024-02-31', Sales: 1 }, { Day: '2024-02-28', Sales: 2 }])
  assert.equal(m.skipped, 1)
  assert.equal(m.groups[0].points.length, 1)
})

test('buildScatter treats a numeric string X as a number, not a date', () => {
  const m = buildScatter(scatterOpts({ x: 'N' }), [{ N: '2024', Sales: 1 }, { N: 5, Sales: 2 }])
  assert.equal(m.xIsDate, false)
  assert.deepEqual(m.groups[0].points.map((p) => p.x), [2024, 5])
})

test('buildScatter counts null and unparsable X or Y as skipped, never zero', () => {
  const m = buildScatter(scatterOpts({ x: 'N' }), [
    { N: 1, Sales: 2 },
    { N: null, Sales: 3 },
    { N: 4, Sales: null },
    { N: 'abc', Sales: 5 },
    { N: 6, Sales: 'oops' },
    { N: undefined, Sales: 7 },
  ])
  assert.equal(m.skipped, 5)
  assert.deepEqual(m.groups[0].points, [{ x: 1, y: 2, size: null, label: null }])
})

test('buildScatter without a series gives one group, and a series splits by first appearance', () => {
  const rows = [
    { N: 1, Sales: 1, G: 'B' },
    { N: 2, Sales: 2, G: 'A' },
    { N: 3, Sales: 3, G: 'B' },
    { N: 4, Sales: 4, G: null },
  ]
  const one = buildScatter(scatterOpts({ x: 'N' }), rows)
  assert.equal(one.groups.length, 1)
  assert.equal(one.groups[0].points.length, 4)
  const split = buildScatter(scatterOpts({ x: 'N', series: 'G' }), rows)
  assert.deepEqual(split.groups.map((g) => g.name), ['B', 'A', '(blank)'])
  assert.deepEqual(split.groups[0].points.map((p) => p.x), [1, 3])
})

test('buildScatter size and label: a negative or unparsable size becomes null', () => {
  const m = buildScatter(scatterOpts({ x: 'N', size: 'S', label: 'L' }), [
    { N: 1, Sales: 1, S: 5, L: 'Alpha' },
    { N: 2, Sales: 2, S: -3, L: null },
    { N: 3, Sales: 3, S: 'x', L: '' },
    { N: 4, Sales: 4, S: null, L: 42 },
    { N: 5, Sales: 5, S: '0', L: '<b>hi</b>' },
  ])
  assert.deepEqual(m.groups[0].points.map((p) => p.size), [5, null, null, null, 0])
  assert.deepEqual(m.groups[0].points.map((p) => p.label), ['Alpha', null, null, '42', '<b>hi</b>'])
  assert.equal(m.skipped, 0)
})

test('buildScatter ignores size and label columns that are not set', () => {
  const m = buildScatter(scatterOpts({ x: 'N' }), [{ N: 1, Sales: 1, S: 9, L: 'a' }])
  assert.deepEqual(m.groups[0].points[0], { x: 1, y: 1, size: null, label: null })
})

test('formatScatterX formats dates in UTC and numbers plainly', () => {
  assert.equal(formatScatterX(Date.UTC(2024, 0, 1), true, false), new Intl.DateTimeFormat(undefined, { timeZone: 'UTC', dateStyle: 'medium' }).format(Date.UTC(2024, 0, 1)))
  assert.match(formatScatterX(Date.UTC(2024, 0, 1, 23, 30), true, true), /23|11/)
  assert.equal(formatScatterX(1234.5, false, false), formatPlainNumber(1234.5))
})

test('Relationship group holds scatter', () => {
  assert.deepEqual(TYPE_GROUPS.find((g) => g.label === 'Relationship')?.types, ['scatter'])
})

test('formatPlainNumber never compacts and keeps a fraction', () => {
  assert.ok(!formatPlainNumber(2024).includes('K'))
  assert.equal(formatPlainNumber(2024), '2024')
  assert.match(formatPlainNumber(1500.5), /1.?500[.,]5/)
  assert.equal(formatScatterX(2024, false, false), formatPlainNumber(2024))
})

test('counter target gives percent of target; none or zero gives null', () => {
  const rows = [{ Sales: 82, Goal: 100 }]
  const withTarget = buildModel('counter', opts({ counter: { value: 'Sales', target: 'Goal', decimals: 0 } }), rows)
  assert.ok(withTarget.kind === 'counter' && withTarget.pctOfTarget === 82 && withTarget.target === 100)
  const zero = buildModel('counter', opts({ counter: { value: 'Sales', target: 'Goal', decimals: 0 } }), [{ Sales: 82, Goal: 0 }])
  assert.ok(zero.kind === 'counter' && zero.pctOfTarget === null)
  const none = buildModel('counter', opts({ counter: { value: 'Sales', target: null, decimals: 0 } }), rows)
  assert.ok(none.kind === 'counter' && none.pctOfTarget === null && none.target === null && none.value === 82)
  const noColumn = buildModel('counter', opts({ counter: { value: 'Sales', decimals: 0 } }), rows)
  assert.ok(noColumn.kind === 'counter' && noColumn.target === null)
  const over = buildModel('counter', opts({ counter: { value: 'Sales', target: 'Goal', decimals: 0 } }), [{ Sales: 130, Goal: 100 }])
  assert.ok(over.kind === 'counter' && over.pctOfTarget === 130)
})

test('counter target of zero or below gives no percent of target', () => {
  const o = opts({ counter: { value: 'Sales', target: 'Goal', decimals: 0 } })
  const neg = buildModel('counter', o, [{ Sales: 5, Goal: -10 }])
  assert.ok(neg.kind === 'counter' && neg.pctOfTarget === null && neg.target === -10)
})

test('toPercentStack leaves a category with a negative value empty and counts it', () => {
  const points = [
    { x: 'EU', s0: 50, s1: -30 },
    { x: 'US', s0: 30, s1: 70 },
  ]
  const out = toPercentStack(points, series)
  assert.equal(out[0].s0, null)
  assert.equal(out[0].s1, null)
  assert.equal(out[1].s0, 30)
  assert.equal(negativeCategoryCount(points, series), 1)
  assert.equal(negativeCategoryCount([{ x: 'a', s0: 1, s1: null }], series), 0)
})

test('describeChart says 100% stacked for a percent stack and keeps the plain name otherwise', () => {
  const plain = buildModel('bar', opts({ stacked: true }), [{ Region: 'EU', Sales: 1 }])
  assert.match(describeChart('bar', opts({ stacked: true, percent: true }), plain), /^100% stacked bar chart:/)
  assert.match(describeChart('area', opts({ stacked: true, percent: true }), plain), /^100% stacked area chart:/)
  assert.match(describeChart('bar', opts({ stacked: true }), plain), /^Bar chart:/)
  assert.match(describeChart('line', opts({ percent: true }), plain), /^Line chart:/)
})

test('percentTick rounds floating-point sums so 100 never prints with a long tail', () => {
  assert.equal(percentTick(100.00000000000001), '100%')
  assert.equal(percentTick(0.1 + 0.2), '0.3%')
  assert.equal(percentTick(25), '25%')
})

test('typeNoun reads naturally for every type', () => {
  assert.equal(typeNoun('table'), 'Table')
  assert.equal(typeNoun('pivot'), 'Pivot table')
  assert.equal(typeNoun('bar'), 'Bar chart')
  assert.equal(typeNoun('horizontalBar'), 'Horizontal bar chart')
})

test('scatter with a negative bubble size carries the server message', () => {
  const o = opts({ x: 'N', measures: ['Y'], size: 'Z' })
  const rows = [
    { N: 1, Y: 2, Z: 5 },
    { N: 2, Y: 3, Z: -1 },
  ]
  assert.equal(buildScatter(o, rows).problem, 'Bubble size cannot be negative.')
  assert.equal(buildScatter(o, [{ N: 1, Y: 2, Z: 5 }]).problem, null)
  assert.equal(buildScatter(opts({ x: 'N', measures: ['Y'] }), rows).problem, null)
})

test('scatterHasTime is true only for date X values that are not midnight', () => {
  const o = opts({ x: 'D', measures: ['Y'] })
  assert.equal(scatterHasTime(buildScatter(o, [{ D: '2024-01-01', Y: 1 }])), false)
  assert.equal(scatterHasTime(buildScatter(o, [{ D: '2024-01-01T09:30:00', Y: 1 }])), true)
  assert.equal(scatterHasTime(buildScatter(opts({ x: 'N', measures: ['Y'] }), [{ N: 1.5, Y: 1 }])), false)
})

test('optionsProblem refuses a name shared by two output columns and a scatter with two measures', () => {
  const dup = [...columns, col('Sales', 'number', 'avg')]
  assert.equal(optionsProblem('bar', opts({}), dup), 'More than one column is named Sales; give them different names.')
  assert.equal(optionsProblem('bar', opts({ measures: ['Cost'] }), dup), null)
  const two = optionsProblem('scatter', opts({ x: 'Day', measures: ['Sales', 'Cost'] }), columns)
  assert.match(two ?? '', /exactly one measure/)
  assert.equal(optionsProblem('scatter', opts({ x: 'Day', measures: ['Sales'] }), columns), null)
})

const roles = (patch: Partial<ChartRoles> = {}): ChartRoles => ({
  x: 'Region',
  measures: ['Sales', 'Cost'],
  series: '',
  stacked: true,
  percent: true,
  lineMeasures: ['Cost'],
  sortX: 'asc',
  counter: { value: 'Sales', label: '', decimals: 0, target: '' },
  tableColumns: ['Region'],
  pivot: { rows: ['Region'], column: '', values: ['Sales'], totals: true, shade: false },
  size: 'Cost',
  label: 'Region',
  format: null,
  ...patch,
})

test('buildOptions: line drops stacked and percent; bars keep them', () => {
  const line = buildOptions('line', roles())
  assert.equal(line.stacked, false)
  assert.equal(line.percent, false)
  const bar = buildOptions('bar', roles())
  assert.equal(bar.stacked, true)
  assert.equal(bar.percent, true)
  assert.equal(buildOptions('bar', roles({ stacked: false })).percent, false)
})

test('buildOptions: pie drops series and keeps one measure', () => {
  const pie = buildOptions('pie', roles({ series: 'Channel' }))
  assert.equal(pie.series, null)
  assert.deepEqual(pie.measures, ['Sales'])
  assert.equal(buildOptions('bar', roles({ series: 'Channel' })).series, 'Channel')
  assert.deepEqual(buildOptions('bar', roles({ series: 'Channel' })).measures, ['Sales'])
})

test('buildOptions: only a combo keeps lineMeasures, and only those that are still measures', () => {
  assert.deepEqual(buildOptions('combo', roles()).lineMeasures, ['Cost'])
  assert.deepEqual(buildOptions('combo', roles({ lineMeasures: ['Gone', 'Cost'] })).lineMeasures, ['Cost'])
  assert.deepEqual(buildOptions('bar', roles()).lineMeasures, [])
})

test('buildOptions: only a scatter keeps size and label', () => {
  const scatter = buildOptions('scatter', roles({ series: 'Channel' }))
  assert.equal(scatter.size, 'Cost')
  assert.equal(scatter.label, 'Region')
  assert.equal(scatter.series, 'Channel')
  assert.deepEqual(scatter.measures, ['Sales'])
  for (const type of ['bar', 'combo', 'funnel', 'waterfall', 'trend', 'pie'] as const) {
    const o = buildOptions(type, roles())
    assert.equal(o.size, undefined, type)
    assert.equal(o.label, undefined, type)
  }
})

test('buildOptions: each new type saves only its own roles', () => {
  const funnel = buildOptions('funnel', roles({ series: 'Channel' }))
  assert.deepEqual(
    [funnel.series, funnel.stacked, funnel.percent, funnel.lineMeasures, funnel.measures],
    [null, false, false, [], ['Sales']],
  )
  assert.equal(funnel.sortX, 'asc')
  const trend = buildOptions('trend', roles())
  assert.deepEqual([trend.sortX, trend.series, trend.measures], ['none', null, ['Sales']])
  const table = buildOptions('table', roles())
  assert.deepEqual([table.table, table.x, table.measures, table.pivot], [{ columns: ['Region'] }, null, [], undefined])
  const pivot = buildOptions('pivot', roles())
  assert.deepEqual(pivot.pivot, { rows: ['Region'], column: null, values: ['Sales'], totals: true, shade: false })
  assert.deepEqual([pivot.x, pivot.measures, pivot.table], [null, [], null])
  const counter = buildOptions('counter', roles())
  assert.deepEqual(counter.counter, { value: 'Sales', label: null, decimals: 0, target: null })
  assert.deepEqual([counter.x, counter.measures, counter.stacked], [null, [], false])
})

test('buildOptions: an old-shape bar keeps exactly what it had', () => {
  const o = buildOptions(
    'bar',
    roles({ measures: ['Sales'], series: '', stacked: false, percent: false, lineMeasures: [], sortX: 'none' }),
  )
  assert.deepEqual(o, {
    version: 1,
    x: 'Region',
    measures: ['Sales'],
    series: null,
    stacked: false,
    percent: false,
    lineMeasures: [],
    sortX: 'none',
    counter: null,
    format: null,
  })
})

test('buildOptions: a currency code is trimmed and upper-cased only for the currency style', () => {
  const fmt = (style: 'currency' | 'number') =>
    buildOptions('bar', roles({ format: { style, decimals: 2, currency: ' usd ' } })).format
  assert.deepEqual(fmt('currency'), { style: 'currency', decimals: 2, currency: 'USD' })
  assert.deepEqual(fmt('number'), { style: 'number', decimals: 2, currency: null })
})

test('measureLimit and canSplit follow the chart type', () => {
  assert.equal(measureLimit('bar', ''), 5)
  assert.equal(measureLimit('bar', 'Channel'), 1)
  assert.equal(measureLimit('combo', 'Channel'), 5)
  for (const t of ['pie', 'funnel', 'waterfall', 'scatter', 'trend'] as const) assert.equal(measureLimit(t, ''), 1, t)
  assert.equal(canSplit('pie'), false)
  assert.equal(canSplit('scatter'), true)
})

test('compareBlankLast keeps the blank bucket last in both directions', () => {
  const sorted = (dir: 1 | -1) => ['b', '(blank)', 'a'].sort((p, q) => compareBlankLast(p, q, dir))
  assert.deepEqual(sorted(1), ['a', 'b', '(blank)'])
  assert.deepEqual(sorted(-1), ['b', 'a', '(blank)'])
})

test('truncateLabel shortens long text with an ellipsis', () => {
  assert.equal(truncateLabel('short'), 'short')
  assert.equal(truncateLabel('x'.repeat(40)).length, 24)
  assert.ok(truncateLabel('x'.repeat(40)).endsWith('…'))
})
