// Mirrors QueryBuilder.Domain.Enums / QueryBuilder.Domain.Model / QueryBuilder.Application.Dtos.
// Kept in lockstep with the backend camelCase JSON contract (see Program.cs JSON options).

export type ColumnDataType = 'text' | 'number' | 'date' | 'dateTime' | 'boolean' | 'guid' | 'unknown'

export type SchemaObjectKind = 'table' | 'view'

export type JoinType = 'inner' | 'left' | 'right' | 'full'

export type AggregateFunction = 'none' | 'sum' | 'avg' | 'count' | 'countDistinct' | 'min' | 'max'

export type SortDirection = 'asc' | 'desc'

export type FilterLogicalOperator = 'and' | 'or'

export type FilterOperator =
  | 'equals'
  | 'notEquals'
  | 'greaterThan'
  | 'greaterOrEqual'
  | 'lessThan'
  | 'lessOrEqual'
  | 'contains'
  | 'notContains'
  | 'startsWith'
  | 'endsWith'
  | 'in'
  | 'notIn'
  | 'between'
  | 'isNull'
  | 'isNotNull'
  | 'isTrue'
  | 'isFalse'

// ---- Catalog (live-introspected, read-only) ----

export interface ColumnMetadata {
  name: string
  sqlType: string
  dataType: ColumnDataType
  isNullable: boolean
  isPrimaryKey: boolean
  isForeignKey: boolean
  foreignKeyTable?: string | null
  foreignKeyColumn?: string | null
  ordinalPosition: number
}

export interface SchemaObjectMetadata {
  schemaName: string
  name: string
  kind: SchemaObjectKind
  columns: ColumnMetadata[]
}

export interface SchemaMetadata {
  name: string
  objects: SchemaObjectMetadata[]
}

export interface DataSourceCatalog {
  dataSourceId: string
  schemas: SchemaMetadata[]
}

export interface DataSourceDto {
  id: string
  name: string
  description?: string | null
  viewsOnly: boolean
}

// ---- Query definition (the builder's persisted state) ----

export interface QuerySource {
  schemaName: string
  objectName: string
  kind: SchemaObjectKind
  alias: string
}

export interface JoinCondition {
  leftAlias: string
  leftColumn: string
  rightAlias: string
  rightColumn: string
}

export interface QueryJoin {
  schemaName: string
  objectName: string
  kind: SchemaObjectKind
  alias: string
  joinType: JoinType
  conditions: JoinCondition[]
}

export interface QueryColumnRef {
  tableAlias: string
  columnName: string
}

export interface QueryColumn {
  tableAlias: string
  columnName: string
  alias?: string | null
  dataType: ColumnDataType
  aggregate: AggregateFunction
  orderIndex: number
  isVisible: boolean
}

export interface QuerySort {
  tableAlias: string
  columnName: string
  direction: SortDirection
  orderIndex: number
}

export interface FilterCondition {
  tableAlias: string
  columnName: string
  dataType: ColumnDataType
  aggregate: AggregateFunction
  operator: FilterOperator
  value?: string | null
  isParameterized: boolean
  parameterName?: string | null
}

export interface FilterGroup {
  operator: FilterLogicalOperator
  conditions: FilterCondition[]
  groups: FilterGroup[]
}

export interface QueryParameter {
  name: string
  label: string
  dataType: ColumnDataType
  defaultValue?: string | null
  isRequired: boolean
}

export interface QueryDefinition {
  source: QuerySource
  joins: QueryJoin[]
  columns: QueryColumn[]
  filters: FilterGroup
  groupBy: QueryColumnRef[]
  having?: FilterGroup | null
  sorts: QuerySort[]
  parameters: QueryParameter[]
  rowLimit?: number | null
}

// ---- Saved queries ----

export interface SavedQuerySummaryDto {
  id: string
  name: string
  description?: string | null
  dataSourceId: string
  dataSourceName: string
  ownerId: string
  ownerName: string
  isOwnedByCurrentUser: boolean
  isFavorite: boolean
  createdAtUtc: string
  updatedAtUtc?: string | null
}

export interface SavedQueryDetailDto {
  id: string
  name: string
  description?: string | null
  dataSourceId: string
  dataSourceName: string
  ownerId: string
  ownerName: string
  isOwnedByCurrentUser: boolean
  isFavorite: boolean
  definition: QueryDefinition
  createdAtUtc: string
  updatedAtUtc?: string | null
}

export interface SaveQueryRequest {
  id?: string | null
  name: string
  description?: string | null
  dataSourceId: string
  definition: QueryDefinition
}

export interface RunQueryRequest {
  dataSourceId: string
  definition: QueryDefinition
  parameterValues: Record<string, string | null>
  maxRows?: number | null
  savedQueryId?: string | null
}

export interface QuerySqlPreviewParameterDto {
  name: string
  dataType: ColumnDataType
  defaultValue?: string | null
  isRuntimeParameter: boolean
  runtimeParameterName?: string | null
}

export interface QuerySqlPreviewDto {
  sql: string
  parameters: QuerySqlPreviewParameterDto[]
}

export interface QueryResultColumnDto {
  name: string
  dataType: ColumnDataType
}

export interface QueryResultDto {
  columns: QueryResultColumnDto[]
  rows: Record<string, unknown>[]
  rowCount: number
  executionTimeMs: number
  truncated: boolean
}

export interface ExportQueryRequest {
  dataSourceId: string
  definition: QueryDefinition
  parameterValues: Record<string, string | null>
  format: 'csv' | 'xlsx'
  fileName: string
  savedQueryId?: string | null
}

export interface ApiProblemDetails {
  status: number
  title: string
  detail?: string
  errors?: Record<string, string[]>
}

export type AuditAction = 'queryCreated' | 'queryUpdated' | 'queryDeleted' | 'queryRun' | 'queryExported'

export interface AuditLogEntryDto {
  id: string
  timestampUtc: string
  actor: string
  action: AuditAction
  entityType: string
  entityId?: string | null
  entityName?: string | null
  dataSourceId?: string | null
  summary: string
  detailsJson?: string | null
  ipAddress?: string | null
}
