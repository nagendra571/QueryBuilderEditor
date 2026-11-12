import { dropOrphanParameters } from '@/features/parameters/param-values'
import { apiClient } from '@/lib/api-client'
import type {
  AdminDataScopeDto,
  ConnectionTestResult,
  CreateDataSourceRequest,
  TestConnectionRequest,
  UpdateDataSourceRequest,
  AdminDataSourceDetailDto,
  EncryptionKeyStatusDto,
  ReencryptResultDto,
  AdminDataSourceSummaryDto,
  AppUserDto,
  AppUsersResult,
  AuditLogEntryDto,
  DataSourceCatalog,
  DashboardDetailDto,
  DashboardSharePreviewDto,
  DashboardSummaryDto,
  DataSourceDto,
  ExportQueryRequest,
  ParameterOption,
  PickableVisualizationDto,
  QueryParameter,
  QueryShareDto,
  RenderVisualizationRequest,
  DashboardContext,
  TileChartDto,
  SaveDashboardRequest,
  SaveDashboardTilesRequest,
  SaveVisualizationRequest,
  ShareDashboardRequest,
  VisualizationDataDto,
  VisualizationDto,
  RunQueryRequest,
  SaveQueryRequest,
  SavedQueryDetailDto,
  SavedQuerySummaryDto,
  ShareQueryRequest,
  QueryResultDto,
  QuerySqlPreviewDto,
  UpdateCatalogPolicyRequest,
  UpdateDataScopeRequest,
  UpdateScopeKeysRequest,
  UpdateRecordLimitRequest,
} from '@/types'

export const dataSourcesApi = {
  list: async (): Promise<DataSourceDto[]> => (await apiClient.get('/data-sources')).data,
  catalog: async (dataSourceId: string): Promise<DataSourceCatalog> =>
    (await apiClient.get(`/data-sources/${dataSourceId}/catalog`)).data,
  appUsers: async (dataSourceId: string): Promise<AppUsersResult> =>
    (await apiClient.get(`/data-sources/${dataSourceId}/app-users`)).data,
  /** Hub mode only: the people launched into the caller's app. */
  people: async (): Promise<AppUserDto[]> => (await apiClient.get('/people')).data,
}

export const savedQueriesApi = {
  list: async (): Promise<SavedQuerySummaryDto[]> => (await apiClient.get('/queries')).data,
  // Queries saved before removing a filter also removed its parameter can still hold orphans. The server
  // ignores them, but every screen reading this would ask for a value that nothing uses.
  getById: async (id: string): Promise<SavedQueryDetailDto> => {
    const query: SavedQueryDetailDto = (await apiClient.get(`/queries/${id}`)).data
    return { ...query, definition: dropOrphanParameters(query.definition) }
  },
  save: async (request: SaveQueryRequest): Promise<{ id: string }> =>
    (await apiClient.post('/queries', request)).data,
  remove: async (id: string): Promise<void> => {
    await apiClient.delete(`/queries/${id}`)
  },
  toggleFavorite: async (id: string): Promise<{ isFavorite: boolean }> =>
    (await apiClient.post(`/queries/${id}/favorite`)).data,
  toggleDisabled: async (id: string): Promise<{ isDisabled: boolean }> =>
    (await apiClient.post(`/queries/${id}/disable`)).data,
  previewSql: async (request: RunQueryRequest): Promise<QuerySqlPreviewDto> =>
    (await apiClient.post('/queries/preview-sql', request)).data,
  run: async (request: RunQueryRequest): Promise<QueryResultDto> =>
    (await apiClient.post('/queries/run', request)).data,
  export: async (request: ExportQueryRequest): Promise<Blob> =>
    (await apiClient.post('/queries/export', request, { responseType: 'blob' })).data,
  parameterOptions: async (id: string, name: string, context?: DashboardContext): Promise<ParameterOption[]> =>
    (
      await apiClient.get(`/queries/${id}/parameters/${encodeURIComponent(name)}/options`, {
        params: context ? { dashboardId: context.dashboardId, tileId: context.tileId } : undefined,
      })
    ).data,
  getShares: async (id: string): Promise<QueryShareDto[]> => (await apiClient.get(`/queries/${id}/shares`)).data,
  share: async (id: string, request: ShareQueryRequest): Promise<{ id: string }> =>
    (await apiClient.post(`/queries/${id}/shares`, request)).data,
  removeShare: async (id: string, shareId: string): Promise<void> => {
    await apiClient.delete(`/queries/${id}/shares/${shareId}`)
  },
}

