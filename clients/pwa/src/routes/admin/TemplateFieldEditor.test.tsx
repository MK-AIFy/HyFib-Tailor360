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

it("states a range in the tailor's unit at the field's precision, never in millimetres", async () => {
  // The fixture is 100 mm to 2000 mm at eighths of an inch. A tailor never sees millimetres
  // (docs/prd/measurement-templates.md section 2), so a bound quoted in them cannot be checked
  // against the tape in anybody's hand — which is the only reason to show it.
  renderEditor()

  const table = within(await screen.findByRole('table'))
  expect(table.getByText('Expected between 3 7/8 in and 78 3/4 in.')).toBeInTheDocument()
  expect(table.queryByText(/mm/)).not.toBeInTheDocument()
})

it('says nothing about the range of a field that accepts any measurement', async () => {
  const user = userEvent.setup()
  const form = await openAddForm(user)

  // A new field opens with no bounds, and says which state it is in rather than leaving it to be
  // read off four empty boxes.
  expect(within(form).getByText('This field accepts any measurement.')).toBeInTheDocument()
  expect(within(form).queryByLabelText('Refuse below')).not.toBeInTheDocument()
})

it('sets both bounds at once, because half a range cannot be sent', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await user.click(form.getByRole('button', { name: 'Set a range' }))

  // Each bound is its own group, and the accessible name says which bound as well as which field,
  // so a screen-reader user landing on the fraction strip knows what they are setting.
  expect(form.getByRole('group', { name: 'Refuse below' })).toBeInTheDocument()
  expect(form.getByRole('group', { name: 'Refuse above' })).toBeInTheDocument()
  expect(form.getByRole('group', { name: 'Ask to confirm below' })).toBeInTheDocument()
  expect(form.getByRole('group', { name: 'Ask to confirm above' })).toBeInTheDocument()
})

it('enters inches as a whole number and a fraction, never as a decimal box', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await user.click(form.getByRole('button', { name: 'Set a range' }))

  // A tape is divided by halving, so the step is a fraction a person can find on it. A decimal box
  // would offer 3.19 in, which is not a number anybody can read off a tape.
  const lower = within(form.getByRole('group', { name: 'Refuse below' }))
  expect(lower.getByLabelText('Refuse below — whole inches')).toBeInTheDocument()

  // And the strip is a radiogroup, so 14½ in can be set from the keyboard alone.
  const strip = lower.getByRole('radiogroup', { name: 'Refuse below — fraction of an inch' })
  expect(within(strip).getByRole('radio', { name: '1/2' })).toBeInTheDocument()
})

it('sends a bound typed in inches as the millimetres the server stores', async () => {
  const user = userEvent.setup()
  transport.route(`POST ${FIELDS}`, () => versionedResponse(TEMPLATE, 'W/"2"'))

  const element = await openAddForm(user)
  const form = within(element)
  await fillMinimum(user, element)
  await user.click(form.getByRole('button', { name: 'Set a range' }))

  const lower = within(form.getByRole('group', { name: 'Refuse below' }))
  await user.type(lower.getByLabelText('Refuse below — whole inches'), '14')
  await user.click(lower.getByRole('radio', { name: '1/2' }))

  const upper = within(form.getByRole('group', { name: 'Refuse above' }))
  await user.type(upper.getByLabelText('Refuse above — whole inches'), '40')

  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`POST ${FIELDS}`)).toHaveLength(1)
  })

  const body = transport.callsTo(`POST ${FIELDS}`)[0]?.body as Record<string, unknown>

  // 14 1/2 in is exactly 368.30 mm. Reaching storage as "near it" is the rounding bug this slice
  // exists to not have.
  expect(body.minimumMillimetres).toBe(368.3)
  expect(body.maximumMillimetres).toBe(1016)
  expect(body.warnBelowMillimetres).toBeNull()
})

it('refuses a confirmation threshold outside the bounds before the server is asked', async () => {
  const user = userEvent.setup()
  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))

  // The stored band is 100–2000 mm with thresholds at 200 and 1800. Pushing the lower threshold
  // under the lower bound makes it unreachable: the value is refused before anybody is asked.
  const threshold = within(form.getByRole('group', { name: 'Ask to confirm below' }))
  const whole = threshold.getByLabelText('Ask to confirm below — whole inches')
  await user.clear(whole)
  await user.type(whole, '0')
  await user.click(threshold.getByRole('radio', { name: '0' }))

  await user.click(form.getByRole('button', { name: 'Save this field' }))

  expect(await form.findByRole('button', { name: /never reached/ })).toBeInTheDocument()
  expect(transport.callsTo(`PUT ${FIELDS}/${FIELD.templateFieldId}`)).toHaveLength(0)
})

