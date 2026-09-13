/**
 * The permission keys the picker screens ask for, named once.
 *
 * The same strings the server's catalogue declares
 * (`src/Platform/Tailor360.Platform.Security/Permissions/CatalogPermissions.cs`). A screen never
 * decides anything from them — the server re-checks every request — but naming them here means a
 * rename on the server is one edit rather than a search through the routes.
 */
export const CATALOG_PERMISSIONS = {
  /** Starting, saving, checking and migrating a design selection draft on the picker (#142). */
  designSelect: 'catalog.design.select',
} as const
