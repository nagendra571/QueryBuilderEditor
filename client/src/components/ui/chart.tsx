"use client"

import * as React from "react"
import * as RechartsPrimitive from "recharts"
import { cn } from "@/lib/utils"

// Same shape as shadcn/ui's Chart: a config maps each series key to a label and a color, and the
// container publishes those colors as --color-<key> CSS variables so marks and legend share them
// (and follow the light/dark theme through the --chart-N tokens).

export type ChartConfig = Record<string, { label?: React.ReactNode; color?: string }>

const ChartContext = React.createContext<{ config: ChartConfig } | null>(null)

function useChart() {
  const context = React.useContext(ChartContext)
  if (!context) {
    throw new Error("useChart must be used within a <ChartContainer />")
  }
  return context
}

function ChartContainer({
  id,
  className,
  children,
  config,
  ...props
}: React.ComponentProps<"div"> & {
  config: ChartConfig
  children: React.ComponentProps<typeof RechartsPrimitive.ResponsiveContainer>["children"]
}) {
  const uniqueId = React.useId()
  const chartId = `chart-${id || uniqueId.replace(/:/g, "")}`

  const colorStyle = React.useMemo(() => {
    const style: Record<string, string> = {}
    for (const [key, item] of Object.entries(config)) {
      if (item.color) style[`--color-${key}`] = item.color
    }
    return style as React.CSSProperties
  }, [config])

  return (
    <ChartContext.Provider value={{ config }}>
      <div
        data-slot="chart"
        data-chart={chartId}
        style={colorStyle}
        className={cn(
          "flex aspect-video justify-center text-xs [&_.recharts-cartesian-axis-tick_text]:fill-muted-foreground [&_.recharts-cartesian-grid_line[stroke='#ccc']]:stroke-border/50 [&_.recharts-curve.recharts-tooltip-cursor]:stroke-border [&_.recharts-dot[stroke='#fff']]:stroke-transparent [&_.recharts-layer]:outline-hidden [&_.recharts-polar-grid_[stroke='#ccc']]:stroke-border [&_.recharts-radial-bar-background-sector]:fill-muted [&_.recharts-rectangle.recharts-tooltip-cursor]:fill-muted [&_.recharts-reference-line_[stroke='#ccc']]:stroke-border [&_.recharts-sector]:outline-hidden [&_.recharts-sector[stroke='#fff']]:stroke-transparent [&_.recharts-surface]:outline-hidden",
          className
        )}
        {...props}
      >
        <RechartsPrimitive.ResponsiveContainer>{children}</RechartsPrimitive.ResponsiveContainer>
      </div>
    </ChartContext.Provider>
  )
}

const ChartTooltip = RechartsPrimitive.Tooltip

export interface TooltipPayloadItem {
  dataKey?: string | number
  name?: string | number
  value?: unknown
  color?: string
  payload?: Record<string, unknown>
}

function ChartTooltipContent({
  active,
  payload,
  label,
  className,
  valueFormatter,
}: {
  active?: boolean
  payload?: TooltipPayloadItem[]
  label?: React.ReactNode
  className?: string
  valueFormatter?: (value: number, item: TooltipPayloadItem) => string
}) {
  const { config } = useChart()

  if (!active || !payload?.length) return null

  return (
    <div
      className={cn(
        "grid min-w-32 items-start gap-1.5 rounded-lg border border-border/50 bg-background px-2.5 py-1.5 text-xs shadow-xl",
        className
      )}
    >
      {label !== undefined && label !== "" && <div className="font-medium">{label}</div>}
      <div className="grid gap-1.5">
        {payload.map((item, index) => {
          const key = String(item.dataKey ?? item.name ?? index)
          const itemConfig = config[key]
          const display = itemConfig?.label ?? item.name
          const value = typeof item.value === "number" && valueFormatter ? valueFormatter(item.value, item) : String(item.value ?? "")
          return (
            <div key={`${key}-${index}`} className="flex items-center gap-2">
              <span
                aria-hidden
                className="size-2.5 shrink-0 rounded-[2px]"
                style={{ backgroundColor: item.color }}
              />
              <span className="min-w-0 flex-1 truncate text-muted-foreground">{display}</span>
              <span className="font-mono font-medium text-foreground tabular-nums">{value}</span>
            </div>
          )
        })}
      </div>
    </div>
  )
}

const ChartLegend = RechartsPrimitive.Legend

interface LegendPayloadItem {
  value?: string | number
  dataKey?: string | number
  color?: string
}

function ChartLegendContent({
  className,
  payload,
  nameKey,
}: {
  className?: string
  payload?: readonly LegendPayloadItem[]
  nameKey?: string
}) {
  const { config } = useChart()

  if (!payload?.length) return null

  return (
    <ul className={cn("flex flex-wrap items-center justify-center gap-x-4 gap-y-1 pt-3", className)}>
      {payload.map((item, index) => {
        const key = String(nameKey ?? item.dataKey ?? item.value ?? index)
        const label = config[key]?.label ?? item.value
        return (
          <li key={`${key}-${index}`} className="flex items-center gap-1.5 text-xs">
            <span aria-hidden className="size-2.5 shrink-0 rounded-[2px]" style={{ backgroundColor: item.color }} />
            <span className="max-w-48 truncate">{label}</span>
          </li>
        )
      })}
    </ul>
  )
}

export { ChartContainer, ChartTooltip, ChartTooltipContent, ChartLegend, ChartLegendContent }
