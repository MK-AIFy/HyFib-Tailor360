import { apiRequest, apiRequestBlob, apiRequestVersioned } from '../auth/apiClient'
import type { BlobDownload, VersionedResponse } from '../auth/apiClient'
import { CUSTOMER_DUPLICATES_CODE } from './types'
import type {
  Customer,
  CustomerDetailsInput,
  CustomerPage,
  CommunicationPreferences,
  ConsentAnswer,
  CustomerConsent,
  CustomerExport,
  CustomerMergeOutcome,
  CustomerTimelinePage,
  DuplicateCandidate,
  DuplicateReview,
} from './types'

const CUSTOMERS = '/api/v1/customers/'

/**
 * The shortest term the server will search on.
 *
 * Held equal to `CustomerSearchQuery.MinimumTermLength` by a contract test, which reads this line.
 * It cannot be generated from the API document: `minLength` is a validation keyword and the
 * generated types render the parameter as `string` either way. Change this and the server's
 * constant together, or that test fails and says so.
 *
 * The screen pre-checks against it so that a term the server would refuse costs no round trip —
 * being told what you could have been told locally is its own small insult — and renders the
 * server's refusal in the same words when it meets one anyway.
 */
export const CUSTOMER_SEARCH_MINIMUM_LENGTH = 3

/**
 * The value of `CustomerPage.refusal` when the term was too short to search on.
 *
 * The page comes back `200` and empty, exactly as it always did — the reason rides in a field
 * rather than in the status code, because changing the status would break a v1 caller that reads
 * `200 []` as "nobody matched".
 */
export const CUSTOMER_SEARCH_TERM_TOO_SHORT_CODE = 'customers.search-term-too-short'

/**
 * What an empty search page actually means.
 *
 * Told apart here rather than at each screen, because two screens have now got it wrong in the same
 * way: an empty list was rendered as "nobody matched" when the page had said, in `refusal`, that it
 * had not searched at all. That is a false negative in front of somebody deciding whether to
 * register a new customer, and it is how one person ends up with two records.
 *
 * `refused` covers a value this build does not recognise. The field is open-ended by contract, so
 * that case is reachable by an old client against a newer server, and it must not collapse into
 * `none`: not knowing why the page is empty is not the same as knowing there is nobody.
 */
export type SearchOutcome = 'results' | 'none' | 'too-short' | 'refused'

/** Classifies a page. See {@link SearchOutcome} for why this is not left to call sites. */
export function searchOutcome(page: CustomerPage): SearchOutcome {
  if (page.customers.length > 0) {
    return 'results'
  }

  if (page.refusal === CUSTOMER_SEARCH_TERM_TOO_SHORT_CODE) {
    return 'too-short'
  }

  return page.refusal === null || page.refusal === undefined ? 'none' : 'refused'
}

/**
 * Finds a customer by name, native name, customer number or the tail of a telephone number.
 *
 * Answers across the organisation. The term travels as a query string, so it is encoded here rather
 * than at every call site; a name with an ampersand in it is a name, not a second parameter.
 */
export async function searchCustomers(
  term: string,
  options: {
    /**
     * Whether to offer records that have been withdrawn from ordinary use.
     *
     * Off by default, which is the server's default too: a search is nearly always somebody starting
     * a new order, and a withdrawn record is precisely the one that should not be offered for that.
     * The customer search screen turns it on when asked, because a record that cannot be found is a
     * record that cannot be put back.
     */
    readonly includeDeactivated?: boolean
    readonly signal?: AbortSignal
  } = {},
): Promise<CustomerPage> {
  const query = new URLSearchParams({ term })
  if (options.includeDeactivated === true) {
    query.set('includeDeactivated', 'true')
  }

  return await apiRequest<CustomerPage>(`${CUSTOMERS}?${query.toString()}`, {
    ...(options.signal === undefined ? {} : { signal: options.signal }),
  })
}

