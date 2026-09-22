import { Check, ChevronRight, ChevronsDownUp, ChevronsUpDown, Database, Eye, Plus, Search, Table2 } from 'lucide-react'
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

function objectKey(schemaName: string, objectName: string) {
  return `${schemaName}.${objectName}`
}

export function CatalogSidebar({ dataSourceId }: { dataSourceId: string }) {
  const { data: catalog, isLoading } = useDataSourceCatalog(dataSourceId)
  const [search, setSearch] = useState('')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const source = useBuilderStore((s) => s.definition.source)
  const columns = useBuilderStore((s) => s.definition.columns)
  const setSource = useBuilderStore((s) => s.setSource)
  const addColumn = useBuilderStore((s) => s.addColumn)

  const term = search.trim().toLowerCase()

  const filtered = useMemo(() => {
    if (!catalog) return []
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
  }, [catalog, term])

  const totalCount = useMemo(() => catalog?.schemas.reduce((n, s) => n + s.objects.length, 0) ?? 0, [catalog])
  const hasSource = !!source.objectName
  const sourceKey = hasSource ? objectKey(source.schemaName, source.objectName) : null

  function toggleExpanded(key: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  function handleSelectAsSource(obj: SchemaObjectMetadata) {
    setSource({ schemaName: obj.schemaName, objectName: obj.name, kind: obj.kind })
    setExpanded((prev) => new Set(prev).add(objectKey(obj.schemaName, obj.name)))
  }

  function expandAll() {
    if (!catalog) return
    setExpanded(new Set(catalog.schemas.flatMap((s) => s.objects.map((o) => objectKey(o.schemaName, o.name)))))
  }

  function collapseAll() {
    setExpanded(new Set())
  }

  return (
    <div className="flex h-full min-h-0 flex-col border-r border-border bg-sidebar/40">
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

        {!isLoading && totalCount > 0 && (
          <div className="mt-2 flex items-center justify-between">
            <span className="text-[11px] text-muted-foreground">
              {totalCount} {totalCount === 1 ? 'object' : 'objects'}
            </span>
            <div className="flex items-center gap-0.5">
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="ghost" size="icon" className="size-6" onClick={expandAll} aria-label="Expand all">
                    <ChevronsUpDown className="size-3.5" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Expand all</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger asChild>
                  <Button variant="ghost" size="icon" className="size-6" onClick={collapseAll} aria-label="Collapse all">
                    <ChevronsDownUp className="size-3.5" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>Collapse all</TooltipContent>
              </Tooltip>
            </div>
          </div>
        )}
      </div>

      <ScrollArea className="min-h-0 flex-1">
        <div className="p-2">
          {isLoading && <p className="px-2 py-4 text-xs text-muted-foreground">Loading catalog…</p>}
          {!isLoading && filtered.length === 0 && (
            <p className="px-2 py-4 text-xs text-muted-foreground">No matching tables or columns.</p>
          )}
          {filtered.map((schema) => (
            <div key={schema.name} className="mb-2">
              <div className="flex items-center gap-1.5 px-2 py-1 text-[11px] font-semibold tracking-wide text-muted-foreground uppercase">
                <Database className="size-3 shrink-0" />
                <span className="min-w-0 flex-1 truncate">{schema.name}</span>
                <span className="shrink-0 font-normal normal-case text-muted-foreground/60">{schema.objects.length}</span>
              </div>
              {schema.objects.map((obj) => {
                const key = objectKey(obj.schemaName, obj.name)
                const isActiveSource = sourceKey === key
                const matchesSearch = term !== '' && obj.columns.some((c) => c.name.toLowerCase().includes(term))
                const isExpanded = expanded.has(key) || isActiveSource || matchesSearch
                return (
                  <CatalogObject
                    key={key}
                    object={obj}
                    isActiveSource={isActiveSource}
                    isBlocked={hasSource && !isActiveSource}
                    expanded={isExpanded}
                    onToggleExpanded={() => toggleExpanded(key)}
                    sourceAlias={source.alias}
                    selectedKeys={columns.map((c) => columnKey(c.tableAlias, c.columnName, c.aggregate))}
                    onSelectAsSource={() => handleSelectAsSource(obj)}
                    onAddColumn={(columnName, dataType) => addColumn(source.alias, columnName, dataType)}
                  />
                )
              })}
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
  expanded,
  onToggleExpanded,
  sourceAlias,
  selectedKeys,
  onSelectAsSource,
  onAddColumn,
}: {
  object: SchemaObjectMetadata
  isActiveSource: boolean
  isBlocked: boolean
  expanded: boolean
  onToggleExpanded: () => void
  sourceAlias: string
  selectedKeys: string[]
  onSelectAsSource: () => void
  onAddColumn: (columnName: string, dataType: SchemaObjectMetadata['columns'][number]['dataType']) => void
}) {
  const ObjectIcon = object.kind === 'view' ? Eye : Table2

  return (
    <div className="mb-0.5">
      <div
        className={cn(
          'flex w-full items-center gap-1 rounded-md pr-2 text-left text-xs font-medium transition-colors',
          isActiveSource && 'bg-primary/10 text-primary',
          !isActiveSource && !isBlocked && 'hover:bg-accent',
          isBlocked && 'opacity-40 hover:opacity-70',
        )}
      >
        <button
          type="button"
          onClick={onToggleExpanded}
          className="flex size-6 shrink-0 items-center justify-center rounded hover:bg-accent"
          aria-label={expanded ? `Collapse ${object.name}` : `Expand ${object.name}`}
        >
          <ChevronRight className={cn('size-3.5 transition-transform', expanded && 'rotate-90')} />
        </button>
        <button
          type="button"
          onClick={onSelectAsSource}
          className="flex min-w-0 flex-1 items-center gap-1.5 py-1.5 text-left"
          title={isBlocked ? 'Joining multiple tables is coming soon — picking this switches your source' : undefined}
        >
          <ObjectIcon className="size-3.5 shrink-0" />
          <span className="min-w-0 flex-1 truncate">{object.name}</span>
          {object.kind === 'view' && (
            <Badge variant="secondary" className="h-4 shrink-0 px-1 text-[9px] font-medium">
              view
            </Badge>
          )}
        </button>
      </div>

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
                <span className="min-w-0 flex-1 truncate text-foreground/90">{col.name}</span>
                {isActiveSource && (
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="size-5 shrink-0 opacity-0 group-hover:opacity-100 data-[selected=true]:opacity-100"
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
