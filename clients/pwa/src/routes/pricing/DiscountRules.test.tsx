import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { RequireSession } from '../../auth/RequireSession'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { aBranch } from '../../admin/testing/fixtures'
import { BILLING_PERMISSIONS } from '../../billing/billingPermissions'
import { BRANCH_ID } from '../../billing/testing/fixtures'
import {
  PRICE_LIST_VERSION_ID,
  aDiscountRule,
  aPriceListItem,
  aPriceListVersion,
  aPriceListVersionSummary,
} from '../../billing/testing/priceListFixtures'
import type { DiscountRule } from '../../billing/priceListTypes'
import { PriceListVersionEditorRoute } from './PriceListVersionEditorRoute'

let transport: FetchStub

const PRICE_LISTS = '/api/v1/billing/price-lists'
const PRICE_LIST_VERSION_BASE = `${PRICE_LISTS}/versions`
const BRANCHES = '/api/v1/admin/branches/'
const TAX_VERSIONS = '/api/v1/billing/tax-configuration/versions'
const DRAFT_ID = PRICE_LIST_VERSION_ID
const SECOND_RULE_ID = '0199dd00-0000-7000-8000-000000007098'

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route(`GET ${BRANCHES}`, () =>
    jsonResponse([aBranch({ branchId: BRANCH_ID, name: 'Coimbatore counter', code: 'CBE01' })]),
  )
  transport.route(`GET ${TAX_VERSIONS}`, () => jsonResponse([]))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

/** A JSON body carrying the `ETag` a write is made against. */
function versionedJson(body: unknown, etag: string, status = 200): Response {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ETag: etag },
  })
}

function renderApp(
  path: string,
  permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists],
) {
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions })))

  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={
                  <RequirePermission permission={BILLING_PERMISSIONS.managePriceLists}>
                    <PriceListVersionEditorRoute />
                  </RequirePermission>
                }
                path="/admin/price-lists/versions/:versionId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function renderEditorAt(
  versionId: string,
  permissions: readonly string[] = [BILLING_PERMISSIONS.managePriceLists],
) {
  return renderApp(`/admin/price-lists/versions/${versionId}`, permissions)
}