/** Reads one customer record, with the version a correction must be made against. */
export async function readCustomer(
  customerId: string,
  signal?: AbortSignal,
): Promise<VersionedResponse<Customer>> {
  return await apiRequestVersioned<Customer>(`${CUSTOMERS}${customerId}`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * What a registration attempt answers, once the duplicate question is settled one way or the other.
 *
 * There is no third case on the wire: the server either creates the record (`customer` present) or
 * refuses with the candidates to read (`customer` null, handled as the 409 this function throws
 * rather than returns — see `RegisterCustomerConflict`). This type exists for the success half only,
 * kept narrow so a caller cannot forget to look at `customer`.
 */
export interface RegisteredCustomer {
  readonly customer: Customer
}

/**
 * Creates a customer record at the branch the caller is working in.
 *
 * Refused with a `409 customers.duplicates-not-reviewed` and a `candidates` list on the problem when
 * an existing record resembles this one strongly enough to be worth reading — the caller reads them,
 * then either opens one or calls this again with `duplicatesReviewed: true`, which records that a
 * person took the decision. `readDuplicateCandidates` reads that list back off the thrown `ApiError`.
 */
export async function registerCustomer(input: {
  readonly details: CustomerDetailsInput
  readonly duplicatesReviewed: boolean
  readonly idempotencyKey: string
}): Promise<RegisteredCustomer> {
  const customer = await apiRequestVersioned<Customer>(CUSTOMERS, {
    method: 'POST',
    body: { ...input.details, duplicatesReviewed: input.duplicatesReviewed },
    idempotencyKey: input.idempotencyKey,
  })

  return { customer: customer.value }
}

/** The shape of the problem `registerCustomer` throws when duplicates have not been reviewed. */
interface DuplicatesProblem {
  readonly code?: string
  readonly candidates?: readonly DuplicateCandidate[]
}

/**
 * Reads the duplicate candidates off a failed `registerCustomer` call, or null when the failure was
 * something else.
 *
 * A separate function rather than a field on `ApiError` itself, because the candidates are specific
 * to this one endpoint's 409 and `ApiProblem` is the shared shape every screen reads — adding a field
 * there for one caller is exactly the kind of quiet coupling that makes the shared type harder to
 * read for everybody else.
 */
export function readDuplicateCandidates(failure: unknown): readonly DuplicateCandidate[] | null {
  if (
    typeof failure !== 'object' ||
    failure === null ||
    !('problem' in failure) ||
    typeof failure.problem !== 'object' ||
    failure.problem === null
  ) {
    return null
  }

  const problem = failure.problem as DuplicatesProblem
  return problem.code === CUSTOMER_DUPLICATES_CODE && problem.candidates !== undefined
    ? problem.candidates
    : null
}

/**
 * Corrects what a customer record says about the person.
 *
 * `PUT`, and whole-record: the server compares every field against what it holds and records which
 * ones changed, so a field left out of `details` is a field being cleared, not a field being left
 * alone. The edit screen therefore sends the record it read back with the person's edits applied,
 * never a sparse patch.
 *
 * Three headers carry the things that make it safe, and `apiClient` puts all three on:
 *
 *  - `If-Match` is the version read from the record's own `GET`. A correction made against a version
 *    that is no longer current is refused with `409 customers.version-conflict` rather than quietly
 *    overwriting whatever a colleague saved in between.
 *  - `Idempotency-Key` makes a resend of *this* correction replay the first outcome instead of
 *    applying it twice.
 *  - The reason travels in the body because the endpoint is audited with `reasonRequired`, and a
 *    correction with no reason is a change nobody can question later.
 */
export async function correctCustomer(input: {
  readonly customerId: string
  readonly details: CustomerDetailsInput
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Customer>> {
  return await apiRequestVersioned<Customer>(`${CUSTOMERS}${input.customerId}`, {
    method: 'PUT',
    body: { ...input.details, reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}

/** The page size the server uses when the client names none. Matches `TimelineQuery.DefaultLimit`. */
export const CUSTOMER_TIMELINE_PAGE_SIZE = 25

/**
 * Reads one page of a customer's history, merged across every module that holds part of it.
 *
 * Newest first and cursor-paged: `nextCursor` is opaque and is sent back verbatim for the next page,
 * never decoded or constructed here. The composition happens in the web host rather than in any one
 * module — no module may read another's tables — which is why this route hangs off the customer and
 * not off a module of its own.
 *
 * What comes back is already filtered to what the caller may see: a module withholds the entries the
 * caller's permissions do not reach, and the approved response view decides which fields of the ones
 * that survive are populated. The client filters nothing and unmasks nothing.
 */
export async function readCustomerTimeline(
  input: { readonly customerId: string; readonly cursor?: string | undefined },
  signal?: AbortSignal,
): Promise<CustomerTimelinePage> {
  const query = new URLSearchParams(input.cursor === undefined ? {} : { cursor: input.cursor })
  const suffix = query.size === 0 ? '' : `?${query.toString()}`

  return await apiRequest<CustomerTimelinePage>(
    `${CUSTOMERS}${input.customerId}/timeline${suffix}`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Lists the records that may be the same person as this one.
 *
 * Scored as the records stand, not read back from the suspicions raised when either was created: a
 * correction to either can create a resemblance or remove one, and a merge is too final to take on a
 * score somebody computed months ago.
 *
 * Gated on `customers.read` and not `customers.merge`, deliberately — reading who might be a
 * duplicate is what Reception does *before* asking a manager to merge, and demanding the merge
 * permission to look would mean nobody could prepare the decision.
 */
export async function readDuplicateReview(
  customerId: string,
  signal?: AbortSignal,
): Promise<DuplicateReview> {
  return await apiRequest<DuplicateReview>(`${CUSTOMERS}${customerId}/duplicates`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Folds one customer record into another. This cannot be undone.
 *
 * The record in the path **survives**; the one named in the body is folded into it. The direction is
 * decided by which identifier goes where, which is why it is two named fields here rather than a
 * pair of positional arguments a call site could transpose.
 *
 * ## Both records are preconditions, and they are not the same kind of precondition
 *
 * `If-Match` carries the survivor's version, and `mergedCustomerVersion` carries the folded record's.
 * What a manager approves is a *pair* — these two records, as they read on the screen, are one
 * person — and an `If-Match` alone protects only one half of that. The half it leaves open is the
 * record about to stop existing, so a correction to its name or number would otherwise be merged
 * away with no undo.
 *
 * `mergedCustomerVersion` is always a concrete version and never `*`: there is no such thing as "any
 * version" of a record somebody approved destroying.
 *
 * ## The step-up is the transport's job
 *
 * The endpoint demands a fresh proof of identity, and `apiClient` answers a
 * `403 security.step-up-required` by raising the re-authentication dialog and replaying this exact
 * request — same body, same version, same retry key. So this function does not ask first: doing so
 * would put a second dialog over the confirmation, and would still not remove the case where the
 * proof goes stale between the asking and the sending.
 */
export async function mergeCustomers(input: {
  /** The record that survives. */
  readonly customerId: string
  /** The record that does not. */
  readonly mergedCustomerId: string
  /** That record's version, from its own `GET`. Never `*`. */
  readonly mergedCustomerVersion: string
  readonly reason: string
  /** The survivor's version, from its own `GET`. */
  readonly version: string
  readonly idempotencyKey: string
}): Promise<CustomerMergeOutcome> {
  const merged = await apiRequestVersioned<CustomerMergeOutcome>(
    `${CUSTOMERS}${input.customerId}/merge`,
    {
      method: 'POST',
      body: {
        mergedCustomerId: input.mergedCustomerId,
        mergedCustomerVersion: input.mergedCustomerVersion,
        reason: input.reason,
      },
      ifMatch: input.version,
      idempotencyKey: input.idempotencyKey,
    },
  )

  return merged.value
}

/** Reads where a customer stands on every purpose the shop asks about. */
export async function readConsent(
  customerId: string,
  signal?: AbortSignal,
): Promise<CustomerConsent> {
  return await apiRequest<CustomerConsent>(`${CUSTOMERS}${customerId}/consent`, {
    ...(signal === undefined ? {} : { signal }),
  })
}

/**
 * Records what a customer said about one purpose.
 *
 * It appends an answer and never edits one: withdrawing is a `Withdrawn` answer and agreeing again is
 * another `Granted` one, so the evidence that she once withdrew survives her changing her mind.
 *
 * The wording version is **not sent**. The server reads it from the register, because a client that
 * could name a version could record an answer against words she was never read — and a purpose with
 * no published wording is refused for the same reason, which is what `canBeAnswered` reports.
 */
export async function recordConsent(input: {
  readonly customerId: string
  readonly purposeKey: string
  /** `Granted`, `Declined` or `Withdrawn`. */
  readonly decision: string
  /** Where it was taken, in words. Free text the trail keeps. */
  readonly source: string
  readonly idempotencyKey: string
}): Promise<ConsentAnswer> {
  return await apiRequest<ConsentAnswer>(`${CUSTOMERS}${input.customerId}/consent`, {
    method: 'POST',
    body: { purposeKey: input.purposeKey, decision: input.decision, source: input.source },
    idempotencyKey: input.idempotencyKey,
  })
}

/** Reads how a customer wants to be reached, with the version a change must be made against. */
export async function readCommunicationPreferences(
  customerId: string,
  signal?: AbortSignal,
): Promise<CommunicationPreferences> {
  return await apiRequest<CommunicationPreferences>(
    `${CUSTOMERS}${customerId}/communication-preferences`,
    { ...(signal === undefined ? {} : { signal }) },
  )
}

/**
 * Replaces how a customer wants to be reached.
 *
 * Whole-state and not a patch, so the trail reads as a state and "which channels does she accept"
 * has one answer. An empty `allowedChannels` is how she says do not message me.
 *
 * ## Why `version` is optional here and required everywhere else
 *
 * `If-Match` is required once a preference exists and **must be omitted before then**, because there
 * is no version of a row that does not exist. `hasBeenRecorded` on the read is what tells the two
 * apart, and sending `*` instead would be asking the server to accept any version of something that
 * has none. So the caller passes the version it read, or nothing, and this function sends exactly
 * what it was given.
 */
export async function replaceCommunicationPreferences(input: {
  readonly customerId: string
  readonly allowedChannels: readonly string[]
  readonly language: string
  readonly quietHoursStart: string | null
  readonly quietHoursEnd: string | null
  /** The version read, or undefined when no preference exists yet. */
  readonly version: string | undefined
  readonly idempotencyKey: string
}): Promise<VersionedResponse<CommunicationPreferences>> {
  return await apiRequestVersioned<CommunicationPreferences>(
    `${CUSTOMERS}${input.customerId}/communication-preferences`,
    {
      method: 'PUT',
      body: {
        allowedChannels: input.allowedChannels,
        language: input.language,
        quietHoursStart: input.quietHoursStart,
        quietHoursEnd: input.quietHoursEnd,
      },
      ...(input.version === undefined ? {} : { ifMatch: input.version }),
      idempotencyKey: input.idempotencyKey,
    },
  )
}

/**
 * Generates the copy of a customer's data that answers a subject-access request.
 *
 * What comes back is a **receipt**, not the document: it names the export and says when the download
 * stops working. `downloadCustomerExport` fetches the bytes, from a route that re-authorises and is
 * audited on every call.
 *
 * Generating destroys any earlier export for the same customer, so at most one copy of a person's
 * record exists outside the record at a time. That is a property of the endpoint and not a choice
 * this function makes, but it is the reason the screen asks before doing it rather than after.
 */
export async function requestCustomerExport(input: {
  readonly customerId: string
  readonly reason: string
  readonly idempotencyKey: string
}): Promise<CustomerExport> {
  return await apiRequest<CustomerExport>(`${CUSTOMERS}${input.customerId}/export`, {
    method: 'POST',
    body: { reason: input.reason },
    idempotencyKey: input.idempotencyKey,
  })
}

/**
 * Fetches the export's bytes.
 *
 * Through the transport, as bytes, never as an address. The permission, the organisation and the
 * expiry are re-checked on this request and the call is written to the audit trail against the
 * customer — none of which a link somebody could copy out of the address bar would do, which is
 * what rule 9 is protecting.
 *
 * The file name comes from the server's `Content-Disposition`, and the fallback here keeps the same
 * promise it does: the export's identifier and nothing about the person. A customer's name in a
 * downloads folder is personal data in a place nobody is auditing.
 */
export async function downloadCustomerExport(input: {
  readonly customerId: string
  readonly exportId: string
  readonly documentCode: string
}): Promise<BlobDownload> {
  return await apiRequestBlob(`${CUSTOMERS}${input.customerId}/exports/${input.exportId}`, {
    accept: 'application/json',
    fallbackFileName: `${input.documentCode}-${input.exportId}.json`,
  })
}

/** Which way a customer record's status is being moved. The two share every rule but their verb. */
export type CustomerStatusCommand = 'deactivate' | 'reactivate'

/**
 * Withdraws a customer record from ordinary use, or returns one to it.
 *
 * **Not a deletion.** The record stays readable and its history stays resolvable — an order placed
 * last year still names the person who placed it. What changes is that a search stops offering the
 * record when somebody starts a new order. That is the whole of the difference, and it is why this
 * is the recoverable thing to reach for when a merge is not: `reactivate` puts it back.
 *
 * Both directions are gated on the same permission, deliberately, so that a record cannot be put
 * beyond the reach of everybody present.
 *
 * `If-Match` is required — this is a state change on a versioned record like any other — and the
 * reason is required because the audit trail is the only place a reader can later ask why somebody
 * was withdrawn.
 */
export async function commandCustomerStatus(input: {
  readonly customerId: string
  readonly command: CustomerStatusCommand
  readonly reason: string
  readonly version: string
  readonly idempotencyKey: string
}): Promise<VersionedResponse<Customer>> {
  return await apiRequestVersioned<Customer>(`${CUSTOMERS}${input.customerId}/${input.command}`, {
    method: 'POST',
    body: { reason: input.reason },
    ifMatch: input.version,
    idempotencyKey: input.idempotencyKey,
  })
}
