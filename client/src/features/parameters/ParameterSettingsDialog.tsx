import { useQuery } from '@tanstack/react-query'
import { AlertCircle, Loader2 } from 'lucide-react'
import { useId, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import { effectiveType, parameterTitle, TYPE_LABELS } from '@/features/parameters/param-values'
import { outputColumns } from '@/features/visualizations/chart-data'
import { useParameterOptions } from '@/hooks/useParameterOptions'
import { useSavedQueries } from '@/hooks/useSavedQueries'
import { savedQueriesApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { ParameterType, QueryParameter } from '@/types'

const NONE = '__none__'
const MAX_OPTIONS = 500

export function ParameterSettingsDialog({
  parameter,
  allowedTypes,
  canAllowMultiple,
  currentQueryId,
  onSave,
  onClose,
}: {
  /** Null keeps the dialog closed. */
  parameter: QueryParameter | null
  /** Types this parameter's filter (column + condition) can be asked as. */
  allowedTypes: ParameterType[]
  /** True when every filter bound to the parameter is "is any of" / "is none of". */
  canAllowMultiple: boolean
  currentQueryId: string | null
  onSave: (parameter: QueryParameter) => void
  onClose: () => void
}) {
  return (
    <Dialog open={!!parameter} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        {parameter && (
          <SettingsForm
            key={parameter.name}
            parameter={parameter}
            allowedTypes={allowedTypes}
            canAllowMultiple={canAllowMultiple}
            currentQueryId={currentQueryId}
            onSave={onSave}
            onClose={onClose}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function SettingsForm({
  parameter,
  allowedTypes,
  canAllowMultiple,
  currentQueryId,
  onSave,
  onClose,
}: {
  parameter: QueryParameter
  allowedTypes: ParameterType[]
  canAllowMultiple: boolean
  currentQueryId: string | null
  onSave: (parameter: QueryParameter) => void
  onClose: () => void
}) {
  const baseId = useId()
  const [draft, setDraft] = useState<QueryParameter>(() => ({ ...parameter, type: effectiveType(parameter) }))
  const [optionsText, setOptionsText] = useState((parameter.options ?? []).join('\n'))

  const type = effectiveType(draft)
  const types = allowedTypes.includes(type) ? allowedTypes : [type, ...allowedTypes]
  const patch = (changes: Partial<QueryParameter>) => setDraft((d) => ({ ...d, ...changes }))

  const options = parseOptions(optionsText)
  const sources = useSavedQueries()
  const sourceChoices = (sources.data ?? []).filter((q) => q.id !== currentQueryId && !q.isDisabled)
  const sourceDefinition = useQuery({
    queryKey: ['saved-query', draft.optionsQueryId],
    queryFn: () => savedQueriesApi.getById(draft.optionsQueryId!),
    enabled: type === 'queryList' && !!draft.optionsQueryId,
  })
  const sourceColumns = outputColumns(sourceDefinition.data?.definition.columns ?? [])
  const preview = useParameterOptions({ kind: 'draft', parameter: draft }, type === 'queryList' && !!draft.optionsQueryId)

  const invalid =
    type === 'list' && options.length === 0
      ? 'Add at least one option.'
      : type === 'list' && options.length > MAX_OPTIONS
        ? `At most ${MAX_OPTIONS} options are allowed.`
        : type === 'queryList' && !draft.optionsQueryId
          ? 'Choose the saved query that supplies the choices.'
          : null

  function changeType(next: ParameterType) {
    patch({
      type: next,
      defaultValue: null,
      allowMultiple: next === 'list' || next === 'queryList' ? draft.allowMultiple && canAllowMultiple : false,
    })
  }

  function save() {
    onSave({
      ...draft,
      label: draft.label.trim() || draft.name,
      options: type === 'list' ? options : [],
      optionsQueryId: type === 'queryList' ? draft.optionsQueryId : null,
      optionsLabelColumn: type === 'queryList' ? draft.optionsLabelColumn || null : null,
      optionsValueColumn: type === 'queryList' ? draft.optionsValueColumn || null : null,
      allowMultiple: (type === 'list' || type === 'queryList') && canAllowMultiple && !!draft.allowMultiple,
    })
  }

  const defaultParameter: QueryParameter = { ...draft, options }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Parameter settings</DialogTitle>
        <DialogDescription>
          How “{parameterTitle(parameter)}” is asked for when the query runs. Values are always checked on the server.
        </DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-4">
        <Field id={`${baseId}-title`} label="Title">
          <Input id={`${baseId}-title`} value={draft.label} onChange={(e) => patch({ label: e.target.value })} className="h-8 text-sm" maxLength={200} />
        </Field>

        <Field id={`${baseId}-type`} label="Type">
          <Select value={type} onValueChange={(v) => changeType(v as ParameterType)}>
            <SelectTrigger id={`${baseId}-type`} size="sm" className="h-8 w-64 text-sm">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {types.map((t) => (
                <SelectItem key={t} value={t} className="text-sm">
                  {TYPE_LABELS[t]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {types.length === 1 && <p className="text-xs text-muted-foreground">This filter can only be asked as {TYPE_LABELS[type].toLowerCase()}.</p>}
        </Field>

        {type === 'list' && (
          <Field id={`${baseId}-options`} label="Options (one per line)">
            <Textarea
              id={`${baseId}-options`}
              value={optionsText}
              onChange={(e) => setOptionsText(e.target.value)}
              rows={5}
              className="text-sm"
              placeholder={'North\nSouth\nEast'}
            />
            <p className="text-xs text-muted-foreground">{options.length} option{options.length === 1 ? '' : 's'}</p>
          </Field>
        )}

        {type === 'queryList' && (
          <div className="flex flex-col gap-3 rounded-md border border-border bg-muted/30 p-3">
            <Field id={`${baseId}-source`} label="Take the choices from this saved query">
              <Select value={draft.optionsQueryId ?? NONE} onValueChange={(v) => patch({ optionsQueryId: v === NONE ? null : v, optionsLabelColumn: null, optionsValueColumn: null, defaultValue: null })}>
                <SelectTrigger id={`${baseId}-source`} size="sm" className="h-8 w-full text-sm">
                  <SelectValue placeholder={sources.isLoading ? 'Loading queries…' : 'Choose a query'} />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE} className="text-sm text-muted-foreground">
                    None
                  </SelectItem>
                  {sourceChoices.map((q) => (
                    <SelectItem key={q.id} value={q.id} className="text-sm">
                      {q.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>

            {draft.optionsQueryId && (
              <div className="grid grid-cols-2 gap-3">
                <ColumnField id={`${baseId}-label`} label="Label column" value={draft.optionsLabelColumn} columns={sourceColumns.map((c) => c.name)} onChange={(v) => patch({ optionsLabelColumn: v })} />
                <ColumnField id={`${baseId}-value`} label="Value column" value={draft.optionsValueColumn} columns={sourceColumns.map((c) => c.name)} onChange={(v) => patch({ optionsValueColumn: v })} />
                <p className="col-span-2 text-xs text-muted-foreground">
                  Automatic uses columns named “name” and “value”, otherwise the first column. The query runs as whoever is asking, so
                  each person only sees choices inside their own data scope.
                </p>
              </div>
            )}

            {draft.optionsQueryId && (
              <div className="text-xs" role="status">
                {preview.isLoading && (
                  <span className="inline-flex items-center gap-1.5 text-muted-foreground">
                    <Loader2 className="size-3.5 animate-spin" /> Loading a preview…
                  </span>
                )}
                {preview.error && (
                  <span className="inline-flex items-center gap-1.5 text-destructive">
                    <AlertCircle className="size-3.5 shrink-0" />
                    {preview.error instanceof ApiError && preview.error.status === 403
                      ? 'You do not have access to that query.'
                      : preview.error instanceof ApiError
                        ? preview.error.message
                        : 'Could not load a preview.'}
                  </span>
                )}
                {preview.data && (
                  <span className="text-muted-foreground">
                    <span className="font-numeric font-medium text-foreground">{preview.data.length}</span> choice{preview.data.length === 1 ? '' : 's'}
                    {preview.data.length > 0 && ': ' + preview.data.slice(0, 6).map((o) => o.label).join(', ') + (preview.data.length > 6 ? '…' : '')}
                  </span>
                )}
              </div>
            )}
          </div>
        )}

        <div className="flex flex-col gap-2">
          {(type === 'list' || type === 'queryList') && (
            <label className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={!!draft.allowMultiple && canAllowMultiple}
                disabled={!canAllowMultiple}
                onCheckedChange={(c) => patch({ allowMultiple: c === true, defaultValue: null })}
              />
              Allow picking several values
              {!canAllowMultiple && <span className="text-xs text-muted-foreground">(needs an “is any of” / “is none of” filter)</span>}
            </label>
          )}
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={draft.isRequired} onCheckedChange={(c) => patch({ isRequired: c === true })} />
            Required — a value must be chosen before the query can run
          </label>
        </div>

        <Field id={`${baseId}-default`} label="Default value">
          <ParameterControl
            // Remount when the shape of the control changes so stale local state never leaks across types.
            key={`${type}-${draft.allowMultiple}-${draft.optionsQueryId}`}
            parameter={defaultParameter}
            id={`${baseId}-default`}
            value={draft.defaultValue ?? null}
            onChange={(v) => patch({ defaultValue: v })}
            optionsSource={type === 'queryList' && draft.optionsQueryId ? { kind: 'draft', parameter: defaultParameter } : null}
          />
          <p className="text-xs text-muted-foreground">Used when nobody picks a value. Relative dates (Today, Last 30 days…) are worked out when the query runs.</p>
        </Field>
      </div>

      {invalid && (
        <p className="text-xs text-destructive" role="alert">
          {invalid}
        </p>
      )}

      <DialogFooter>
        <Button variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button disabled={!!invalid} onClick={save}>
          Done
        </Button>
      </DialogFooter>
    </>
  )
}

function Field({ id, label, children }: { id: string; label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
    </div>
  )
}

function ColumnField({
  id,
  label,
  value,
  columns,
  onChange,
}: {
  id: string
  label: string
  value: string | null | undefined
  columns: string[]
  onChange: (value: string | null) => void
}) {
  return (
    <Field id={id} label={label}>
      <Select value={value ?? NONE} onValueChange={(v) => onChange(v === NONE ? null : v)}>
        <SelectTrigger id={id} size="sm" className="h-8 w-full text-sm">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={NONE} className="text-sm">
            Automatic
          </SelectItem>
          {columns.map((c) => (
            <SelectItem key={c} value={c} className="text-sm">
              {c}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </Field>
  )
}

function parseOptions(text: string): string[] {
  const seen = new Set<string>()
  const options: string[] = []
  for (const line of text.split('\n')) {
    const value = line.trim()
    if (value && !seen.has(value)) {
      seen.add(value)
      options.push(value)
    }
  }
  return options
}
