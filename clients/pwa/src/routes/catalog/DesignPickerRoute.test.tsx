import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
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
import { CATALOG_PERMISSIONS } from '../../catalog/catalogPermissions'
import {
  aDesignCheck,
  aDesignMigrationPrompt,
  aDesignPicker,
  aDesignPickerGroup,
  aDesignPickerOption,
  aDesignPickerRule,
  aDesignSelectionDraft,
  anOperand,
} from '../../catalog/testing/fixtures'
import type { DesignDraftSelection } from '../../catalog/types'
import { DesignPickerRoute } from './DesignPickerRoute'

let transport: FetchStub

const CATALOG = '/api/v1/catalog'
const SERVICE_TYPE_ID = '0199bb00-0000-7000-8000-0000000000d1'
const DRAFT_ID = '0199bb00-0000-7000-8000-000000009a1'

const ROUND = aDesignPickerOption({ code: 'ROUND', name: 'Round' })
const V_NECK = aDesignPickerOption({ code: 'V_NECK', name: 'V neck', displayOrder: 1 })
const NECKLINE = aDesignPickerGroup({
  code: 'neckline',
  name: 'Neckline',
  options: [ROUND, V_NECK],
})
const SHORT = aDesignPickerOption({ code: 'SHORT', name: 'Short' })
const PUFF = aDesignPickerOption({ code: 'PUFF', name: 'Puff', displayOrder: 1 })
const SLEEVE = aDesignPickerGroup({
  designOptionGroupId: 'group-sleeve',
  code: 'sleeve',
  name: 'Sleeve length',
  required: false,
  displayOrder: 1,
  options: [SHORT, PUFF],
})

