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
import {
  aMeasurementTemplate,
  aTemplateField,
  aTemplateVersion,
  versionedResponse,
} from '../../admin/testing/fixtures'
import { ADMIN_PERMISSIONS } from '../../admin/adminPermissions'
import { TemplateVersionEditorRoute } from './TemplateVersionEditorRoute'

let transport: FetchStub

const TEMPLATES = '/api/v1/customers/measurement-templates'

const FIELD = aTemplateField()
const DRAFT = aTemplateVersion({ fields: [FIELD] })

const PUBLISHED = aTemplateVersion({
  templateVersionId: '0199bb00-0000-7000-8000-0000000000e2',
  versionNumber: 2,
  status: 'Published',
  isApproved: true,
  publishedAt: '2026-09-05T09:15:00.000Z',
})

const TEMPLATE = aMeasurementTemplate({ versions: [DRAFT, PUBLISHED] })
const DETAIL = `${TEMPLATES}/${TEMPLATE.measurementTemplateId}`
const FIELDS = `${DETAIL}/versions/${DRAFT.templateVersionId}/fields`

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  transport.route('GET /api/v1/me', () =>
    jsonResponse(
      aCurrentUser({
        permissions: [ADMIN_PERMISSIONS.templatesEdit, ADMIN_PERMISSIONS.templatesPublish],
      }),
    ),
  )
  transport.route(`GET ${DETAIL}`, () => versionedResponse(TEMPLATE, 'W/"1"'))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderEditor(versionId = DRAFT.templateVersionId) {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter
          initialEntries={[
            `/admin/templates/${TEMPLATE.measurementTemplateId}/versions/${versionId}`,
          ]}
        >
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={<TemplateVersionEditorRoute />}
                path="/admin/templates/:templateId/versions/:versionId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

/** The form element, once it is open. Callers scope every query to it with `within`. */
async function openAddForm(user: ReturnType<typeof userEvent.setup>): Promise<HTMLElement> {
  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Add a field' }))
  return await screen.findByRole('form', { name: 'A new field' })
}

/** The three things every field needs, so a test can change one thing and submit. */
async function fillMinimum(user: ReturnType<typeof userEvent.setup>, form: HTMLElement) {
  const inside = within(form)
  await user.type(inside.getByLabelText(/^Key/), 'shoulder_width')
  await user.type(inside.getByLabelText(/^Label$/), 'Shoulder width')
  await user.type(inside.getByLabelText(/How to measure it/), 'Shoulder point to shoulder point.')
}

it('lists the fields of a draft and offers the three acts that change them', async () => {
  const { container } = renderEditor()

  const table = within(await screen.findByRole('table'))
  expect(table.getByText('Chest / bust')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add a field' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Edit Chest / bust' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Remove Chest / bust' })).toBeInTheDocument()

  await expectNoAccessibilityViolations(container)
})

it('refuses to edit a version that is not a draft, and says where the change is made', async () => {
  // A published version is immutable in the database — a trigger refuses the write, not a validator
  // — so the control that would fail is not offered at all.
  renderEditor(PUBLISHED.templateVersionId)

  expect(await screen.findByText(/Only a draft can be edited/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Add a field' })).not.toBeInTheDocument()
})

it('says so when the address names no version of this template', async () => {
  renderEditor('0199bb00-0000-7000-8000-00000000dead')

  expect(await screen.findByText(/No version of this template matches/)).toBeInTheDocument()
})

it('sends all nineteen members, the rendered tag and a retry key when a field is added', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${FIELDS}`, () => versionedResponse(TEMPLATE, 'W/"2"'))

  const element = await openAddForm(user)
  const form = within(element)
  await fillMinimum(user, element)
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(1)
  })

  const sent = transport.callsTo(`POST ${FIELDS}`)[0]
  const body = sent?.body as Record<string, unknown>

  // The precondition is the tag of the read that painted the screen, not one fetched to make the
  // write succeed — which is what makes a concurrent edit answer 409 rather than being overwritten.
  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(Object.keys(body)).toHaveLength(19)
  expect(body.key).toBe('shoulder_width')
  expect(body.groupName).toBe('Bodice')

  // The bands of a new field are the `ValidationBands.None` sentinel: the two hard bounds are not
  // nullable, so 0 / 0 / null / null is the only way to say "no bounds", and a threshold sent
  // beside it would be refused as outside them.
  expect(body.minimumMillimetres).toBe(0)
  expect(body.warnBelowMillimetres).toBeNull()

  // Ordering is a later slice; a new field goes to the end, where somebody adding one expects it.
  expect(body.displayOrder).toBe(1)
})

it('sends a changed field back whole, keeping the bands another screen set', async () => {
  const user = userEvent.setup()
  const route = `PUT ${FIELDS}/${FIELD.templateFieldId}`
  transport.route(route, () => versionedResponse(TEMPLATE, 'W/"2"'))

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.clear(form.getByLabelText(/^Label$/))
  await user.type(form.getByLabelText(/^Label$/), 'Chest')
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(route)).toHaveLength(1)
  })

  const body = transport.callsTo(route)[0]?.body as Record<string, unknown>

  // There is no PATCH: changing one label sends all nineteen members. The ones this screen does not
  // edit are echoed from the read, so a save here cannot reset a bound set on the bands screen.
  expect(body.label).toBe('Chest')
  expect(body.minimumMillimetres).toBe(100)
  expect(body.maximumMillimetres).toBe(2000)
  expect(body.warnBelowMillimetres).toBe(200)
  expect(body.displayOrder).toBe(0)
})

it('does not offer to rename a key, and says what a rename actually is', async () => {
  const user = userEvent.setup()
  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  const key = form.getByLabelText(/^Key/)

  // The server ignores a key sent with a PUT, so an editable box would appear to work and change
  // nothing. The control is still reachable and readable — read-only is not disabled.
  expect(key).toHaveAttribute('readonly')
  expect(form.getByText(/remove this field and add it again/)).toBeInTheDocument()
})

it('offers a choice field a list and no bands at all, rather than boxes it can never use', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await user.selectOptions(form.getByLabelText('Stored as'), 'None')

  // A `None` field has no inch step and no decimal places, and it never will — so the controls are
  // absent, not disabled. A disabled box says "not now" where the truth is "never".
  expect(form.queryByLabelText('Inch step')).not.toBeInTheDocument()
  expect(form.queryByLabelText('Centimetre places')).not.toBeInTheDocument()
  expect(form.getByText(/A choice has no range and no units/)).toBeInTheDocument()
  expect(form.getByRole('button', { name: 'Add a choice' })).toBeInTheDocument()
})

it('gives a count no precision either, and says why it is whole', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await user.selectOptions(form.getByLabelText('Stored as'), 'Count')

  expect(form.queryByLabelText('Inch step')).not.toBeInTheDocument()
  expect(form.getByText(/so it has no inch step and no decimal places/)).toBeInTheDocument()
  expect(form.queryByRole('button', { name: 'Add a choice' })).not.toBeInTheDocument()
})

it('sends a count exactly (0, 0), because the server does not correct a precision', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${FIELDS}`, () => versionedResponse(TEMPLATE, 'W/"2"'))

  const element = await openAddForm(user)
  const form = within(element)
  await fillMinimum(user, element)
  await user.selectOptions(form.getByLabelText('Stored as'), 'Count')
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(1)
  })

  const body = transport.callsTo(`POST ${FIELDS}`)[0]?.body as Record<string, unknown>
  expect(body.inchFraction).toBe(0)
  expect(body.centimetreDecimals).toBe(0)
  expect(body.options).toBeNull()
})

