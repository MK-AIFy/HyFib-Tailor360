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
import { versionedResponse } from '../../admin/testing/fixtures'
import { RequirePermission } from '../../admin/RequirePermission'
import { ShellStatusProvider } from '../../components/layout/ShellStatusProvider'
import { MEASUREMENT_PERMISSIONS } from '../../measurements/measurementsPermissions'
import {
  DRAFT_ID,
  aCaptureTemplate,
  aCustomerCard,
  aMeasurementCheck,
  aMeasurementDraft,
  aMeasurementVersion,
  anOrderableCatalog,
  anOrderableService,
} from '../../measurements/testing/fixtures'
import { MeasurementDraftRoute } from './MeasurementDraftRoute'
import { MeasurementStartRoute } from './MeasurementStartRoute'

let transport: FetchStub

const DRAFTS = '/api/v1/customers/measurement-drafts'
const DRAFT = `${DRAFTS}/${DRAFT_ID}`
const COUNTER = [
  MEASUREMENT_PERMISSIONS.capture,
  MEASUREMENT_PERMISSIONS.customersRead,
  MEASUREMENT_PERMISSIONS.catalogRead,
]

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: COUNTER })))
  transport.route('GET /api/v1/catalog/current', () => jsonResponse(anOrderableCatalog()))
  transport.route(`GET ${DRAFT}`, () => versionedResponse(aMeasurementDraft(), 'W/"1"'))
  transport.route(`GET ${DRAFT}/template`, () => jsonResponse(aCaptureTemplate()))
  transport.route(`GET ${DRAFT}/check`, () => jsonResponse(aMeasurementCheck()))
  transport.route(`POST ${DRAFT}/sections`, () =>
    versionedResponse(aMeasurementDraft({ updatedAt: '2026-09-11T04:05:00.000Z' }), 'W/"2"'),
  )
  transport.route(`POST ${DRAFT}/confirm`, () => jsonResponse(aMeasurementVersion(), 201))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderAt(path: string) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <ShellStatusProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route element={<RequireSession />}>
                <Route
                  path="/measurements/new"
                  element={
                    <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                      <MeasurementStartRoute />
                    </RequirePermission>
                  }
                />
                <Route
                  path="/measurements/drafts/:draftId"
                  element={
                    <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                      <MeasurementDraftRoute />
                    </RequirePermission>
                  }
                />
                <Route path="/measurements" element={<h1>Measurements home</h1>} />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** Waits for the wizard to have read the draft and the template it is pinned to. */
async function openWizard() {
  renderAt(`/measurements/drafts/${DRAFT_ID}`)
  await screen.findByRole('heading', { name: 'Bodice' })
}

/** Enters 36 1/2 in for the chest and chooses the closure, which is everything the bodice needs. */
async function fillBodice(user: ReturnType<typeof userEvent.setup>) {
  await user.clear(screen.getByLabelText('Chest / bust — whole inches'))
  await user.type(screen.getByLabelText('Chest / bust — whole inches'), '36')
  await user.click(screen.getByRole('radio', { name: '1/2' }))
  await user.selectOptions(screen.getByLabelText('Closure'), 'front_hooks')
}

