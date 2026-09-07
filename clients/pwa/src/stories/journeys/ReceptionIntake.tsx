import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { EmptyState } from '../../components/states/EmptyState'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { CUSTOMERS, findCustomers } from '../fixtures/customers'
import type { JourneyCustomer } from '../fixtures/customers'
import { INVOICE } from '../fixtures/billing'
import { DATES, STAFF } from '../fixtures/branch'

/**
 * `A11Y-RJ-01` — Reception: find the customer, then intake.
 *
 * Covered end to end by the priority-zero record `A11Y-PZ-01` (order confirmation), whose steps 1 to
 * 8 are this journey; the checklist says plainly it is not to be walked twice. This screen exists so
 * that the record has something to be walked *against*.
 *
 * ## The four steps, and what each one is proving
 *
 *  1. **Search by the last six digits.** A counter phone number is typed one-handed on a keypad, so
 *     the field carries `inputmode="tel"` and `autocomplete="tel"` — 1.3.5 Identify Input Purpose,
 *     and the shop-floor rule of docs/nfr/accessibility-localisation.md section 5. The search
 *     matches a partial number on purpose: nobody at a counter types thirteen characters.
 *  2. **Consent, as a record either way.** A declined consent is stored as a decline, never as an
 *     absence, so the three checkboxes each show their current state and the wording version they
 *     were recorded against. Changing one announces through the shell's autosave channel rather
 *     than through a toast, which section 6 forbids for anything actionable.
 *  3. **The draft order**, with its money already formatted by the `formatters` module — never by
 *     `toLocaleString` at a call site — so the Tamil and pseudo-locale stories show real grouping.
 *  4. **Confirmation**, through the plain `confirm` tier. Order confirmation freezes the snapshots
 *     and allocates the numbers in one transaction, so the dialog says what will happen before the
 *     person reaches the control that does it (checklist item A11Y-39).
 *
 * There is no backend. The state is `useState` over the fixtures, because #23 onward deliver the
 * screens this evidence is preparing the design system for.
 */
export function ReceptionIntakeScreen() {
  const formatters = getFormatters()
  const status = useShellStatus()
  const [term, setTerm] = useState('')
  const [selected, setSelected] = useState<JourneyCustomer | null>(null)
  const [photoConsent, setPhotoConsent] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [confirmed, setConfirmed] = useState(false)

  const results = findCustomers(term)

  function openCustomer(customer: JourneyCustomer | undefined) {
    if (customer === undefined) {
      return
    }
    setSelected(customer)
    setPhotoConsent(customer.consent.photoCapture)
  }

  return (
    <section className="page journey-screen">
      <h1>New order</h1>
      <p>
        {STAFF.reception} at the counter, {formatters.formatDateTime(DATES.today)}.
      </p>

      <section aria-labelledby="reception-find" className="journey-section">
        <h2 id="reception-find">1. Find the customer</h2>
        <TextField
          autoComplete="tel"
          description="The last six digits are enough."
          enterKeyHint="search"
          inputMode="tel"
          label="Customer telephone number"
          name="customerPhone"
          onValueChange={setTerm}
          type="tel"
          value={term}
        />

        {term !== '' && results.length === 0 ? (
          <EmptyState
            actions={
              <Button
                iconName="plus"
                onClick={() => {
                  // Walkthrough 2 step 1: no record matches, so one is created and its consent is
                  // taken at the counter before anything is stored.
                  openCustomer(CUSTOMERS[1])
                }}
                variant="primary"
              >
                Create a new customer
              </Button>
            }
            headingLevel={3}
            title="No customer matches that number"
          >
            Check the digits, or create a new record. A new record asks for consent before anything
            is stored.
          </EmptyState>
        ) : null}

        {results.length > 0 ? (
          <ul className="journey-list">
            {results.map((customer) => (
              <li key={customer.id}>
                <Card
                  actions={
                    <Button
                      onClick={() => {
                        openCustomer(customer)
                      }}
                      variant="primary"
                    >
                      Open {customer.name}
                    </Button>
                  }
                  headingLevel={3}
                  selected={selected?.id === customer.id}
                  title={customer.name}
                >
                  <p className="journey-row__meta">
                    {customer.id} · {customer.phone}
                  </p>
                  <p className="journey-row__meta">
                    {customer.lastOrder === null
                      ? 'No previous order'
                      : `Last order ${formatters.formatShortDate(customer.lastOrder)}`}
                  </p>
                </Card>
              </li>
            ))}
          </ul>
        ) : null}
      </section>

      {selected === null ? null : (
        <>
          <section aria-labelledby="reception-consent" className="journey-section">
            <h2 id="reception-consent">2. Consent</h2>
            <p className="journey-row__meta">
              Recorded against wording version {selected.consent.wordingVersion} on{' '}
              {formatters.formatShortDate(selected.consent.recordedAt)}.
            </p>
            <div className="journey-fields">
              <Checkbox
                description="Keeping the measurement versions so the next order can reuse them."
                label="Storing measurements"
                name="consentMeasurements"
                readOnly
                value={selected.consent.measurementStorage}
              />
              <Checkbox
                description="Photographs of the garment and the customer's own material."
                label="Photographs"
                name="consentPhotos"
                onValueChange={(next) => {
                  setPhotoConsent(next)
                  status.announceAutosave(
                    next ? 'Photograph consent recorded.' : 'Photograph consent withdrawn.',
                  )
                }}
                value={photoConsent}
              />
              <Checkbox
                description="Ready-for-collection and reschedule messages. Declined is recorded as a decline."
                label="Marketing messages"
                name="consentMarketing"
                readOnly
                value={selected.consent.marketingMessages}
              />
            </div>
          </section>

          <section aria-labelledby="reception-order" className="journey-section">
            <h2 id="reception-order">3. The draft order</h2>
            <Card headingLevel={3} title="Blouse — pattern, one garment">
              <dl className="journey-summary">
                <dt>Customer</dt>
                <dd>
                  {selected.name} · {selected.id}
                </dd>
                <dt>Promised</dt>
                <dd>{formatters.formatShortDate(DATES.nextWeek)}</dd>
                <dt>Total, including GST</dt>
                <dd className="journey-amount journey-total">
                  {formatters.formatMoney(INVOICE.total)}
                </dd>
                <dt>Advance to take now</dt>
                <dd className="journey-amount">
                  {formatters.formatMoney(INVOICE.advanceReceived)}
                </dd>
              </dl>
            </Card>
          </section>

          <ButtonGroup size="primary">
            <Button
              iconName="check"
              onClick={() => {
                setConfirming(true)
              }}
              size="primary"
              variant="primary"
            >
              Confirm order
            </Button>
          </ButtonGroup>
        </>
      )}

      {confirmed ? (
        <Alert live="polite" title="Order confirmed" tone="success">
          <p>
            {INVOICE.order} and job J-CBE01-2627-000512-01 have been allocated, and the price and
            measurement snapshots are frozen.
          </p>
          <StatusBadge detail="Label not yet printed" status="draft" />
        </Alert>
      ) : null}

      <ConfirmDialog
        action="confirming this order"
        confirmLabel="Confirm order"
        onCancel={() => {
          setConfirming(false)
        }}
        onConfirm={() => {
          setConfirming(false)
          setConfirmed(true)
        }}
        open={confirming}
        tier="confirm"
        title="Confirm this order?"
      >
        The order and job numbers are allocated now, and the price, design and measurement snapshots
        are frozen against them. The order can still be revised until production starts.
      </ConfirmDialog>
    </section>
  )
}
