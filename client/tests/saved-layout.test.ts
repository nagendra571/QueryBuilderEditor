import { test } from 'node:test'
import assert from 'node:assert/strict'
import { defaultView } from '../src/features/builder/results/grid-view.ts'
import {
  applySavedLayout,
  isPlainLayout,
  layoutPartsEqual,
  parseSavedLayout,
  toSavedLayout,
} from '../src/features/builder/results/saved-layout.ts'

const rule = (i: number) => ({ id: `r${i}`, op: 'gt', value: String(i), value2: '', color: 'red' })

test('round trip keeps saved parts', () => {
  const cols = ['A', 'B', 'C']
  const view = {
    ...defaultView(cols),
    order: ['C', 'A', 'B'],
    hidden: ['B'],
    pinnedLeft: ['C'],
    pinnedRight: ['A'],
    widths: { A: 120 },
    wrap: true,
    formats: {
      A: {
        number: { style: 'currency', decimals: 2, currency: 'EUR', thousands: false, fraction: false },
        bars: true,
        scale: 'blue',
        rules: [{ id: 'x', op: 'gt', value: '5', value2: '', color: 'green' }],
      },
    },
    totals: { on: true, fns: { A: 'sum' } },
  } as never
  const back = applySavedLayout(parseSavedLayout(JSON.parse(JSON.stringify(toSavedLayout(view)))), cols)
  assert.equal(layoutPartsEqual(back, view), true)
})

test('toSavedLayout drops sort, search, filters and density', () => {
  const view = { ...defaultView(['A']), search: 'x', density: 'compact', sort: { column: 'A', direction: 'asc' } } as never
  const saved = toSavedLayout(view) as unknown as Record<string, unknown>
  for (const k of ['sort', 'search', 'filters', 'density']) assert.equal(k in saved, false)
  assert.equal(saved.v, 1)
})

test('stale names are dropped and new columns appended', () => {
  const layout = parseSavedLayout({ v: 1, order: ['B', 'X', 'A'], hidden: ['X'], widths: { X: 100, A: 90 } })
  const view = applySavedLayout(layout, ['A', 'B', 'C'])
  assert.deepEqual(view.order, ['B', 'A', 'C'])
  assert.deepEqual(view.hidden, [])
  assert.equal('X' in view.widths, false)
  assert.equal(view.widths.A, 90)
})

test('never hides every column', () => {
  const view = applySavedLayout(parseSavedLayout({ v: 1, hidden: ['A', 'B'] }), ['A', 'B'])
  assert.deepEqual(view.hidden, [])
})

test('pinned both sides stays left', () => {
  const view = applySavedLayout(parseSavedLayout({ v: 1, pinnedLeft: ['A'], pinnedRight: ['A', 'B'] }), ['A', 'B'])
  assert.deepEqual(view.pinnedLeft, ['A'])
  assert.deepEqual(view.pinnedRight, ['B'])
})

test('junk input never throws', () => {
  for (const j of [null, 'x', [], { v: 2 }]) assert.equal(parseSavedLayout(j), null)
  const p = parseSavedLayout({
    v: 1,
    order: 'A',
    widths: { A: -5, B: '9', C: 9999 },
    formats: { A: { rules: [{ op: 'nope', color: 'red' }], bars: 'yes' } },
    totals: { on: 'y', fns: { A: 'median' } },
  })!
  assert.deepEqual(p.order, [])
  assert.deepEqual(p.widths, { A: 60, C: 480 })
  assert.deepEqual(p.formats, {})
  assert.deepEqual(p.totals, { on: false, fns: {} })
})

test('rules are capped at five', () => {
  const p = parseSavedLayout({ v: 1, formats: { A: { rules: [0, 1, 2, 3, 4, 5, 6].map(rule) } } })!
  assert.equal(p.formats.A.rules.length, 5)
})

test('isPlainLayout true for default, false after one width', () => {
  const cols = ['A', 'B']
  assert.equal(isPlainLayout(defaultView(cols), cols), true)
  assert.equal(isPlainLayout({ ...defaultView(cols), widths: { A: 100 } }, cols), false)
})

test('rule ids are unique within a column', () => {
  const mk = (id: unknown) => ({ id, op: 'gt', value: '1', color: 'red' })
  const p = parseSavedLayout({ v: 1, formats: { A: { rules: [mk('a'), mk('a'), mk('r1'), mk(7)] } } })!
  const ids = p.formats.A.rules.map((r) => r.id)
  assert.equal(new Set(ids).size, 4)
  assert.equal(ids[0], 'a')
  assert.equal(ids[2], 'r1')
})

test('currency must be a three-letter code', () => {
  const cur = (c: unknown) =>
    parseSavedLayout({ v: 1, formats: { A: { number: { style: 'currency', currency: c } } } })!.formats.A.number!.currency
  assert.equal(cur('eur'), 'EUR')
  assert.equal(cur('EURO'), 'USD')
  assert.equal(cur('<b>'), 'USD')
  assert.equal(cur(5), 'USD')
})

test('a column named __proto__ survives as an own key', () => {
  const p = parseSavedLayout(JSON.parse('{"v":1,"widths":{"__proto__":100},"totals":{"fns":{"__proto__":"sum"}}}'))!
  assert.equal(Object.hasOwn(p.widths, '__proto__'), true)
  assert.equal(Object.hasOwn(p.totals.fns, '__proto__'), true)
})
