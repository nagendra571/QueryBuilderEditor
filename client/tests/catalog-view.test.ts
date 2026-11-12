import assert from 'node:assert/strict'
import { test } from 'node:test'
import { buildCatalogView, countLabel } from '../src/features/catalog/catalog-view.ts'
import { BUSINESS_UNAVAILABLE_REASON, adminUnavailableReason, providerLabel } from '../src/features/catalog/unavailable.ts'
import type { DataSourceCatalog, SchemaObjectMetadata, UnavailableObject } from '../src/types/index.ts'

const col = (name: string) => ({ name, dataType: 'string', isNullable: true, isPrimaryKey: false, isForeignKey: false }) as never
const obj = (schemaName: string, name: string, columns: string[] = []): SchemaObjectMetadata => ({
  schemaName,
  name,
  kind: 'table',
  columns: columns.map(col),
})
const bad = (schemaName: string, name: string): UnavailableObject => ({ schemaName, name, kind: 'table', reason: 'nameClash' })

function catalog(schemas: { name: string; objects: SchemaObjectMetadata[] }[], unavailableObjects?: UnavailableObject[]): DataSourceCatalog {
  return { dataSourceId: 'd', provider: 'postgreSql', schemas, unavailableObjects } as DataSourceCatalog
}

test('a schema holding only unavailable objects still gets a section', () => {
  const view = buildCatalogView(catalog([{ name: 'public', objects: [obj('public', 'a')] }], [bad('sales', 'Orders')]), '')
  assert.deepEqual(view.map((s) => s.name), ['public', 'sales'])
  assert.equal(view[1].objects.length, 0)
  assert.equal(view[1].unavailable[0].name, 'Orders')
})

test('search matches an unavailable object by name', () => {
  const view = buildCatalogView(catalog([{ name: 'public', objects: [obj('public', 'a')] }], [bad('public', 'Orders'), bad('public', 'zzz')]), 'ord')
  assert.equal(view.length, 1)
  assert.deepEqual(view[0].objects, [])
  assert.deepEqual(view[0].unavailable.map((u) => u.name), ['Orders'])
})

test('searching by a column name does not crash on rows with no columns', () => {
  const noCols = { schemaName: 'public', name: 'x', kind: 'table' } as SchemaObjectMetadata
  const view = buildCatalogView(catalog([{ name: 'public', objects: [noCols, obj('public', 'y', ['email'])] }]), 'email')
  assert.deepEqual(view[0].objects.map((o) => o.name), ['y'])
})

test('sections are sorted by name', () => {
  const view = buildCatalogView(
    catalog([{ name: 'b', objects: [obj('b', 'x')] }, { name: 'a', objects: [obj('a', 'x')] }], [bad('c', 'q'), bad('a', 'q')]),
    '',
  )
  assert.deepEqual(view.map((s) => s.name), ['a', 'b', 'c'])
})

test('countLabel pluralises and reports unavailable objects', () => {
  assert.equal(countLabel(catalog([{ name: 'p', objects: [obj('p', 'a')] }], [])), '1 object')
  const twelve = Array.from({ length: 12 }, (_, i) => obj('p', `o${i}`))
  assert.equal(countLabel(catalog([{ name: 'p', objects: twelve }], [bad('p', 'a'), bad('p', 'b')])), '12 objects · 2 unavailable')
})

test('a catalog without unavailableObjects (older cache) does not throw', () => {
  const c = catalog([{ name: 'p', objects: [obj('p', 'a')] }])
  assert.equal(countLabel(c), '1 object')
  assert.equal(buildCatalogView(c, 'a')[0].unavailable.length, 0)
})

test('admin reason names each clashing object', () => {
  assert.equal(
    adminUnavailableReason(['public.orders']),
    "Clashes with public.orders. QueryBuilder can't tell these names apart. Add a uniquely named view over the one you want to expose.",
  )
  assert.match(adminUnavailableReason(['public.a', 'public.b']), /^Clashes with public\.a, public\.b\./)
})

test('business reason and provider labels', () => {
  assert.equal(
    BUSINESS_UNAVAILABLE_REASON,
    "Can't be used: its full name clashes with another table or view. Ask your admin to add a view with a unique name.",
  )
  assert.equal(providerLabel('sqlServer'), 'SQL Server')
  assert.equal(providerLabel('postgreSql'), 'PostgreSQL')
  assert.equal(providerLabel('mySql'), 'MySQL')
  assert.equal(providerLabel('sqlite'), 'SQLite')
})