it('clears the thresholds with the bounds, so none is left to be refused', async () => {
  const user = userEvent.setup()
  const route = `PUT ${FIELDS}/${FIELD.templateFieldId}`
  transport.route(route, () => versionedResponse(TEMPLATE, 'W/"2"'))

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.click(form.getByRole('button', { name: 'Accept any measurement' }))
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(route)).toHaveLength(1)
  })

  const body = transport.callsTo(route)[0]?.body as Record<string, unknown>

  // The sentinel, whole: two zeroes and two nulls. A threshold left behind would be refused as
  // outside the bounds it no longer has.
  expect(body.minimumMillimetres).toBe(0)
  expect(body.maximumMillimetres).toBe(0)
  expect(body.warnBelowMillimetres).toBeNull()
  expect(body.warnAboveMillimetres).toBeNull()
})

it('offers no bands at all to a choice field, and none to a count', async () => {
  const user = userEvent.setup()
  const element = await openAddForm(user)
  const form = within(element)

  await user.selectOptions(form.getByLabelText('Stored as'), 'None')
  expect(form.queryByText('The range this field accepts')).not.toBeInTheDocument()
  expect(form.queryByRole('button', { name: 'Set a range' })).not.toBeInTheDocument()

  await user.selectOptions(form.getByLabelText('Stored as'), 'Count')
  // A count has no unit to read a bound in, so the range is there but has no display unit; the
  // editor still offers it, because a count of hooks may legitimately be bounded.
  expect(form.getByText('The range this field accepts')).toBeInTheDocument()
})

/* The rule builder (#95) ------------------------------------------------------------------------ */

it('says a field is always asked for until somebody says otherwise', async () => {
  const user = userEvent.setup()
  const form = within(await openAddForm(user))

  expect(form.getByText('This field is always asked for.')).toBeInTheDocument()
  expect(form.queryByLabelText('What the rule does')).not.toBeInTheDocument()
})

it('builds a rule from controls, and never from free text where an operand belongs', async () => {
  const user = userEvent.setup()
  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.click(form.getByRole('button', { name: 'Add a rule' }))

  // The effect, the comparison and — for a field operand — the operand itself are all chosen.
  expect(form.getByLabelText('What the rule does')).toBeInTheDocument()
  expect(form.getByLabelText('Condition 1: what to look at')).toBeInTheDocument()
  expect(form.getByLabelText('Condition 1: how to compare')).toBeInTheDocument()
})

it('offers no “and”, no “not” and no grouping, and says so rather than leaving it to be found', async () => {
  const user = userEvent.setup()
  const form = within(await openAddForm(user))

  await user.click(form.getByRole('button', { name: 'Add a rule' }))

  // A screen that offered a control the server rejects would teach an administrator that the
  // application is unreliable, so the limits are stated where they bite.
  expect(form.getByText(/Conditions are joined by “or”, and nothing else/)).toBeInTheDocument()
})

it('stops at six conditions, and says why rather than refusing silently', async () => {
  const user = userEvent.setup()
  const form = within(await openAddForm(user))

  await user.click(form.getByRole('button', { name: 'Add a rule' }))
  for (let added = 1; added < 6; added += 1) {
    await user.click(form.getByRole('button', { name: 'Add another condition' }))
  }

  expect(form.getByLabelText('Condition 6: how to compare')).toBeInTheDocument()
  expect(form.queryByRole('button', { name: 'Add another condition' })).not.toBeInTheDocument()
  expect(form.getByText(/Six conditions is the most a rule may carry/)).toBeInTheDocument()
})

it('types a design operand and says why there is no list to pick from', async () => {
  const user = userEvent.setup()
  const form = within(await openAddForm(user))

  await user.click(form.getByRole('button', { name: 'Add a rule' }))
  await user.selectOptions(form.getByLabelText('Condition 1: what to look at'), 'DesignSelection')

  // The design option groups are #30 and do not exist: nothing in the API lists one. Offering only
  // the field scope would silently drop half the language and make an existing design rule
  // uneditable, so the scope is offered with the reason there is no picker.
  expect(form.getByLabelText('Condition 1: which design choice')).toBeInTheDocument()
  expect(form.getByText(/no catalogue of design choices to pick from yet/)).toBeInTheDocument()
})

