import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  countObjectsUsingKey,
  MAX_SCOPE_KEYS,
  removeKeyWarning,
  validateNewScopeKey,
} from '../src/features/admin/scope-keys.ts'

test('a plain name is accepted', () => {
  assert.equal(validateNewScopeKey('RegionId', ['ProgramId'], []), null)
  assert.equal(validateNewScopeKey('_tenant_2', [], []), null)
})

test('names with spaces, dashes, a leading digit, or surrounding whitespace are invalid', () => {
  for (const key of ['x-y', 'Re gion', '1Region', ' RegionId', 'RegionId ', '']) {
    assert.match(validateNewScopeKey(key, [], []) ?? '', /is invalid — use letters, digits and underscores only/)
  }
})

test('128 characters is the longest name', () => {
  assert.equal(validateNewScopeKey('a'.repeat(128), [], []), null)
  assert.match(validateNewScopeKey('a'.repeat(129), [], []) ?? '', /invalid/)
})

test('a host key is refused ignoring case', () => {
  assert.equal(validateNewScopeKey('programid', ['ProgramId'], []), "'programid' is already set in the app's code.")
})

test('a key already on the data source is refused ignoring case', () => {
  assert.match(validateNewScopeKey('REGIONID', [], ['RegionId']) ?? '', /already added/)
})

test('the twenty-first key is refused', () => {
  const keys = Array.from({ length: MAX_SCOPE_KEYS }, (_, i) => `K${i}`)
  assert.match(validateNewScopeKey('Extra', [], keys) ?? '', /at most 20/)
  assert.equal(validateNewScopeKey('Extra', [], keys.slice(1)), null)
})

test('objects are counted by mapped or stale key, ignoring case, once each', () => {
  const objects = [
    { mappings: { RegionId: 'RegionCode' }, staleKeys: [] },
    { mappings: {}, staleKeys: ['regionid'] },
    { mappings: { RegionId: 'A' }, staleKeys: ['RegionId'] },
    { mappings: { ProgramId: 'ProgramId' }, staleKeys: [] },
  ]
  assert.equal(countObjectsUsingKey(objects, 'REGIONID'), 3)
  assert.equal(countObjectsUsingKey(objects, 'Other'), 0)
})

test('the warning names the key and agrees in number', () => {
  assert.match(removeKeyWarning(3, 'RegionId'), /^3 views use RegionId\. They'll be hidden from scoped users/)
  assert.match(removeKeyWarning(1, 'RegionId'), /^1 view uses RegionId\. It'll be hidden/)
})