describe('the price-list version editor’s discount rules', () => {
  it('adds a percentage rule, edits it, adds an amount rule, removes one, and reads the bounds back', async () => {
    const user = userEvent.setup()
    const NEW_RULE_ID = '0199dd00-0000-7000-8000-000000007005'
    let rules: readonly DiscountRule[] = []
    const version = aPriceListVersionSummary({
      priceListVersionId: DRAFT_ID,
      status: 'Draft',
      overrideThresholdPercent: 7,
    })
    let tag = 'W/"1"'
    const versionBody = () => ({ version, items: [], discountRules: rules })

    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(versionBody(), tag),
    )

    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, (call) => {
      const body = call.body as {
        code: string
        description: string
        kind: string
        maximumWithoutApproval: string
        maximum: string
        active: boolean
      }
      const created = aDiscountRule({
        ...body,
        discountRuleId: rules.length === 0 ? NEW_RULE_ID : SECOND_RULE_ID,
      })
      rules = [...rules, created]
      tag = rules.length === 1 ? 'W/"2"' : 'W/"4"'
      return versionedJson(created, tag, 201)
    })

    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${NEW_RULE_ID}`,
      (call) => {
        const body = call.body as { description: string }
        const edited = { ...rules[0], description: body.description } as DiscountRule
        rules = [edited, ...rules.slice(1)]
        tag = 'W/"3"'
        return versionedJson(edited, tag)
      },
    )

    transport.route(
      `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${NEW_RULE_ID}/delete`,
      () => {
        rules = rules.filter((rule) => rule.discountRuleId !== NEW_RULE_ID)
        tag = 'W/"5"'
        return versionedJson(null, tag, 204)
      },
    )

    renderEditorAt(DRAFT_ID)
    expect(await screen.findByText('Price-list version 1')).toBeInTheDocument()

    // Adds a percentage rule.
    await user.click(screen.getByRole('button', { name: 'Add a discount rule' }))
    const addForm = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(addForm.getByLabelText('Code'), 'FESTIVE10')
    await user.type(addForm.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      addForm.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    await user.type(addForm.getByLabelText('Maximum without approval'), '10')
    await user.type(addForm.getByLabelText('Maximum'), '20')
    await user.click(addForm.getByLabelText('Active'))
    await user.click(addForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the discount rule FESTIVE10.')).toBeInTheDocument()
    expect(screen.getByText('FESTIVE10')).toBeInTheDocument()
    expect(screen.getByText('10%')).toBeInTheDocument()
    expect(screen.getByText('20%')).toBeInTheDocument()

    const added = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`)[0]
    expect(added?.body).toEqual({
      code: 'FESTIVE10',
      description: 'Festive season discount',
      kind: 'Percentage',
      maximumWithoutApproval: '10',
      maximum: '20',
      active: true,
      reason: null,
    })
    expect(added?.headers.get('If-Match')).toBe('W/"1"')

    // Edits it.
    await user.click(screen.getByRole('button', { name: 'Edit FESTIVE10' }))
    const editForm = within(
      await screen.findByRole('form', { name: 'Edit the discount rule FESTIVE10' }),
    )
    await user.clear(editForm.getByLabelText('Description'))
    await user.type(editForm.getByLabelText('Description'), 'Festive season discount, extended')
    await user.click(editForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Festive season discount, extended')).toBeInTheDocument()
    const edited = transport.callsTo(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${NEW_RULE_ID}`,
    )[0]
    expect(edited?.headers.get('If-Match')).toBe('W/"2"')

    // Adds an amount rule alongside it.
    await user.click(screen.getByRole('button', { name: 'Add a discount rule' }))
    const secondAddForm = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(secondAddForm.getByLabelText('Code'), 'CLEARANCE_FLAT')
    await user.type(secondAddForm.getByLabelText('Description'), 'Clearance flat discount')
    await user.click(secondAddForm.getByLabelText('A fixed amount off the line'))
    await user.type(secondAddForm.getByLabelText('Maximum without approval'), '50')
    await user.type(secondAddForm.getByLabelText('Maximum'), '150')
    await user.click(secondAddForm.getByLabelText('Active'))
    await user.click(secondAddForm.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved the discount rule CLEARANCE_FLAT.')).toBeInTheDocument()
    expect(screen.getByText('₹50.00')).toBeInTheDocument()
    expect(screen.getByText('₹150.00')).toBeInTheDocument()

    // Removes the first rule.
    await user.click(screen.getByRole('button', { name: 'Remove FESTIVE10' }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'Season ended.')
    await user.click(dialog.getByRole('button', { name: 'Remove FESTIVE10' }))

    expect(await screen.findByText('Removed the discount rule FESTIVE10.')).toBeInTheDocument()
    expect(screen.queryByText('FESTIVE10')).not.toBeInTheDocument()
    expect(screen.getByText('CLEARANCE_FLAT')).toBeInTheDocument()
  }, 30000)

  it('sends the request rather than pre-empting the bounds rule', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      versionedJson(aDiscountRule(), 'W/"2"', 201),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'FESTIVE10')
    await user.type(form.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      form.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    // The bounds are typed the wrong way round on purpose: the client must not pre-empt this.
    await user.type(form.getByLabelText('Maximum without approval'), '30')
    await user.type(form.getByLabelText('Maximum'), '20')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`),
      ).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`)[0]
    expect(sent?.body).toMatchObject({ maximumWithoutApproval: '30', maximum: '20' })
  })

  it('refuses a maximum below the approval bound in words, keeping typed values and reusing the same idempotency key on retry', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      problemResponse(400, 'billing.discount-bounds-not-ordered', {
        errors: { maximumWithoutApproval: ['Not ordered.'] },
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'FESTIVE10')
    await user.type(form.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      form.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    await user.type(form.getByLabelText('Maximum without approval'), '30')
    await user.type(form.getByLabelText('Maximum'), '20')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'The maximum without approval cannot be more than the maximum anybody may give.',
      ),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Maximum without approval')).toHaveAttribute('aria-invalid', 'true')
    expect(form.getByLabelText('Code')).toHaveValue('FESTIVE10')
    expect(form.getByLabelText('Maximum without approval')).toHaveValue('30')
    expect(form.getByLabelText('Maximum')).toHaveValue('20')

    await user.click(form.getByRole('button', { name: 'Save' }))
    await waitFor(() => {
      expect(
        transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`),
      ).toHaveLength(2)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`)
    expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
  })

  it('refuses a rule with no bound, naming the field, and sends no value the person did not type', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      problemResponse(400, 'billing.amount-not-well-formed', {
        errors: { maximum: ['Not well formed.'] },
      }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'CLEARANCE_FLAT')
    await user.type(form.getByLabelText('Description'), 'Clearance flat discount')
    await user.click(form.getByLabelText('A fixed amount off the line'))
    await user.type(form.getByLabelText('Maximum without approval'), '50')
    // Maximum is deliberately left blank.
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'An amount is a non-negative value in rupees with at most two decimal places.',
      ),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Maximum')).toHaveAttribute('aria-invalid', 'true')

    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`)[0]
    expect((sent?.body as { maximum: string | null }).maximum).toBeNull()
  })

  it('refuses a rule with no active choice, sending no request the person did not author', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'FESTIVE10')
    await user.type(form.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      form.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    await user.type(form.getByLabelText('Maximum without approval'), '10')
    await user.type(form.getByLabelText('Maximum'), '20')
    // Active is deliberately left unanswered.
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('This is required.')).toBeInTheDocument()
    expect(
      transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`),
    ).toHaveLength(0)
  })

  it('sends every field on a whole-value write, with no value invented for what was left blank', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      versionedJson(aDiscountRule(), 'W/"2"', 201),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'FESTIVE10')
    await user.type(form.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      form.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    await user.type(form.getByLabelText('Maximum without approval'), '10')
    await user.type(form.getByLabelText('Maximum'), '20')
    await user.click(form.getByLabelText('Active'))
    // The note is deliberately left blank.
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`),
      ).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`)[0]
    expect(sent?.body).toEqual({
      code: 'FESTIVE10',
      description: 'Festive season discount',
      kind: 'Percentage',
      maximumWithoutApproval: '10',
      maximum: '20',
      active: true,
      reason: null,
    })
  })

  it('refuses a duplicate rule code in words', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [aDiscountRule()],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      problemResponse(400, 'billing.code-not-unique', { errors: { code: ['Already used.'] } }),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'FESTIVE10')
    await user.type(form.getByLabelText('Description'), 'Another festive discount')
    await user.click(form.getByLabelText('A fixed amount off the line'))
    await user.type(form.getByLabelText('Maximum without approval'), '50')
    await user.type(form.getByLabelText('Maximum'), '100')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('That code is already used. Choose a different one.'),
    ).toBeInTheDocument()
    expect(form.getByLabelText('Code')).toHaveValue('FESTIVE10')
  })

  it('offers a re-read after a stale tag, rather than overwriting silently', async () => {
    const user = userEvent.setup()
    const existing = aDiscountRule()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [existing],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${existing.discountRuleId}`,
      () => problemResponse(412, 'billing.version-changed'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Edit ${existing.code}` }))
    const form = within(
      await screen.findByRole('form', { name: `Edit the discount rule ${existing.code}` }),
    )
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Someone else changed this version while it was open here. Read it again to see what changed.',
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Read it again' }))

    await waitFor(() => {
      expect(transport.callsTo(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`)).toHaveLength(2)
    })
  })

  it('does not retry against the stale tag while the re-read it triggered is still in flight', async () => {
    const user = userEvent.setup()
    const existing = aDiscountRule()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [existing],
    })
    let getCount = 0
    // A plain `let` closed over inside the responder below defeats TypeScript's narrowing (it
    // proves the variable can never be reassigned before its later, optional call, which is wrong
    // at runtime); a mutable holder object sidesteps that.
    const secondGet: { resolve: ((response: Response) => void) | null } = { resolve: null }
    // `useAdminResource.reload()` keeps the stale version on screen until this resolves — the
    // point of this test is that a retry in that window must not fire against it.
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () => {
      getCount += 1
      if (getCount === 1) {
        return versionedJson(draft, 'W/"1"')
      }
      return new Promise<Response>((resolve) => {
        secondGet.resolve = resolve
      })
    })
    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${existing.discountRuleId}`,
      () => problemResponse(412, 'billing.version-changed'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Edit ${existing.code}` }))
    const form = within(
      await screen.findByRole('form', { name: `Edit the discount rule ${existing.code}` }),
    )
    await user.click(form.getByRole('button', { name: 'Save' }))

    await screen.findByText(
      'Someone else changed this version while it was open here. Read it again to see what changed.',
    )

    await user.click(screen.getByRole('button', { name: 'Read it again' }))
    await waitFor(() => {
      expect(transport.callsTo(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`)).toHaveLength(2)
    })

    // The re-read has not landed yet: retrying now must be refused locally, not sent.
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'Somebody else changed this while you were working on it. Look at it again before you try.',
      ),
    ).toBeInTheDocument()
    expect(
      transport.callsTo(
        `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${existing.discountRuleId}`,
      ),
    ).toHaveLength(1)

    secondGet.resolve?.(versionedJson(draft, 'W/"2"'))
  })

  it('carries the tag from a 204 removal into the next write', async () => {
    const user = userEvent.setup()
    const first = aDiscountRule()
    const second = aDiscountRule({ discountRuleId: SECOND_RULE_ID, code: 'CLEARANCE_FLAT' })
    let rules: readonly DiscountRule[] = [first, second]
    const version = aPriceListVersionSummary({
      priceListVersionId: DRAFT_ID,
      status: 'Draft',
      overrideThresholdPercent: 7,
    })

    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson({ version, items: [], discountRules: rules }, 'W/"1"'),
    )
    transport.route(
      `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${first.discountRuleId}/delete`,
      () => {
        rules = [second]
        return versionedJson(null, 'W/"2"', 204)
      },
    )
    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${second.discountRuleId}`,
      () => versionedJson({ ...second, active: false }, 'W/"3"'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Remove ${first.code}` }))
    const dialog = within(await screen.findByRole('dialog'))
    await user.type(dialog.getByLabelText('Reason'), 'No longer offered.')
    await user.click(dialog.getByRole('button', { name: `Remove ${first.code}` }))

    await waitFor(() => {
      expect(
        transport.callsTo(
          `POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${first.discountRuleId}/delete`,
        ),
      ).toHaveLength(1)
    })

    await user.click(await screen.findByRole('button', { name: `Edit ${second.code}` }))
    const form = within(
      await screen.findByRole('form', { name: `Edit the discount rule ${second.code}` }),
    )
    await user.click(form.getByLabelText('Inactive'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(
          `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${second.discountRuleId}`,
        ),
      ).toHaveLength(1)
    })
    const sent = transport.callsTo(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules/${second.discountRuleId}`,
    )[0]
    expect(sent?.headers.get('If-Match')).toBe('W/"2"')
  })

  it('carries the tag from an item write into a rule write', async () => {
    const user = userEvent.setup()
    const item = aPriceListItem()
    const version = aPriceListVersionSummary({
      priceListVersionId: DRAFT_ID,
      status: 'Draft',
      overrideThresholdPercent: 7,
    })

    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson({ version, items: [item], discountRules: [] }, 'W/"1"'),
    )
    transport.route(
      `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${item.priceListItemId}`,
      () => versionedJson({ ...item, active: false }, 'W/"2"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      versionedJson(aDiscountRule(), 'W/"3"', 201),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: `Edit ${item.code}` }))
    const itemForm = within(await screen.findByRole('form', { name: `Edit the item ${item.code}` }))
    await user.click(itemForm.getByLabelText('Inactive'))
    await user.click(itemForm.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(
          `PUT ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/items/${item.priceListItemId}`,
        ),
      ).toHaveLength(1)
    })

    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const ruleForm = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(ruleForm.getByLabelText('Code'), 'FESTIVE10')
    await user.type(ruleForm.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      ruleForm.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    await user.type(ruleForm.getByLabelText('Maximum without approval'), '10')
    await user.type(ruleForm.getByLabelText('Maximum'), '20')
    await user.click(ruleForm.getByLabelText('Active'))
    await user.click(ruleForm.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(
        transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`),
      ).toHaveLength(1)
    })
    const sent = transport.callsTo(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`)[0]
    expect(sent?.headers.get('If-Match')).toBe('W/"2"')
  })

  it('names the unit of each bound: a percentage rule’s render through formatPercent, an amount rule’s through formatMoney', async () => {
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [
        aDiscountRule(),
        aDiscountRule({
          discountRuleId: SECOND_RULE_ID,
          code: 'CLEARANCE_FLAT',
          kind: 'Amount',
          maximumWithoutApproval: 50,
          maximum: 150,
        }),
      ],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    renderEditorAt(DRAFT_ID)
    expect(await screen.findByText('FESTIVE10')).toBeInTheDocument()

    // The percentage rule's bounds read as percentages.
    expect(screen.getByText('10%')).toBeInTheDocument()
    expect(screen.getByText('20%')).toBeInTheDocument()
    // The amount rule's bounds read as rupee amounts — the same typed "50"/"150" as money, not per cent.
    expect(screen.getByText('₹50.00')).toBeInTheDocument()
    expect(screen.getByText('₹150.00')).toBeInTheDocument()
  })

  it('offers no rule control on a published version, but still shows the bounds', async () => {
    const published = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: PRICE_LIST_VERSION_ID,
        status: 'Published',
        overrideThresholdPercent: 7,
      }),
      discountRules: [aDiscountRule()],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`, () =>
      versionedJson(published, 'W/"1"'),
    )

    renderEditorAt(PRICE_LIST_VERSION_ID)

    expect(await screen.findByText('FESTIVE10')).toBeInTheDocument()
    expect(screen.getByText('10%')).toBeInTheDocument()
    expect(screen.getByText('20%')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add a discount rule' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit FESTIVE10' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove FESTIVE10' })).not.toBeInTheDocument()
  })

  it('shows a sentence without the manage key, and makes no request', async () => {
    renderEditorAt(PRICE_LIST_VERSION_ID, [])

    expect(await screen.findByText('You do not have access to this')).toBeInTheDocument()
    expect(
      transport.callsTo(`GET ${PRICE_LIST_VERSION_BASE}/${PRICE_LIST_VERSION_ID}`),
    ).toHaveLength(0)
  })

  it("handles the server's own refusal when the claim list is stale", async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )
    transport.route(`POST ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}/discount-rules`, () =>
      problemResponse(403, 'security.forbidden'),
    )

    renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))
    await user.type(form.getByLabelText('Code'), 'FESTIVE10')
    await user.type(form.getByLabelText('Description'), 'Festive season discount')
    await user.click(
      form.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    )
    await user.type(form.getByLabelText('Maximum without approval'), '10')
    await user.type(form.getByLabelText('Maximum'), '20')
    await user.click(form.getByLabelText('Active'))
    await user.click(form.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText('This did not go through, and the reason is not clear.'),
    ).toBeInTheDocument()
  })

  it('blocks every rule control while offline, without queuing anything', async () => {
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [aDiscountRule()],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    renderEditorAt(DRAFT_ID)
    await screen.findByText('FESTIVE10')

    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    expect(
      await screen.findAllByText('Needs connection — this will not be queued'),
    ).not.toHaveLength(0)
    expect(screen.queryByRole('button', { name: 'Add a discount rule' })).not.toBeInTheDocument()
    // A row's own Edit/Remove must not stay clickable only to be refused once the form opens.
    expect(screen.queryByRole('button', { name: 'Edit FESTIVE10' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove FESTIVE10' })).not.toBeInTheDocument()

    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('has no accessibility violations adding a percentage rule', async () => {
    const user = userEvent.setup()
    const draft = aPriceListVersion({
      version: aPriceListVersionSummary({
        priceListVersionId: DRAFT_ID,
        status: 'Draft',
        overrideThresholdPercent: 7,
      }),
      items: [],
      discountRules: [],
    })
    transport.route(`GET ${PRICE_LIST_VERSION_BASE}/${DRAFT_ID}`, () =>
      versionedJson(draft, 'W/"1"'),
    )

    const { container } = renderEditorAt(DRAFT_ID)
    await user.click(await screen.findByRole('button', { name: 'Add a discount rule' }))
    const form = within(await screen.findByRole('form', { name: 'Add a discount rule' }))

    expect(form.getByLabelText('Maximum without approval')).toHaveValue('')
    expect(form.getByLabelText('Maximum')).toHaveValue('')
    expect(
      form.getByLabelText(
        'A percentage of the line’s gross amount: its base and its surcharges together',
      ),
    ).not.toBeChecked()
    expect(form.getByLabelText('A fixed amount off the line')).not.toBeChecked()
    expect(form.getByLabelText('Active')).not.toBeChecked()
    expect(form.getByLabelText('Inactive')).not.toBeChecked()

    await expectNoAccessibilityViolations(container)
  })
})
