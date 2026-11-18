import { useMemo, useState, useSyncExternalStore } from 'react'
import { Cell, Pie, PieChart } from 'recharts'
import { Table as TableIcon } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from '@/components/ui/chart'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import {
  buildModel,
  buildScatter,
  formatScatterX,
  describeChart,
  duplicateXCount,
  hasStackGap,
  isPercentStack,
  negativeCategoryCount,
  scatterHasTime,
  type CounterModel,
  type PieModel,
} from '@/features/visualizations/chart-data'
import { TableView } from '@/features/visualizations/TableView'
import { PivotView } from '@/features/visualizations/PivotView'
import { PIVOT_TOO_WIDE, MAX_PIVOT_COLUMNS, buildPivot } from '@/features/visualizations/pivot-model'
import { FunnelView } from '@/features/visualizations/FunnelView'
import { WaterfallView } from '@/features/visualizations/WaterfallView'
import { TrendView } from '@/features/visualizations/TrendView'
import { ScatterView } from '@/features/visualizations/ScatterView'
import { KIND_LABEL, buildFunnel, buildTrend, buildWaterfall } from '@/features/visualizations/story-models'
import { CartesianView } from '@/features/visualizations/CartesianView'
import { paletteColor } from '@/features/visualizations/chart-types'
import { formatValue } from '@/features/visualizations/format'
import { counterText, fitFontSize, targetText, trendSummary } from '@/features/visualizations/single-number'
import { isAggregated, stepsForMark, type ClickedMark } from '@/features/drill/drill-model'
import { fireMark, type MarkDrill } from '@/features/drill/mark-click'
import { pickRow, rowsOf } from '@/features/visualizations/mark-sources'
import type {
  AggregateFunction,
  ColumnDataType,
  QueryResultColumnDto,
  ValueFormat,
  VisualizationOptions,
  VisualizationType,
} from '@/types'

const REDUCED_MOTION = '(prefers-reduced-motion: reduce)'

function subscribeReducedMotion(onChange: () => void) {
  const query = window.matchMedia(REDUCED_MOTION)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}

function usePrefersReducedMotion() {
  return useSyncExternalStore(
    subscribeReducedMotion,
    () => window.matchMedia(REDUCED_MOTION).matches,
    () => false,
  )
}

type RendererProps = {
  type: VisualizationType
  options: VisualizationOptions
  rows: Record<string, unknown>[]
  name: string
  /** Stretch to the parent's height (dashboard tiles) instead of the fixed 360px editor height. */
  fill?: boolean
  /** A shorter natural height, for dashboard tiles that size themselves to their content. */
  compact?: boolean
  columns?: { name: string; dataType?: ColumnDataType; aggregate?: AggregateFunction | null }[]
  truncated?: boolean
  rowLimit?: number
  /** Present = marks are clickable. Absent = the chart behaves exactly as it always did. */
  onMarkClick?: (mark: ClickedMark) => void
  /** Plain columns the chart does not draw but that identify the clicked result row (from the render response). */
  keyColumns?: string[]
}

type InnerProps = Omit<RendererProps, 'onMarkClick' | 'keyColumns'> & { drill?: MarkDrill }

function toResultColumns(columns: NonNullable<RendererProps['columns']>): QueryResultColumnDto[] {
  return columns.map((c) => ({ name: c.name, dataType: c.dataType ?? 'unknown', aggregate: c.aggregate }) as QueryResultColumnDto)
}

