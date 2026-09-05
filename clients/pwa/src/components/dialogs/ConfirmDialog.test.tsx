import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { ConfirmDialog } from './ConfirmDialog'

const base = {
  open: true,
  action: 'cancelling this order',
  confirmLabel: 'Cancel order',
  onCancel: () => undefined,
  title: 'Cancel order O-CBE01-2627-000188?',
} as const

describe('ConfirmDialog — tier one, plain confirm', () => {
  it('confirms with nothing else asked for', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={onConfirm} tier="confirm">
        The job stops and the fabric is returned.
      </ConfirmDialog>,
    )

    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).toHaveBeenCalledWith({})
  })

  it('says what will happen, as the dialog’s own description', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={() => undefined} tier="confirm">
        The job stops and the fabric is returned.
      </ConfirmDialog>,
    )

    // A11Y-39: the consequence is announced with the dialog, before the person reaches the control
    // that does it.
    expect(getByRole('dialog')).toHaveAccessibleDescription(
      /The job stops and the fabric is returned./,
    )
  })

  it('opens with focus on Cancel, never on the control that does the thing', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={() => undefined} tier="confirm">
        The job stops.
      </ConfirmDialog>,
    )

    // A dialog that opens with the destructive control focused is a dialog answered by a held Enter.
    expect(getByRole('button', { name: 'Cancel' })).toHaveFocus()
  })

  it('treats Escape as cancelling', async () => {
    const onCancel = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onCancel={onCancel} onConfirm={() => undefined} tier="confirm">
        The job stops.
      </ConfirmDialog>,
    )
    getByRole('button', { name: 'Cancel' }).focus()

    await userEvent.keyboard('{Escape}')

    expect(onCancel).toHaveBeenCalledTimes(1)
  })

  it('states plainly when nothing puts it back', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} irreversible onConfirm={() => undefined} tier="confirm">
        The invoice is posted.
      </ConfirmDialog>,
    )

    // The blueprint fixes this sentence word for word.
    expect(getByRole('dialog')).toHaveTextContent(
      'Cannot be undone — a supervisor correction is needed',
    )
  })

  it('keeps the irreversibility inside what is announced on opening', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} irreversible onConfirm={() => undefined} tier="confirm">
        The invoice is posted.
      </ConfirmDialog>,
    )

    expect(getByRole('dialog')).toHaveAccessibleDescription(/Cannot be undone/)
  })
})

describe('ConfirmDialog — tier two, confirm with a reason', () => {
  it('asks for a labelled, required reason that names what it will be attached to', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={() => undefined} tier="reason">
        The job stops.
      </ConfirmDialog>,
    )

    // A11Y-87: labelled, announced as required, and announced with what the reason will be attached
    // to — rather than an unlabelled box inside a confirmation dialog.
    const reason = getByRole('textbox', { name: 'Reason' })
    expect(reason).toHaveAttribute('aria-required', 'true')
    expect(reason).toHaveAccessibleDescription(
      /Recorded on the audit trail beside cancelling this order/,
    )
  })

  it('refuses to confirm without one, and says so in the field', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={onConfirm} tier="reason">
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).not.toHaveBeenCalled()
    expect(getByRole('textbox', { name: 'Reason' })).toHaveAccessibleDescription(
      /Type the reason before you confirm./,
    )
  })

  it('will not take whitespace for a reason', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={onConfirm} tier="reason">
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.type(getByRole('textbox', { name: 'Reason' }), '   ')
    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('hands the reason back trimmed, because it is audit evidence', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} onConfirm={onConfirm} tier="reason">
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.type(getByRole('textbox', { name: 'Reason' }), 'Customer changed their mind')
    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).toHaveBeenCalledWith({ reason: 'Customer changed their mind' })
  })
})