it('sends the rule as a structure, never as the sentence the response renders', async () => {
  const user = userEvent.setup()
  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.click(form.getByRole('button', { name: 'Add a rule' }))
  await user.selectOptions(form.getByLabelText('Condition 1: what to look at'), 'DesignSelection')
  await user.type(form.getByLabelText('Condition 1: which design choice'), 'sleeve_style')
  await user.type(form.getByLabelText('Condition 1, value 1'), 'SLEEVELESS')
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(`PUT ${FIELDS}/${FIELD.templateFieldId}`)).toHaveLength(1)
  })

  const body = transport.callsTo(`PUT ${FIELDS}/${FIELD.templateFieldId}`)[0]?.body as Record<
    string,
    unknown
  >

  expect(body.rule).toEqual({
    effect: 'ShownWhen',
    anyOf: [
      {
        scope: 'DesignSelection',
        name: 'sleeve_style',
        operator: 'IsAnyOf',
        values: ['SLEEVELESS'],
      },
    ],
  })
})

it('refuses a field that reads its own answer, before the server is asked', async () => {
  const user = userEvent.setup()
  const two = aTemplateField({
    templateFieldId: '0199bb00-0000-7000-8000-0000000000da',
    key: 'has_lining',
    label: 'Has lining',
    displayOrder: 1,
  })
  const version = aTemplateVersion({ fields: [FIELD, two] })
  const template = aMeasurementTemplate({ versions: [version] })
  transport.route(`GET ${DETAIL}`, () => versionedResponse(template, 'W/"1"'))

  renderEditor(version.templateVersionId)
  await screen.findByRole('heading', { name: 'Step: Bodice' })
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.click(form.getByRole('button', { name: 'Add a rule' }))

  // The operand list is the version's *other* keys, so the field's own is not offered at all —
  // a field whose visibility depends on its own value can never settle.
  const operand = form.getByLabelText('Condition 1: which measurement')
  expect(within(operand).queryByRole('option', { name: 'chest_bust' })).not.toBeInTheDocument()
  expect(within(operand).getByRole('option', { name: 'has_lining' })).toBeInTheDocument()
})

it('sends nothing at all for a field whose rule is removed', async () => {
  const user = userEvent.setup()
  const withRule = aTemplateField({
    ruleDefinition: {
      effect: 'HiddenWhen',
      anyOf: [{ scope: 'Field', name: 'has_lining', operator: 'IsAnyOf', values: ['NO'] }],
    },
    rule: 'Hidden when the lining is not chosen.',
  })
  const version = aTemplateVersion({ fields: [withRule] })
  const template = aMeasurementTemplate({ versions: [version] })
  const route = `PUT ${DETAIL}/versions/${version.templateVersionId}/fields/${withRule.templateFieldId}`

  transport.route(`GET ${DETAIL}`, () => versionedResponse(template, 'W/"1"'))
  transport.route(route, () => versionedResponse(template, 'W/"2"'))

  renderEditor(version.templateVersionId)
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))

  // The stored rule opens as a rule, and the sentence the server rendered is shown beside it so an
  // administrator can read what they built in words.
  expect(form.getByText('Hidden when the lining is not chosen.')).toBeInTheDocument()

  await user.click(form.getByRole('button', { name: 'Always ask for this field' }))
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  await waitFor(() => {
    expect(transport.callsTo(route)).toHaveLength(1)
  })

  expect((transport.callsTo(route)[0]?.body as Record<string, unknown>).rule).toBeNull()
})

it('passes axe with the rule builder open', async () => {
  const user = userEvent.setup()
  const { container } = renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))

  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.click(form.getByRole('button', { name: 'Add a rule' }))
  await user.click(form.getByRole('button', { name: 'Add another condition' }))

  // The checklist singles the rule builder out as one of two controls most likely to strand a
  // keyboard user: every condition has to be addable, editable and removable without a pointer,
  // and every control's accessible name has to say which condition it belongs to.
  await expectNoAccessibilityViolations(container)
  expect(form.getByRole('button', { name: 'Remove condition 2' })).toBeInTheDocument()
})

