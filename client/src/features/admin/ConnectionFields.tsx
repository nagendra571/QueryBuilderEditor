import { AlertTriangle, CheckCircle2, XCircle } from 'lucide-react'
import { useId } from 'react'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  INSECURE_PASSWORD_WARNING,
  shouldWarnInsecure,
  SSL_OPTIONS,
  type ConnectionForm,
  type FormField,
} from '@/features/admin/connection-form'
import type { ConnectionSslMode, ConnectionTestResult } from '@/types'

export function ConnectionFields({
  form,
  errors,
  onChange,
  passwordPlaceholder,
  passwordHint,
}: {
  form: ConnectionForm
  /** Shown only for fields the admin has already tried to submit, so an empty form isn't a wall of red. */
  errors: Partial<Record<FormField, string>>
  onChange: (next: ConnectionForm) => void
  passwordPlaceholder?: string
  passwordHint?: string
}) {
  const id = useId()
  const set = <K extends keyof ConnectionForm>(key: K, value: ConnectionForm[K]) => onChange({ ...form, [key]: value })

  function field(key: FormField, label: string, input: React.ReactNode) {
    const error = errors[key]
    return (
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={`${id}-${key}`}>{label}</Label>
        {input}
        {error && (
          <p id={`${id}-${key}-error`} className="text-xs text-destructive">
            {error}
          </p>
        )}
      </div>
    )
  }

  const common = (key: FormField) => ({
    id: `${id}-${key}`,
    'aria-invalid': !!errors[key],
    'aria-describedby': errors[key] ? `${id}-${key}-error` : undefined,
  })

  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-[1fr_7rem] gap-3">
        {field(
          'host',
          'Host',
          <Input {...common('host')} value={form.host} onChange={(e) => set('host', e.target.value)} placeholder="db.example.com" autoComplete="off" spellCheck={false} />,
        )}
        {field(
          'port',
          'Port',
          <Input {...common('port')} value={form.port} onChange={(e) => set('port', e.target.value)} inputMode="numeric" autoComplete="off" />,
        )}
      </div>
      {field(
        'database',
        'Database',
        <Input {...common('database')} value={form.database} onChange={(e) => set('database', e.target.value)} autoComplete="off" spellCheck={false} />,
      )}
      {field(
        'username',
        'Username',
        <Input {...common('username')} value={form.username} onChange={(e) => set('username', e.target.value)} autoComplete="off" spellCheck={false} />,
      )}
      {field(
        'password',
        'Password',
        <>
          <Input
            {...common('password')}
            type="password"
            value={form.password}
            onChange={(e) => set('password', e.target.value)}
            autoComplete="new-password"
            placeholder={passwordPlaceholder}
          />
          {passwordHint && <p className="text-xs text-muted-foreground">{passwordHint}</p>}
          {shouldWarnInsecure(typeof window === 'undefined' ? undefined : window.isSecureContext) && (
            <p role="alert" className="flex items-start gap-1.5 text-xs text-warning-foreground">
              <AlertTriangle className="mt-0.5 size-3.5 shrink-0 text-warning" aria-hidden="true" />
              <span>{INSECURE_PASSWORD_WARNING}</span>
            </p>
          )}
        </>,
      )}
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={`${id}-ssl`}>Encryption</Label>
        <Select
          value={form.sslMode}
          // Radix Select can report '' while its items aren't mounted; that is never a real choice.
          onValueChange={(v) => {
            if (v !== '') set('sslMode', v as ConnectionSslMode)
          }}
        >
          <SelectTrigger id={`${id}-ssl`} className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SSL_OPTIONS.map((o) => (
              <SelectItem key={o.value} value={o.value}>
                {o.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  )
}

/** Result in words with an icon, so the outcome never depends on colour alone. */
export function TestResultLine({ result }: { result: ConnectionTestResult }) {
  const Icon = result.ok ? CheckCircle2 : XCircle
  return (
    <p role="status" className={`flex items-start gap-1.5 text-sm ${result.ok ? 'text-success' : 'text-destructive'}`}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span>
        <span className="font-medium">{result.ok ? 'Connection works.' : "Connection didn't work."}</span>{' '}
        {result.ok ? '' : result.message}
      </span>
    </p>
  )
}