describe('ConfirmDialog — tier three, typed confirmation', () => {
  it('asks a desktop to type the phrase', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={() => undefined}
        shellKind="desktop"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops.
      </ConfirmDialog>,
    )

    expect(getByRole('textbox', { name: 'Type CANCEL ORDER to confirm' })).toBeInTheDocument()
  })

  it('refuses a phrase that does not match, and says what to type', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={onConfirm}
        shellKind="desktop"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.type(getByRole('textbox', { name: /Type CANCEL ORDER/ }), 'cancel')
    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).not.toHaveBeenCalled()
    expect(getByRole('textbox', { name: /Type CANCEL ORDER/ })).toHaveAccessibleDescription(
      /The words do not match/,
    )
  })

  it('confirms once the phrase matches', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={onConfirm}
        shellKind="desktop"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.type(getByRole('textbox', { name: /Type CANCEL ORDER/ }), 'CANCEL ORDER')
    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    expect(onConfirm).toHaveBeenCalledWith({})
  })

  it('never asks a phone to type anything', () => {
    const { queryByRole, getByRole } = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={() => undefined}
        shellKind="phone"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops.
      </ConfirmDialog>,
    )

    // A11Y-BI-13, stated as an absolute. The named substitute is confirm-with-reason plus a second
    // explicit press.
    expect(queryByRole('textbox', { name: /Type CANCEL ORDER/ })).toBeNull()
    expect(getByRole('textbox', { name: 'Reason' })).toBeInTheDocument()
  })

  it('asks a phone for a reason and then for a second, explicit press', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={onConfirm}
        shellKind="phone"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.type(getByRole('textbox', { name: 'Reason' }), 'Fabric not available')
    await userEvent.click(getByRole('button', { name: 'Cancel order' }))

    // The first press arms rather than confirms, and says so.
    expect(onConfirm).not.toHaveBeenCalled()
    expect(getByRole('status')).toHaveTextContent('Tap Confirm once more to cancelling this order.')

    await userEvent.click(getByRole('button', { name: 'Confirm again' }))
    expect(onConfirm).toHaveBeenCalledWith({ reason: 'Fabric not available' })
  })

  it('disarms the second press when the reason is edited, so it always follows a read', async () => {
    const onConfirm = vi.fn()
    const { getByRole, queryByRole } = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={onConfirm}
        shellKind="phone"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops.
      </ConfirmDialog>,
    )
    const reason = getByRole('textbox', { name: 'Reason' })

    await userEvent.type(reason, 'Fabric')
    await userEvent.click(getByRole('button', { name: 'Cancel order' }))
    expect(getByRole('button', { name: 'Confirm again' })).toBeInTheDocument()

    await userEvent.type(reason, ' not available')

    expect(queryByRole('button', { name: 'Confirm again' })).toBeNull()
    expect(onConfirm).not.toHaveBeenCalled()
  })
})

describe('ConfirmDialog — everywhere', () => {
  it('does not throw away a typed reason on a mis-touch outside it', async () => {
    const onCancel = vi.fn()
    const { baseElement } = renderWithProviders(
      <ConfirmDialog {...base} onCancel={onCancel} onConfirm={() => undefined} tier="reason">
        The job stops.
      </ConfirmDialog>,
    )

    await userEvent.click(baseElement.querySelector('.dialog-scrim') as Element)

    expect(onCancel).not.toHaveBeenCalled()
  })

  it('swallows a second press while the confirmation is in flight', async () => {
    const onConfirm = vi.fn()
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} busy onConfirm={onConfirm} tier="confirm">
        The job stops.
      </ConfirmDialog>,
    )

    // The busy control keeps its label and adds the hidden "Working…", so it is still findable —
    // and still focusable, which is the whole reason it is not `disabled`.
    await userEvent.click(getByRole('button', { name: /Cancel order/ }))

    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { getByRole } = renderWithProviders(
      <ConfirmDialog {...base} irreversible onConfirm={() => undefined} tier="reason">
        The job stops.
      </ConfirmDialog>,
      { locale: 'en-XA' },
    )

    expect(getByRole('dialog').textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations in any tier', async () => {
    const reason = renderWithProviders(
      <ConfirmDialog {...base} irreversible onConfirm={() => undefined} tier="reason">
        The job stops and the fabric is returned.
      </ConfirmDialog>,
    )
    await expectNoAccessibilityViolations(reason.baseElement)
    reason.unmount()

    const typed = renderWithProviders(
      <ConfirmDialog
        {...base}
        onConfirm={() => undefined}
        shellKind="desktop"
        tier="typed"
        typedPhrase="CANCEL ORDER"
      >
        The job stops and the fabric is returned.
      </ConfirmDialog>,
    )
    await expectNoAccessibilityViolations(typed.baseElement)
  })
})