describe('starting a measurement', () => {
  it('finds the customer, chooses the garment and opens the draft the server hands back', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/customers/?term=Asha', () =>
      jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
    )
    transport.route(`POST ${DRAFTS}`, () => versionedResponse(aMeasurementDraft(), 'W/"1"', 201))

    renderAt('/measurements/new')

    await user.type(await screen.findByLabelText('Find the customer'), 'Asha')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    await user.click(
      await screen.findByRole('radio', { name: 'Asha Example · C-000123 · ••••••4321' }),
    )
    await user.selectOptions(screen.getByLabelText('Garment'), 'Pattern work — Blouse')
    await user.click(screen.getByRole('button', { name: 'Start measuring' }))

    await screen.findByRole('heading', { name: 'Bodice' })

    const [started] = transport.callsTo(`POST ${DRAFTS}`)
    expect(started?.body).toEqual({
      customerId: aCustomerCard().customerId,
      measurementTemplateId: aCaptureTemplate().measurementTemplateId,
      reuseFromVersionId: null,
    })
    expect(started?.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
  })

  it('searches on Enter rather than starting, which is what the keyboard’s Search key does', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/customers/?term=Asha', () =>
      jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
    )
    renderAt('/measurements/new')

    await user.type(await screen.findByLabelText('Find the customer'), 'Asha{Enter}')

    await screen.findByRole('radio', { name: 'Asha Example · C-000123 · ••••••4321' })
    expect(transport.callsTo(`POST ${DRAFTS}`)).toHaveLength(0)
    expect(
      screen.queryByText('Choose a customer and a garment before starting.'),
    ).not.toBeInTheDocument()
  })

  it('forgets a customer chosen from a list that is no longer on screen', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/customers/?term=Asha', () =>
      jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
    )
    transport.route('GET /api/v1/customers/?term=Ravi', () =>
      jsonResponse({ customers: [], nextCursor: null }),
    )
    renderAt('/measurements/new')

    const term = await screen.findByLabelText('Find the customer')
    await user.type(term, 'Asha{Enter}')
    await user.click(
      await screen.findByRole('radio', { name: 'Asha Example · C-000123 · ••••••4321' }),
    )
    await user.clear(term)
    await user.type(term, 'Ravi{Enter}')
    await screen.findByText(
      'No customer matches. Check the spelling, or register the customer at the counter first.',
    )

    await user.selectOptions(screen.getByLabelText('Garment'), 'Pattern work — Blouse')
    await user.click(screen.getByRole('button', { name: 'Start measuring' }))

    // The person chosen from the first list is not started for: nothing on screen names them.
    expect(screen.getByText('Choose a customer and a garment before starting.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${DRAFTS}`)).toHaveLength(0)
  })

  it('says when more customers match than are shown, rather than showing page one as everyone', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/customers/?term=Asha', () =>
      jsonResponse({ customers: [aCustomerCard()], nextCursor: 'page-2' }),
    )
    renderAt('/measurements/new')

    await user.type(await screen.findByLabelText('Find the customer'), 'Asha{Enter}')

    expect(
      await screen.findByText(/More customers match than are shown\. Narrow the search/),
    ).toBeInTheDocument()
  })

  it('refuses to start until both the customer and the garment are chosen', async () => {
    const user = userEvent.setup()
    renderAt('/measurements/new')

    await user.click(await screen.findByRole('button', { name: 'Start measuring' }))

    expect(screen.getByText('Choose a customer and a garment before starting.')).toBeInTheDocument()
    expect(transport.callsTo(`POST ${DRAFTS}`)).toHaveLength(0)
  })

  it('says a short term will not be searched rather than searching for everything', async () => {
    const user = userEvent.setup()
    renderAt('/measurements/new')

    await user.type(await screen.findByLabelText('Find the customer'), 'As')
    await user.click(screen.getByRole('button', { name: 'Search' }))

    expect(screen.getByText('Type at least 3 characters, then search.')).toBeInTheDocument()
    expect(transport.calls.filter((call) => call.path.startsWith('/api/v1/customers/?'))).toEqual(
      [],
    )
  })

  it('says what to do when nothing the branch offers is measured against a template', async () => {
    transport.route('GET /api/v1/catalog/current', () =>
      jsonResponse(
        anOrderableCatalog({ services: [anOrderableService({ measurementTemplateId: null })] }),
      ),
    )
    renderAt('/measurements/new')

    expect(
      await screen.findByRole('heading', { name: 'Nothing to measure against yet' }),
    ).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt('/measurements/new')
    await screen.findByLabelText('Find the customer')
    await expectNoAccessibilityViolations(container)
  })
})