export function ChartRenderer({ onMarkClick, keyColumns, ...props }: RendererProps) {
  const { type, options, columns } = props
  const drill = useMemo<MarkDrill | undefined>(() => {
    // Without the result columns the server's rules (which columns identify a mark) cannot be applied, so no click.
    if (!onMarkClick || !columns) return undefined
    const resultColumns = toResultColumns(columns)
    return {
      onMark: onMarkClick,
      keyColumns,
      canDrill: (mark) => stepsForMark(type, options, resultColumns, keyColumns ?? [], { ...mark, anchor: { x: 0, y: 0 } }) !== null,
    }
  }, [onMarkClick, columns, keyColumns, type, options])

  const inner = { ...props, drill }
  if (props.type === 'pivot') {
    return <PivotRenderer options={props.options} rows={props.rows} name={props.name} columns={props.columns} fill={props.fill ?? false} drill={drill} />
  }
  if (props.type === 'funnel') {
    return <FunnelRenderer {...inner} fill={props.fill ?? false} />
  }
  if (props.type === 'waterfall') return <WaterfallRenderer {...inner} />
  if (props.type === 'scatter') return <ScatterRenderer {...inner} />
  if (props.type === 'trend') return <TrendRenderer {...inner} />
  return <DrawnRenderer {...inner} />
}

