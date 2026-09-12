import { render, screen, within } from '@testing-library/react'
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
  CUSTOMER_ID,
  DRAFT_ID,
  TEMPLATE_ID,
  VERSION_ONE_ID,
  VERSION_TWO_ID,
  aCaptureTemplate,
  aCustomerCard,
  aMeasurementCheck,
  aMeasurementComparison,
  aMeasurementDraft,
  aMeasurementSheet,
  aMeasurementSummary,
  aMeasurementVersion,
  aMeasurementVersionTemplate,
  anOrderableCatalog,
} from '../../measurements/testing/fixtures'
import { MeasurementCompareRoute } from './MeasurementCompareRoute'
import { MeasurementDraftRoute } from './MeasurementDraftRoute'
import { MeasurementSheetRoute } from './MeasurementSheetRoute'
import { MeasurementStartRoute } from './MeasurementStartRoute'

let transport: FetchStub

const CUSTOMERS = '/api/v1/customers'
const DRAFTS = `${CUSTOMERS}/measurement-drafts`
const DRAFT = `${DRAFTS}/${DRAFT_ID}`
const MEASUREMENTS = `${CUSTOMERS}/measurements`
const LIST = `${CUSTOMERS}/${CUSTOMER_ID}/measurements?templateId=${TEMPLATE_ID}`

const COUNTER = [
  MEASUREMENT_PERMISSIONS.capture,
  MEASUREMENT_PERMISSIONS.customersRead,
  MEASUREMENT_PERMISSIONS.catalogRead,
  MEASUREMENT_PERMISSIONS.readSheet,
]