describe('the capture wizard', () => {
  it('says which template and version is open, and lists the steps in order', async () => {
    await openWizard()

    expect(
      screen.getByText('Blouse, pattern work — version 2, published 05-09-2026.'),
    ).toBeInTheDocument()
    const steps = within(screen.getByRole('navigation', { name: 'Steps' })).getAllByRole('listitem')
    expect(steps.map((step) => step.textContent)).toEqual(['1. Bodice', '2. Sleeve', '3. Review'])
    expect(steps[0]).toHaveAttribute('aria-current', 'step')
  })

  it('saves the step against the tag it read, moves on, and confirms with a held key', async () => {
    const user = userEvent.setup()
    await openWizard()
    await fillBodice(user)

    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('heading', { name: 'Sleeve' })

    const [saved] = transport.callsTo(`POST ${DRAFT}/sections`)
    expect(saved?.headers.get('If-Match')).toBe('W/"1"')
    expect(saved?.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    expect(saved?.body).toEqual({
      groupName: 'Bodice',
      values: [
        { key: 'chest_bust', entered: 36.5, unit: 'Inch', choice: null, acknowledged: false },
        { key: 'closure', entered: null, unit: null, choice: 'front_hooks', acknowledged: false },
      ],
    })

    // The sleeve is optional, so the step may be left empty.
    await user.click(screen.getByRole('button', { name: 'Review' }))
    await screen.findByRole('heading', { name: 'Review and confirm' })
    expect(screen.getByText('36 1/2 in')).toBeInTheDocument()
    expect(screen.getByText('Front hooks')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Confirm measurements' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Confirm measurements' }))

    expect(await screen.findByText('Measurements confirmed')).toBeInTheDocument()

    const [confirmed] = transport.callsTo(`POST ${DRAFT}/confirm`)
    // The tag the last save answered with, not the one the screen was opened on.
    expect(confirmed?.headers.get('If-Match')).toBe('W/"2"')
    expect(confirmed?.body).toEqual({ reason: null, correctsVersionId: null })
    expect(transport.callsTo(`GET ${DRAFT}/check`)).toHaveLength(1)
  })

  it('cannot move on with a required field empty: the summary names each one and moves focus', async () => {
    const user = userEvent.setup()
    await openWizard()

    await user.click(screen.getByRole('button', { name: 'Next' }))

    const summary = screen.getByRole('group', { name: 'There are 2 problems' })
    expect(summary).toHaveFocus()
    expect(transport.callsTo(`POST ${DRAFT}/sections`)).toHaveLength(0)

    await user.click(within(summary).getByRole('button', { name: 'Chest / bust is required.' }))
    expect(screen.getByLabelText('Chest / bust — whole inches')).toHaveFocus()
  })

  it('cannot move on with a value outside the hard bounds, and quotes them in the unit on screen', async () => {
    const user = userEvent.setup()
    await openWizard()

    await user.type(screen.getByLabelText('Chest / bust — whole inches'), '300')
    await user.selectOptions(screen.getByLabelText('Closure'), 'back_hooks')
    await user.click(screen.getByRole('button', { name: 'Next' }))

    expect(screen.getByRole('group', { name: 'There is a problem' })).toHaveFocus()
    expect(
      screen.getByRole('button', { name: 'Chest / bust must be between 3 7/8 in and 78 3/4 in.' }),
    ).toBeInTheDocument()
    expect(transport.callsTo(`POST ${DRAFT}/sections`)).toHaveLength(0)
  })

  it('carries the server’s own findings into the summary and opens the step they belong to', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${DRAFT}/check`, () =>
      jsonResponse(
        aMeasurementCheck({
          confirmable: false,
          findings: [
            {
              code: 'measurements.value-needs-acknowledgement',
              field: 'sleeve_length',
              message: 'Sleeve length is unusual and has not been checked.',
            },
          ],
        }),
      ),
    )
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await user.click(await screen.findByRole('button', { name: 'Confirm measurements' }))
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', {
        name: 'Confirm measurements',
      }),
    )

    expect(await screen.findByRole('heading', { name: 'Sleeve' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Sleeve length is unusual and has not been checked.' }),
    ).toBeInTheDocument()
    expect(transport.callsTo(`POST ${DRAFT}/confirm`)).toHaveLength(0)
  })

  it('says in words when somebody else saved the step first, and re-reads on request', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${DRAFT}/sections`, () =>
      problemResponse(409, 'measurements.draft-changed'),
    )
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))

    expect(await screen.findByText('Somebody else saved this step first')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Bodice' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Reload' }))
    await waitFor(() => {
      expect(transport.callsTo(`GET ${DRAFT}`)).toHaveLength(2)
    })
  })

  it('re-reads after a conflict and saves against the tag the re-read carried, not the stale one', async () => {
    const user = userEvent.setup()
    let reads = 0
    transport.route(`GET ${DRAFT}`, () => {
      reads += 1
      return versionedResponse(aMeasurementDraft(), reads === 1 ? 'W/"1"' : 'W/"9"')
    })
    let saves = 0
    transport.route(`POST ${DRAFT}/sections`, () => {
      saves += 1
      return saves === 1
        ? problemResponse(409, 'measurements.draft-changed')
        : versionedResponse(aMeasurementDraft(), 'W/"10"')
    })
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Somebody else saved this step first')

    await user.click(screen.getByRole('button', { name: 'Reload' }))
    // The wizard remounts on the tag the re-read carried, so the conflict and the typed values go
    // with the stale draft, and the fields show what the colleague saved.
    await waitFor(() => {
      expect(screen.queryByText('Somebody else saved this step first')).not.toBeInTheDocument()
    })
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('heading', { name: 'Sleeve' })

    const [, second] = transport.callsTo(`POST ${DRAFT}/sections`)
    expect(second?.headers.get('If-Match')).toBe('W/"9"')
  })

  it('saves a step left through a summary link before confirming, so the record holds what the review showed', async () => {
    const user = userEvent.setup()
    let checks = 0
    transport.route(`GET ${DRAFT}/check`, () => {
      checks += 1
      return jsonResponse(
        checks === 1
          ? aMeasurementCheck({
              confirmable: false,
              findings: [
                {
                  code: 'measurements.value-out-of-bounds',
                  field: 'chest_bust',
                  message: 'Chest / bust is outside what this field can hold.',
                },
                {
                  code: 'measurements.value-needs-acknowledgement',
                  field: 'sleeve_length',
                  message: 'Sleeve length is unusual and has not been checked.',
                },
              ],
            })
          : aMeasurementCheck(),
      )
    })
    let saves = 0
    transport.route(`POST ${DRAFT}/sections`, () => {
      saves += 1
      return versionedResponse(aMeasurementDraft(), `W/"${String(saves + 1)}"`)
    })
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await user.click(await screen.findByRole('button', { name: 'Confirm measurements' }))
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', {
        name: 'Confirm measurements',
      }),
    )

    // Two findings; the first opens the bodice. The person corrects the chest, then follows the
    // second finding's link to the sleeve — without pressing Next, so the bodice is unsaved.
    await screen.findByRole('heading', { name: 'Bodice' })
    await user.clear(screen.getByLabelText('Chest / bust — whole inches'))
    await user.type(screen.getByLabelText('Chest / bust — whole inches'), '38')
    await user.click(screen.getByRole('radio', { name: '1/2' }))
    await user.click(
      screen.getByRole('button', { name: /Sleeve length is unusual and has not been checked/ }),
    )
    await screen.findByRole('heading', { name: 'Sleeve' })
    await user.click(screen.getByRole('button', { name: 'Review' }))
    await screen.findByRole('heading', { name: 'Review and confirm' })
    expect(screen.getByText('38 1/2 in')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Confirm measurements' }))
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', {
        name: 'Confirm measurements',
      }),
    )
    await screen.findByText('Measurements confirmed')

    // Bodice was saved twice: once on the first Next, and again — with the corrected chest and
    // against the tag the last save answered — before the confirmation.
    const bodiceSaves = transport
      .callsTo(`POST ${DRAFT}/sections`)
      .filter((call) => (call.body as { groupName: string }).groupName === 'Bodice')
    expect(bodiceSaves).toHaveLength(2)
    const last = bodiceSaves[1]
    expect((last?.body as { values: { entered: number }[] }).values[0]?.entered).toBe(38.5)
    expect(last?.headers.get('If-Match')).toBe('W/"2"')
    const [confirmed] = transport.callsTo(`POST ${DRAFT}/confirm`)
    expect(confirmed?.headers.get('If-Match')).toBe('W/"3"')
  })

  it('says an expired draft has expired when a save finds out, in the shop’s words', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${DRAFT}/sections`, () =>
      problemResponse(409, 'measurements.draft-expired'),
    )
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))

    expect(
      await screen.findByRole('heading', { name: 'This draft has expired' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Measure another customer' })).toBeInTheDocument()
  })

  it('explains a refused confirmation inside the dialog, in the shop’s words, and keeps it open', async () => {
    const user = userEvent.setup()
    transport.route(`POST ${DRAFT}/confirm`, () =>
      problemResponse(409, 'measurements.consent-missing'),
    )
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await user.click(await screen.findByRole('button', { name: 'Confirm measurements' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Confirm measurements' }))

    expect(
      await within(dialog).findByText(
        'The customer has not agreed to their measurements being kept. Record the consent on the customer, then confirm.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('retries an interrupted confirmation with the same key, so it cannot make two versions', async () => {
    const user = userEvent.setup()
    let attempts = 0
    transport.route(`POST ${DRAFT}/confirm`, () => {
      attempts += 1
      return attempts === 1
        ? problemResponse(503, 'platform.unavailable')
        : jsonResponse(aMeasurementVersion(), 201)
    })
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))

    await user.click(await screen.findByRole('button', { name: 'Confirm measurements' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Confirm measurements' }))

    // The dialog stays open with the refusal inside it — it is modal, and an alert behind it would
    // sit under the backdrop — which is also what keeps the retry key for the next attempt.
    await within(dialog).findByText(
      'The shop system could not finish this. It is not something you did wrong.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Confirm measurements' }))
    await screen.findByText('Measurements confirmed')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()

    const keys = transport
      .callsTo(`POST ${DRAFT}/confirm`)
      .map((call) => call.headers.get('Idempotency-Key'))
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
  })

  it('lets a pre-filled value be cleared, and saves the step without it', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${DRAFT}`, () =>
      versionedResponse(
        aMeasurementDraft({
          values: [
            {
              key: 'chest_bust',
              millimetres: 927.1,
              enteredUnit: 'Inch',
              choice: null,
              acknowledged: false,
            },
            {
              key: 'closure',
              millimetres: null,
              enteredUnit: 'Inch',
              choice: 'back_hooks',
              acknowledged: false,
            },
            {
              key: 'sleeve_length',
              millimetres: 500,
              enteredUnit: 'Inch',
              choice: null,
              acknowledged: false,
            },
          ],
        }),
        'W/"4"',
      ),
    )
    await openWizard()
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('heading', { name: 'Sleeve' })

    // Neither numeric control reports an emptied box, so clearing is an act of its own.
    await user.click(screen.getByRole('button', { name: 'Clear Sleeve length' }))
    expect(screen.queryByRole('button', { name: 'Clear Sleeve length' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Review' }))
    await screen.findByRole('heading', { name: 'Review and confirm' })

    const [saved] = transport.callsTo(`POST ${DRAFT}/sections`)
    expect(saved?.body).toEqual({ groupName: 'Sleeve', values: [] })
  })

  it('enters a centimetre-only field in centimetres however the wizard is set', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${DRAFT}/template`, () => {
      const template = aCaptureTemplate()
      return jsonResponse({
        ...template,
        version: {
          ...template.version,
          fields: (template.version.fields ?? []).map((field) =>
            field.key === 'sleeve_length'
              ? { ...field, inchFraction: 0, centimetreDecimals: 1, displayUnits: ['Centimetre'] }
              : field,
          ),
        },
      })
    })
    await openWizard()
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByRole('heading', { name: 'Sleeve' })

    // Inches are chosen for the wizard; this field offers only centimetres, so it is a decimal
    // box with a centimetre adornment, and the request says Centimetre.
    expect(screen.queryByLabelText('Sleeve length — whole inches')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Sleeve length'), '50')
    await user.click(screen.getByRole('button', { name: 'Review' }))
    await screen.findByRole('heading', { name: 'Review and confirm' })

    const [, sleeve] = transport.callsTo(`POST ${DRAFT}/sections`)
    expect(sleeve?.body).toEqual({
      groupName: 'Sleeve',
      values: [
        {
          key: 'sleeve_length',
          entered: 50,
          unit: 'Centimetre',
          choice: null,
          acknowledged: false,
        },
      ],
    })
    expect(screen.getByText('50.0 cm')).toBeInTheDocument()
  })

  it('saves a step whose field another step’s answer hid, so the record does not keep it', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${DRAFT}/template`, () => {
      const template = aCaptureTemplate()
      return jsonResponse({
        ...template,
        version: {
          ...template.version,
          fields: (template.version.fields ?? []).map((field) =>
            field.key === 'sleeve_length'
              ? {
                  ...field,
                  ruleDefinition: {
                    effect: 'HiddenWhen',
                    anyOf: [
                      {
                        scope: 'Field',
                        name: 'closure',
                        operator: 'IsAnyOf',
                        values: ['front_hooks'],
                      },
                    ],
                  },
                }
              : field,
          ),
        },
      })
    })
    transport.route(`GET ${DRAFT}`, () =>
      versionedResponse(
        aMeasurementDraft({
          values: [
            {
              key: 'sleeve_length',
              millimetres: 500,
              enteredUnit: 'Inch',
              choice: null,
              acknowledged: false,
            },
          ],
        }),
        'W/"4"',
      ),
    )
    await openWizard()
    // Front hooks hide the sleeve length that an earlier session had saved.
    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('Sleeve length is not asked for with these answers.')
    await user.click(screen.getByRole('button', { name: 'Review' }))
    await screen.findByRole('heading', { name: 'Review and confirm' })

    // The sleeve step was saved although nobody typed in it, and without the hidden value.
    const sleeveSaves = transport
      .callsTo(`POST ${DRAFT}/sections`)
      .filter((call) => (call.body as { groupName: string }).groupName === 'Sleeve')
    expect(sleeveSaves).toHaveLength(1)
    expect(sleeveSaves[0]?.body).toEqual({ groupName: 'Sleeve', values: [] })
    expect(screen.getByText('Not asked for')).toBeInTheDocument()
  })

  it('states that a hidden field is not asked for, rather than leaving a gap', async () => {
    transport.route(`GET ${DRAFT}/template`, () => {
      const template = aCaptureTemplate()
      const fields = template.version.fields ?? []
      return jsonResponse({
        ...template,
        version: {
          ...template.version,
          fields: fields.map((field) =>
            field.key === 'closure'
              ? {
                  ...field,
                  ruleDefinition: {
                    effect: 'ShownWhen',
                    anyOf: [
                      {
                        scope: 'Field',
                        name: 'sleeve_length',
                        operator: 'IsAnyOf',
                        values: ['1'],
                      },
                    ],
                  },
                }
              : field,
          ),
        },
      })
    })
    await openWizard()

    // The rule reads a field nobody has answered: undecidable, so the field is shown and says why.
    expect(
      screen.getByText(/Closure is asked for because its rule cannot be settled here/),
    ).toBeInTheDocument()
  })

  it('shows a closed draft as already confirmed, with the way onward', async () => {
    transport.route(`GET ${DRAFT}`, () =>
      versionedResponse(aMeasurementDraft({ consumedAt: '2026-09-11T04:30:00.000Z' }), 'W/"3"'),
    )
    renderAt(`/measurements/drafts/${DRAFT_ID}`)

    expect(
      await screen.findByRole('heading', { name: 'These measurements are already confirmed' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Measure another customer' })).toBeInTheDocument()
  })

  it('says an expired draft has expired, and where to start again', async () => {
    transport.route(`GET ${DRAFT}`, () => problemResponse(410, 'measurements.draft-expired'))
    transport.route(`GET ${DRAFT}/template`, () =>
      problemResponse(410, 'measurements.draft-expired'),
    )
    renderAt(`/measurements/drafts/${DRAFT_ID}`)

    expect(
      await screen.findByRole('heading', { name: 'This draft has expired' }),
    ).toBeInTheDocument()
  })

  it('blocks saving while offline, keeps what was typed, and says it will not be queued', async () => {
    const user = userEvent.setup()
    await openWizard()
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    window.dispatchEvent(new Event('offline'))

    await user.type(screen.getByLabelText('Chest / bust — whole inches'), '36')

    expect(
      await screen.findByText('Needs connection — this will not be queued'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Chest / bust — whole inches')).toHaveValue('36')

    // The connection state is a module-level store shared by every test after this one.
    vi.restoreAllMocks()
    window.dispatchEvent(new Event('online'))
  })

  it('tells somebody without the permission so in a sentence, never a redirect', async () => {
    transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: [] })))
    renderAt(`/measurements/drafts/${DRAFT_ID}`)

    expect(
      await screen.findByRole('heading', { name: 'You do not have access to this' }),
    ).toBeInTheDocument()
    expect(transport.callsTo(`GET ${DRAFT}`)).toHaveLength(0)
  })

  it('has no accessibility violations on a step and on the review', async () => {
    const user = userEvent.setup()
    const { container } = renderAt(`/measurements/drafts/${DRAFT_ID}`)
    await screen.findByRole('heading', { name: 'Bodice' })
    await expectNoAccessibilityViolations(container)

    await fillBodice(user)
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await screen.findByRole('heading', { name: 'Review and confirm' })
    await expectNoAccessibilityViolations(container)
  })
})
