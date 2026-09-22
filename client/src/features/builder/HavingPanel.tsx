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
import { aggregateResultDataType, describeAggregate } from '@/features/builder/aggregate-functions'
import { useBuilderStore } from '@/features/builder/builder-store'
import { FilterValueInput } from '@/features/builder/FilterValueInput'
import { isFilterConditionComplete, operatorsForDataType, OPERATOR_LABELS } from '@/features/builder/filter-operators'
import { cn } from '@/lib/utils'
import type { QueryColumn } from '@/types'

function measureKey(m: Pick<QueryColumn, 'tableAlias' | 'columnName' | 'aggregate'>) {
  return `${m.tableAlias}.${m.columnName}.${m.aggregate}`
}

export function AddHavingButton() {
  const addHavingCondition = useBuilderStore((s) => s.addHavingCondition)
  const having = useBuilderStore((s) => s.definition.having)
  const columns = useBuilderStore((s) => s.definition.columns)

  const conditions = having?.conditions ?? []
  const measures = columns.filter((c) => c.aggregate !== 'none')

  const lastCondition = conditions[conditions.length - 1]
  const blockedByIncomplete = !!lastCondition && !isFilterConditionComplete(lastCondition)
  const disabled = measures.length === 0 || blockedByIncomplete

  const button = (
    <Button variant="outline" size="sm" className="h-7 text-xs" onClick={addHavingCondition} disabled={disabled}>
      <Plus className="size-3.5" />
      Add condition
    </Button>
  )

  if (!blockedByIncomplete) return button

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span>{button}</span>
      </TooltipTrigger>
      <TooltipContent>Finish the current condition (or set it to prompt at run time) before adding another.</TooltipContent>
    </Tooltip>
  )
}

export function HavingPanel() {
  const having = useBuilderStore((s) => s.definition.having)
  const columns = useBuilderStore((s) => s.definition.columns)
  const removeHavingCondition = useBuilderStore((s) => s.removeHavingCondition)
  const updateHavingCondition = useBuilderStore((s) => s.updateHavingCondition)
  const setHavingLogicalOperator = useBuilderStore((s) => s.setHavingLogicalOperator)
  const toggleHavingParameterized = useBuilderStore((s) => s.toggleHavingParameterized)

  const measures = columns.filter((c) => c.aggregate !== 'none')
  const conditions = having?.conditions ?? []

  if (measures.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-border p-(--space-section) text-center text-xs text-muted-foreground">
        Give a column a total (Sum, Count, etc.) in the Columns section above to filter on it here.
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      {conditions.length > 1 && (
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          Match
          <Tabs value={having?.operator ?? 'and'} onValueChange={(v) => setHavingLogicalOperator(v as 'and' | 'or')}>
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

      {conditions.length === 0 && (
        <div className="rounded-md border border-dashed border-border p-(--space-section) text-center text-xs text-muted-foreground">
          No conditions yet. Every group will be included.
        </div>
      )}

      {conditions.map((condition, index) => {
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
              value={measureKey(condition)}
              onValueChange={(value) => {
                const measure = measures.find((m) => measureKey(m) === value)
                if (!measure) return
                const dataType = aggregateResultDataType(measure.dataType, measure.aggregate)
                updateHavingCondition(index, {
                  tableAlias: measure.tableAlias,
                  columnName: measure.columnName,
                  aggregate: measure.aggregate,
                  dataType,
                  operator: operatorsForDataType(dataType)[0],
                  value: '',
                })
              }}
            >
              <SelectTrigger size="sm" className="h-7 w-44 text-xs">
                <SelectValue placeholder="Total" />
              </SelectTrigger>
              <SelectContent>
                {measures.map((m) => (
                  <SelectItem key={measureKey(m)} value={measureKey(m)} className="text-xs">
                    {describeAggregate(m.aggregate, m.alias || m.columnName)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select
              value={condition.operator}
              onValueChange={(operator) => updateHavingCondition(index, { operator: operator as typeof condition.operator })}
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
                onChange={(value) => updateHavingCondition(index, { value })}
              />
            )}

            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className={cn('size-6', condition.isParameterized && 'text-query')}
                  onClick={() => toggleHavingParameterized(index)}
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
              onClick={() => removeHavingCondition(index)}
              aria-label="Remove condition"
            >
              <X className="size-3.5" />
            </Button>
          </div>
        )
      })}
    </div>
  )
}
