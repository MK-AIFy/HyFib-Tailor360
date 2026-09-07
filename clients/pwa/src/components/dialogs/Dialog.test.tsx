import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import type { ShellKind } from '../../design-system/foundations/types'
import { Button } from '../primitives/Button'
import { BottomSheet } from './BottomSheet'
import { Dialog } from './Dialog'
import { Drawer } from './Drawer'

/** A screen with something that opens a dialog, so focus has somewhere real to come from and go back to. */
function Harness({ shellKind }: { readonly shellKind?: ShellKind }) {
  const [open, setOpen] = useState(false)

  return (
    <div>
      <button
        onClick={() => {
          setOpen(true)
        }}
        type="button"
      >
        Open the dialog
      </button>
      <Dialog
        footer={<Button>Save</Button>}
        onClose={() => {
          setOpen(false)
        }}
        open={open}
        title="Alteration note"
        {...(shellKind === undefined ? {} : { shellKind })}
      >
        <p>Anything typed here belongs to the job card.</p>
      </Dialog>
    </div>
  )
}

describe('Dialog', () => {
  it('renders nothing at all while it is closed', () => {
    const { queryByRole } = renderWithProviders(
      <Dialog onClose={() => undefined} open={false} title="Alteration note">
        Body
      </Dialog>,
    )

    expect(queryByRole('dialog')).toBeNull()
  })

  it('is a modal named by its heading', () => {
    const { getByRole } = renderWithProviders(
      <Dialog onClose={() => undefined} open title="Alteration note">
        Body
      </Dialog>,
    )

    // A11Y-61: the dialog's name is announced when it opens, or it is invisible and the screen
    // behind it merely appears to have stopped working.
    const dialog = getByRole('dialog', { name: 'Alteration note' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
  })

  it('describes itself with what it is about, so the consequence is heard before the controls', () => {
    const { getByRole } = renderWithProviders(
      <Dialog
        description="The job will move back to cutting."
        onClose={() => undefined}
        open
        title="Send back"
      >
        Body
      </Dialog>,
    )

    expect(getByRole('dialog')).toHaveAccessibleDescription('The job will move back to cutting.')
  })

  it('moves focus into itself when it opens', async () => {
    const { getByRole } = renderWithProviders(<Harness />)

    await userEvent.click(getByRole('button', { name: 'Open the dialog' }))

    // The first focusable element, which is the close control — never the confirming one, because a
    // dialog that opens with Confirm focused is a dialog a held Enter key answers.
    expect(getByRole('button', { name: 'Close' })).toHaveFocus()
  })

  it('gives focus back to whatever opened it', async () => {
    const { getByRole } = renderWithProviders(<Harness />)
    const opener = getByRole('button', { name: 'Open the dialog' })

    await userEvent.click(opener)
    await userEvent.click(getByRole('button', { name: 'Close' }))

    // A11Y-64. Focus dumped on the document body means re-tabbing the whole screen, one-handed.
    expect(opener).toHaveFocus()
  })

  it('closes on Escape', async () => {
    const onClose = vi.fn()
    const { getByRole } = renderWithProviders(
      <Dialog onClose={onClose} open title="Alteration note">
        Body
      </Dialog>,
    )
    getByRole('button', { name: 'Close' }).focus()

    await userEvent.keyboard('{Escape}')

    // A11Y-63, and 2.1.2 is absolute: there is no prop on this component that could switch it off.
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('cycles the tab order back into itself at both ends', async () => {
    const { getByRole } = renderWithProviders(
      <Dialog footer={<Button>Save</Button>} onClose={() => undefined} open title="Alteration note">
        <p>Body</p>
      </Dialog>,
    )
    const close = getByRole('button', { name: 'Close' })
    const save = getByRole('button', { name: 'Save' })

    save.focus()
    await userEvent.tab()
    // A11Y-62: reading the page behind a modal is how a confirmation gets answered for the wrong record.
    expect(close).toHaveFocus()

    await userEvent.tab({ shift: true })
    expect(save).toHaveFocus()
  })

  it('makes the rest of the document inert while it is open, and releases it afterwards', async () => {
    const { getByRole, container } = renderWithProviders(<Harness />)

    await userEvent.click(getByRole('button', { name: 'Open the dialog' }))
    // inert rather than aria-hidden: aria-hidden over focusable content is itself a violation,
    // because a keyboard user can land on a control no screen reader will describe.
    expect(container).toHaveAttribute('inert')

    await userEvent.click(getByRole('button', { name: 'Close' }))
    expect(container).not.toHaveAttribute('inert')
  })

  it('stops the page behind it scrolling, and lets it go again', async () => {
    const { getByRole } = renderWithProviders(<Harness />)

    await userEvent.click(getByRole('button', { name: 'Open the dialog' }))
    expect(document.documentElement).toHaveClass('has-open-dialog')

    await userEvent.click(getByRole('button', { name: 'Close' }))
    expect(document.documentElement).not.toHaveClass('has-open-dialog')
  })

  it('closes when the scrim is pressed, and only when the press landed on the scrim', async () => {
    const onClose = vi.fn()
    const { getByRole, baseElement } = renderWithProviders(
      <Dialog onClose={onClose} open title="Alteration note">
        Body
      </Dialog>,
    )

    await userEvent.click(getByRole('dialog'))
    expect(onClose).not.toHaveBeenCalled()

    const scrim = baseElement.querySelector('.dialog-scrim')
    expect(scrim).not.toBeNull()
    await userEvent.click(scrim as Element)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('keeps a scrim press from throwing away typed input when told to', async () => {
    const onClose = vi.fn()
    const { baseElement } = renderWithProviders(
      <Dialog closeOnScrimPress={false} onClose={onClose} open title="Reason">
        Body
      </Dialog>,
    )

    await userEvent.click(baseElement.querySelector('.dialog-scrim') as Element)

    // 2.5.2 Pointer Cancellation, in the form that matters here: a mis-touch while holding a
    // garment must not throw away a typed reason.
    expect(onClose).not.toHaveBeenCalled()
  })

  it('arrives from the bottom edge on a phone and is centred on a desktop', () => {
    const phone = renderWithProviders(
      <Dialog onClose={() => undefined} open shellKind="phone" title="Alteration note">
        Body
      </Dialog>,
    )
    expect(phone.getByRole('dialog')).toHaveAttribute('data-presentation', 'sheet')
    phone.unmount()

    const desktop = renderWithProviders(
      <Dialog onClose={() => undefined} open shellKind="desktop" title="Alteration note">
        Body
      </Dialog>,
    )
    expect(desktop.getByRole('dialog')).toHaveAttribute('data-presentation', 'centre')
  })

  it('reads its own words from the catalogue, which the pseudo-locale makes visible', () => {
    const { getByRole } = renderWithProviders(
      <Dialog onClose={() => undefined} open title="Alteration note">
        Body
      </Dialog>,
      { locale: 'en-XA' },
    )

    // The one string this component supplies itself is the close control's name.
    expect(getByRole('dialog').textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { baseElement } = renderWithProviders(
      <Dialog
        description="The job will move back to cutting."
        footer={<Button>Save</Button>}
        onClose={() => undefined}
        open
        title="Alteration note"
      >
        <p>Anything typed here belongs to the job card.</p>
      </Dialog>,
    )

    await expectNoAccessibilityViolations(baseElement)
  })
})

describe('BottomSheet', () => {
  it('arrives from the bottom edge whatever the width', () => {
    const { getByRole } = renderWithProviders(
      <BottomSheet onClose={() => undefined} open shellKind="desktop" title="Choose a phase">
        Body
      </BottomSheet>,
    )

    expect(getByRole('dialog')).toHaveAttribute('data-presentation', 'sheet')
  })
})

describe('Drawer', () => {
  it('is a full-height panel on the trailing edge by default', () => {
    const { getByRole } = renderWithProviders(
      <Drawer onClose={() => undefined} open title="Filters">
        Body
      </Drawer>,
    )

    const dialog = getByRole('dialog')
    expect(dialog).toHaveAttribute('data-presentation', 'drawer')
    expect(dialog).toHaveClass('dialog--drawer-end')
  })

  it('can come from the leading edge instead', () => {
    const { getByRole } = renderWithProviders(
      <Drawer onClose={() => undefined} open side="inline-start" title="Sections">
        Body
      </Drawer>,
    )

    expect(getByRole('dialog')).toHaveClass('dialog--drawer-start')
  })

  it('is still a modal: the list behind it cannot be tabbed past', () => {
    const { container } = renderWithProviders(
      <Drawer onClose={() => undefined} open title="Filters">
        Body
      </Drawer>,
    )

    // A filter panel that can be tabbed past is a filter panel whose Apply button nobody finds.
    expect(container).toHaveAttribute('inert')
  })
})
