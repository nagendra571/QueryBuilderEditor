import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { dashboardParameterTitle, toQueryParameter } from '@/features/dashboards/dashboard-params'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import { TYPE_LABELS } from '@/features/parameters/param-values'
import type { DashboardParameterDto } from '@/types'

/** Edit mode: rename a dashboard parameter, set the value viewers start from, or remove it. */
export function DashboardParameterDialog({
  parameter,
  usedBy,
  onOpenChange,
  onSave,
  onRemove,
}: {
  parameter: DashboardParameterDto | null
  /** How many tiles are mapped to it. */
  usedBy: number
  onOpenChange: (open: boolean) => void
  onSave: (name: string, title: string, value: string | null) => void
  onRemove: (name: string) => void
}) {
  return (
    <Dialog open={parameter !== null} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {parameter && (
          <Form
            key={parameter.name}
            parameter={parameter}
            usedBy={usedBy}
            onCancel={() => onOpenChange(false)}
            onSave={onSave}
            onRemove={onRemove}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function Form({
  parameter,
  usedBy,
  onCancel,
  onSave,
  onRemove,
}: {
  parameter: DashboardParameterDto
  usedBy: number
  onCancel: () => void
  onSave: (name: string, title: string, value: string | null) => void
  onRemove: (name: string) => void
}) {
  const [title, setTitle] = useState(dashboardParameterTitle(parameter))
  const [value, setValue] = useState<string | null>(parameter.value ?? null)
  const asQuery = toQueryParameter(parameter)

  return (
    <>
      <DialogHeader>
        <DialogTitle>Dashboard parameter</DialogTitle>
        <DialogDescription>
          {TYPE_LABELS[parameter.type]} · used by {usedBy} {usedBy === 1 ? 'chart' : 'charts'}. Viewers start from the value below.
        </DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="dash-param-title">Title</Label>
          <Input id="dash-param-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="dash-param-value">Starting value</Label>
          <ParameterControl
            parameter={asQuery}
            id="dash-param-value"
            value={value}
            onChange={setValue}
            optionsSource={parameter.type === 'queryList' ? { kind: 'draft', parameter: asQuery } : null}
          />
        </div>
      </div>
      <DialogFooter className="sm:justify-between">
        <Button variant="ghost" className="text-destructive" onClick={() => onRemove(parameter.name)}>
          Remove parameter
        </Button>
        <div className="flex gap-2">
          <Button variant="outline" onClick={onCancel}>
            Cancel
          </Button>
          <Button disabled={title.trim() === ''} onClick={() => onSave(parameter.name, title.trim(), value)}>
            Done
          </Button>
        </div>
      </DialogFooter>
    </>
  )
}
