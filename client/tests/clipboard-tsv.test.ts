import assert from 'node:assert/strict'
import { test } from 'node:test'
import { cellToClipboardText, toTsv } from '../src/features/builder/results/clipboard-tsv.ts'

test('null and undefined become empty text', () => {
  assert.equal(cellToClipboardText(null, 'text'), '')
  assert.equal(cellToClipboardText(undefined, 'number'), '')
})

test('booleans become TRUE and FALSE', () => {
  assert.equal(cellToClipboardText(true, 'boolean'), 'TRUE')
  assert.equal(cellToClipboardText(false, 'boolean'), 'FALSE')
})

test('numbers carry no thousands separators; numeric text passes through', () => {
  assert.equal(cellToClipboardText(1234567.5, 'number'), '1234567.5')
  assert.equal(cellToClipboardText('12345678901234567890.5', 'number'), '12345678901234567890.5')
})

test('dates stay as they are', () => {
  assert.equal(cellToClipboardText('2026-01-14', 'date'), '2026-01-14')
})

test('toTsv joins with tabs and CRLF, no trailing newline', () => {
  assert.equal(toTsv(['A', 'B'], [['1', 'x'], ['2', 'y']]), 'A\tB\r\n1\tx\r\n2\ty')
})

test('fields with tab, newline or quote are quoted with quotes doubled', () => {
  assert.equal(toTsv(['N'], [['say "hi"\nthere']]), 'N\r\n"say ""hi""\nthere"')
  assert.equal(toTsv(['N'], [['a\tb']]), 'N\r\n"a\tb"')
})

test('no rows gives the header line only', () => {
  assert.equal(toTsv(['A', 'B'], []), 'A\tB')
})
