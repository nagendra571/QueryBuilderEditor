import test from 'node:test'
import assert from 'node:assert/strict'
import {
  createTable,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
} from '@tanstack/table-core'
import { buildColumnDefs, tableBehaviour, type RowData } from '../src/features/builder/results/grid-table.ts'
import { compareCells } from '../src/features/builder/results/compare-cells.ts'

const columns = [
  { name: 'n', dataType: 'number' as const },
  { name: 's', dataType: 'text' as const },
  { name: 'hid', dataType: 'text' as const },
]

function run(rows: RowData[], state: { sort?: { id: string; desc: boolean }; search?: string; hidden?: string[] }) {
  const table = createTable<RowData>({
    data: rows,
    columns: buildColumnDefs(columns as never),
    state: {
      sorting: state.sort ? [state.sort] : [],
      globalFilter: state.search ?? '',
      columnVisibility: Object.fromEntries((state.hidden ?? []).map((n) => [n, false])),
    },
    onStateChange: () => {},
    renderFallbackValue: null,
    ...tableBehaviour,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getSortedRowModel: getSortedRowModel(),
  })
  return table.getRowModel().rows.map((r) => r.original)
}

const rows: RowData[] = [
  { n: 3, s: 'c', hid: 'x' },
  { n: null, s: 'a', hid: 'secret' },
  { n: 1, s: 'b', hid: 'x' },
  { n: 2, s: null, hid: 'x' },
]

for (const dir of ['asc', 'desc'] as const) {
  test(`number sort ${dir} matches the old comparator order`, () => {
    const mult = dir === 'asc' ? 1 : -1
    const expected = [...rows].sort((a, b) => mult * compareCells(a.n, b.n, true))
    assert.deepEqual(run(rows, { sort: { id: 'n', desc: dir === 'desc' } }), expected)
  })
}

test('nulls last ascending, first descending', () => {
  assert.equal(run(rows, { sort: { id: 'n', desc: false } }).at(-1)?.n, null)
  assert.equal(run(rows, { sort: { id: 'n', desc: true } })[0].n, null)
})

test('numeric text sorts by value in a number column', () => {
  const r = [{ n: '10' }, { n: '1.5' }, { n: '1.25' }]
  assert.deepEqual(run(r, { sort: { id: 'n', desc: false } }).map((x) => x.n), ['1.25', '1.5', '10'])
})

test('search skips hidden columns but keeps visible matches', () => {
  assert.equal(run(rows, { search: 'secret' }).length, 1)
  assert.equal(run(rows, { search: 'secret', hidden: ['hid'] }).length, 0)
  assert.equal(run(rows, { search: ' B ', hidden: ['hid'] }).length, 1)
  assert.equal(run(rows, { search: '   ' }).length, 4)
})

test('column filterFn delegates to matchesFilter', () => {
  const defs = buildColumnDefs(columns as never)
  const fn = defs[0].filterFn as (row: unknown, id: string, f: unknown) => boolean
  assert.equal(typeof fn, 'function')
  const row = { getValue: () => '12.50' }
  const f = { kind: 'range', column: 'n', min: 10, max: 20, includeEmpty: false }
  assert.equal(fn(row, 'n', f), true)
  assert.equal(fn(row, 'n', { ...f, min: 13, max: 20 }), false)
})