/* Grouping and ordering (#104) ---------------------------------------------------------------- */

const SLEEVE = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000dd',
  key: 'sleeve_length',
  label: 'Sleeve length',
  groupName: 'Sleeve',
  displayOrder: 2,
})

const WAIST = aTemplateField({
  templateFieldId: '0199bb00-0000-7000-8000-0000000000de',
  key: 'waist',
  label: 'Waist',
  groupName: 'Bodice',
  displayOrder: 1,
})

const ORDERED = aTemplateVersion({ fields: [FIELD, WAIST, SLEEVE] })
const ORDERED_TEMPLATE = aMeasurementTemplate({ versions: [ORDERED, PUBLISHED] })
const ORDERED_FIELDS = `${DETAIL}/versions/${ORDERED.templateVersionId}/fields`

/** The editor over a version with two steps in it. */
function renderOrdered() {
  transport.route(`GET ${DETAIL}`, () => versionedResponse(ORDERED_TEMPLATE, 'W/"1"'))
  return renderEditor(ORDERED.templateVersionId)
}

it('shows the fields grouped as the capture wizard will ask for them', async () => {
  renderOrdered()

  // A heading per step, in the order their first field sits — which is what the server derives.
  expect(await screen.findByRole('heading', { name: 'Step: Bodice' })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Step: Sleeve' })).toBeInTheDocument()

  // The field list's step headings, in order. The page carries other level-three headings — the
  // preview and the comparison — so the assertion is about the steps rather than about every one.
  const headings = screen
    .getAllByRole('heading', { level: 3 })
    .map((one) => one.textContent)
    .filter((text) => text?.startsWith('Step: ') === true)
  expect(headings).toEqual(['Step: Bodice', 'Step: Sleeve'])
})

it('says where each field sits in its step, in words rather than as a raw number', async () => {
  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })

  // A person moving a field with the keyboard has to be able to read the position back, and the
  // display order is global to the version and not contiguous until something renumbers it.
  expect(screen.getByText('1 of 2 in Bodice')).toBeInTheDocument()
  expect(screen.getByText('2 of 2 in Bodice')).toBeInTheDocument()
  expect(screen.getByText('1 of 1 in Sleeve')).toBeInTheDocument()
})

it('offers a button for every move, and never only a drag', async () => {
  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })

  // clients/pwa/CLAUDE.md section 3 and checklist item A11Y-75: every drag has a button
  // alternative. There is no drag here at all — the buttons are the whole control.
  expect(screen.getByRole('button', { name: 'Measure Waist earlier' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Measure Chest / bust later' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Move the Sleeve step earlier' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Move the Bodice step later' })).toBeInTheDocument()
})

it('sends one write per changed field, in order, each carrying the last response tag', async () => {
  const user = userEvent.setup()
  let tag = 1
  transport.route(`PUT ${ORDERED_FIELDS}/${WAIST.templateFieldId}`, () =>
    versionedResponse(ORDERED_TEMPLATE, `W/"${String(++tag)}"`),
  )
  transport.route(`PUT ${ORDERED_FIELDS}/${FIELD.templateFieldId}`, () =>
    versionedResponse(ORDERED_TEMPLATE, `W/"${String(++tag)}"`),
  )

  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })
  await user.click(screen.getByRole('button', { name: 'Measure Waist earlier' }))

  await waitFor(() => {
    expect(transport.callsTo(`PUT ${ORDERED_FIELDS}/${FIELD.templateFieldId}`)).toHaveLength(1)
  })

  // There is no reorder endpoint and no bulk save: a move is one full-body PUT per affected field,
  // sequentially, each needing the ETag the previous response returned because the template's
  // version advances on every write.
  const first = transport.callsTo(`PUT ${ORDERED_FIELDS}/${WAIST.templateFieldId}`)[0]
  const second = transport.callsTo(`PUT ${ORDERED_FIELDS}/${FIELD.templateFieldId}`)[0]

  expect(first?.headers.get('If-Match')).toBe('W/"1"')
  expect(second?.headers.get('If-Match')).toBe('W/"2"')
  expect((first?.body as Record<string, unknown>).displayOrder).toBe(0)
  expect((second?.body as Record<string, unknown>).displayOrder).toBe(1)

  // The whole field goes back, with only its number changed: there is no PATCH, and echoing the
  // read is what stops a reorder quietly resetting a bound it does not own.
  expect((first?.body as Record<string, unknown>).minimumMillimetres).toBe(100)
  expect(Object.keys(first?.body as Record<string, unknown>)).toHaveLength(19)
})

