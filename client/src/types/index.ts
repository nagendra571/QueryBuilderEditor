// Mirrors QueryBuilder.Domain.Enums / QueryBuilder.Domain.Model / QueryBuilder.Application.Dtos.
// Kept in lockstep with the backend camelCase JSON contract (see Program.cs JSON options).

export type ColumnDataType = 'text' | 'number' | 'date' | 'dateTime' | 'boolean' | 'guid' | 'unknown'

export type SchemaObjectKind = 'table' | 'view'

export type JoinType = 'inner' | 'left' | 'right' | 'full'

export type AggregateFunction = 'none' | 'sum' | 'avg' | 'count' | 'countDistinct' | 'min' | 'max'

export type SortDirection = 'asc' | 'desc'

export type QueryAccessLevel = 'viewer' | 'editor' | 'owner'

export type DataSourceProvider = 'sqlServer' | 'postgreSql' | 'mySql' | 'sqlite'

export type CatalogScope = 'views' | 'tables' | 'tablesAndViews'

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

export interface UnavailableObject {
  schemaName: string
  name: string
  kind: SchemaObjectKind
  reason: 'nameClash'
}

export interface DataSourceCatalog {
  dataSourceId: string
  provider: DataSourceProvider
  schemas: SchemaMetadata[]
  /** Absent in responses cached before this field existed. */
  unavailableObjects?: UnavailableObject[]
}

export interface AdminUnavailableObject extends UnavailableObject {
  /** Already formatted for display (`public.orders`, or quoted when a dot is ambiguous). */
  clashesWith: string[]
}

