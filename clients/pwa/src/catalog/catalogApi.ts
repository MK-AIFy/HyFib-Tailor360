import { apiRequest, apiRequestVersioned } from '../auth/apiClient'
import type { VersionedResponse } from '../auth/apiClient'
import type {
  CatalogDesignGroup,
  CatalogDesignOption,
  CatalogDesignRule,
  CatalogPublication,
  CatalogValidationReport,
  CatalogVersion,
  CatalogVersionSummary,
  CategoryRequest,
  DesignGroupPresentationRequest,
  DesignGroupRequest,
  DesignOptionPresentationRequest,
  DesignOptionRequest,
  DesignRuleRequest,
  OrderableCatalog,
  PresentationRequest,
  ServiceTypeRequest,
} from './types'

/**
 * Every call the catalogue administration screens make.
 *
 * ## What each command carries
 *
 * **A retry key** on every one of them, and **the version it is acting on** in `If-Match` on all but
 * the two that only create. That is the server's requirement rather than a precaution: the ten
 * routes that change a draft, and the four that publish, retire or correct a label, all declare
 * `RequireIfMatch()`, so a command without a precondition does not run.
 *
 * The precondition is the `ETag` of the read that painted the screen, never one fetched immediately
 * before the write — which would make it true by construction and is exactly what `If-Match` exists
 * to refuse.
 *
 * ## Two permissions, not one
 *
 * Everything that changes a *draft* needs `catalog.edit`. Publishing, retiring and correcting a
 * published label need `catalog.publish`, which is a different key and is why a screen offering all
 * of them to somebody holding only the first would offer four controls that each end in a 403.
 */

const CATALOG = '/api/v1/catalog'

/**
 * What the caller's branch may order today.
 *
 * The counter's read rather than the administrator's, and a different shape for that reason: a flat
 * list already scoped to the caller's branch, with each category's name inline. It is here so that
 * "the administrator publishes and the category appears at the counter" can be shown to have
 * happened, which is #29's acceptance criterion and needs no deployment in between.
 */