it('announces the new position rather than leaving it to be re-read', async () => {
  const user = userEvent.setup()
  let tag = 1
  transport.route(`PUT ${ORDERED_FIELDS}/${WAIST.templateFieldId}`, () =>
    versionedResponse(ORDERED_TEMPLATE, `W/"${String(++tag)}"`),
  )
  transport.route(`PUT ${ORDERED_FIELDS}/${FIELD.templateFieldId}`, () =>
    versionedResponse(ORDERED_TEMPLATE, `W/"${String(++tag)}"`),
  )

  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })
  await user.click(screen.getByRole('button', { name: 'Measure Waist earlier' }))

  expect(await screen.findByText('Waist is now 1 of 2 in Bodice.')).toBeInTheDocument()
})

it('says why nothing happened at the end of a step, rather than hiding the control', async () => {
  const user = userEvent.setup()
  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })

  // A control that disappears at the boundary is a control whose position moves under the pointer.
  await user.click(screen.getByRole('button', { name: 'Measure Chest / bust earlier' }))

  expect(
    await screen.findByText('Chest / bust is already measured first in this step.'),
  ).toBeInTheDocument()
  expect(transport.callsTo(`PUT ${ORDERED_FIELDS}/${FIELD.templateFieldId}`)).toHaveLength(0)
})

it('says how far a move got when one of its writes is refused', async () => {
  const user = userEvent.setup()
  transport.route(`PUT ${ORDERED_FIELDS}/${WAIST.templateFieldId}`, () =>
    versionedResponse(ORDERED_TEMPLATE, 'W/"2"'),
  )
  transport.route(`PUT ${ORDERED_FIELDS}/${FIELD.templateFieldId}`, () =>
    problemResponse(409, 'measurements.version-changed'),
  )

  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })
  await user.click(screen.getByRole('button', { name: 'Measure Waist earlier' }))

  // The writes are ascending, so what is saved is a prefix of the new sequence: correct as far as
  // it went, and not the state the person asked for. Saying nothing would leave them believing the
  // move happened.
  expect(await screen.findByText(/stopped after 1 of 2 changes/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  expect(screen.queryByText(/is now 1 of 2 in Bodice/)).not.toBeInTheDocument()
})

it('moves a whole step as a block', async () => {
  const user = userEvent.setup()
  let tag = 1
  for (const one of [FIELD, WAIST, SLEEVE]) {
    transport.route(`PUT ${ORDERED_FIELDS}/${one.templateFieldId}`, () =>
      versionedResponse(ORDERED_TEMPLATE, `W/"${String(++tag)}"`),
    )
  }

  renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })
  await user.click(screen.getByRole('button', { name: 'Move the Sleeve step earlier' }))

  await waitFor(() => {
    expect(transport.callsTo(`PUT ${ORDERED_FIELDS}/${SLEEVE.templateFieldId}`)).toHaveLength(1)
  })

  // A group's position *is* the position of its first field, so moving one field of it would either
  // take the group with it or split the group. All three fields are renumbered.
  expect(
    (
      transport.callsTo(`PUT ${ORDERED_FIELDS}/${SLEEVE.templateFieldId}`)[0]?.body as Record<
        string,
        unknown
      >
    ).displayOrder,
  ).toBe(0)
  await waitFor(() => {
    expect(transport.callsTo(`PUT ${ORDERED_FIELDS}/${WAIST.templateFieldId}`)).toHaveLength(1)
  })
  expect(await screen.findByText('The Sleeve step is now step 1 of 2.')).toBeInTheDocument()
})

it('passes axe over the grouped editor', async () => {
  const { container } = renderOrdered()
  await screen.findByRole('heading', { name: 'Step: Bodice' })

  await expectNoAccessibilityViolations(container)
})

/* Validation report, capture preview and comparison (#96) --------------------------------------- */

const VALIDATION = `${DETAIL}/versions/${DRAFT.templateVersionId}/validation`

it('says a version is ready when every check passed', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: true,
      findings: [],
    }),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  expect(
    await screen.findByText('Every check passed. This version can be published.'),
  ).toBeInTheDocument()
})