it('refuses a choice field with no choices before the server is asked', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await fillMinimum(user, element)
  await user.selectOptions(form.getByLabelText('Stored as'), 'None')
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  expect(await form.findByText('Add at least one choice.')).toBeInTheDocument()
  expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(0)
})

it('shows a refusal as a sentence beside the control it is about, and sends nothing', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await user.type(form.getByLabelText(/^Key/), 'Shoulder Width')
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  // Twice on purpose (3.3.1): once in the summary at the top of the form, as a control that moves
  // focus to the field, and once beside the control itself.
  const key = /A key is 2 to 60 characters/
  expect(await form.findByRole('button', { name: key })).toBeInTheDocument()
  expect(form.getAllByText(key)).toHaveLength(2)
  expect(form.getAllByText('Give this field a label.')).toHaveLength(2)
  expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(0)
})

it('keeps what was typed when the server refuses, and retries on the same key', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${FIELDS}`, () => problemResponse(409, 'measurements.version-changed'))

  const element = await openAddForm(user)
  const form = within(element)
  await fillMinimum(user, element)
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await screen.findByRole('button', { name: 'Reload' })

  // conventions section 4.3: a retry after a conflict reuses the same key. A fresh one would let a
  // save whose answer was lost rather than refused add the field twice.
  await user.click(screen.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(2)
  })

  const sent = transport.callsTo(`POST ${FIELDS}`)
  expect(sent[1]?.headers.get('Idempotency-Key')).toBe(sent[0]?.headers.get('Idempotency-Key'))
  expect(sent[1]?.body).toEqual(sent[0]?.body)
})

it('asks for a reason before removing a field, and says what removal does not touch', async () => {
  const user = userEvent.setup()
  const route = `POST ${FIELDS}/${FIELD.templateFieldId}/delete`
  transport.route(route, () => versionedResponse(TEMPLATE, 'W/"2"'))

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Remove Chest / bust' }))

  const dialog = await screen.findByRole('dialog')
  expect(
    within(dialog).getByText(/every measurement already captured under it is untouched/),
  ).toBeInTheDocument()

  await user.type(within(dialog).getByRole('textbox'), 'Superseded by the two-part chest fields.')
  await user.click(within(dialog).getByRole('button', { name: 'Remove Chest / bust' }))

  await waitFor(() => {
    expect(transport.callsTo(route)).toHaveLength(1)
  })

  const sent = transport.callsTo(route)[0]
  expect(sent?.body).toEqual({ reason: 'Superseded by the two-part chest fields.' })
  expect(sent?.headers.get('If-Match')).toBe('W/"1"')
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
})

it('acts on the tag the last command returned, not the one the first read carried', async () => {
  const user = userEvent.setup()
  // Every field route answers with the whole template and a fresh tag. The next command has to
  // present that one: keeping the read's tag would make the second save of a sitting fail with a
  // conflict blaming an administrator who was never there.
  transport.route(`POST ${FIELDS}`, () => versionedResponse(TEMPLATE, 'W/"7"'))

  const element = await openAddForm(user)
  const form = within(element)
  await fillMinimum(user, element)
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(1)
  })
  await screen.findByText('Added Shoulder width.')

  await user.click(screen.getByRole('button', { name: 'Add a field' }))
  const again = await screen.findByRole('form', { name: 'A new field' })
  await fillMinimum(user, again)
  await user.click(within(again).getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(2)
  })
  expect(transport.callsTo(`POST ${FIELDS}`)[1]?.headers.get('If-Match')).toBe('W/"7"')
})

it('asks for the description of a diagram only once there is a diagram to describe', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  expect(form.queryByLabelText('Describe the diagram')).not.toBeInTheDocument()

  await user.type(form.getByLabelText('Diagram'), 'blouse_front_v1')

  // A picture nobody can describe is a picture a screen-reader user is simply not given, so the
  // server refuses the pair — and the control appears at the moment it becomes required.
  expect(form.getByLabelText('Describe the diagram')).toBeRequired()
})

it('passes axe with the form open, at the three profiles the guide names', async () => {
  const user = userEvent.setup()
  const { container } = render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter
          initialEntries={[
            `/admin/templates/${TEMPLATE.measurementTemplateId}/versions/${DRAFT.templateVersionId}`,
          ]}
        >
          <Routes>
            <Route element={<RequireSession />}>
              <Route
                element={<TemplateVersionEditorRoute />}
                path="/admin/templates/:templateId/versions/:versionId"
              />
            </Route>
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )

  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Add a field' }))
  await screen.findByRole('form', { name: 'A new field' })

  await expectNoAccessibilityViolations(container)

  // And with the choice list open, which is a different set of controls entirely.
  await user.selectOptions(screen.getByLabelText('Stored as'), 'None')
  await user.click(screen.getByRole('button', { name: 'Add a choice' }))

  await expectNoAccessibilityViolations(container)
})
