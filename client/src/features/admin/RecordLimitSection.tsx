import { Loader2, Save } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useUpdateRecordLimit } from '@/hooks/useAdmin'
import type { AdminDataSourceDetailDto } from '@/types'

const MIN = 1
const MAX = 100_000

export function RecordLimitSection({ dataSource }: { dataSource: AdminDataSourceDetailDto }) {
  const updateLimit = useUpdateRecordLimit(dataSource.id)
  const [draft, setDraft] = useState<string | null>(null)

  const saved = dataSource.maxRecords
  const value = draft ?? (saved?.toString() ?? '')
  const trimmed = value.trim()
  const parsed = trimmed === '' ? null : Number(trimmed)
  const invalid = parsed !== null && (!Number.isInteger(parsed) || parsed < MIN || parsed > MAX)
  const isDirty = parsed !== saved

  const fallback = dataSource.defaultMaxRecords
  const fallbackText =
    fallback !== null
      ? `the app default of ${fallback.toLocaleString()} records`
      : 'no limit (the grid still shows at most 1,000 rows, and exports stop at 100,000 rows without an error)'

  function handleSave() {
    if (invalid) return
    updateLimit.mutate({ maxRecords: parsed }, { onSuccess: () => setDraft(null) })
  }

  return (
    <div className="flex flex-col gap-2">
      <div>
        <h2 className="text-sm font-semibold">Maximum records per query</h2>
        <p className="text-xs text-muted-foreground">
          When a query returns more than this, the results show only the first{' '}
          {parsed !== null && !invalid ? parsed.toLocaleString() : 'N'} rows with a message asking the user to add filters,
          and exports are refused. Leave empty to use {fallbackText}.
        </p>
      </div>
      <div className="flex items-center gap-3">
        <Input
          type="number"
          inputMode="numeric"
          min={MIN}
          max={MAX}
          value={value}
          onChange={(e) => setDraft(e.target.value)}
          placeholder={fallback !== null ? `Default: ${fallback.toLocaleString()}` : 'No limit'}
          className="h-8 w-44 text-sm"
          aria-label="Maximum records per query"
          aria-invalid={invalid}
        />
        <Button onClick={handleSave} disabled={updateLimit.isPending || invalid || !isDirty}>
          {updateLimit.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save limit
        </Button>
        {invalid && (
          <p className="text-xs text-destructive">
            Enter a whole number from {MIN.toLocaleString()} to {MAX.toLocaleString()}, or leave empty.
          </p>
        )}
      </div>
    </div>
  )
}