it('puts each finding beside the field that caused it, not only in a list', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: false,
      findings: [
        {
          severity: 'Error',
          code: 'measurements.field-precision-missing',
          message: 'This field has no unit it can be entered in.',
          target: `fields[${FIELD.key}].precision`,
        },
      ],
    }),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  // Anchoring is the feature: a list makes an administrator map a target path onto a form by eye,
  // and the whole point of reporting every finding rather than the first is that the list is long.
  const row = (await screen.findAllByRole('row')).find((one) =>
    one.textContent?.includes('Chest / bust'),
  )
  expect(
    within(row as HTMLElement).getByText(/This field has no unit it can be entered in/),
  ).toBeInTheDocument()

  // And the summary offers a way to get to it.
  expect(screen.getByRole('button', { name: 'Go to Chest / bust' })).toBeInTheDocument()
})

it('opens the field a finding is about when the summary link is pressed', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: false,
      findings: [
        {
          severity: 'Error',
          code: 'measurements.field-precision-missing',
          message: 'This field has no unit it can be entered in.',
          target: `fields[${FIELD.key}].precision`,
        },
      ],
    }),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))
  await user.click(await screen.findByRole('button', { name: 'Go to Chest / bust' }))

  expect(await screen.findByRole('form', { name: 'Editing Chest / bust' })).toBeInTheDocument()
})

it('keeps a finding it cannot anchor, rather than quietly losing a refusal', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: false,
      findings: [
        {
          severity: 'Error',
          code: 'measurements.field-precision-missing',
          message: 'A field this screen has never heard of is wrong.',
          target: 'fields[gone_away].precision',
        },
      ],
    }),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  // The version changed under the reader. Dropping this would tell an administrator their version
  // is ready when the server will refuse to publish it.
  // The message sits inside its summary entry beside the severity word, so it is matched rather
  // than compared whole.
  expect(
    await screen.findByText(/A field this screen has never heard of is wrong\./),
  ).toBeInTheDocument()
  expect(screen.getByText(/reported about something not on this screen/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /^Go to/ })).not.toBeInTheDocument()
})

it('does not let a warning read as something that blocks publication', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: true,
      findings: [
        {
          severity: 'Warning',
          code: 'measurements.label-not-translated',
          message: 'This field has no Tamil label.',
          target: `fields[${FIELD.key}].labelTamil`,
        },
      ],
    }),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))

  expect(await screen.findByText(/Nothing here refuses publication/)).toBeInTheDocument()
  expect(screen.queryByText(/Publication is refused/)).not.toBeInTheDocument()
})

it('says a report is stale once a field has been written since it ran', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: false,
      findings: [
        {
          severity: 'Error',
          code: 'measurements.field-precision-missing',
          message: 'This field has no unit it can be entered in.',
          target: `fields[${FIELD.key}].precision`,
        },
      ],
    }),
  )
  transport.route(`PUT ${FIELDS}/${FIELD.templateFieldId}`, () =>
    versionedResponse(TEMPLATE, 'W/"2"'),
  )

  renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))
  await screen.findByRole('button', { name: 'Go to Chest / bust' })

  await user.click(screen.getByRole('button', { name: 'Edit Chest / bust' }))
  const form = within(await screen.findByRole('form', { name: 'Editing Chest / bust' }))
  await user.clear(form.getByLabelText(/^Label$/))
  await user.type(form.getByLabelText(/^Label$/), 'Chest')
  await user.click(form.getByRole('button', { name: 'Save this field' }))

  // A stale report that looked current would let somebody fix a finding, see it still listed, and
  // fix it twice.
  expect(await screen.findByText(/ran against an earlier state/)).toBeInTheDocument()
})

it('previews the version through the real capture control, saving nothing', async () => {
  const user = userEvent.setup()
  renderEditor()
  await screen.findByRole('table')

  const preview = within(
    screen
      .getByRole('heading', { name: 'How a tailor will see this' })
      .closest('section') as HTMLElement,
  )

  // The real control, so the bands it enforces cannot drift from what the wizard will do — which is
  // also what makes this the test-data check #27 asks for and no screen exercised.
  const whole = preview.getByLabelText('Chest / bust — whole inches')
  await user.type(whole, '2')

  // The fixture refuses below 100 mm; two inches is 50.8 mm.
  expect(await preview.findByText(/must be between/)).toBeInTheDocument()
  expect(transport.callsTo(`PUT ${FIELDS}/${FIELD.templateFieldId}`)).toHaveLength(0)
})