export const parametersApi = {
  /** Choices for a query-list parameter that is still being edited (not saved yet). */
  previewOptions: async (parameter: QueryParameter, dataSourceId?: string | null): Promise<ParameterOption[]> =>
    (await apiClient.post('/parameters/options', { dataSourceId: dataSourceId ?? null, parameter })).data,
}

export const auditApi = {
  forQuery: async (queryId: string): Promise<AuditLogEntryDto[]> =>
    (await apiClient.get('/audit', { params: { entityType: 'SavedQuery', entityId: queryId } })).data,
}

export const adminApi = {
  access: async (): Promise<void> => {
    await apiClient.get('/admin/access')
  },
  listDataSources: async (): Promise<AdminDataSourceSummaryDto[]> => (await apiClient.get('/admin/data-sources')).data,
  getDataSource: async (id: string): Promise<AdminDataSourceDetailDto> => (await apiClient.get(`/admin/data-sources/${id}`)).data,
  updateCatalogPolicy: async (id: string, request: UpdateCatalogPolicyRequest): Promise<void> => {
    await apiClient.put(`/admin/data-sources/${id}/catalog-policy`, request)
  },
  updateRecordLimit: async (id: string, request: UpdateRecordLimitRequest): Promise<void> => {
    await apiClient.put(`/admin/data-sources/${id}/record-limit`, request)
  },
  encryptionKeyStatus: async (): Promise<EncryptionKeyStatusDto> => (await apiClient.get('/admin/encryption-key')).data,
  reencrypt: async (): Promise<ReencryptResultDto> => (await apiClient.post('/admin/encryption-key/reencrypt')).data,
  dataSourceManagement: async (): Promise<{ enabled: boolean; canManageConnections?: boolean }> => (await apiClient.get('/admin/data-source-management')).data,
  createDataSource: async (request: CreateDataSourceRequest): Promise<AdminDataSourceSummaryDto> =>
    (await apiClient.post('/admin/data-sources', request)).data,
  updateDataSource: async (id: string, request: UpdateDataSourceRequest): Promise<void> => {
    await apiClient.put(`/admin/data-sources/${id}`, request)
  },
  setDataSourceActive: async (id: string, isActive: boolean): Promise<void> => {
    await apiClient.put(`/admin/data-sources/${id}/active`, { isActive })
  },
  testConnection: async (request: TestConnectionRequest): Promise<ConnectionTestResult> =>
    (await apiClient.post('/admin/data-sources/test', request)).data,
  testStoredConnection: async (id: string): Promise<ConnectionTestResult> =>
    (await apiClient.post(`/admin/data-sources/${id}/test`)).data,
  getDataScope: async (id: string): Promise<AdminDataScopeDto> => (await apiClient.get(`/admin/data-sources/${id}/data-scope`)).data,
  updateDataScope: async (id: string, request: UpdateDataScopeRequest): Promise<void> => {
    await apiClient.put(`/admin/data-sources/${id}/data-scope`, request)
  },
  updateScopeKeys: async (id: string, request: UpdateScopeKeysRequest): Promise<AdminDataScopeDto> =>
    (await apiClient.put(`/admin/data-sources/${id}/scope-keys`, request)).data,
}

export const visualizationsApi = {
  list: async (queryId: string): Promise<VisualizationDto[]> =>
    (await apiClient.get(`/queries/${queryId}/visualizations`)).data,
  create: async (queryId: string, request: SaveVisualizationRequest): Promise<VisualizationDto> =>
    (await apiClient.post(`/queries/${queryId}/visualizations`, request)).data,
  update: async (queryId: string, id: string, request: SaveVisualizationRequest): Promise<VisualizationDto> =>
    (await apiClient.put(`/queries/${queryId}/visualizations/${id}`, request)).data,
  remove: async (queryId: string, id: string): Promise<void> => {
    await apiClient.delete(`/queries/${queryId}/visualizations/${id}`)
  },
  render: async (queryId: string, id: string, request: RenderVisualizationRequest): Promise<VisualizationDataDto> =>
    (await apiClient.post(`/queries/${queryId}/visualizations/${id}/data`, request)).data,
}

