import test from 'node:test'
import assert from 'node:assert/strict'
import {
  carryOver,
  defaultView,
  isDefaultView,
  nextSort,
  type GridView,
} from '../src/features/builder/results/grid-view.ts'

const cols = ['A', 'B', 'C']

function customised(): GridView {
  return {
    order: ['C', 'A', 'B'],
    hidden: ['B'],
    pinnedLeft: ['C'],
    pinnedRight: ['A'],
    widths: { A: 150 },
    sort: { column: 'A', direction: 'desc' },
    search: 'x',
    density: 'compact' as GridView['density'],
    wrap: true,
    filters: [{ kind: 'values', column: 'A', mode: 'include', values: ['x'] }],
    formats: {},
    totals: { on: false, fns: {} },
  }
}

const aFormat = () => ({
  number: { style: 'currency' as const, decimals: 2, currency: 'USD', thousands: true, fraction: false },
  date: { pattern: 'iso' as const, showTime: false },
  rules: [{ id: 'r1', op: 'gt' as const, value: '5', value2: '', color: 'red' as const }],
})

test('defaultView lists columns in order with nothing else set', () => {
  assert.deepEqual(defaultView(cols), {
    order: cols,
    hidden: [],
    pinnedLeft: [],
    pinnedRight: [],
    widths: {},
    sort: null,
    search: '',
    density: null,
    wrap: false,
    filters: [],
    formats: {},
    totals: { on: false, fns: {} },
  })
})

test('carryOver deep-copies formats and totals, and resets them on another set', () => {
  const v = customised()
  v.formats = { A: aFormat() }
  v.totals = { on: true, fns: { A: 'avg' } }
  const next = carryOver(v, cols, ['B', 'C', 'A'])
  assert.deepEqual(next.formats, v.formats)
  assert.deepEqual(next.totals, v.totals)
  next.formats.A.rules[0].value = '9'
  next.formats.A.number!.decimals = 0
  next.totals.fns.A = 'max'
  assert.equal(v.formats.A.rules[0].value, '5')
  assert.equal(v.formats.A.number!.decimals, 2)
  assert.equal(v.totals.fns.A, 'avg')
  const reset = carryOver(v, cols, ['A', 'B'])
  assert.deepEqual(reset.formats, {})
  assert.deepEqual(reset.totals, { on: false, fns: {} })
})

test('isDefaultView is false with totals on or any format', () => {
  const a = defaultView(cols)
  a.totals = { on: true, fns: {} }
  assert.equal(isDefaultView(a, cols), false)
  const b = defaultView(cols)
  b.formats = { A: aFormat() }
  assert.equal(isDefaultView(b, cols), false)
  assert.equal(isDefaultView({ ...defaultView(cols), formats: {} }, cols), true)
})

test('carryOver keeps filters on the same column set and drops them otherwise', () => {
  const v = customised()
  assert.deepEqual(carryOver(v, cols, ['C', 'B', 'A']).filters, v.filters)
  assert.deepEqual(carryOver(v, cols, ['A', 'B']).filters, [])
})

test('isDefaultView is false with filters', () => {
  const v = { ...defaultView(cols), filters: [{ kind: 'range' as const, column: 'A', min: 1, max: null, includeEmpty: false }] }
  assert.equal(isDefaultView(v, cols), false)
})

test('carryOver keeps everything when the same names come back in another order', () => {
  const prev = customised()
  const next = carryOver(prev, cols, ['B', 'C', 'A'])
  assert.deepEqual(next, prev)
  assert.notEqual(next.order, prev.order)
  assert.notEqual(next.hidden, prev.hidden)
  assert.notEqual(next.pinnedLeft, prev.pinnedLeft)
  assert.notEqual(next.pinnedRight, prev.pinnedRight)
  assert.notEqual(next.widths, prev.widths)
  assert.notEqual(next.sort, prev.sort)
})

test('carryOver resets fully when the set differs, even with heavy overlap', () => {
  const next = carryOver(customised(), cols, ['A', 'B', 'D'])
  assert.deepEqual(next, defaultView(['A', 'B', 'D']))
})

test('carryOver resets when a name is added or removed', () => {
  assert.deepEqual(carryOver(customised(), cols, ['A', 'B']), defaultView(['A', 'B']))
  assert.deepEqual(carryOver(customised(), cols, [...cols, 'D']), defaultView([...cols, 'D']))
})

test('carryOver never leaves a name outside the next columns', () => {
  const prev = customised()
  const nextCols = ['A', 'B', 'Z']
  const next = carryOver(prev, cols, nextCols)
  const all = [
    ...next.order,
    ...next.hidden,
    ...next.pinnedLeft,
    ...next.pinnedRight,
    ...Object.keys(next.widths),
    ...(next.sort ? [next.sort.column] : []),
  ]
  for (const n of all) assert.ok(nextCols.includes(n), n)
})

test('isDefaultView is true for a default view and false after any change', () => {
  assert.equal(isDefaultView(defaultView(cols), cols), true)
  const d = () => defaultView(cols)
  const changes: Array<(v: GridView) => void> = [
    (v) => { v.hidden = ['A'] },
    (v) => { v.pinnedLeft = ['A'] },
    (v) => { v.pinnedRight = ['A'] },
    (v) => { v.widths = { A: 100 } },
    (v) => { v.sort = { column: 'A', direction: 'asc' } },
    (v) => { v.search = 'q' },
    (v) => { v.density = 'compact' as GridView['density'] },
    (v) => { v.wrap = true },
    (v) => { v.order = ['B', 'A', 'C'] },
  ]
  for (const change of changes) {
    const v = d()
    change(v)
    assert.equal(isDefaultView(v, cols), false)
  }
})

test('isDefaultView treats whitespace-only search as default', () => {
  const v = defaultView(cols)
  v.search = '   '
  assert.equal(isDefaultView(v, cols), true)
})

test('nextSort cycles asc, desc, none and restarts on another column', () => {
  const a1 = nextSort(null, 'A')
  assert.deepEqual(a1, { column: 'A', direction: 'asc' })
  const a2 = nextSort(a1, 'A')
  assert.deepEqual(a2, { column: 'A', direction: 'desc' })
  assert.equal(nextSort(a2, 'A'), null)
  assert.deepEqual(nextSort(a1, 'B'), { column: 'B', direction: 'asc' })
})
