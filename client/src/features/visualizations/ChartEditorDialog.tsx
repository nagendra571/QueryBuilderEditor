import { useMemo, useState } from 'react'
import { ArrowDown, ArrowUp, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { ChartRenderer } from '@/features/visualizations/ChartRenderer'
import {
  MAX_MEASURES,
  MAX_TABLE_COLUMNS,
  MAX_PIVOT_ROWS,
  MAX_PIVOT_VALUES,
  buildOptions,
  canSplit as canSplitType,
  canStack as canStackType,
  defaultRoles,
  defaultTableColumns,
  measureLimit as measureLimitFor,
  optionsProblem,
  referencedColumns,
  typeLabel,
  type OutputColumn,
} from '@/features/visualizations/chart-data'
import { TYPE_GROUPS } from '@/features/visualizations/chart-types'
import type {
  QueryResultDto,
  SaveVisualizationRequest,
  ValueFormatStyle,
  VisualizationDto,
  VisualizationOptions,
  VisualizationSort,
  VisualizationType,
} from '@/types'

const PARTIAL_PREVIEW = 'Preview uses partial results, so totals may be lower than the saved chart.'
const NONE = '__none__'
const AUTO = '__auto__'
const FORMAT_STYLES: { value: ValueFormatStyle | typeof NONE; label: string }[] = [
  { value: NONE, label: 'Default' },
  { value: 'number', label: 'Number' },
  { value: 'compact', label: 'Compact (1.2K)' },
  { value: 'currency', label: 'Currency' },
  { value: 'percent', label: 'Percent' },
]

export function ChartEditorDialog({
  open,
  onOpenChange,
  columns,
  editing,
  previewResult,
  isSaving,
  onSave,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Output columns of the *saved* query — what the server validates charts against. */
  columns: OutputColumn[]
  editing?: VisualizationDto | null
  /** The latest results grid, used for the live preview. */
  previewResult?: QueryResultDto
  isSaving: boolean
  onSave: (request: SaveVisualizationRequest) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>{editing ? 'Edit chart' : 'Add chart'}</DialogTitle>
          <DialogDescription>
            Charts are drawn from the saved query, and each viewer only sees the rows they are allowed to see.
          </DialogDescription>
        </DialogHeader>
        {/* Mounted only while open, so the form state resets on every open. */}
        <ChartEditorForm
          columns={columns}
          editing={editing ?? null}
          previewResult={previewResult}
          isSaving={isSaving}
          onCancel={() => onOpenChange(false)}
          onSave={onSave}
        />
      </DialogContent>
    </Dialog>
  )
}

function ChartEditorForm({
  columns,
  editing,
  previewResult,
  isSaving,
  onCancel,
  onSave,
}: {
  columns: OutputColumn[]
  editing: VisualizationDto | null
  previewResult?: QueryResultDto
  isSaving: boolean
  onCancel: () => void
  onSave: (request: SaveVisualizationRequest) => void
}) {
  const numeric = useMemo(() => columns.filter((c) => c.dataType === 'number'), [columns])
  const defaults = useMemo(() => defaultRoles(columns), [columns])

  const [type, setType] = useState<VisualizationType>(editing?.type ?? 'bar')
  const [x, setX] = useState<string>(editing?.options.x ?? defaults.x)
  const [measures, setMeasures] = useState<string[]>(editing?.options.measures ?? (defaults.measure ? [defaults.measure] : []))
  const [series, setSeries] = useState<string>(editing?.options.series ?? '')
  const [stacked, setStacked] = useState(editing?.options.stacked ?? false)
  const [percent, setPercent] = useState(editing?.options.percent ?? false)
  const [lineMeasures, setLineMeasures] = useState<string[]>(editing?.options.lineMeasures ?? [])
  const [formatStyle, setFormatStyle] = useState<ValueFormatStyle | typeof NONE>(editing?.options.format?.style ?? NONE)
  const [formatDecimals, setFormatDecimals] = useState<number | null>(editing?.options.format?.decimals ?? null)
  const [currency, setCurrency] = useState(editing?.options.format?.currency ?? 'USD')
  const [sortX, setSortX] = useState<VisualizationSort>(editing?.options.sortX ?? 'none')
  const [counterValue, setCounterValue] = useState(editing?.options.counter?.value ?? defaults.measure ?? numeric[0]?.name ?? '')
  const [counterLabel, setCounterLabel] = useState(editing?.options.counter?.label ?? '')
  const [decimals, setDecimals] = useState(editing?.options.counter?.decimals ?? 0)
  const [counterTarget, setCounterTarget] = useState(editing?.options.counter?.target ?? '')
  const [tableColumns, setTableColumns] = useState<string[]>(editing?.options.table?.columns ?? [])
  const [pivotRows, setPivotRows] = useState<string[]>(editing?.options.pivot?.rows ?? [])
  const [pivotColumn, setPivotColumn] = useState<string>(editing?.options.pivot?.column ?? '')
  const [pivotValues, setPivotValues] = useState<string[]>(editing?.options.pivot?.values ?? [])
  const [pivotTotals, setPivotTotals] = useState(editing?.options.pivot?.totals ?? true)
  const [pivotShade, setPivotShade] = useState(editing?.options.pivot?.shade ?? false)
  const [size, setSize] = useState<string>(editing?.options.size ?? '')
  const [label, setLabel] = useState<string>(editing?.options.label ?? '')
  const [name, setName] = useState(editing?.name ?? '')

  const isCounter = type === 'counter'
  const isTable = type === 'table'
  const isPivot = type === 'pivot'
  const isCombo = type === 'combo'
  const isStory = type === 'funnel' || type === 'waterfall'
  const isScatter = type === 'scatter'
  const isTrend = type === 'trend'
  const xNeedsNumberOrDate = isScatter || isTrend
  const canStack = canStackType(type)
  const canSplit = canSplitType(type)
  const effectiveSeries = canSplit && series ? series : ''
  const measureLimit = measureLimitFor(type, effectiveSeries)
  const scatterXColumns = useMemo(() => columns.filter((c) => ['number', 'date', 'dateTime'].includes(c.dataType)), [columns])

  const options: VisualizationOptions = useMemo(
    () =>
      buildOptions(type, {
        x,
        measures,
        series,
        stacked,
        percent,
        lineMeasures,
        sortX,
        counter: { value: counterValue, label: counterLabel, decimals, target: counterTarget },
        tableColumns,
        pivot: { rows: pivotRows, column: pivotColumn, values: pivotValues, totals: pivotTotals, shade: pivotShade },
        size,
        label,
        format: formatStyle === NONE ? null : { style: formatStyle, decimals: formatDecimals, currency },
      }),
    [type, x, measures, series, stacked, percent, lineMeasures, sortX, counterValue, counterLabel, decimals, counterTarget, tableColumns, pivotRows, pivotColumn, pivotValues, pivotTotals, pivotShade, size, label, formatStyle, formatDecimals, currency],
  )

  // Until the user types their own name, the suggestion follows the chosen type and measure.
  const suggestedName = isCounter
    ? `Counter of ${counterValue || 'value'}`
    : isTable
      ? 'Table'
      : isPivot
        ? 'Pivot'
        : `${typeLabel(type)} of ${options.measures[0] ?? 'measure'}`
  const effectiveName = name.trim() || suggestedName

  const problem = optionsProblem(type, options, columns)
  const previewHasColumns =
    !!previewResult &&
    referencedColumns(type, options).every((c) => previewResult.columns.some((rc) => rc.name === c))
  const previewColumns = useMemo(() => {
    const aggregates = new Map(columns.map((c) => [c.name, c.aggregate]))
    return previewResult?.columns.map((rc) => ({ ...rc, aggregate: aggregates.get(rc.name) ?? null }))
  }, [columns, previewResult])
  const staleRows = pivotRows.filter((n) => !columns.some((c) => c.name === n))
  const staleX = !!x && !(xNeedsNumberOrDate ? scatterXColumns : columns).some((c) => c.name === x)
  const staleTarget = isCounter && !!counterTarget && !numeric.some((c) => c.name === counterTarget)
  // Taken from what would be saved, so a measure the type would drop never shows up as something to fix.
  const staleMeasures = options.measures.filter((m) => !numeric.some((c) => c.name === m))
  const staleSize = isScatter && !!size && !numeric.some((c) => c.name === size)
  const staleLabel = isScatter && !!label && !columns.some((c) => c.name === label)
  const staleValues = pivotValues.filter((n) => !numeric.some((c) => c.name === n))
  const averagedPercent =
    !!options.percent &&
    columns.some((c) => options.measures.includes(c.name) && (c.aggregate === 'avg' || c.aggregate === 'countDistinct'))

  function setMeasureAsLine(column: string, asLine: boolean) {
    setLineMeasures((prev) => (asLine ? (prev.includes(column) ? prev : [...prev, column]) : prev.filter((m) => m !== column)))
  }

  function changeType(next: VisualizationType) {
    setType(next)
    if (next === 'funnel' || next === 'waterfall') {
      if (!isStory) setSortX('none')
      setMeasures((prev) => prev.slice(0, 1))
    }
    if (next === 'scatter' || next === 'trend') {
      setMeasures((prev) => prev.slice(0, 1))
      let nextX = x
      if (!scatterXColumns.some((c) => c.name === x) && scatterXColumns[0]) {
        nextX = scatterXColumns[0].name
        setX(nextX)
      }
      // The Split by list leaves out the X column, so a split equal to X would show as a blank select.
      if (series === nextX) setSeries('')
    }
    // Roles stay in state across switches; only the Table starts from "everything" so its preview isn't empty.
    if (next === 'table' && tableColumns.length === 0) setTableColumns(defaultTableColumns(columns))
    if (next === 'pivot' && pivotRows.length === 0 && pivotValues.length === 0) {
      const row = columns.some((c) => c.name === defaults.x) ? defaults.x : ''
      if (row) setPivotRows([row])
      if (defaults.measure && defaults.measure !== row) setPivotValues([defaults.measure])
    }
  }

  function togglePivotRole(role: 'rows' | 'values', column: string, checked: boolean) {
    const limit = role === 'rows' ? MAX_PIVOT_ROWS : MAX_PIVOT_VALUES
    const set = role === 'rows' ? setPivotRows : setPivotValues
    set((prev) => {
      if (!checked) return prev.filter((c) => c !== column)
      if (prev.includes(column) || prev.length >= limit) return prev
      return [...prev, column]
    })
  }

  function toggleTableColumn(column: string, checked: boolean) {
    setTableColumns((prev) => {
      if (!checked) return prev.filter((c) => c !== column)
      if (prev.includes(column) || prev.length >= MAX_TABLE_COLUMNS) return prev
      return [...prev, column]
    })
  }

  function moveTableColumn(column: string, by: -1 | 1) {
    setTableColumns((prev) => {
      const i = prev.indexOf(column)
      const j = i + by
      if (i < 0 || j < 0 || j >= prev.length) return prev
      const next = [...prev]
      ;[next[i], next[j]] = [next[j], next[i]]
      return next
    })
  }

  function toggleMeasure(column: string, checked: boolean) {
    // Works on the measures that are actually saved: ticks beyond this type's limit are not shown, so they must not come back.
    setMeasures((prev) => {
      const kept = prev.slice(0, measureLimit)
      if (!checked) return kept.filter((m) => m !== column)
      if (kept.includes(column)) return kept
      return measureLimit === 1 ? [column] : [...kept, column].slice(0, MAX_MEASURES)
    })
  }

  return (
    <>
      <div className="grid gap-4 md:grid-cols-[minmax(0,18rem)_minmax(0,1fr)]">
        <div className="flex min-w-0 flex-col gap-3">
          <Field label="Chart type">
            <Select value={type} onValueChange={(v) => v && changeType(v as VisualizationType)}>
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TYPE_GROUPS.filter((g) => g.types.length > 0).map((g) => (
                  <SelectGroup key={g.label}>
                    <SelectLabel>{g.label}</SelectLabel>
                    {g.types.map((t) => (
                      <SelectItem key={t} value={t}>
                        {typeLabel(t)}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                ))}
              </SelectContent>
            </Select>
          </Field>

          <Field label="Name">
            <Input value={name} maxLength={200} placeholder={suggestedName} onChange={(e) => setName(e.target.value)} />
          </Field>

          {isPivot ? (
            <>
              <fieldset className="flex flex-col gap-1.5">
                <legend className="text-xs font-medium">{`Rows (up to ${MAX_PIVOT_ROWS})`}</legend>
                {staleRows.map((n) => (
                  <StaleRole key={`stale-${n}`} name={n} hint="no longer in this query" onRemove={() => togglePivotRole('rows', n, false)} />
                ))}
                {columns.map((c) => {
                  const taken = pivotValues.includes(c.name) || pivotColumn === c.name
                  return (
                    <label key={c.name} className="flex min-w-0 items-center gap-2 text-sm">
                      <Checkbox
                        checked={pivotRows.includes(c.name)}
                        disabled={taken || (!pivotRows.includes(c.name) && pivotRows.length >= MAX_PIVOT_ROWS)}
                        onCheckedChange={(v) => togglePivotRole('rows', c.name, v === true)}
                      />
                      <span className="min-w-0 flex-1 truncate">{c.name}</span>
                    </label>
                  )
                })}
              </fieldset>
              <Field label="Column (optional)">
                <Select value={pivotColumn || NONE} onValueChange={(v) => v && setPivotColumn(v === NONE ? '' : v)}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NONE}>No column</SelectItem>
                    {pivotColumn && !columns.some((c) => c.name === pivotColumn) && (
                      <SelectItem value={pivotColumn}>{`${pivotColumn} (no longer in this query)`}</SelectItem>
                    )}
                    {columns
                      .filter((c) => !pivotRows.includes(c.name) && !pivotValues.includes(c.name))
                      .map((c) => (
                        <SelectItem key={c.name} value={c.name}>
                          {c.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              </Field>
              <fieldset className="flex flex-col gap-1.5">
                <legend className="text-xs font-medium">{`Values (up to ${MAX_PIVOT_VALUES})`}</legend>
                {numeric.length === 0 && (
                  <p className="text-xs text-muted-foreground">
                    This query has no number column. Give a column a total (Sum, Count...) to use it here.
                  </p>
                )}
                {staleValues.map((n) => (
                  <StaleRole
                    key={`stale-${n}`}
                    name={n}
                    hint={columns.some((c) => c.name === n) ? 'not a number column' : 'no longer in this query'}
                    onRemove={() => togglePivotRole('values', n, false)}
                  />
                ))}
                {numeric.map((c) => {
                  const taken = pivotRows.includes(c.name) || pivotColumn === c.name
                  return (
                    <label key={c.name} className="flex min-w-0 items-center gap-2 text-sm">
                      <Checkbox
                        checked={pivotValues.includes(c.name)}
                        disabled={taken || (!pivotValues.includes(c.name) && pivotValues.length >= MAX_PIVOT_VALUES)}
                        onCheckedChange={(v) => togglePivotRole('values', c.name, v === true)}
                      />
                      <span className="min-w-0 flex-1 truncate">{c.name}</span>
                    </label>
                  )
                })}
              </fieldset>
              <div className="flex items-center justify-between gap-2">
                <Label htmlFor="pivot-totals" className="text-xs">
                  Totals
                </Label>
                <Switch id="pivot-totals" checked={pivotTotals} onCheckedChange={setPivotTotals} />
              </div>
              <div className="flex items-center justify-between gap-2">
                <Label htmlFor="pivot-shade" className="text-xs">
                  Shade cells
                </Label>
                <Switch id="pivot-shade" checked={pivotShade} onCheckedChange={setPivotShade} />
              </div>
            </>
          ) : isTable ? (
            <fieldset className="flex flex-col gap-1.5">
              <legend className="text-xs font-medium">{`Columns (up to ${MAX_TABLE_COLUMNS})`}</legend>
              {tableColumns.map((c, i) => (
                <div key={`${c}:${i}`} className="flex min-w-0 items-center gap-1.5 text-sm">
                  <span className="min-w-0 flex-1 truncate">{c}</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-6 shrink-0"
                    aria-label={`Move ${c} up`}
                    disabled={i === 0}
                    onClick={() => moveTableColumn(c, -1)}
                  >
                    <ArrowUp className="size-3.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-6 shrink-0"
                    aria-label={`Move ${c} down`}
                    disabled={i === tableColumns.length - 1}
                    onClick={() => moveTableColumn(c, 1)}
                  >
                    <ArrowDown className="size-3.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="size-6 shrink-0"
                    aria-label={`Remove ${c}`}
                    onClick={() => toggleTableColumn(c, false)}
                  >
                    <X className="size-3.5" />
                  </Button>
                </div>
              ))}
              <span className="mt-1 text-xs font-medium text-muted-foreground">Available columns</span>
              {columns.map((c) => (
                <label key={c.name} className="flex min-w-0 items-center gap-2 text-sm">
                  <Checkbox
                    checked={tableColumns.includes(c.name)}
                    disabled={!tableColumns.includes(c.name) && tableColumns.length >= MAX_TABLE_COLUMNS}
                    onCheckedChange={(v) => toggleTableColumn(c.name, v === true)}
                  />
                  <span className="min-w-0 flex-1 truncate">{c.name}</span>
                </label>
              ))}
            </fieldset>
          ) : isCounter ? (
            <>
              <Field label="Number to show">
                <ColumnSelect columns={numeric} value={counterValue} onChange={setCounterValue} />
              </Field>
              <Field label="Label (optional)">
                <Input value={counterLabel} maxLength={100} onChange={(e) => setCounterLabel(e.target.value)} />
              </Field>
              <Field label="Decimals">
                <Select value={String(decimals)} onValueChange={(v) => setDecimals(Number(v))}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {[0, 1, 2, 3, 4].map((d) => (
                      <SelectItem key={d} value={String(d)}>
                        {d}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </Field>
              <Field label="Target (optional)">
                <Select
                  value={counterTarget && !staleTarget ? counterTarget : NONE}
                  onValueChange={(v) => v && setCounterTarget(v === NONE ? '' : v)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NONE}>No target</SelectItem>
                    {numeric
                      .filter((c) => c.name !== counterValue)
                      .map((c) => (
                        <SelectItem key={c.name} value={c.name}>
                          {c.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
                {staleTarget && (
                  <StaleRole
                    name={counterTarget}
                    hint={columns.some((c) => c.name === counterTarget) ? 'not a number column' : 'no longer in this query'}
                    onRemove={() => setCounterTarget('')}
                  />
                )}
              </Field>
              <p className="text-xs text-muted-foreground">The counter shows the first row of the results.</p>
            </>
          ) : (
            <>
              <Field label={xNeedsNumberOrDate ? 'X axis (number or date)' : 'Category (X axis)'}>
                <ColumnSelect columns={xNeedsNumberOrDate ? scatterXColumns : columns} value={staleX ? '' : x} onChange={(v) => v && setX(v)} />
                {staleX && (
                  <StaleRole
                    name={x}
                    hint={columns.some((c) => c.name === x) ? 'not a number or date column' : 'no longer in this query'}
                    onRemove={() => setX('')}
                  />
                )}
              </Field>

              <fieldset className="flex flex-col gap-1.5">
                <legend className="text-xs font-medium">
                  {measureLimit === 1 ? 'Measure' : `Measures (up to ${MAX_MEASURES})`}
                </legend>
                {numeric.length === 0 && (
                  <p className="text-xs text-muted-foreground">
                    This query has no number column. Give a column a total (Sum, Count...) to chart it.
                  </p>
                )}
                {staleMeasures.map((n) => (
                  <StaleRole
                    key={`stale-${n}`}
                    name={n}
                    hint={columns.some((c) => c.name === n) ? 'not a number column' : 'no longer in this query'}
                    onRemove={() => toggleMeasure(n, false)}
                  />
                ))}
                {numeric.map((c) => (
                  <label key={c.name} className="flex min-w-0 items-center gap-2 text-sm">
                    <Checkbox
                      checked={options.measures.includes(c.name)}
                      onCheckedChange={(v) => toggleMeasure(c.name, v === true)}
                    />
                    <span className="min-w-0 flex-1 truncate">{c.name}</span>
                    {isCombo && options.measures.includes(c.name) && (
                      <span className="flex shrink-0 gap-1" role="group" aria-label={`${c.name} drawn as`}>
                        {[false, true].map((asLine) => (
                          <Button
                            key={String(asLine)}
                            type="button"
                            size="sm"
                            variant={lineMeasures.includes(c.name) === asLine ? 'secondary' : 'ghost'}
                            aria-pressed={lineMeasures.includes(c.name) === asLine}
                            className="h-6 px-2 text-xs"
                            onClick={() => setMeasureAsLine(c.name, asLine)}
                          >
                            {asLine ? 'Line' : 'Bar'}
                          </Button>
                        ))}
                      </span>
                    )}
                  </label>
                ))}
              </fieldset>

              {canSplit && (
                <Field label="Split by (optional)">
                  <Select value={series || NONE} onValueChange={(v) => v && setSeries(v === NONE ? '' : v)}>
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NONE}>No split</SelectItem>
                      {columns
                        .filter((c) => c.name !== x)
                        .map((c) => (
                          <SelectItem key={c.name} value={c.name}>
                            {c.name}
                          </SelectItem>
                        ))}
                    </SelectContent>
                  </Select>
                </Field>
              )}

              {isScatter && (
                <>
                  <Field label="Bubble size (optional)">
                    <Select value={size && !staleSize ? size : NONE} onValueChange={(v) => v && setSize(v === NONE ? '' : v)}>
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={NONE}>No size</SelectItem>
                        {numeric.map((c) => (
                          <SelectItem key={c.name} value={c.name}>
                            {c.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    {staleSize && (
                      <StaleRole
                        name={size}
                        hint={columns.some((c) => c.name === size) ? 'not a number column' : 'no longer in this query'}
                        onRemove={() => setSize('')}
                      />
                    )}
                  </Field>
                  <Field label="Point label (optional)">
                    <Select value={label && !staleLabel ? label : NONE} onValueChange={(v) => v && setLabel(v === NONE ? '' : v)}>
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={NONE}>No label</SelectItem>
                        {columns.map((c) => (
                          <SelectItem key={c.name} value={c.name}>
                            {c.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    {staleLabel && <StaleRole name={label} hint="no longer in this query" onRemove={() => setLabel('')} />}
                  </Field>
                </>
              )}

              {!isScatter && !isTrend && (
                <Field label="Order of X">
                  <Select value={sortX} onValueChange={(v) => v && setSortX(v as VisualizationSort)}>
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="none">As returned by the query</SelectItem>
                      <SelectItem value="asc">Ascending</SelectItem>
                      <SelectItem value="desc">Descending</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
              )}

              {type === 'funnel' && (
                <p className="text-xs text-muted-foreground">
                  The funnel follows the order of the query rows, so sort the query by step to set the step order.
                </p>
              )}

              {canStack && (
                <div className="flex items-center justify-between gap-2">
                  <Label htmlFor="chart-stacked" className="text-xs">
                    Stacked
                  </Label>
                  <Switch id="chart-stacked" checked={stacked} onCheckedChange={setStacked} />
                </div>
              )}
              {canStack && stacked && (
                <div className="flex items-center justify-between gap-2">
                  <Label htmlFor="chart-percent" className="text-xs">
                    Show as 100%
                  </Label>
                  <Switch id="chart-percent" checked={percent} onCheckedChange={setPercent} />
                </div>
              )}
              {averagedPercent && (
                <p className="text-xs text-muted-foreground">Percentages of averages are rarely meaningful.</p>
              )}
            </>
          )}

          <fieldset className="flex flex-col gap-3">
            <legend className="mb-1.5 text-xs font-medium">Format</legend>
            <Field label="Number style">
              <Select value={formatStyle} onValueChange={(v) => v && setFormatStyle(v as ValueFormatStyle | typeof NONE)}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {FORMAT_STYLES.map((f) => (
                    <SelectItem key={f.value} value={f.value}>
                      {f.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            {formatStyle !== NONE && (
              <Field label="Decimal places">
                <Select
                  value={formatDecimals === null ? AUTO : String(formatDecimals)}
                  onValueChange={(v) => v && setFormatDecimals(v === AUTO ? null : Number(v))}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={AUTO}>Automatic</SelectItem>
                    {[0, 1, 2, 3, 4].map((d) => (
                      <SelectItem key={d} value={String(d)}>
                        {d}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </Field>
            )}
            {formatStyle === 'currency' && (
              <Field label="Currency code">
                <Input value={currency} maxLength={3} placeholder="USD" onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
              </Field>
            )}
          </fieldset>
        </div>

        <div className="flex min-w-0 flex-col gap-2 rounded-md border border-border p-3">
          <span className="text-xs font-medium text-muted-foreground">Preview</span>
          {problem ? (
            <p className="py-8 text-center text-sm text-muted-foreground">{problem}</p>
          ) : !previewResult ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              Run the query to preview this chart with your results.
            </p>
          ) : !previewHasColumns ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              Your latest results don't include these columns. Save and re-run the query to preview.
            </p>
          ) : (
            <>
              {previewResult.truncated && !isTable && <p className="text-xs text-muted-foreground">{PARTIAL_PREVIEW}</p>}
              {/* Only a table keeps the truncation note of its own: the server refuses to draw any other type from partial rows. */}
              <ChartRenderer type={type} options={options} rows={previewResult.rows} name={effectiveName} columns={previewColumns} truncated={isTable && previewResult.truncated} rowLimit={previewResult.rowLimit} />
            </>
          )}
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button
          disabled={!!problem || isSaving}
          onClick={() => onSave({ name: effectiveName, type, options })}
        >
          {isSaving ? 'Saving…' : editing ? 'Save changes' : 'Add chart'}
        </Button>
      </DialogFooter>
    </>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label className="text-xs">{label}</Label>
      {children}
    </div>
  )
}

function ColumnSelect({
  columns,
  value,
  onChange,
}: {
  columns: OutputColumn[]
  value: string
  onChange: (value: string) => void
}) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger className="w-full">
        <SelectValue placeholder="Choose a column" />
      </SelectTrigger>
      <SelectContent>
        {columns.map((c) => (
          <SelectItem key={c.name} value={c.name}>
            {c.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

function StaleRole({ name, hint, onRemove }: { name: string; hint: string; onRemove: () => void }) {
  return (
    <div className="flex min-w-0 items-center gap-1.5 text-sm text-destructive">
      <span className="min-w-0 flex-1 truncate">{`${name} (${hint})`}</span>
      <Button type="button" variant="ghost" size="icon" className="size-6 shrink-0" aria-label={`Remove ${name}`} onClick={onRemove}>
        <X className="size-3.5" />
      </Button>
    </div>
  )
}
