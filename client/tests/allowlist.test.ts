import assert from 'node:assert/strict'
import { test } from 'node:test'
import { initialAllowlist, pruneAllowlist } from '../src/features/catalog/allowlist.ts'
import { objectRowKey, qualifiedName } from '../src/features/catalog/unavailable.ts'

const kinds = new Map([['s.v', 'view'], ['s.t', 'table'], ['s.u', 'table']])
const unavailable = new Set(['s.u'])

test('switching to views keeps an unavailable entry and drops usable tables', () => {
  const out = pruneAllowlist(new Set(['s.v', 's.t', 's.u']), kinds, unavailable, 'views')
  assert.deepEqual([...out].sort(), ['s.u', 's.v'])
})

test('turning restriction on keeps existing unavailable entries', () => {
  const out = initialAllowlist(['s.v', 's.t'], ['s.u', 's.gone'], unavailable)
  assert.deepEqual([...out].sort(), ['s.t', 's.u', 's.v'])
})

test('dot clashes display quoted and get distinct keys', () => {
  assert.equal(qualifiedName('public', 'orders'), 'public.orders')
  assert.equal(qualifiedName('a.b', 'c'), '"a.b"."c"')
  assert.equal(qualifiedName('a', 'b.c'), '"a"."b.c"')
  assert.notEqual(objectRowKey('a.b', 'c'), objectRowKey('a', 'b.c'))
})