const NEWER = aMeasurementSummary({
  measurementVersionId: VERSION_TWO_ID,
  versionNumber: 2,
  takenAt: '2026-09-11T06:00:00.000Z',
  takenByName: 'Devi (owner)',
  reusedFromVersionId: VERSION_ONE_ID,
})
const OLDER = aMeasurementSummary()

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () => jsonResponse(aCurrentUser({ permissions: COUNTER })))
  transport.route('GET /api/v1/catalog/current', () => jsonResponse(anOrderableCatalog()))
  transport.route(`GET ${CUSTOMERS}/?term=Asha`, () =>
    jsonResponse({ customers: [aCustomerCard()], nextCursor: null }),
  )
  transport.route(`GET ${LIST}`, () => jsonResponse([NEWER, OLDER]))
  transport.route(`POST ${DRAFTS}`, () => versionedResponse(aMeasurementDraft(), 'W/"1"', 201))
  transport.route(`GET ${DRAFT}`, () => versionedResponse(aMeasurementDraft(), 'W/"1"'))
  transport.route(`GET ${DRAFT}/template`, () => jsonResponse(aCaptureTemplate()))
  transport.route(`GET ${DRAFT}/check`, () => jsonResponse(aMeasurementCheck()))
  transport.route(`POST ${DRAFT}/sections`, () => versionedResponse(aMeasurementDraft(), 'W/"2"'))
  transport.route(`POST ${DRAFT}/confirm`, () =>
    jsonResponse(aMeasurementVersion({ versionNumber: 3, correctsVersionId: VERSION_TWO_ID }), 201),
  )
  transport.route(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}`, () =>
    jsonResponse(aMeasurementVersion({ measurementVersionId: VERSION_TWO_ID, versionNumber: 2 })),
  )
  transport.route(`GET ${MEASUREMENTS}/${VERSION_ONE_ID}/compare/${VERSION_TWO_ID}`, () =>
    jsonResponse(aMeasurementComparison()),
  )
  transport.route(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}/template`, () =>
    jsonResponse(aMeasurementVersionTemplate()),
  )
  transport.route(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}/sheet`, () =>
    jsonResponse(aMeasurementSheet({ measurementVersionId: VERSION_TWO_ID, versionNumber: 2 })),
  )
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
                <Route
                  path="/measurements/compare/:beforeId/:afterId"
                  element={
                    <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                      <MeasurementCompareRoute />
                    </RequirePermission>
                  }
                />
                <Route
                  path="/measurements/:versionId/sheet"
                  element={
                    <RequirePermission permission={MEASUREMENT_PERMISSIONS.readSheet}>
                      <MeasurementSheetRoute />
                    </RequirePermission>
                  }
                />
              </Route>
            </Routes>
          </MemoryRouter>
        </ShellStatusProvider>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** Chooses the customer and the garment, which is what makes the earlier measurements appear. */
async function chooseCustomerAndGarment(user: ReturnType<typeof userEvent.setup>) {
  renderAt('/measurements/new')
  await user.type(await screen.findByLabelText('Find the customer'), 'Asha{Enter}')
  await user.click(
    await screen.findByRole('radio', { name: 'Asha Example · C-000123 · ••••••4321' }),
  )
  await user.selectOptions(screen.getByLabelText('Garment'), 'Pattern work — Blouse')
  await screen.findByRole('heading', { name: 'Earlier measurements for this garment' })
}

describe('reusing an earlier measurement', () => {
  it('lists every earlier measurement with its date and who took it, nothing pre-selected', async () => {
    const user = userEvent.setup()
    await chooseCustomerAndGarment(user)

    const table = screen.getByRole('table', { name: 'Every earlier measurement, newest first' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('Version 2')
    expect(rows[0]).toHaveTextContent('Devi (owner)')
    expect(rows[1]).toHaveTextContent('Version 1')
    expect(rows[1]).toHaveTextContent('Asha (counter)')
    // Nothing has been started and nothing is chosen for the person.
    expect(transport.callsTo(`POST ${DRAFTS}`)).toHaveLength(0)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('cannot reuse without the screen naming the source version, its date and who took it', async () => {
    const user = userEvent.setup()
    await chooseCustomerAndGarment(user)

    await user.click(screen.getByRole('button', { name: 'Reuse version 2' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Reuse version 2?')
    expect(dialog).toHaveTextContent(/pre-filled from version 2, taken .* by Devi \(owner\)/)
    expect(transport.callsTo(`POST ${DRAFTS}`)).toHaveLength(0)

    await user.click(within(dialog).getByRole('button', { name: 'Reuse version 2' }))
    await screen.findByRole('heading', { name: 'Bodice' })

    const [started] = transport.callsTo(`POST ${DRAFTS}`)
    expect(started?.body).toEqual({
      customerId: CUSTOMER_ID,
      measurementTemplateId: TEMPLATE_ID,
      reuseFromVersionId: VERSION_TWO_ID,
    })
  })

  it('abandoning the reuse dialog starts nothing', async () => {
    const user = userEvent.setup()
    await chooseCustomerAndGarment(user)

    await user.click(screen.getByRole('button', { name: 'Reuse version 1' }))
    await user.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }),
    )

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(transport.callsTo(`POST ${DRAFTS}`)).toHaveLength(0)
  })

  it('says there is nothing earlier, rather than looking unfinished', async () => {
    const user = userEvent.setup()
    transport.route(`GET ${LIST}`, () => jsonResponse([]))
    await chooseCustomerAndGarment(user)

    expect(
      screen.getByText(/No earlier measurement of this customer for this garment/),
    ).toBeInTheDocument()
  })
})

describe('correcting a measurement', () => {
  it('says before it starts that a correction creates a new version, then demands a reason to confirm', async () => {
    const user = userEvent.setup()
    await chooseCustomerAndGarment(user)

    await user.click(screen.getByRole('button', { name: 'Correct version 2' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('This does not edit version 2.')
    expect(dialog).toHaveTextContent('version 2 stays readable')
    await user.click(within(dialog).getByRole('button', { name: 'Start the correction' }))

    // The wizard opens as a correction of version 2 and says what confirming will do.
    await screen.findByRole('heading', { name: 'Bodice' })
    expect(screen.getByText(/Correcting version 2, taken/)).toBeInTheDocument()
    const [started] = transport.callsTo(`POST ${DRAFTS}`)
    expect(started?.body).toMatchObject({ reuseFromVersionId: VERSION_TWO_ID })

    await user.type(screen.getByLabelText('Chest / bust — whole inches'), '36')
    await user.selectOptions(screen.getByLabelText('Closure'), 'front_hooks')
    await user.click(screen.getByRole('button', { name: 'Next' }))
    await user.click(await screen.findByRole('button', { name: 'Review' }))
    await user.click(await screen.findByRole('button', { name: 'Confirm measurements' }))

    const confirmDialog = await screen.findByRole('dialog')
    expect(confirmDialog).toHaveTextContent('Confirm the correction?')
    // Without a reason the dialog refuses and nothing is sent.
    await user.click(within(confirmDialog).getByRole('button', { name: 'Confirm correction' }))
    expect(transport.callsTo(`POST ${DRAFT}/confirm`)).toHaveLength(0)

    await user.type(
      within(confirmDialog).getByLabelText('Reason'),
      'Chest re-measured at the fitting',
    )
    await user.click(within(confirmDialog).getByRole('button', { name: 'Confirm correction' }))
    await screen.findByText('Measurements confirmed')

    const [confirmed] = transport.callsTo(`POST ${DRAFT}/confirm`)
    expect(confirmed?.body).toEqual({
      reason: 'Chest re-measured at the fitting',
      correctsVersionId: VERSION_TWO_ID,
    })
    expect(screen.getByText(/It corrects version 2, which stays readable/)).toBeInTheDocument()
  })
})

describe('comparing two measurements', () => {
  it('shows each field old beside new, marks a difference with a word, and says a dropped field is dropped', async () => {
    transport.route(`GET ${MEASUREMENTS}/${VERSION_ONE_ID}/compare/${VERSION_TWO_ID}`, () =>
      jsonResponse(
        aMeasurementComparison({
          differences: [
            ...aMeasurementComparison().differences,
            {
              key: 'old_hip',
              change: 'Dropped',
              before: {
                key: 'old_hip',
                millimetres: 1000,
                enteredUnit: 'Inch',
                choice: null,
                acknowledged: false,
              },
              after: null,
            },
          ],
          changedCount: 3,
        }),
      ),
    )
    renderAt(`/measurements/compare/${VERSION_ONE_ID}/${VERSION_TWO_ID}`)

    expect(await screen.findByText(/Version 1, taken .* against version 2/)).toBeInTheDocument()
    expect(screen.getByText(/3 fields differ\./)).toBeInTheDocument()

    const table = screen.getByRole('table', {
      name: 'Every field either version holds, in template order',
    })
    const chest = within(table).getByRole('row', { name: /Chest \/ bust/ })
    expect(chest).toHaveTextContent('36 in')
    expect(chest).toHaveTextContent('36 1/2 in')
    expect(chest).toHaveTextContent('Changed')

    const closure = within(table).getByRole('row', { name: /Closure/ })
    expect(closure).toHaveTextContent('Front hooks')
    expect(closure).toHaveTextContent('Unchanged')

    const sleeve = within(table).getByRole('row', { name: /Sleeve length/ })
    expect(sleeve).toHaveTextContent('Not held')
    expect(sleeve).toHaveTextContent('Added')

    // A field the newer template version dropped is shown as dropped, by its key, not quietly missing.
    const dropped = within(table).getByRole('row', { name: /old_hip/ })
    expect(dropped).toHaveTextContent('Dropped — not on the newer template version')
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAt(`/measurements/compare/${VERSION_ONE_ID}/${VERSION_TWO_ID}`)
    await screen.findByRole('table')
    await expectNoAccessibilityViolations(container)
  })
})

describe('the measurement sheet', () => {
  it('carries the measurements, the template version and the date, and nothing else about the customer', async () => {
    const user = userEvent.setup()
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined)
    const { container } = renderAt(`/measurements/${VERSION_TWO_ID}/sheet`)

    expect(
      await screen.findByText(/Blouse, pattern work — version 2, taken .* by Asha \(counter\)/),
    ).toBeInTheDocument()
    expect(screen.getByText(/Rendered through template version 2/)).toBeInTheDocument()

    const bodice = screen.getByRole('heading', { name: 'Bodice' })
    expect(bodice).toBeInTheDocument()
    expect(screen.getByText('36 1/2 in')).toBeInTheDocument()
    expect(screen.getByText('Back hooks')).toBeInTheDocument()

    // Asserted, not eyeballed: no customer field of any name reaches the screen.
    const text = container.textContent ?? ''
    for (const forbidden of ['Asha Example', 'C-000123', '4321', '@', 'example.invalid']) {
      expect(text).not.toContain(forbidden)
    }
    expect(transport.callsTo(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}/sheet`)).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Print' }))
    expect(print).toHaveBeenCalledTimes(1)
    // Printing is a browser act: it re-reads nothing and is audited once.
    expect(transport.callsTo(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}/sheet`)).toHaveLength(1)

    await expectNoAccessibilityViolations(container)
  })

  it('tells somebody without the sheet permission so in a sentence, and reads nothing', async () => {
    transport.route('GET /api/v1/me', () =>
      jsonResponse(aCurrentUser({ permissions: [MEASUREMENT_PERMISSIONS.capture] })),
    )
    renderAt(`/measurements/${VERSION_TWO_ID}/sheet`)

    expect(
      await screen.findByRole('heading', { name: 'You do not have access to this' }),
    ).toBeInTheDocument()
    expect(transport.callsTo(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}/sheet`)).toHaveLength(0)
  })

  it('shows the server’s refusal for a sheet the caller may not read', async () => {
    transport.route(`GET ${MEASUREMENTS}/${VERSION_TWO_ID}/sheet`, () =>
      problemResponse(404, 'measurements.measurement-not-found'),
    )
    renderAt(`/measurements/${VERSION_TWO_ID}/sheet`)

    expect(
      await screen.findByText('This could not be found. It may have been changed or removed.'),
    ).toBeInTheDocument()
  })

  it('offers the sheet link on the earlier measurements only to somebody holding the key', async () => {
    const user = userEvent.setup()
    transport.route('GET /api/v1/me', () =>
      jsonResponse(
        aCurrentUser({
          permissions: COUNTER.filter((key) => key !== MEASUREMENT_PERMISSIONS.readSheet),
        }),
      ),
    )
    await chooseCustomerAndGarment(user)

    expect(screen.queryByRole('link', { name: /Open the sheet/ })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Compare version 2 with version 1' })).toHaveAttribute(
      'href',
      `/measurements/compare/${VERSION_ONE_ID}/${VERSION_TWO_ID}`,
    )
  })
})
