import { formatDistanceToNow } from 'date-fns'
import { Database, MoreHorizontal, Plus, Search, Star, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { NewQueryDialog } from '@/features/builder/NewQueryDialog'
import { useDeleteSavedQuery, useSavedQueries, useToggleFavorite } from '@/hooks/useSavedQueries'
import { cn } from '@/lib/utils'
import type { SavedQuerySummaryDto } from '@/types'

type FilterTab = 'all' | 'mine' | 'favorites'

export function QueryListPage() {
  const { data: queries, isLoading } = useSavedQueries()
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState<FilterTab>('all')
  const [newQueryOpen, setNewQueryOpen] = useState(false)
  const navigate = useNavigate()
  const toggleFavorite = useToggleFavorite()
  const deleteQuery = useDeleteSavedQuery()

  const filtered = useMemo(() => {
    if (!queries) return []
    const term = search.trim().toLowerCase()
    return queries.filter((q) => {
      if (tab === 'mine' && !q.isOwnedByCurrentUser) return false
      if (tab === 'favorites' && !q.isFavorite) return false
      if (term && !q.name.toLowerCase().includes(term)) return false
      return true
    })
  }, [queries, search, tab])

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Queries</h1>
          <p className="text-sm text-muted-foreground">Build, run, and share reusable queries over your data views.</p>
        </div>
        <Button onClick={() => setNewQueryOpen(true)}>
          <Plus className="size-4" />
          New query
        </Button>
      </div>

      <div className="flex items-center justify-between gap-3">
        <Tabs value={tab} onValueChange={(v) => setTab(v as FilterTab)}>
          <TabsList>
            <TabsTrigger value="all">All</TabsTrigger>
            <TabsTrigger value="mine">Mine</TabsTrigger>
            <TabsTrigger value="favorites">Favorites</TabsTrigger>
          </TabsList>
        </Tabs>

        <div className="relative w-64">
          <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search queries…" className="h-8 pl-8 text-xs" />
        </div>
      </div>

      {isLoading && (
        <div className="flex flex-col gap-2">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-(--row-h) w-full" />
          ))}
        </div>
      )}

      {!isLoading && filtered.length === 0 && (
        <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed border-border py-16 text-center">
          <Database className="size-8 text-muted-foreground" />
          <div>
            <p className="text-sm font-medium">No queries yet</p>
            <p className="text-xs text-muted-foreground">Create your first query to browse your data views.</p>
          </div>
          <Button size="sm" onClick={() => setNewQueryOpen(true)}>
            <Plus className="size-3.5" />
            New query
          </Button>
        </div>
      )}

      {!isLoading && filtered.length > 0 && (
        <div className="overflow-hidden rounded-lg border border-border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-8" />
                <TableHead>Name</TableHead>
                <TableHead>Data source</TableHead>
                <TableHead>Owner</TableHead>
                <TableHead>Updated</TableHead>
                <TableHead className="w-8" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {filtered.map((query) => (
                <QueryRow
                  key={query.id}
                  query={query}
                  onOpen={() => navigate(`/queries/${query.id}`)}
                  onToggleFavorite={() => toggleFavorite.mutate(query.id)}
                  onDelete={() => deleteQuery.mutate(query.id)}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <NewQueryDialog open={newQueryOpen} onOpenChange={setNewQueryOpen} />
    </div>
  )
}

function QueryRow({
  query,
  onOpen,
  onToggleFavorite,
  onDelete,
}: {
  query: SavedQuerySummaryDto
  onOpen: () => void
  onToggleFavorite: () => void
  onDelete: () => void
}) {
  return (
    <TableRow className="cursor-pointer" onClick={onOpen}>
      <TableCell onClick={(e) => e.stopPropagation()}>
        <Button variant="ghost" size="icon" className="size-6" onClick={onToggleFavorite}>
          <Star className={cn('size-3.5', query.isFavorite && 'fill-warning text-warning')} />
        </Button>
      </TableCell>
      <TableCell>
        <div className="flex flex-col">
          <span className="text-(length:--text-body) font-medium">{query.name}</span>
          {query.description && <span className="truncate text-xs text-muted-foreground">{query.description}</span>}
        </div>
      </TableCell>
      <TableCell>
        <Badge variant="secondary" className="text-xs font-normal">
          {query.dataSourceName}
        </Badge>
      </TableCell>
      <TableCell className="text-xs text-muted-foreground">{query.isOwnedByCurrentUser ? 'You' : query.ownerName}</TableCell>
      <TableCell className="text-xs text-muted-foreground">
        {formatDistanceToNow(new Date(query.updatedAtUtc ?? query.createdAtUtc), { addSuffix: true })}
      </TableCell>
      <TableCell onClick={(e) => e.stopPropagation()}>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="size-6">
              <MoreHorizontal className="size-3.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem variant="destructive" onClick={onDelete}>
              <Trash2 className="size-3.5" />
              Delete
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </TableCell>
    </TableRow>
  )
}