export interface MeDto {
  userId: string
  displayName: string
  /** Null when the host configured no data scoping. */
  zone: { unrestricted: boolean; values: Record<string, string[]> } | null
  app?: { id: string; key: string; name: string } | null
  role?: 'Admin' | 'Author' | 'Viewer'
  isSuperAdmin?: boolean
  returnUrl?: string | null
  /** A super admin's chosen app; null until they pick one. */
  viewingApp?: { id: string; key: string; name: string } | null
  /** Present only in hub mode; its presence is how the client knows it is running under the hub. */
  permissions?: { canBuildQueries: boolean; canManageDashboards: boolean; canPublish: boolean; canAdmin: boolean; isViewer: boolean }
}

export const meApi = {
  get: async (): Promise<MeDto> => (await apiClient.get('/me')).data,
}

export interface HubAppDto {
  id: string
  key: string
  name: string
  isEnabled: boolean
  version: number
}

export const hubApi = {
  listApps: async (): Promise<HubAppDto[]> => (await apiClient.get('/hub/apps')).data,
  viewApp: async (appId: string): Promise<void> => {
    await apiClient.post('/hub/view-app', { appId })
  },
  // Outside /api on the server, so it can't use apiClient's base URL; same CSRF header.
  logout: async (): Promise<{ redirectUrl: string }> =>
    (await apiClient.post('/querybuilder/logout', undefined, { baseURL: '' })).data,
}

export const dashboardsApi = {
  tileChart: async (dashboardId: string, tileId: string): Promise<TileChartDto> =>
    (await apiClient.get(`/dashboards/${dashboardId}/tiles/${tileId}/chart`)).data,
  parameterOptions: async (dashboardId: string, name: string): Promise<ParameterOption[]> =>
    (await apiClient.get(`/dashboards/${dashboardId}/parameters/${encodeURIComponent(name)}/options`)).data,
  list: async (): Promise<DashboardSummaryDto[]> => (await apiClient.get('/dashboards')).data,
  getById: async (id: string): Promise<DashboardDetailDto> => (await apiClient.get(`/dashboards/${id}`)).data,
  create: async (request: SaveDashboardRequest): Promise<DashboardDetailDto> =>
    (await apiClient.post('/dashboards', request)).data,
  rename: async (id: string, request: SaveDashboardRequest): Promise<DashboardDetailDto> =>
    (await apiClient.put(`/dashboards/${id}`, request)).data,
  saveTiles: async (id: string, request: SaveDashboardTilesRequest): Promise<DashboardDetailDto> =>
    (await apiClient.put(`/dashboards/${id}/tiles`, request)).data,
  addChart: async (id: string, visualizationId: string): Promise<DashboardSummaryDto> =>
    (await apiClient.post(`/dashboards/${id}/tiles/chart`, { visualizationId })).data,
  remove: async (id: string): Promise<void> => {
    await apiClient.delete(`/dashboards/${id}`)
  },
  setPublished: async (id: string, published: boolean): Promise<void> => {
    await apiClient.put(`/dashboards/${id}/published`, { published })
  },
  getShares: async (id: string): Promise<QueryShareDto[]> => (await apiClient.get(`/dashboards/${id}/shares`)).data,
  share: async (id: string, request: ShareDashboardRequest): Promise<{ id: string }> =>
    (await apiClient.post(`/dashboards/${id}/shares`, request)).data,
  removeShare: async (id: string, shareId: string): Promise<void> => {
    await apiClient.delete(`/dashboards/${id}/shares/${shareId}`)
  },
  sharePreview: async (id: string, userId: string): Promise<DashboardSharePreviewDto> =>
    (await apiClient.get(`/dashboards/${id}/share-preview`, { params: { userId } })).data,
  pickableVisualizations: async (): Promise<PickableVisualizationDto[]> =>
    (await apiClient.get('/visualizations/pickable')).data,
}