export interface DataSourceDto {
  id: string
  name: string
  description?: string | null
  catalogScope: CatalogScope
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

export type ParameterType =
  | 'text'
  | 'number'
  | 'date'
  | 'dateTime'
  | 'dateRange'
  | 'dateTimeRange'
  | 'list'
  | 'queryList'
  | 'boolean'

export interface QueryParameter {
  name: string
  label: string
  dataType: ColumnDataType
  /** Absent on queries saved before parameter types existed; then derived from `dataType`. */
  type?: ParameterType | null
  /**
   * Single value, a JSON array string for several values, `start~end` for a range, or a relative
   * token such as `last_30_days` / `today` that the server resolves at run time.
   */
  defaultValue?: string | null
  isRequired: boolean
  options?: string[]
  optionsQueryId?: string | null
  optionsLabelColumn?: string | null
  optionsValueColumn?: string | null
  allowMultiple?: boolean
}

export interface ParameterOption {
  label: string
  value: string
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
  myAccessLevel: QueryAccessLevel
  isFavorite: boolean
  isDisabled: boolean
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
  myAccessLevel: QueryAccessLevel
  isFavorite: boolean
  isDisabled: boolean
  definition: QueryDefinition
  createdAtUtc: string
  updatedAtUtc?: string | null
}

export interface QueryShareDto {
  id: string
  sharedWithUserId: string
  sharedWithUserEmail: string
  accessLevel: QueryAccessLevel
  createdAtUtc: string
}

export interface ShareQueryRequest {
  sharedWithUserId: string
  sharedWithUserEmail: string
  accessLevel: QueryAccessLevel
}

export interface AppUserDto {
  id: string
  displayName: string
  email?: string | null
}

export interface AppUsersResult {
  available: boolean
  users: AppUserDto[]
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
  aggregate?: AggregateFunction | null
}

export interface QueryResultDto {
  columns: QueryResultColumnDto[]
  rows: Record<string, unknown>[]
  rowCount: number
  executionTimeMs: number
  /** True when the query had more rows than `rowLimit` and only the first `rowLimit` came back. */
  truncated: boolean
  rowLimit: number
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

export type AuditAction =
  | 'queryCreated'
  | 'queryUpdated'
  | 'queryDeleted'
  | 'queryRun'
  | 'queryExported'
  | 'queryShared'
  | 'queryUnshared'
  | 'queryDisabled'
  | 'queryEnabled'
  | 'dataSourceCatalogPolicyUpdated'
  | 'dataSourceDataScopeUpdated'
  | 'dataSourceRecordLimitUpdated'
  | 'visualizationCreated'
  | 'visualizationUpdated'
  | 'visualizationDeleted'

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

// ---- Admin ----

export interface AdminDataSourceSummaryDto {
  id: string
  name: string
  provider: DataSourceProvider
  isActive: boolean
}

export interface AdminDataSourceDetailDto {
  id: string
  name: string
  catalogScope: CatalogScope
  allowedObjects: string[]
  objects: SchemaObjectMetadata[]
  /** This data source's own record limit; null = falls back to `defaultMaxRecords`. */
  maxRecords: number | null
  /** The host app's package-wide default; null = no limit when the data source has none either. */
  defaultMaxRecords: number | null
  unavailable?: AdminUnavailableObject[]
}

export interface UpdateRecordLimitRequest {
  maxRecords: number | null
}

export interface UpdateCatalogPolicyRequest {
  catalogScope: CatalogScope
  allowedObjects: string[]
}

export type DataScopeState = 'undecided' | 'notScoped' | 'scoped'

export interface AdminDataScopeObjectDto {
  objectName: string
  kind: SchemaObjectKind
  columns: string[]
  state: DataScopeState
  /** Declared scope key → column name, only for `scoped`. */
  mappings: Record<string, string>
  /** Keys mapped in the database that the host no longer declares. */
  staleKeys: string[]
}

export interface AdminDataScopeDto {
  enabled: boolean
  declaredKeys: string[]
  objects: AdminDataScopeObjectDto[]
  unavailable?: AdminUnavailableObject[]
}

export interface DataScopeObjectRuleDto {
  objectName: string
  state: DataScopeState
  mappings?: Record<string, string> | null
}

export interface UpdateDataScopeRequest {
  objects: DataScopeObjectRuleDto[]
}

// ---- Visualizations ----

export type VisualizationType =
  | 'bar'
  | 'line'
  | 'pie'
  | 'counter'
  | 'area'
  | 'horizontalBar'
  | 'combo'
  | 'scatter'
  | 'table'
  | 'pivot'
  | 'funnel'
  | 'waterfall'
  | 'trend'

export type ValueFormatStyle = 'number' | 'compact' | 'currency' | 'percent'

export interface ValueFormat {
  style: ValueFormatStyle
  decimals?: number | null
  currency?: string | null
}

export interface TableOptions {
  columns: string[]
}

export interface PivotOptions {
  rows: string[]
  column?: string | null
  values: string[]
  totals: boolean
  shade: boolean
}

export type VisualizationSort = 'none' | 'asc' | 'desc'

export interface CounterOptions {
  value: string
  label?: string | null
  decimals: number
  target?: string | null
}

/** Columns are referenced by output name (a column's alias, or its name when it has none). */
export interface VisualizationOptions {
  version: number
  x?: string | null
  measures: string[]
  series?: string | null
  stacked: boolean
  sortX: VisualizationSort
  counter?: CounterOptions | null
  percent?: boolean
  lineMeasures?: string[]
  size?: string | null
  label?: string | null
  table?: TableOptions | null
  pivot?: PivotOptions | null
  format?: ValueFormat | null
}

export interface VisualizationDto {
  id: string
  savedQueryId: string
  name: string
  type: VisualizationType
  options: VisualizationOptions
  displayOrder: number
  createdBy: string
  createdAtUtc: string
  updatedBy?: string | null
  updatedAtUtc?: string | null
}

export interface SaveVisualizationRequest {
  name: string
  type: VisualizationType
  options: VisualizationOptions
}

export interface RenderVisualizationRequest {
  parameterValues: Record<string, string | null>
}

/** Only the columns the chart references — never the query's full output. */
export interface VisualizationDataDto {
  columns: QueryResultColumnDto[]
  rows: Record<string, unknown>[]
  rowCount: number
  executionTimeMs: number
  rowLimit: number
  truncated?: boolean
}

export type DashboardTileKind = 'chart' | 'text' | 'section'

export interface DashboardSummaryDto {
  id: string
  name: string
  description?: string | null
  ownerId: string
  ownerName: string
  isOwner: boolean
  accessLevel: QueryAccessLevel
  tileCount: number
  createdAtUtc: string
  updatedAtUtc?: string | null
}

export interface DashboardTileChartDto {
  savedQueryId: string
  savedQueryName: string
  visualizationId: string
  visualizationName: string
  type: VisualizationType
}

/** A restricted tile carries only id, kind, order, width, height and restricted = true. */
export interface DashboardTileDto {
  id: string
  kind: DashboardTileKind
  order: number
  /** Grid units in a 12-column grid: column and row of the top-left corner. */
  x: number
  y: number
  width: number
  height: number
  autoHeight: boolean
  restricted: boolean
  missing: boolean
  title?: string | null
  text?: string | null
  chart?: DashboardTileChartDto | null
  /** Absent for restricted, removed and text tiles, and for charts that use every query default. */
  parameterMappings?: Record<string, ParameterMappingDto> | null
}

/** A parameter shared by several tiles. Same shape rules as a query parameter, plus the saved default. */
export interface DashboardParameterDto {
  name: string
  title?: string | null
  type: ParameterType
  options?: string[] | null
  optionsQueryId?: string | null
  optionsLabelColumn?: string | null
  optionsValueColumn?: string | null
  allowMultiple: boolean
  value?: string | null
}

export type ParameterMappingSource = 'default' | 'dashboard' | 'tile' | 'fixed'

/** Where one query parameter of a chart tile gets its value. `default` is never stored. */
export interface ParameterMappingDto {
  source: ParameterMappingSource
  dashboardParameter?: string | null
  value?: string | null
  title?: string | null
}

export interface DashboardDetailDto {
  id: string
  name: string
  description?: string | null
  ownerId: string
  ownerName: string
  isOwner: boolean
  accessLevel: QueryAccessLevel
  version: number
  tiles: DashboardTileDto[]
  parameters?: DashboardParameterDto[] | null
}

export interface SaveDashboardRequest {
  name: string
  description?: string | null
  version?: number | null
}

export interface SaveDashboardTileRequest {
  id?: string | null
  kind: DashboardTileKind
  order: number
  x: number
  y: number
  width: number
  height: number
  autoHeight: boolean
  title?: string | null
  text?: string | null
  visualizationId?: string | null
  /** Omitted = keep what is stored; `{}` = clear. */
  parameterMappings?: Record<string, ParameterMappingDto> | null
}

export interface SaveDashboardTilesRequest {
  version: number
  tiles: SaveDashboardTileRequest[]
  /** Omitted = keep the stored list; `[]` = clear. */
  parameters?: DashboardParameterDto[]
}

export interface ShareDashboardRequest {
  sharedWithUserId: string
  sharedWithUserEmail: string
  accessLevel: QueryAccessLevel
  alsoShareQueryIds?: string[]
}

export interface InaccessibleQueryDto {
  id: string
  name: string
  canShare: boolean
}

export interface DashboardSharePreviewDto {
  inaccessibleQueries: InaccessibleQueryDto[]
}

export interface PickableVisualizationDto {
  visualizationId: string
  visualizationName: string
  type: VisualizationType
  savedQueryId: string
  savedQueryName: string
}