function ProblemAlert({ message }: { message: string }) {
  return (
    <Alert variant="destructive">
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}

function ShowData({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="flex shrink-0 flex-col gap-2">
      <Button variant="ghost" size="sm" className="h-7 w-fit gap-1.5 px-2 text-xs" aria-expanded={open} onClick={() => setOpen((v) => !v)}>
        <TableIcon className="size-3.5" />
        {open ? 'Hide data' : 'Show data'}
      </Button>
      {open && children}
    </div>
  )
}

function FunnelRenderer({ options, rows, name, truncated, rowLimit, fill, drill }: InnerProps & { fill: boolean }) {
  const model = useMemo(() => buildFunnel(options, rows), [options, rows])
  if (model.problem) return <ProblemAlert message={model.problem} />
  return <FunnelView model={model} name={name} format={options.format} truncated={truncated} rowLimit={rowLimit} fill={fill} drill={drill} />
}

function WaterfallRenderer({ options, rows, name, fill = false, compact = false, truncated = false, rowLimit, drill }: InnerProps) {
  const model = useMemo(() => buildWaterfall(options, rows), [options, rows])
  const reducedMotion = usePrefersReducedMotion()
  const heightClass = compact ? 'h-[280px]' : 'h-[360px]'
  const format = options.format

  if (model.problem) return <ProblemAlert message={model.problem} />

  if (rows.length === 0 || model.bars.length === 0) {
    return (
      <div className="py-10 text-center text-sm text-muted-foreground">
        <p>No data to chart for these filters.</p>
        {model.skipped > 0 && <p className="mt-1 text-xs">{model.skipped} value(s) could not be plotted.</p>}
      </div>
    )
  }

  return (
    <div className={`flex min-w-0 flex-col gap-3 ${fill ? 'h-full' : ''}`}>
      <div
        role="img"
        aria-label={`${name} — Waterfall chart: ${options.measures[0]} by ${options.x}, ${model.bars.length - 1} steps ending in ${formatValue(model.bars[model.bars.length - 1].value, format)}`}
        className={fill ? 'min-w-0 flex-1 shrink-0' : 'min-w-0'}
      >
        <WaterfallView model={model} animate={!reducedMotion} heightClass={heightClass} format={format} fill={fill} drill={drill} />
      </div>
      {truncated && (
        <p className="text-xs text-muted-foreground">
          {rowLimit ? `Only the first ${rowLimit.toLocaleString()} rows are shown.` : 'Only the first rows are shown.'}
        </p>
      )}
      {model.skipped > 0 && <p className="text-xs text-muted-foreground">{model.skipped} value(s) could not be plotted.</p>}
      <ShowData>
        <div className="max-h-72 overflow-auto rounded-md border border-border">
          <Table>
            <caption className="sr-only">Data plotted in {name}</caption>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Step</TableHead>
                <TableHead scope="col">Type</TableHead>
                <TableHead scope="col" className="text-right">
                  Amount
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {model.bars.map((b, i) => (
                <TableRow key={i}>
                  <TableCell>{b.name}</TableCell>
                  <TableCell>{KIND_LABEL[b.kind]}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatValue(b.value, format)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </ShowData>
    </div>
  )
}

function TrendRenderer({ options, rows, name, fill = false, compact = false, truncated = false, rowLimit, drill }: InnerProps) {
  const model = useMemo(() => buildTrend(options, rows), [options, rows])
  const reducedMotion = usePrefersReducedMotion()
  const format = options.format
  const measure = options.measures[0] ?? ''

  if (rows.length === 0 || model.points.length === 0) {
    return (
      <div className="py-10 text-center text-sm text-muted-foreground">
        <p>No data to chart for these filters.</p>
        {model.skipped > 0 && <p className="mt-1 text-xs">{model.skipped} row(s) could not be plotted.</p>}
      </div>
    )
  }

  return (
    <div className={`flex min-w-0 flex-col gap-3 ${fill ? 'h-full' : ''}`}>
      <div
        role="img"
        aria-label={`${name} — ${trendSummary(model, measure, format)}`}
        className={fill ? 'min-w-0 flex-1 shrink-0' : compact ? 'h-[200px] min-w-0' : 'h-[260px] min-w-0'}
      >
        <TrendView model={model} animate={!reducedMotion} format={format} drill={drill} />
      </div>
      {truncated && (
        <p className="text-xs text-muted-foreground">
          {rowLimit ? `Only the first ${rowLimit.toLocaleString()} rows are shown.` : 'Only the first rows are shown.'}
        </p>
      )}
      {model.skipped > 0 && <p className="text-xs text-muted-foreground">{model.skipped} row(s) could not be plotted.</p>}
      <ShowData>
        <div className="max-h-72 overflow-auto rounded-md border border-border">
          <Table>
            <caption className="sr-only">Data plotted in {name}</caption>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">{options.x}</TableHead>
                <TableHead scope="col" className="text-right">
                  {measure}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {model.points.map((p, i) => (
                <TableRow key={i}>
                  <TableCell>{p.x}</TableCell>
                  <TableCell className="text-right tabular-nums">{p.y === null ? '' : formatValue(p.y, format)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </ShowData>
    </div>
  )
}

function ScatterRenderer({ options, rows, name, fill = false, compact = false, truncated = false, rowLimit, drill }: InnerProps) {
  const model = useMemo(() => buildScatter(options, rows), [options, rows])
  const reducedMotion = usePrefersReducedMotion()
  const heightClass = compact ? 'h-[280px]' : 'h-[360px]'
  const format = options.format
  const yName = options.measures[0] ?? ''
  const xName = options.x ?? ''
  const pointCount = model.groups.reduce((n, g) => n + g.points.length, 0)
  const withTime = scatterHasTime(model)

  if (model.problem) return <ProblemAlert message={model.problem} />

  if (rows.length === 0 || pointCount === 0) {
    return (
      <div className="py-10 text-center text-sm text-muted-foreground">
        <p>No data to chart for these filters.</p>
        {model.skipped > 0 && <p className="mt-1 text-xs">{model.skipped} value(s) could not be plotted.</p>}
      </div>
    )
  }

  const bubbles = model.groups.some((g) => g.points.some((p) => p.size !== null))
  const splitNote = options.series ? ` split by ${options.series}` : ''
  const summary = `${bubbles ? 'Bubble' : 'Scatter'} chart: ${yName} against ${xName}${splitNote}, ${pointCount} points`

  return (
    <div className={`flex min-w-0 flex-col gap-3 ${fill ? 'h-full' : ''}`}>
      <div role="img" aria-label={`${name} — ${summary}`} className={fill ? 'min-w-0 flex-1 shrink-0' : 'min-w-0'}>
        <ScatterView
          model={model}
          animate={!reducedMotion}
          heightClass={heightClass}
          format={format}
          xName={xName}
          yName={yName}
          sizeName={options.size}
          seriesName={options.series}
          fill={fill}
          drill={drill}
        />
      </div>
      {truncated && (
        <p className="text-xs text-muted-foreground">
          {rowLimit ? `Only the first ${rowLimit.toLocaleString()} rows are shown.` : 'Only the first rows are shown.'}
        </p>
      )}
      {model.skipped > 0 && <p className="text-xs text-muted-foreground">{model.skipped} value(s) could not be plotted.</p>}
      <ShowData>
        <div className="max-h-72 overflow-auto rounded-md border border-border">
            <Table>
              <caption className="sr-only">Data plotted in {name}</caption>
              <TableHeader>
                <TableRow>
                  {options.series && <TableHead scope="col">{options.series}</TableHead>}
                  {options.label && <TableHead scope="col">{options.label}</TableHead>}
                  <TableHead scope="col">{xName}</TableHead>
                  <TableHead scope="col" className="text-right">
                    {yName}
                  </TableHead>
                  {options.size && (
                    <TableHead scope="col" className="text-right">
                      {options.size}
                    </TableHead>
                  )}
                </TableRow>
              </TableHeader>
              <TableBody>
                {model.groups.flatMap((g, gi) =>
                  g.points.map((p, i) => (
                    <TableRow key={`${gi}:${i}`}>
                      {options.series && <TableCell>{g.name}</TableCell>}
                      {options.label && <TableCell>{p.label ?? ''}</TableCell>}
                      <TableCell className="tabular-nums">{formatScatterX(p.x, model.xIsDate, withTime)}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatValue(p.y, format)}</TableCell>
                      {options.size && (
                        <TableCell className="text-right tabular-nums">{p.size === null ? '' : formatValue(p.size)}</TableCell>
                      )}
                    </TableRow>
                  )),
                )}
              </TableBody>
            </Table>
        </div>
      </ShowData>
    </div>
  )
}

function PivotRenderer({
  options,
  rows,
  name,
  columns,
  fill,
  drill,
}: Pick<RendererProps, 'options' | 'rows' | 'name' | 'columns'> & { fill: boolean; drill?: MarkDrill }) {
  const model = useMemo(
    () =>
      buildPivot(options, rows, (n) => {
        const found = columns?.find((c) => c.name === n)
        return found?.aggregate
      }),
    [options, rows, columns],
  )
  if (model.columnHeaders.length > MAX_PIVOT_COLUMNS) {
    return (
      <Alert variant="destructive">
        <AlertDescription>{PIVOT_TOO_WIDE}</AlertDescription>
      </Alert>
    )
  }
  return (
    <PivotView
      model={model}
      name={name}
      rowNames={options.pivot?.rows ?? []}
      columnName={options.pivot?.column}
      format={options.format}
      shade={options.pivot?.shade ?? false}
      fill={fill}
      drill={drill}
    />
  )
}

function DrawnRenderer({
  type,
  options,
  rows,
  name,
  columns,
  fill = false,
  compact = false,
  truncated = false,
  rowLimit,
  drill,
}: InnerProps) {
  const model = useMemo(() => buildModel(type, options, rows), [type, options, rows])
  const reducedMotion = usePrefersReducedMotion()
  const summary = describeChart(type, options, model)
  const heightClass = fill ? 'h-full min-h-[96px]' : compact ? 'h-[280px]' : 'h-[360px]'

  if (model.kind !== 'counter' && model.kind !== 'table' && rows.length === 0) {
    return (
      <p className="py-10 text-center text-sm text-muted-foreground">No data to chart for these filters.</p>
    )
  }

  if (model.kind === 'table') {
    return (
      <TableView
        model={model}
        name={name}
        columns={columns}
        format={options.format}
        truncated={truncated}
        rowLimit={rowLimit}
        fill={fill}
        drill={columns && isAggregated(toResultColumns(columns)) ? drill : undefined}
      />
    )
  }

  const skipped = model.kind === 'counter' ? 0 : model.skipped
  const sharedX = model.kind === 'cartesian' ? duplicateXCount(options, rows) : 0
  // Stacked bars read as missing-means-zero, and only an area visibly dips where a value is absent.
  const stackGap = model.kind === 'cartesian' && type === 'area' && !!options.stacked && hasStackGap(model)
  const percent = model.kind === 'cartesian' && isPercentStack(type, options)
  const negativeShares = model.kind === 'cartesian' && percent ? negativeCategoryCount(model.points, model.series) : 0
  const format = options.format
  const counterClickable = model.kind === 'counter' && !!drill && drill.canDrill({})

  return (
    <div className={`flex min-w-0 flex-col gap-3 ${fill ? 'h-full' : ''}`}>
      {model.kind === 'pie' && model.problem ? (
        <ProblemAlert message={model.problem} />
      ) : (
        <div
          // Children of role="img" are presentational, which would hide the counter's button from assistive tech.
          {...(counterClickable ? {} : { role: 'img', 'aria-label': `${name} — ${summary}` })}
          className={fill ? 'min-h-[96px] min-w-0 flex-1 shrink-0' : 'min-w-0'}
        >
          {model.kind === 'counter' && (
            <CounterView model={model} decimals={options.counter?.decimals ?? 0} format={format} drill={drill} />
          )}
          {model.kind === 'pie' && (
            <PieView model={model} animate={!reducedMotion} heightClass={heightClass} format={format} drill={drill} />
          )}
          {model.kind === 'cartesian' && (
            <CartesianView
              type={type}
              options={options}
              model={model}
              animate={!reducedMotion}
              heightClass={heightClass}
              format={format}
              drill={drill}
            />
          )}
        </div>
      )}

      {truncated && (
        <p className="text-xs text-muted-foreground">
          {rowLimit ? `Only the first ${rowLimit.toLocaleString()} rows are shown.` : 'Only the first rows are shown.'}
        </p>
      )}

      {skipped > 0 && (
        <p className="text-xs text-muted-foreground">{skipped} value(s) could not be plotted.</p>
      )}

      {sharedX > 0 && (
        <p className="text-xs text-muted-foreground">
          {sharedX} rows share the same X value. Add a Split by column or a total.
        </p>
      )}

      {stackGap && (
        <p className="text-xs text-muted-foreground">Some values are missing, so this stack may understate the total.</p>
      )}

      {negativeShares > 0 && (
        <p className="text-xs text-muted-foreground">
          {negativeShares === 1
            ? "1 category contains a negative value and can't be shown as a share."
            : `${negativeShares} categories contain negative values and can't be shown as a share.`}
        </p>
      )}

      <ShowData>
        <DataTable model={model} name={name} decimals={options.counter?.decimals ?? 0} format={format} percent={percent} />
      </ShowData>
    </div>
  )
}

function CounterView({
  model,
  decimals,
  format,
  drill,
}: {
  model: CounterModel
  decimals: number
  format?: ValueFormat | null
  drill?: MarkDrill
}) {
  const text = counterText(model.value, decimals, format)
  const target = targetText(model.pctOfTarget)
  const fill = Math.min(Math.max(model.pctOfTarget ?? 0, 0), 100)
  return (
    <div className="@container flex h-full min-w-0 flex-col items-center justify-center gap-1 py-6">
      {drill && drill.canDrill({}) ? (
        // A real button, so the number can be reached and activated from the keyboard too.
        <button
          type="button"
          className="font-numeric block max-w-full cursor-pointer truncate rounded-md leading-none font-semibold outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
          style={{ fontSize: fitFontSize(text) }}
          aria-label={`${text}, ${model.label}. Explore this value`}
          onClick={(e) => fireMark(drill, {}, e)}
        >
          {text}
        </button>
      ) : (
        <span className="font-numeric block max-w-full truncate leading-none font-semibold" style={{ fontSize: fitFontSize(text) }}>
          {text}
        </span>
      )}
      <span className="map-label max-w-full truncate text-muted-foreground">{model.label}</span>
      {target && (
        <>
          <span className="text-sm text-foreground">{target}</span>
          <div aria-hidden="true" className="mt-1 h-1.5 w-full max-w-48 overflow-hidden rounded-full bg-muted">
            <div className="h-full rounded-full bg-primary" style={{ width: `${fill}%` }} />
          </div>
        </>
      )}
    </div>
  )
}

function PieView({
  model,
  animate,
  heightClass,
  format,
  drill,
}: {
  model: PieModel
  animate: boolean
  heightClass: string
  format?: ValueFormat | null
  drill?: MarkDrill
}) {
  const config: ChartConfig = {}
  const markAt = (index: number) => {
    const row = pickRow(rowsOf(model.slices[index]), drill?.keyColumns)
    return row ? { row } : null
  }
  return (
    <ChartContainer config={config} className={`aspect-auto w-full font-numeric text-[13px] ${heightClass}`}>
      <PieChart accessibilityLayer>
        <ChartTooltip content={<ChartTooltipContent valueFormatter={(v) => formatValue(v, format)} />} />
        <Pie
          data={model.slices}
          dataKey="value"
          nameKey="name"
          innerRadius="45%"
          outerRadius="80%"
          paddingAngle={0}
          isAnimationActive={animate}
          {...(drill ? { onClick: (_d: unknown, index: number, e: React.MouseEvent) => fireMark(drill, markAt(index), e) } : {})}
        >
          {model.slices.map((slice, i) => (
            <Cell
              key={slice.name}
              style={drill && markAt(i) && drill.canDrill(markAt(i)!) ? { cursor: 'pointer' } : undefined}
              fill={paletteColor(i)}
              stroke={model.slices.length > 1 ? 'var(--mark-outline)' : 'none'}
              strokeWidth={1}
            />
          ))}
        </Pie>
        <ChartLegend content={<ChartLegendContent />} />
      </PieChart>
    </ChartContainer>
  )
}

/** Exactly the rows that are plotted, as a real table for assistive tech and copy/paste. */
function DataTable({
  model,
  name,
  decimals,
  format,
  percent,
}: {
  model: ReturnType<typeof buildModel>
  name: string
  decimals: number
  format?: ValueFormat | null
  percent: boolean
}) {
  const cell = (v: unknown) => (typeof v === 'number' ? formatValue(v, format) : v === null || v === undefined ? '' : String(v))

  return (
    <div className="max-h-72 overflow-auto rounded-md border border-border">
      {percent && (
        <p className="px-3 py-2 text-xs text-muted-foreground">
          The chart shows each category as a share of its total; this table shows the raw values.
        </p>
      )}
      <Table>
        <caption className="sr-only">Data plotted in {name}</caption>
        {model.kind === 'cartesian' && (
          <>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Category</TableHead>
                {model.series.map((s) => (
                  <TableHead key={s.key} scope="col" className="text-right">
                    {s.label}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {model.points.map((p, i) => (
                <TableRow key={i}>
                  <TableCell>{String(p.x)}</TableCell>
                  {model.series.map((s) => (
                    <TableCell key={s.key} className="text-right tabular-nums">
                      {cell(p[s.key])}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </>
        )}
        {model.kind === 'pie' && (
          <>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Category</TableHead>
                <TableHead scope="col" className="text-right">
                  Value
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {model.slices.map((s) => (
                <TableRow key={s.name}>
                  <TableCell>{s.name}</TableCell>
                  <TableCell className="text-right tabular-nums">{cell(s.value)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </>
        )}
        {model.kind === 'counter' && (
          <>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Measure</TableHead>
                <TableHead scope="col" className="text-right">
                  Value
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow>
                <TableCell>{model.label}</TableCell>
                <TableCell className="text-right tabular-nums">{counterText(model.value, decimals, format)}</TableCell>
              </TableRow>
              {model.target !== null && (
                <TableRow>
                  <TableCell>Target</TableCell>
                  <TableCell className="text-right tabular-nums">{counterText(model.target, decimals, format)}</TableCell>
                </TableRow>
              )}
            </TableBody>
          </>
        )}
      </Table>
    </div>
  )
}
