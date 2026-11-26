import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import {
  HEADER_BADGE,
  HEADER_EXTRA,
  HEADER_GAP,
  HEADER_GRIP,
  HEADER_MENU,
  HEADER_PADDING_X,
  HEADER_PIN,
  HEADER_SLACK,
  HEADER_SORT,
  PINNED_HEADER_EXTRA,
} from '../src/features/builder/results/header-width.ts'

test('the header extra is the sum of the fixed header parts', () => {
  assert.equal(
    HEADER_EXTRA,
    HEADER_PADDING_X + HEADER_GRIP + HEADER_BADGE + HEADER_SORT + HEADER_MENU + 3 * HEADER_GAP + HEADER_SLACK,
  )
  assert.equal(HEADER_EXTRA, 116)
  assert.equal(PINNED_HEADER_EXTRA, HEADER_PIN + HEADER_GAP)
})

test('the header markup still uses the sizes the constants assume', () => {
  const src = readFileSync(new URL('../src/features/builder/results/GridHeaderCell.tsx', import.meta.url), 'utf8')
  for (const cls of ['px-2.5', 'gap-1', 'w-3.5', 'w-8', 'size-3', 'size-[18px]']) {
    assert.ok(src.includes(cls), `GridHeaderCell.tsx no longer uses ${cls}`)
  }
})
