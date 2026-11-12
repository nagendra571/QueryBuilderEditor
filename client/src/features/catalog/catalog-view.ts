import type { DataSourceCatalog, SchemaObjectMetadata, UnavailableObject } from '../../types/index.ts'

export interface CatalogViewSection {
  name: string
  objects: SchemaObjectMetadata[]
  unavailable: UnavailableObject[]
}

export function buildCatalogView(catalog: DataSourceCatalog, term: string): CatalogViewSection[] {
  const sections = new Map<string, CatalogViewSection>()
  const section = (name: string) => {
    let s = sections.get(name)
    if (!s) sections.set(name, (s = { name, objects: [], unavailable: [] }))
    return s
  }

  for (const schema of catalog.schemas) {
    const s = section(schema.name)
    s.objects = term
      ? schema.objects.filter(
          (o) => o.name.toLowerCase().includes(term) || (o.columns ?? []).some((c) => c.name.toLowerCase().includes(term)),
        )
      : schema.objects
  }
  for (const u of catalog.unavailableObjects ?? []) {
    if (term && !u.name.toLowerCase().includes(term)) continue
    section(u.schemaName).unavailable.push(u)
  }

  return Array.from(sections.values())
    .filter((s) => !term || s.objects.length > 0 || s.unavailable.length > 0)
    .sort((a, b) => a.name.localeCompare(b.name))
}

export function countLabel(catalog: DataSourceCatalog): string {
  const usable = catalog.schemas.reduce((n, s) => n + s.objects.length, 0)
  const unavailable = catalog.unavailableObjects?.length ?? 0
  const base = `${usable} ${usable === 1 ? 'object' : 'objects'}`
  return unavailable > 0 ? `${base} · ${unavailable} unavailable` : base
}
