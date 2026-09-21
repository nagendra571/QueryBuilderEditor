import { Check, ChevronRight, Database, Plus, Search, Table2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ScrollArea } from '@/components/ui/scroll-area'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { useBuilderStore, columnKey } from '@/features/builder/builder-store'
import { useDataSourceCatalog } from '@/hooks/useDataSources'
import { DataTypeIcon } from '@/lib/data-type-icons'
import { cn } from '@/lib/utils'
import type { SchemaObjectMetadata } from '@/types'

export function CatalogSidebar({ dataSourceId }: { dataSourceId: string }) {
  const { data: catalog, isLoading } = useDataSourceCatalog(dataSourceId)
  const [search, setSearch] = useState('')
  const source = useBuilderStore((s) => s.definition.source)
  const columns = useBuilderStore((s) => s.definition.columns)
  const setSource = useBuilderStore((s) => s.setSource)
  const addColumn = useBuilderStore((s) => s.addColumn)

  const filtered = useMemo(() => {
    if (!catalog) return []
    const term = search.trim().toLowerCase()
    if (!term) return catalog.schemas

    return catalog.schemas
      .map((schema) => ({
        ...schema,
        objects: schema.objects.filter(
          (obj) =>
            obj.name.toLowerCase().includes(term) ||
            obj.columns.some((c) => c.name.toLowerCase().includes(term)),
        ),
      }))
      .filter((schema) => schema.objects.length > 0)
  }, [catalog, search])

  const hasSource = !!source.objectName

  return (
    <div className="flex h-full flex-col border-r border-border bg-sidebar/40">
      <div className="border-b border-border p-3">
        <div className="relative">
          <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search tables, views, columns..."
            className="h-8 pl-8 text-xs"
          />
        </div>
      </div>

      <ScrollArea className="flex-1">
        <div className="p-2">
          {isLoading && <p className="px-2 py-4 text-xs text-muted-foreground">Loading catalog…</p>}
          {!isLoading && filtered.length === 0 && (
            <p className="px-2 py-4 text-xs text-muted-foreground">No matching tables or columns.</p>
          )}
          {filtered.map((schema) => (
            <div key={schema.name} className="mb-2">
              <div className="flex items-center gap-1.5 px-2 py-1 text-[11px] font-semibold tracking-wide text-muted-foreground uppercase">
                <Database className="size-3" />
                {schema.name}
              </div>
              {schema.objects.map((obj) => (
                <CatalogObject
                  key={`${obj.schemaName}.${obj.name}`}
                  object={obj}
                  isActiveSource={hasSource && source.objectName === obj.name && source.schemaName === obj.schemaName}
                  isBlocked={hasSource && !(source.objectName === obj.name && source.schemaName === obj.schemaName)}
                  defaultExpanded={!hasSource}
                  sourceAlias={source.alias}
                  selectedKeys={columns.map((c) => columnKey(c.tableAlias, c.columnName, c.aggregate))}
                  onSelectAsSource={() => setSource({ schemaName: obj.schemaName, objectName: obj.name, kind: obj.kind })}
                  onAddColumn={(columnName, dataType) => addColumn(source.alias, columnName, dataType)}
                />
              ))}
            </div>
          ))}
        </div>
      </ScrollArea>
    </div>
  )
}

function CatalogObject({
  object,
  isActiveSource,
  isBlocked,
  defaultExpanded,
  sourceAlias,
  selectedKeys,
  onSelectAsSource,
  onAddColumn,
}: {
  object: SchemaObjectMetadata
  isActiveSource: boolean
  isBlocked: boolean
  defaultExpanded: boolean
  sourceAlias: string
  selectedKeys: string[]
  onSelectAsSource: () => void
  onAddColumn: (columnName: string, dataType: SchemaObjectMetadata['columns'][number]['dataType']) => void
}) {
  const [expanded, setExpanded] = useState(defaultExpanded || isActiveSource)

  return (
    <div className="mb-0.5">
      <button
        type="button"
        onClick={() => (isActiveSource ? setExpanded((v) => !v) : onSelectAsSource())}
        className={cn(
          'flex w-full items-center gap-1.5 rounded-md px-2 py-1.5 text-left text-xs font-medium transition-colors',
          isActiveSource && 'bg-primary/10 text-primary',
          !isActiveSource && !isBlocked && 'hover:bg-accent',
          isBlocked && 'opacity-40 hover:opacity-70',
        )}
        title={isBlocked ? 'Joining multiple tables is coming soon — pick this as your source instead' : undefined}
      >
        <ChevronRight className={cn('size-3.5 shrink-0 transition-transform', expanded && 'rotate-90')} />
        <Table2 className="size-3.5 shrink-0" />
        <span className="truncate">{object.name}</span>
        {object.kind === 'view' && (
          <Badge variant="secondary" className="ml-auto h-4 px-1 text-[9px] font-medium">
            view
          </Badge>
        )}
      </button>

      {expanded && (
        <div className="ml-4 border-l border-border pl-2">
          {object.columns.map((col) => {
            const key = columnKey(sourceAlias, col.name, 'none')
            const isSelected = isActiveSource && selectedKeys.includes(key)
            return (
              <div
                key={col.name}
                className="group flex items-center gap-1.5 rounded px-1.5 py-1 text-xs hover:bg-accent/60"
              >
                <DataTypeIcon dataType={col.dataType} isForeignKey={col.isForeignKey} />
                <span className="truncate text-foreground/90">{col.name}</span>
                {isActiveSource && (
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="ml-auto size-5 shrink-0 opacity-0 group-hover:opacity-100 data-[selected=true]:opacity-100"
                        data-selected={isSelected}
                        disabled={isSelected}
                        onClick={() => onAddColumn(col.name, col.dataType)}
                        aria-label={`Add ${col.name}`}
                      >
                        {isSelected ? <Check className="size-3 text-success" /> : <Plus className="size-3" />}
                      </Button>
                    </TooltipTrigger>
                    <TooltipContent>{isSelected ? 'Already added' : 'Add to query'}</TooltipContent>
                  </Tooltip>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
