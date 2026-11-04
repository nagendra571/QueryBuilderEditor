import assert from 'node:assert/strict'
import { test } from 'node:test'
import { applyBox, compactTiles } from '../src/features/dashboards/layout.ts'
import { MAX_SECTION_DESCRIPTION, MAX_SECTION_TITLE, sectionBox, validateSection } from '../src/features/dashboards/section.ts'

test('a section box is full width, one row, at column 0', () => {
  assert.deepEqual(sectionBox(7), { x: 0, y: 7, width: 12, height: 1 })
})

test('title is required and limited to 80 characters', () => {
  assert.equal(validateSection('', '').valid, false)
  assert.equal(validateSection('   ', '').valid, false)
  assert.equal(validateSection('a'.repeat(MAX_SECTION_TITLE), '').valid, true)
  assert.equal(validateSection('a'.repeat(MAX_SECTION_TITLE + 1), '').valid, false)
})

test('description is optional, one line, at most 160 characters', () => {
  assert.equal(validateSection('t', '').valid, true)
  assert.equal(validateSection('t', 'b'.repeat(MAX_SECTION_DESCRIPTION)).valid, true)
  assert.equal(validateSection('t', 'b'.repeat(MAX_SECTION_DESCRIPTION + 1)).valid, false)
  assert.equal(validateSection('t', 'a\nb').valid, false)
  assert.equal(validateSection('t', 'a\rb').valid, false)
})

test('a keyboard move keeps a section one row high and full width', () => {
  const tiles = [
    { key: 'a', x: 0, y: 0, width: 12, height: 4 },
    { key: 's', ...sectionBox(4) },
    { key: 'b', x: 0, y: 5, width: 6, height: 3 },
  ]
  const moved = applyBox(tiles, 's', { y: 0 })
  const section = moved.find((t) => t.key === 's')!
  assert.deepEqual([section.x, section.width, section.height], [0, 12, 1])
  assert.equal(moved.find((t) => t.key === 'a')!.y, 1)
})

test('compaction places a one-row tile directly under the tile above it', () => {
  const out = compactTiles([
    { key: 'a', x: 0, y: 0, width: 12, height: 4 },
    { key: 's', ...sectionBox(9) },
    { key: 'b', x: 0, y: 12, width: 12, height: 3 },
  ])
  assert.deepEqual(out.map((t) => t.y), [0, 4, 5])
})
