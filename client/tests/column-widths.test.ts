import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import {
  CELL_FONT_PX,
  CELL_PADDING_X,
  DEFAULT_COL_CAP,
  FIT_SAMPLE_ROWS,
  MAX_COL_WIDTH,
  MIN_COL_WIDTH,
  cachedWidth,
  cellFont,
  createCanvasMeasure,
  fitWidth,
} from '../src/features/builder/results/column-widths.ts'

const measure = (t: string) => t.length * 7

test('constants', () => {
  assert.equal(MIN_COL_WIDTH, 60)
  assert.equal(MAX_COL_WIDTH, 480)
  assert.equal(DEFAULT_COL_CAP, 320)
  assert.equal(FIT_SAMPLE_ROWS, 200)
  assert.equal(CELL_PADDING_X, 20)
})

test('short header and values fall back to the minimum', () => {
  assert.equal(fitWidth('id', ['1', '2'], measure, 10), MIN_COL_WIDTH)
})

test('empty values: header plus extra decides', () => {
  // 10 chars * 7 + 30 = 100
  assert.equal(fitWidth('0123456789', [], measure, 30), 100)
})

test('long value is capped by the given cap', () => {
  assert.equal(fitWidth('a', ['x'.repeat(100)], measure, 10, DEFAULT_COL_CAP), 320)
})

test('long value is capped at MAX when no cap is given', () => {
  assert.equal(fitWidth('a', ['x'.repeat(100)], measure, 10), 480)
})

test('cells get padding only: 10 chars = 70 + 20', () => {
  assert.equal(fitWidth('a', ['0123456789'], measure, 10), 90)
})

test('header wider than every value wins', () => {
  // header 20*7+40 = 180; cell 10*7+20 = 90
  assert.equal(fitWidth('h'.repeat(20), ['0123456789'], measure, 40), 180)
})

test('result is rounded up to whole pixels', () => {
  assert.equal(fitWidth('a', ['x'], () => 50.2, 0), 71)
})

test('only the first FIT_SAMPLE_ROWS values are measured', () => {
  const values = Array.from({ length: 1000 }, () => 'ab')
  values[500] = 'x'.repeat(100)
  // cell 2*7+20 = 34 -> min 60; the huge value at 500 is ignored
  assert.equal(fitWidth('a', values, measure, 10), MIN_COL_WIDTH)
  const inside = Array.from({ length: 1000 }, () => 'ab')
  inside[FIT_SAMPLE_ROWS - 1] = 'x'.repeat(20)
  assert.equal(fitWidth('a', inside, measure, 10), 160)
})

test('createCanvasMeasure imports under Node and falls back without a DOM', () => {
  const m = createCanvasMeasure({ header: '600 12px sans-serif', cell: '12px sans-serif' })
  assert.equal(m('abcd', 'cell'), 32)
})

test('cell widths are measured at the body size each density renders', () => {
  assert.equal(cellFont('comfortable'), '400 14px "Public Sans Variable"')
  assert.equal(cellFont('compact'), '400 13px "Public Sans Variable"')
  const css = readFileSync(new URL('../src/index.css', import.meta.url), 'utf8')
  const bodyPx = (density: string) => {
    const block = css.slice(css.indexOf(`[data-density="${density}"]`))
    const rem = /--text-body:\s*([\d.]+)rem/.exec(block)
    assert.ok(rem, `no --text-body for ${density}`)
    return Number(rem[1]) * 16
  }
  assert.equal(bodyPx('comfortable'), CELL_FONT_PX.comfortable)
  assert.equal(bodyPx('compact'), CELL_FONT_PX.compact)
})

test('cachedWidth measures again only when rows, type or key change', () => {
  const rows = [{ a: 1 }]
  let calls = 0
  const measure = () => ({ plain: 100 + ++calls, pinned: 120 })
  const col = { name: 'a', dataType: 'number' }
  const first = cachedWidth(rows, col, 'k1', measure)
  assert.equal(cachedWidth(rows, col, 'k1', measure), first)
  assert.equal(calls, 1)
  cachedWidth(rows, col, 'k2', measure)
  assert.equal(calls, 2)
  cachedWidth(rows, { ...col, dataType: 'text' }, 'k2', measure)
  assert.equal(calls, 3)
  cachedWidth([...rows], col, 'k2', measure)
  assert.equal(calls, 4)
})
