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
  }
}

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
  })
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
