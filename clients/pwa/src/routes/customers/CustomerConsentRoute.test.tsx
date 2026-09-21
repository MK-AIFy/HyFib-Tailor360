import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { forgetAntiforgeryToken } from '../../auth/antiforgery'
import { setSessionChallengeHandler } from '../../auth/apiClient'
import { SessionProvider } from '../../auth/SessionProvider'
import { aCurrentUser, jsonResponse, problemResponse, stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import {
  aCommunicationPreference,
  aConsentAnswer,
  aConsentPurpose,
  versionedResponse,
} from '../../customers/testing/fixtures'
import { CustomerConsentRoute } from './CustomerConsentRoute'

const CUSTOMER_ID = '0199cc00-0000-7000-8000-000000000001'
const CONSENT = `GET /api/v1/customers/${CUSTOMER_ID}/consent`
const RECORD = `POST /api/v1/customers/${CUSTOMER_ID}/consent`
const PREFS = `GET /api/v1/customers/${CUSTOMER_ID}/communication-preferences`
const SAVE_PREFS = `PUT /api/v1/customers/${CUSTOMER_ID}/communication-preferences`

let transport: FetchStub

function signedInAs(permissions: readonly string[]) {
  transport.route('GET /api/v1/me', () =>
    jsonResponse(aCurrentUser({ permissions: [...permissions] })),
  )
}

beforeEach(() => {
  forgetAntiforgeryToken()
  setSessionChallengeHandler(null)
  transport = stubFetch()
  signedInAs(['customers.read_consent', 'customers.update'])
  transport.route(CONSENT, () => jsonResponse({ purposes: [aConsentPurpose()] }))
  transport.route(PREFS, () => jsonResponse(aCommunicationPreference()))
})

afterEach(() => {
  setSessionChallengeHandler(null)
  forgetAntiforgeryToken()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function renderConsent() {
  return render(
    <AppIntlProvider locale="en-IN">
      <SessionProvider>
        <MemoryRouter initialEntries={[`/customers/${CUSTOMER_ID}/consent`]}>
          <Routes>
            <Route path="/customers/:customerId" element={<p>the record</p>} />
            <Route path="/customers/:customerId/consent" element={<CustomerConsentRoute />} />
          </Routes>
        </MemoryRouter>
      </SessionProvider>
    </AppIntlProvider>,
  )
}

it('says where she stands, in a word and not only a colour', async () => {
  renderConsent()

  expect(await screen.findByRole('heading', { name: 'Appointment reminders' })).toBeInTheDocument()
  expect(screen.getAllByText('She agreed').length).toBeGreaterThan(0)
})

// An answer is appended and never edited, so the evidence that she once withdrew survives her
// changing her mind. That is why every previous answer stays on the page.
it('keeps every answer she has given, not only the one that stands', async () => {
  transport.route(CONSENT, () =>
    jsonResponse({
      purposes: [
        aConsentPurpose({
          answers: [
            aConsentAnswer(),
            aConsentAnswer({
              recordId: '0199cc00-0000-7000-8000-00000000c002',
              decision: 'Withdrawn',
              recordedAt: '2026-06-01T10:00:00Z',
              source: 'Over the telephone',
            }),
          ],
        }),
      ],
    }),
  )
  renderConsent()

  await screen.findByRole('heading', { name: 'Appointment reminders' })

  expect(screen.getByText(/She withdrew —.*Over the telephone/)).toBeInTheDocument()
  expect(screen.getByText(/She agreed —.*At the counter/)).toBeInTheDocument()
})

it('records what she said, with where she said it, and never the wording version', async () => {
  transport.route(RECORD, () => jsonResponse(aConsentAnswer()))
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  await userEvent.type(screen.getByRole('textbox', { name: 'Where she said it' }), 'At the counter')
  await userEvent.click(screen.getByRole('button', { name: 'She said no' }))

  await waitFor(() => {
    expect(transport.callsTo(RECORD)).toHaveLength(1)
  })
  const [sent] = transport.callsTo(RECORD)
  expect(sent?.headers.get('Idempotency-Key')).not.toBeNull()
  expect(sent?.body).toEqual({
    purposeKey: 'appointment-reminders',
    decision: 'Declined',
    source: 'At the counter',
  })
  // A client that could name a wording version could record an answer against words she was never
  // read, so it is the server's to look up and must not appear here.
  expect(JSON.stringify(sent?.body)).not.toContain('wordingVersion')
})

it('will not record an answer with no source, and says which field is empty', async () => {
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  await userEvent.click(screen.getByRole('button', { name: 'She agreed' }))

  expect(transport.callsTo(RECORD)).toHaveLength(0)
  const source = screen.getByRole('textbox', { name: 'Where she said it' })
  expect(source).toHaveAttribute('aria-invalid', 'true')
  expect(source).toHaveAccessibleDescription(/Say where she said it/)
})

// `canBeAnswered` is false for two different reasons, and the screen says which rather than
// recomputing it from `isRetired`, which is only one of them.
it('explains a purpose with no published wording, and offers no way to answer it', async () => {
  transport.route(CONSENT, () =>
    jsonResponse({
      purposes: [
        aConsentPurpose({
          canBeAnswered: false,
          currentWordingVersion: 0,
          status: 'NeverAsked',
          answers: [],
        }),
      ],
    }),
  )
  renderConsent()

  expect(await screen.findByText(/No wording has been published/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'She agreed' })).not.toBeInTheDocument()
})

it('explains a retired purpose differently from one with no wording', async () => {
  transport.route(CONSENT, () =>
    jsonResponse({ purposes: [aConsentPurpose({ canBeAnswered: false, isRetired: true })] }),
  )
  renderConsent()

  expect(await screen.findByText(/no longer asked about/)).toBeInTheDocument()
  expect(screen.queryByText(/No wording has been published/)).not.toBeInTheDocument()
})

it('offers withdrawal only where there is something to withdraw', async () => {
  transport.route(CONSENT, () =>
    jsonResponse({ purposes: [aConsentPurpose({ status: 'Declined' })] }),
  )
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  expect(screen.queryByRole('button', { name: 'She withdrew it' })).not.toBeInTheDocument()
})

it('shows the record but no controls to a caller who may read and not change', async () => {
  signedInAs(['customers.read_consent'])
  renderConsent()

  await screen.findByRole('heading', { name: 'Appointment reminders' })

  expect(screen.queryByRole('button', { name: 'She agreed' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Save how to reach her' })).not.toBeInTheDocument()
})

/* How to reach her ------------------------------------------------------------------------------ */

it('sends the version as If-Match once a preference exists', async () => {
  transport.route(SAVE_PREFS, () => versionedResponse(aCommunicationPreference(), 'W/"5"'))
  renderConsent()
  await screen.findByRole('checkbox', { name: 'WhatsApp' })

  await userEvent.click(screen.getByRole('checkbox', { name: 'WhatsApp' }))
  await userEvent.click(screen.getByRole('button', { name: 'Save how to reach her' }))

  await waitFor(() => {
    expect(transport.callsTo(SAVE_PREFS)).toHaveLength(1)
  })
  const [sent] = transport.callsTo(SAVE_PREFS)
  expect(sent?.headers.get('If-Match')).toBe('W/"4"')
  expect(sent?.body).toMatchObject({ allowedChannels: ['Sms', 'WhatsApp'], language: 'en-IN' })
})

// The precondition is required once a preference exists and must be *omitted* before then, because
// there is no version of a row that does not exist. Sending `*` would be asking the server to accept
// any version of something that has none.
it('sends no If-Match at all before a preference has ever been recorded', async () => {
  transport.route(PREFS, () =>
    jsonResponse(
      aCommunicationPreference({
        hasBeenRecorded: false,
        version: null,
        allowedChannels: [],
        quietHoursStart: null,
        quietHoursEnd: null,
        updatedAt: null,
      }),
    ),
  )
  transport.route(SAVE_PREFS, () => versionedResponse(aCommunicationPreference(), 'W/"1"'))
  renderConsent()
  await screen.findByRole('checkbox', { name: 'Text message' })

  await userEvent.click(screen.getByRole('checkbox', { name: 'Text message' }))
  await userEvent.click(screen.getByRole('button', { name: 'Save how to reach her' }))

  await waitFor(() => {
    expect(transport.callsTo(SAVE_PREFS)).toHaveLength(1)
  })
  expect(transport.callsTo(SAVE_PREFS)[0]?.headers.get('If-Match')).toBeNull()
})

it('sends no channels at all when she asks not to be messaged', async () => {
  transport.route(SAVE_PREFS, () => versionedResponse(aCommunicationPreference(), 'W/"5"'))
  renderConsent()
  await screen.findByRole('checkbox', { name: 'Text message' })

  await userEvent.click(screen.getByRole('checkbox', { name: 'Text message' }))
  expect(screen.getByText(/how she says do not message me/)).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Save how to reach her' }))

  await waitFor(() => {
    expect(transport.callsTo(SAVE_PREFS)).toHaveLength(1)
  })
  expect(transport.callsTo(SAVE_PREFS)[0]?.body).toMatchObject({ allowedChannels: [] })
})

// Quiet hours are wall-clock at the branch and may run backwards over midnight — 21:00 to 08:00 is a
// quiet night, not an error — so the only rule enforced is the server's: both ends or neither.
it('refuses one end of the quiet hours without the other', async () => {
  transport.route(PREFS, () =>
    jsonResponse(aCommunicationPreference({ quietHoursStart: null, quietHoursEnd: null })),
  )
  renderConsent()
  const start = await screen.findByLabelText(/Do not message after/)

  await userEvent.type(start, '21:00')
  await userEvent.click(screen.getByRole('button', { name: 'Save how to reach her' }))

  expect(transport.callsTo(SAVE_PREFS)).toHaveLength(0)
  expect(screen.getByText(/both ends of the quiet hours, or neither/)).toBeInTheDocument()
})

it('accepts quiet hours that run across midnight', async () => {
  transport.route(SAVE_PREFS, () => versionedResponse(aCommunicationPreference(), 'W/"5"'))
  renderConsent()
  await screen.findByLabelText(/Do not message after/)

  await userEvent.click(screen.getByRole('button', { name: 'Save how to reach her' }))

  await waitFor(() => {
    expect(transport.callsTo(SAVE_PREFS)).toHaveLength(1)
  })
  expect(transport.callsTo(SAVE_PREFS)[0]?.body).toMatchObject({
    quietHoursStart: '21:00:00',
    quietHoursEnd: '08:00:00',
  })
})

it('renders a failed consent read as a problem, not as an empty record', async () => {
  transport.route(CONSENT, () => problemResponse(503, 'platform.unavailable'))
  renderConsent()

  expect(
    await screen.findByText('This record could not be found, or is not one you can reach.'),
  ).toBeInTheDocument()
})

it('keeps the consent record readable when the preferences alone fail', async () => {
  transport.route(PREFS, () => problemResponse(503, 'platform.unavailable'))
  renderConsent()

  expect(await screen.findByRole('heading', { name: 'Appointment reminders' })).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'How to reach her' })).toBeInTheDocument()
})

// A second decision while the first is in flight would be a second *answer* under the first's retry
// key — "same key, different body" on a consent record. The siblings go unavailable, and the handler
// refuses re-entry as well, because a key is cheap and a lost decision is not.
it('takes one decision at a time, and does not send a second under the first key', async () => {
  transport.route(RECORD, () => new Promise<Response>(() => {}))
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  await userEvent.type(screen.getByRole('textbox', { name: 'Where she said it' }), 'At the counter')
  await userEvent.click(screen.getByRole('button', { name: 'She agreed' }))
  await waitFor(() => {
    expect(transport.callsTo(RECORD)).toHaveLength(1)
  })

  // The other decisions say they cannot be used, rather than quietly doing nothing.
  expect(screen.getByRole('button', { name: 'She said no' })).toHaveAttribute(
    'aria-disabled',
    'true',
  )
  await userEvent.click(screen.getByRole('button', { name: 'She said no' }))

  expect(transport.callsTo(RECORD)).toHaveLength(1)
})

it('announces that the answer was taken, since the card is only redrawn', async () => {
  transport.route(RECORD, () => jsonResponse(aConsentAnswer()))
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  // Mounted empty, so the sentence arrives into a region that already exists.
  expect(screen.getAllByRole('status')[0]).toHaveTextContent('')

  await userEvent.type(screen.getByRole('textbox', { name: 'Where she said it' }), 'At the counter')
  await userEvent.click(screen.getByRole('button', { name: 'She said no' }))

  expect(await screen.findByText('Recorded: She said no.')).toBeInTheDocument()
})

// A genuine server refusal must not be reported as "say where she said it" just because the field
// happens to be empty — which is what reading the validation state out of the failure would do.
it('reports a server refusal as itself, not as a missing source', async () => {
  transport.route(RECORD, () => problemResponse(400, 'customers.consent-decision-not-understood'))
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  const source = screen.getByRole('textbox', { name: 'Where she said it' })
  await userEvent.type(source, 'At the counter')
  await userEvent.click(screen.getByRole('button', { name: 'She agreed' }))

  await waitFor(() => {
    expect(transport.callsTo(RECORD)).toHaveLength(1)
  })
  expect(source).not.toHaveAttribute('aria-invalid', 'true')
  expect(screen.queryByText(/Say where she said it/)).not.toBeInTheDocument()
})

// The connection goes *after* the screen is up, which is both the real case and the only one the
// store can see: `useNetworkState` reads `navigator.onLine` once, and attaches its window listeners
// when the first component subscribes — so an event fired before anything rendered is missed.
it('blocks both writes with an explanation when the connection goes', async () => {
  renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })
  expect(screen.getByRole('button', { name: 'She agreed' })).toBeInTheDocument()

  try {
    act(() => {
      window.dispatchEvent(new Event('offline'))
    })

    expect(screen.queryByRole('button', { name: 'She agreed' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save how to reach her' })).not.toBeInTheDocument()
    // The wording the blueprint fixes: a write is refused, never quietly queued.
    expect(screen.getAllByText(/Needs connection — this will not be queued/).length).toBe(2)
  } finally {
    act(() => {
      window.dispatchEvent(new Event('online'))
    })
  }
})

// The form must not fall back to the pre-save preference while the reload is in flight: a toggle in
// that window would build the next draft on the state that was just replaced, putting back a channel
// she had only just been taken off.
it('keeps showing what was saved while the record is read again', async () => {
  let reads = 0
  transport.route(PREFS, () => {
    reads += 1
    // The second read never answers, so the whole test sits inside the reload window.
    return reads === 1
      ? jsonResponse(aCommunicationPreference({ allowedChannels: ['Sms'] }))
      : new Promise<Response>(() => {})
  })
  transport.route(SAVE_PREFS, () =>
    versionedResponse(aCommunicationPreference({ allowedChannels: [] }), 'W/"5"'),
  )
  renderConsent()

  const sms = await screen.findByRole('checkbox', { name: 'Text message' })
  await userEvent.click(sms)
  await userEvent.click(screen.getByRole('button', { name: 'Save how to reach her' }))

  await screen.findByText('Saved.')

  // Still off, because that is what the server now holds — not on, which is what the stale read says.
  expect(screen.getByRole('checkbox', { name: 'Text message' })).not.toBeChecked()
})

it('has no accessibility violations', async () => {
  const { container } = renderConsent()
  await screen.findByRole('heading', { name: 'Appointment reminders' })

  await expectNoAccessibilityViolations(container)
})