const PICKER_PATH = `${CATALOG}/current/service-types/${SERVICE_TYPE_ID}/design`
const DRAFT_PATH = `${CATALOG}/design-drafts/${DRAFT_ID}`
const CHECK_PATH = `${DRAFT_PATH}/check?hasReferenceImage=false`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [CATALOG_PERMISSIONS.designSelect] })),
  )
  transport.route(`GET ${DRAFT_PATH}`, () =>
    versionedResponse(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"1"'),
  )
  transport.route(`GET ${CHECK_PATH}`, () => jsonResponse(aDesignCheck()))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderPicker(
  groups = [NECKLINE, SLEEVE],
  rules = [] as ReturnType<typeof aDesignPickerRule>[],
) {
  transport.route(`GET ${PICKER_PATH}`, () =>
    jsonResponse(aDesignPicker({ serviceTypeId: SERVICE_TYPE_ID, groups, rules })),
  )
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/catalog/design/${SERVICE_TYPE_ID}/${DRAFT_ID}`]}>
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={<DesignPickerRoute />}
                path="/catalog/design/:serviceTypeId/:draftId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

function lastPutSelections(): readonly DesignDraftSelection[] {
  const calls = transport.callsTo(`PUT ${DRAFT_PATH}`)
  const call = calls.at(-1)
  const body = call?.body as { selections?: readonly DesignDraftSelection[] } | undefined
  return body?.selections ?? []
}

it('renders every group as a card grid, with the alternative text when an option has no illustration', async () => {
  transport.route(`PUT ${DRAFT_PATH}`, () =>
    versionedResponse(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"2"'),
  )
  const { container } = renderPicker()

  expect(await screen.findByRole('heading', { name: 'Neckline' })).toBeInTheDocument()
  expect(screen.getByLabelText('Round')).toBeInTheDocument()
  expect(screen.getByLabelText('V neck')).toBeInTheDocument()
  expect(screen.getAllByText(/No illustration yet/).length).toBeGreaterThan(0)

  await expectNoAccessibilityViolations(container)
})

it('saves the whole selection set with If-Match and a retry key, and the summary shows the choice', async () => {
  const user = userEvent.setup()
  transport.route(`PUT ${DRAFT_PATH}`, () =>
    versionedResponse(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"2"'),
  )
  renderPicker()

  await user.click(await screen.findByLabelText('Round'))

  await waitFor(
    () => {
      expect(transport.callsTo(`PUT ${DRAFT_PATH}`)).toHaveLength(1)
    },
    { timeout: 2000 },
  )

  const sent = transport.callsTo(`PUT ${DRAFT_PATH}`)[0]
  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(lastPutSelections()).toContainEqual({ groupCode: 'neckline', optionCodes: ['ROUND'] })

  const summary = within(await screen.findByRole('region', { name: 'Summary' }))
  await waitFor(() => {
    expect(summary.getByText('Round')).toBeInTheDocument()
  })
})

it('greys out the option an excludes rule forbids, with the reason on the card rather than hidden', async () => {
  const user = userEvent.setup()
  transport.route(`PUT ${DRAFT_PATH}`, () =>
    versionedResponse(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"2"'),
  )
  const rule = aDesignPickerRule({
    identifier: 'DR-09',
    type: 'Excludes',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
    consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['PUFF'] }),
  })
  renderPicker([NECKLINE, SLEEVE], [rule])

  expect(await screen.findByLabelText('Puff')).toBeEnabled()

  await user.click(screen.getByLabelText('Round'))

  await waitFor(() => {
    expect(screen.getByLabelText('Puff')).toBeDisabled()
  })
  expect(screen.getByText('Not available because Neckline is Round.')).toBeInTheDocument()
  // Never hidden: the card and its label stay in the document, only disabled.
  expect(screen.getByLabelText('Puff')).toBeInTheDocument()
})

it('folds a single-option `requires` auto-selection back in once the server settles it, and says so', async () => {
  const user = userEvent.setup()
  const rule = aDesignPickerRule({
    identifier: 'DR-02',
    type: 'Requires',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
    consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['SHORT'] }),
  })
  let version = 1
  transport.route(`PUT ${DRAFT_PATH}`, () => {
    version += 1
    return versionedResponse(
      aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }),
      `W/"${String(version)}"`,
    )
  })
  // Mirrors the server: DR-02 auto-selects `sleeve = SHORT` until the saved draft already holds it.
  transport.route(`GET ${CHECK_PATH}`, () => {
    const settled = lastPutSelections().some(
      (selection) => selection.groupCode === 'sleeve' && selection.optionCodes.includes('SHORT'),
    )
    return jsonResponse(
      aDesignCheck(
        settled
          ? {}
          : {
              confirmable: false,
              autoSelections: [
                { ruleIdentifier: 'DR-02', groupCode: 'sleeve', optionCode: 'SHORT' },
              ],
            },
      ),
    )
  })

  renderPicker([NECKLINE, SLEEVE], [rule])

  await user.click(await screen.findByLabelText('Round'))

  await waitFor(
    () => {
      expect(screen.getByLabelText('Short')).toBeChecked()
    },
    { timeout: 3000 },
  )

  const summary = within(screen.getByRole('region', { name: 'Summary' }))
  await waitFor(() => {
    expect(summary.getByText('Short')).toBeInTheDocument()
  })
})

it('discards a stale check’s auto-selection once the selection it was computed against has moved on', async () => {
  const user = userEvent.setup()
  const rule = aDesignPickerRule({
    identifier: 'DR-02',
    type: 'Requires',
    antecedent: anOperand({ groupCode: 'neckline', form: 'Equals', optionCodes: ['ROUND'] }),
    consequent: anOperand({ groupCode: 'sleeve', form: 'Equals', optionCodes: ['SHORT'] }),
  })
  transport.route(`PUT ${DRAFT_PATH}`, () =>
    versionedResponse(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"2"'),
  )
  const deferredCheck: { resolve: (response: Response) => void } = {
    resolve: () => {
      throw new Error('the check response was resolved before the request was made')
    },
  }
  transport.route(
    `GET ${CHECK_PATH}`,
    () =>
      new Promise<Response>((resolve) => {
        deferredCheck.resolve = resolve
      }),
  )

  renderPicker([NECKLINE, SLEEVE], [rule])

  await user.click(await screen.findByLabelText('Round'))
  await waitFor(() => {
    expect(transport.callsTo(`GET ${CHECK_PATH}`)).toHaveLength(1)
  })

  // Moves away from Round while that check is still in flight — DR-02's antecedent no longer
  // holds, so whatever the stalled check answers about it is about a state that no longer exists.
  await user.click(screen.getByLabelText('V neck'))

  deferredCheck.resolve(
    jsonResponse(
      aDesignCheck({
        confirmable: false,
        autoSelections: [{ ruleIdentifier: 'DR-02', groupCode: 'sleeve', optionCode: 'SHORT' }],
      }),
    ),
  )

  // Give the resolved promise a turn to be handled, then assert the stale auto-selection was
  // never folded in: `sleeve` stays unset, not `SHORT`.
  await waitFor(() => {
    expect(screen.getByLabelText('V neck')).toBeChecked()
  })
  expect(screen.getByLabelText('Short')).not.toBeChecked()
})

it('shows a blocking violation in the summary, naming both options, and never reaches a clean summary', async () => {
  transport.route(`PUT ${DRAFT_PATH}`, () =>
    versionedResponse(aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }), 'W/"2"'),
  )
  transport.route(`GET ${CHECK_PATH}`, () =>
    jsonResponse(
      aDesignCheck({
        confirmable: false,
        violations: [
          {
            code: 'design.excluded',
            ruleIdentifier: 'DR-09',
            groupCode: 'sleeve',
            optionCodes: ['PUFF'],
            relatedGroupCode: 'neckline',
            relatedOptionCodes: ['ROUND'],
            message: 'DR-09: neckline is Round excludes sleeve is Puff, and both are chosen.',
            blocks: true,
          },
        ],
      }),
    ),
  )
  const user = userEvent.setup()
  renderPicker()

  await user.click(await screen.findByLabelText('Round'))

  const summary = within(await screen.findByRole('region', { name: 'Summary' }))
  await waitFor(() => {
    expect(summary.getByText('This cannot be confirmed yet — see below.')).toBeInTheDocument()
  })
  expect(
    summary.getByText('DR-09: neckline is Round excludes sleeve is Puff, and both are chosen.'),
  ).toBeInTheDocument()
})

it('blocks changing a selection while offline and says it will not be queued', async () => {
  renderPicker()
  await screen.findByLabelText('Round')

  vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
  window.dispatchEvent(new Event('offline'))

  expect(await screen.findByText('Needs connection — this will not be queued')).toBeInTheDocument()
  expect(screen.getByLabelText('Round')).toBeDisabled()

  // `useNetworkState` holds its snapshot at module scope, outside React — restoring it here is
  // what keeps this test from leaving every test after it starting from "offline".
  vi.restoreAllMocks()
  window.dispatchEvent(new Event('online'))
})

it('shows a conflict when another device saved first, and reads the draft again on request', async () => {
  const user = userEvent.setup()
  transport.route(`PUT ${DRAFT_PATH}`, () => problemResponse(409, 'catalog.design-draft-changed'))
  renderPicker()

  await user.click(await screen.findByLabelText('Round'))

  expect(await screen.findByText('Someone else changed this design.')).toBeInTheDocument()

  await user.click(screen.getByRole('button', { name: 'Read it again' }))

  await waitFor(() => {
    expect(transport.callsTo(`GET ${DRAFT_PATH}`).length).toBeGreaterThan(1)
  })
})

it('offers migrating or finishing on the pinned version when the catalogue changed, without ever reading the picker for a stale service type', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${DRAFT_PATH}`, () =>
    versionedResponse(
      aDesignSelectionDraft({
        designSelectionDraftId: DRAFT_ID,
        migrationPrompt: aDesignMigrationPrompt(),
      }),
      'W/"1"',
    ),
  )
  // A republish gives every service type a fresh row id (`CatalogVersion.CloneAsDraft`), so the
  // draft's pinned `serviceTypeId` never resolves against `/current` once any newer version has
  // published — this route answers 404 to prove the migration gate never attempts that read at all.
  transport.route(`GET ${PICKER_PATH}`, () => jsonResponse(null, 404))
  renderPicker()

  expect(
    await screen.findByText('The catalogue changed since this was started'),
  ).toBeInTheDocument()
  expect(
    screen.getByText(/no longer offered\. A selection naming it cannot survive migration\./),
  ).toBeInTheDocument()
  expect(transport.callsTo(`GET ${PICKER_PATH}`)).toHaveLength(0)

  await user.click(screen.getByRole('button', { name: 'Finish on this version' }))

  expect(screen.queryByText('The catalogue changed since this was started')).not.toBeInTheDocument()
  expect(await screen.findByText('Finishing on the version already chosen.')).toBeInTheDocument()
  expect(transport.callsTo(`GET ${PICKER_PATH}`)).toHaveLength(0)
})

it('migrates a draft and then reads the current picker fresh', async () => {
  const user = userEvent.setup()
  let reads = 0
  transport.route(`GET ${DRAFT_PATH}`, () => {
    reads += 1
    return versionedResponse(
      aDesignSelectionDraft({
        designSelectionDraftId: DRAFT_ID,
        migrationPrompt: reads === 1 ? aDesignMigrationPrompt() : null,
      }),
      'W/"1"',
    )
  })
  transport.route(`POST ${DRAFT_PATH}/migrate?hasReferenceImage=false`, () =>
    versionedResponse(
      {
        draft: aDesignSelectionDraft({ designSelectionDraftId: DRAFT_ID }),
        appliedChanges: [],
        evaluation: aDesignCheck(),
      },
      'W/"2"',
    ),
  )
  renderPicker()

  await user.click(await screen.findByRole('button', { name: 'Update to the current version' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${DRAFT_PATH}/migrate?hasReferenceImage=false`)).toHaveLength(1)
  })
  expect(await screen.findByLabelText('Round')).toBeInTheDocument()
  expect(transport.callsTo(`GET ${PICKER_PATH}`)).toHaveLength(1)
})