it('says a field is not asked for rather than simply not showing it', async () => {
  const hidden = aTemplateField({
    templateFieldId: '0199bb00-0000-7000-8000-0000000000db',
    key: 'lining_length',
    label: 'Lining length',
    displayOrder: 1,
    ruleDefinition: {
      effect: 'ShownWhen',
      anyOf: [{ scope: 'Field', name: 'chest_bust', operator: 'IsAnyOf', values: ['NEVER'] }],
    },
  })
  const version = aTemplateVersion({ fields: [FIELD, hidden] })
  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(aMeasurementTemplate({ versions: [version] }), 'W/"1"'),
  )

  const user = userEvent.setup()
  renderEditor(version.templateVersionId)
  await screen.findByRole('table')

  const preview = within(
    screen
      .getByRole('heading', { name: 'How a tailor will see this' })
      .closest('section') as HTMLElement,
  )

  // The rule reads `chest_bust`, which has no answer yet — so it cannot be settled, and an
  // undecidable rule *shows* the field. Answering it makes the clause readable and false.
  expect(preview.getByText(/its rule cannot be settled here/)).toBeInTheDocument()
  await user.type(preview.getByLabelText('Chest / bust — whole inches'), '20')

  // A reviewer checking a rule needs to see that it hid the field; a field that simply is not there
  // looks identical to a field somebody forgot to add.
  expect(
    await preview.findByText('Lining length is not asked for with these answers.'),
  ).toBeInTheDocument()
})

it('says why a field whose rule cannot be settled is asked for anyway', async () => {
  const undecidable = aTemplateField({
    templateFieldId: '0199bb00-0000-7000-8000-0000000000dc',
    key: 'sleeve_length',
    label: 'Sleeve length',
    displayOrder: 1,
    ruleDefinition: {
      effect: 'ShownWhen',
      anyOf: [
        { scope: 'DesignSelection', name: 'sleeve_style', operator: 'IsAnyOf', values: ['FULL'] },
      ],
    },
  })
  const version = aTemplateVersion({ fields: [undecidable] })
  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(aMeasurementTemplate({ versions: [version] }), 'W/"1"'),
  )

  renderEditor(version.templateVersionId)

  // Design selections only exist inside an order, and hiding on missing information drops a
  // measurement the tailor needs.
  expect(await screen.findByText(/its rule cannot be settled here/)).toBeInTheDocument()
})

it('compares this version with another, and says which attribute of each field differs', async () => {
  const changed = aTemplateField({ label: 'Chest', minimumMillimetres: 200 })
  const other = aTemplateVersion({
    templateVersionId: '0199bb00-0000-7000-8000-0000000000ef',
    versionNumber: 4,
    fields: [changed],
  })
  const version = aTemplateVersion({ fields: [FIELD] })
  transport.route(`GET ${DETAIL}`, () =>
    versionedResponse(aMeasurementTemplate({ versions: [version, other] }), 'W/"1"'),
  )

  renderEditor(version.templateVersionId)
  await screen.findByRole('heading', { name: 'What changed' })

  // A reviewer approving a version is asked one question — is this change right — and a screen that
  // hands them every field of both versions makes them answer a different one.
  expect(screen.getByText(/Label: was Chest, now Chest \/ bust/)).toBeInTheDocument()
  expect(screen.getByText(/1 field changed/)).toBeInTheDocument()
})

it('passes axe with the report, the preview and the comparison on screen', async () => {
  const user = userEvent.setup()
  transport.route(`GET ${VALIDATION}`, () =>
    jsonResponse({
      templateVersionId: DRAFT.templateVersionId,
      isReadyToPublish: false,
      findings: [
        {
          severity: 'Error',
          code: 'measurements.field-precision-missing',
          message: 'This field has no unit it can be entered in.',
          target: `fields[${FIELD.key}].precision`,
        },
      ],
    }),
  )

  const { container } = renderEditor()
  await screen.findByRole('table')
  await user.click(screen.getByRole('button', { name: 'Check this version' }))
  await screen.findByRole('button', { name: 'Go to Chest / bust' })

  await expectNoAccessibilityViolations(container)
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
