import { AlertCircle, Plus, Sparkles, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useBuilderStore } from '@/features/builder/builder-store'
import { FilterValueInput } from '@/features/builder/FilterValueInput'
import { isFilterConditionComplete, operatorsForDataType, OPERATOR_LABELS } from '@/features/builder/filter-operators'
import { DataTypeIcon } from '@/lib/data-type-icons'
import { cn } from '@/lib/utils'
import type { ColumnMetadata } from '@/types'

export function AddFilterButton({ disabled }: { disabled?: boolean }) {
  const addFilterCondition = useBuilderStore((s) => s.addFilterCondition)
  const conditions = useBuilderStore((s) => s.definition.filters.conditions)

  const lastCondition = conditions[conditions.length - 1]
  const blockedByIncomplete = !!lastCondition && !isFilterConditionComplete(lastCondition)

  const button = (
    <Button
      variant="outline"
      size="sm"
      className="h-7 text-xs"
      onClick={addFilterCondition}
      disabled={disabled || blockedByIncomplete}
    >
      <Plus className="size-3.5" />
      Add filter
    </Button>
  )

  if (!blockedByIncomplete) return button

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        {/* span wrapper so the tooltip still fires while the button itself is disabled */}
        <span>{button}</span>
      </TooltipTrigger>
      <TooltipContent>Finish the current filter (or set it to prompt at run time) before adding another.</TooltipContent>
    </Tooltip>
  )
}

export function FiltersPanel({ availableColumns }: { availableColumns: ColumnMetadata[] }) {
  const filters = useBuilderStore((s) => s.definition.filters)
  const removeFilterCondition = useBuilderStore((s) => s.removeFilterCondition)
  const updateFilterCondition = useBuilderStore((s) => s.updateFilterCondition)
  const setFilterLogicalOperator = useBuilderStore((s) => s.setFilterLogicalOperator)
  const toggleFilterParameterized = useBuilderStore((s) => s.toggleFilterParameterized)

  return (
    <div className="flex flex-col gap-2">
      {filters.conditions.length > 1 && (
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          Match
          <Tabs value={filters.operator} onValueChange={(v) => setFilterLogicalOperator(v as 'and' | 'or')}>
            <TabsList className="h-6">
              <TabsTrigger value="and" className="h-5 px-2 text-[11px]">
                All (AND)
              </TabsTrigger>
              <TabsTrigger value="or" className="h-5 px-2 text-[11px]">
                Any (OR)
              </TabsTrigger>
            </TabsList>
          </Tabs>
          of the following conditions
        </div>
      )}

      {filters.conditions.length === 0 && (
        <div className="rounded-md border border-dashed border-border p-(--space-section) text-center text-xs text-muted-foreground">
          No filters yet. Results will include every row.
        </div>
      )}

      {filters.conditions.map((condition, index) => {
        const isComplete = isFilterConditionComplete(condition)
        return (
          <div
            key={index}
            className={cn(
              'flex flex-wrap items-center gap-1.5 rounded-md border border-border bg-card px-2 py-(--space-row-y)',
              !isComplete && 'border-warning/50',
            )}
          >
            {!isComplete && (
              <Tooltip>
                <TooltipTrigger asChild>
                  <AlertCircle className="size-3.5 shrink-0 text-warning" />
                </TooltipTrigger>
                <TooltipContent>Pick a value, or set this to prompt at run time.</TooltipContent>
              </Tooltip>
            )}

            <Select
              value={condition.columnName || undefined}
              onValueChange={(columnName) => {
                const meta = availableColumns.find((c) => c.name === columnName)
                updateFilterCondition(index, {
                  columnName,
                  dataType: meta?.dataType ?? 'text',
                  operator: operatorsForDataType(meta?.dataType ?? 'text')[0],
                  value: '',
                })
              }}
            >
              <SelectTrigger size="sm" className="h-7 w-40 text-xs">
                <SelectValue placeholder="Column" />
              </SelectTrigger>
              <SelectContent>
                {availableColumns.map((col) => (
                  <SelectItem key={col.name} value={col.name} className="text-xs">
                    <DataTypeIcon dataType={col.dataType} isForeignKey={col.isForeignKey} />
                    {col.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select
              value={condition.operator}
              onValueChange={(operator) => updateFilterCondition(index, { operator: operator as typeof condition.operator })}
            >
              <SelectTrigger size="sm" className="h-7 w-36 text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {operatorsForDataType(condition.dataType).map((op) => (
                  <SelectItem key={op} value={op} className="text-xs">
                    {OPERATOR_LABELS[op]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            {condition.isParameterized ? (
              <span className="inline-flex h-7 items-center gap-1 rounded-md bg-query/10 px-2 text-xs font-medium text-query">
                <Sparkles className="size-3" />
                Prompted as "{condition.parameterName}"
              </span>
            ) : (
              <FilterValueInput
                dataType={condition.dataType}
                operator={condition.operator}
                value={condition.value}
                onChange={(value) => updateFilterCondition(index, { value })}
              />
            )}

            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className={cn('size-6', condition.isParameterized && 'text-query')}
                  onClick={() => toggleFilterParameterized(index)}
                  aria-label="Toggle prompt at run time"
                >
                  <Sparkles className="size-3.5" />
                </Button>
              </TooltipTrigger>
              <TooltipContent>{condition.isParameterized ? 'Use a fixed value' : 'Ask the user at run time'}</TooltipContent>
            </Tooltip>

            <Button
              variant="ghost"
              size="icon"
              className="size-6 text-destructive hover:text-destructive"
              onClick={() => removeFilterCondition(index)}
              aria-label="Remove filter"
            >
              <X className="size-3.5" />
            </Button>
          </div>
        )
      })}
    </div>
  )
}