export async function readCurrentCatalog(signal?: AbortSignal): Promise<OrderableCatalog> {
  return await apiRequest<OrderableCatalog>(`${CATALOG}/current`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Every version, newest first. Contents are not carried. */
export async function listCatalogVersions(
  signal?: AbortSignal,
): Promise<readonly CatalogVersionSummary[]> {
  return await apiRequest<readonly CatalogVersionSummary[]>(`${CATALOG}/versions`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** One version and its whole tree, with the tag an edit presents back. */
export async function readCatalogVersion(
  versionId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(`${CATALOG}/versions/${versionId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Runs every registered validator without changing anything.
 *
 * The preview a publish dialog is built from: it reports *every* finding rather than the first, so
 * a draft is corrected in one sitting rather than one round trip per problem.
 */
export async function validateCatalogVersion(
  versionId: string,
  signal?: AbortSignal,
): Promise<CatalogValidationReport> {
  return await apiRequest<CatalogValidationReport>(`${CATALOG}/versions/${versionId}/validation`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/** Starts a draft, empty or copied from an existing version. */
export async function createCatalogDraft(input: {
  readonly name: string
  readonly notes: string | null
  readonly cloneFromVersionId: string | null
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(`${CATALOG}/versions`, {
    method: 'POST',
    body: {
      name: input.name,
      notes: input.notes,
      cloneFromVersionId: input.cloneFromVersionId,
    },
    idempotencyKey: input.idempotencyKey,
  })
}

/** Adds a category to a draft. */
export async function addCatalogCategory(input: {
  readonly versionId: string
  readonly category: CategoryRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/categories`,
    {
      method: 'POST',
      body: input.category,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces a category of a draft. */
export async function editCatalogCategory(input: {
  readonly versionId: string
  readonly categoryId: string
  readonly category: CategoryRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/categories/${input.categoryId}`,
    {
      method: 'PUT',
      body: input.category,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Removes a category from a draft, with the reason the trail records. */
export async function removeCatalogCategory(input: {
  readonly versionId: string
  readonly categoryId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/categories/${input.categoryId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Adds a service type under a category of a draft. */
export async function addCatalogServiceType(input: {
  readonly versionId: string
  readonly categoryId: string
  readonly serviceType: ServiceTypeRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/categories/${input.categoryId}/service-types`,
    {
      method: 'POST',
      body: input.serviceType,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces a service type of a draft. */
export async function editCatalogServiceType(input: {
  readonly versionId: string
  readonly serviceTypeId: string
  readonly serviceType: ServiceTypeRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/service-types/${input.serviceTypeId}`,
    {
      method: 'PUT',
      body: input.serviceType,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Removes a service type from a draft, with the reason the trail records. */
export async function removeCatalogServiceType(input: {
  readonly versionId: string
  readonly serviceTypeId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/service-types/${input.serviceTypeId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Corrects the label of a category in a **published** version.
 *
 * The only edit a published version admits, and deliberately narrow: a name, a Tamil name, a
 * description and a position. Nothing that changes what ordering the thing *means*, because an order
 * already placed against it was placed against what it meant then.
 */
export async function correctCategoryPresentation(input: {
  readonly versionId: string
  readonly categoryId: string
  readonly presentation: PresentationRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/categories/${input.categoryId}/presentation`,
    {
      method: 'POST',
      body: input.presentation,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** The same correction, for a service type. */
export async function correctServiceTypePresentation(input: {
  readonly versionId: string
  readonly serviceTypeId: string
  readonly presentation: PresentationRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/service-types/${input.serviceTypeId}/presentation`,
    {
      method: 'POST',
      body: input.presentation,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Publishes a draft, superseding whatever was published before it. */
export async function publishCatalogVersion(input: {
  readonly versionId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogPublication>> {
  return await apiRequestVersioned<CatalogPublication>(
    `${CATALOG}/versions/${input.versionId}/publish`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Adds a design option group to a category of a draft (#141).
 *
 * The write answers with the group alone, not the whole version — unlike a category or service type
 * — so the screen always reloads the version afterward to see the group in place among the rest of
 * the tree. Only the `ETag` this carries is used directly, as every other command's is.
 */
export async function addCatalogDesignGroup(input: {
  readonly versionId: string
  readonly categoryId: string
  readonly group: DesignGroupRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogDesignGroup>> {
  return await apiRequestVersioned<CatalogDesignGroup>(
    `${CATALOG}/versions/${input.versionId}/categories/${input.categoryId}/design-groups`,
    {
      method: 'POST',
      body: input.group,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces a design option group of a draft. */
export async function editCatalogDesignGroup(input: {
  readonly versionId: string
  readonly designOptionGroupId: string
  readonly group: DesignGroupRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogDesignGroup>> {
  return await apiRequestVersioned<CatalogDesignGroup>(
    `${CATALOG}/versions/${input.versionId}/design-groups/${input.designOptionGroupId}`,
    {
      method: 'PUT',
      body: input.group,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Removes a design option group, its options and the rules that read it from a draft. */
export async function removeCatalogDesignGroup(input: {
  readonly versionId: string
  readonly designOptionGroupId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<void>> {
  return await apiRequestVersioned<void>(
    `${CATALOG}/versions/${input.versionId}/design-groups/${input.designOptionGroupId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Adds an option to a design option group in a draft. */
export async function addCatalogDesignOption(input: {
  readonly versionId: string
  readonly designOptionGroupId: string
  readonly option: DesignOptionRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogDesignOption>> {
  return await apiRequestVersioned<CatalogDesignOption>(
    `${CATALOG}/versions/${input.versionId}/design-groups/${input.designOptionGroupId}/options`,
    {
      method: 'POST',
      body: input.option,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces a design option of a draft. */
export async function editCatalogDesignOption(input: {
  readonly versionId: string
  readonly designOptionId: string
  readonly option: DesignOptionRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogDesignOption>> {
  return await apiRequestVersioned<CatalogDesignOption>(
    `${CATALOG}/versions/${input.versionId}/design-options/${input.designOptionId}`,
    {
      method: 'PUT',
      body: input.option,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Removes a design option from a draft. */
export async function removeCatalogDesignOption(input: {
  readonly versionId: string
  readonly designOptionId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<void>> {
  return await apiRequestVersioned<void>(
    `${CATALOG}/versions/${input.versionId}/design-options/${input.designOptionId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Adds a requires, excludes, requires-attachment or note rule to a category in a draft.
 *
 * The rule's `DR-nn` number is allocated by the catalogue; whether the options it names exist, and
 * whether it agrees with the other rules, is a publication check rather than something this call
 * answers — the editor shows its own composed sentence before saving, but the server's findings are
 * what a save can actually be refused on.
 */
export async function addCatalogDesignRule(input: {
  readonly versionId: string
  readonly categoryId: string
  readonly rule: DesignRuleRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogDesignRule>> {
  return await apiRequestVersioned<CatalogDesignRule>(
    `${CATALOG}/versions/${input.versionId}/categories/${input.categoryId}/design-rules`,
    {
      method: 'POST',
      body: input.rule,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Replaces a design rule of a draft. Its number and category never change. */
export async function editCatalogDesignRule(input: {
  readonly versionId: string
  readonly designRuleId: string
  readonly rule: DesignRuleRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogDesignRule>> {
  return await apiRequestVersioned<CatalogDesignRule>(
    `${CATALOG}/versions/${input.versionId}/design-rules/${input.designRuleId}`,
    {
      method: 'PUT',
      body: input.rule,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Removes a design rule from a draft. Its number is retired with it. */
export async function removeCatalogDesignRule(input: {
  readonly versionId: string
  readonly designRuleId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<void>> {
  return await apiRequestVersioned<void>(
    `${CATALOG}/versions/${input.versionId}/design-rules/${input.designRuleId}/delete`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Corrects the label or position of a design group in a **published** version.
 *
 * The same narrow edit the category and service-type presentation corrections make: what a person
 * reads, never what ordering it means. Unlike those, this answers with the whole version — there is
 * no group-only read a screen would otherwise need a second round trip for.
 */
export async function correctCatalogDesignGroupPresentation(input: {
  readonly versionId: string
  readonly designOptionGroupId: string
  readonly presentation: DesignGroupPresentationRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/design-groups/${input.designOptionGroupId}/presentation`,
    {
      method: 'POST',
      body: input.presentation,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * The same correction, for a design option.
 *
 * The illustration itself is never correctable here — the drawing a customer was shown is part of
 * what they agreed to — only its label, help text and alternative text.
 */
export async function correctCatalogDesignOptionPresentation(input: {
  readonly versionId: string
  readonly designOptionId: string
  readonly presentation: DesignOptionPresentationRequest
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersion>> {
  return await apiRequestVersioned<CatalogVersion>(
    `${CATALOG}/versions/${input.versionId}/design-options/${input.designOptionId}/presentation`,
    {
      method: 'POST',
      body: input.presentation,
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/** Retires a version, with the reason the trail records. */
export async function retireCatalogVersion(input: {
  readonly versionId: string
  readonly reason: string | null
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CatalogVersionSummary>> {
  return await apiRequestVersioned<CatalogVersionSummary>(
    `${CATALOG}/versions/${input.versionId}/retire`,
    {
      method: 'POST',
      body: { reason: input.reason },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )
}
