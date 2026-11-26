import assert from 'node:assert/strict'
import { test } from 'node:test'
import { toTsv } from '../src/features/builder/results/clipboard-tsv.ts'
import { buildCopyText, buildRecordText } from '../src/features/builder/results/copy-selection.ts'

const cols = [
  { name: 'Id', dataType: 'number' },
  { name: 'Name', dataType: 'text' },
  { name: 'Active', dataType: 'boolean' },
] as const
const rows = [
  { Id: 1, Name: 'Ann', Active: true },
  { Id: 2, Name: null, Active: false },
  { Id: 3, Name: 'Cy\tD', Active: true },
]

test('a range copies only its columns and rows, in display order', () => {
  const text = buildCopyText([...cols], rows, { top: 0, bottom: 1, left: 1, right: 2 })
  assert.equal(text, 'Name\tActive\r\nAnn\tTRUE\r\n\tFALSE')
})

test('no range copies every displayed row and column', () => {
  const text = buildCopyText([...cols], rows, null)
  assert.equal(text.split('\r\n').length, 4)
  assert.ok(text.startsWith('Id\tName\tActive\r\n1\tAnn\tTRUE'))
})

test('a single column copies header plus values, quoting tabs', () => {
  const text = buildCopyText([cols[1]], rows, null)
  assert.equal(text, 'Name\r\nAnn\r\n\r\n"Cy\tD"')
})

test('a range past the data is clipped', () => {
  const text = buildCopyText([...cols], rows, { top: 2, bottom: 9, left: 0, right: 0 })
  assert.equal(text, 'Id\r\n3')
})

test('a record copies as Column/Value lines', () => {
  const rec = [
    { name: 'Name', dataType: 'text' },
    { name: 'Fee', dataType: 'number' },
    { name: 'Active', dataType: 'boolean' },
    { name: 'Note', dataType: 'text' },
  ] as const
  const text = buildRecordText({ Name: 'A	B', Fee: 12.5, Active: true, Note: null }, [...rec])
  assert.equal(
    text,
    toTsv(['Column', 'Value'], [['Name', 'A	B'], ['Fee', '12.5'], ['Active', 'TRUE'], ['Note', '']]),
  )
})
