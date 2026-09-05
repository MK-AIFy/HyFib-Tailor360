import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import type { ReactNode } from 'react'
import type { ShellKind } from '../../design-system/foundations/types'
import { Button } from '../primitives/Button'
import { BottomSheet } from './BottomSheet'
import { Dialog } from './Dialog'
import { Drawer } from './Drawer'
import type { DialogPresentation } from './dialogVariants'
import './dialogsStories.css'

/**
 * A modal: a bottom sheet on a phone, a centred dialog on a tablet or a desktop, a drawer where the
 * screen behind has to stay visible.
 *
 * Open one and try the four things checklist items A11Y-61 to A11Y-64 ask about — focus moves in,
 * `Tab` cycles only within it, `Escape` closes it, and focus goes back to the control that opened
 * it. Then narrow the viewport to 320 px and open it again: the same dialog arrives from the bottom
 * edge, with its footer clear of the gesture bar.
 */
const meta = {
  title: 'Dialogs/Dialog',
  component: Dialog,
  // Defaults so that a story which supplies its own `render` — every one below, because a modal
  // needs something to open it — does not have to restate the required props.
  args: {
    open: true,
    title: 'Alteration note',
    children: 'Anything typed here is added to the job card.',
    onClose: () => undefined,
  },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof Dialog>

export default meta
type Story = StoryObj<typeof meta>

interface OpenerProps {
  readonly label: string
  readonly presentation?: DialogPresentation
  readonly shellKind?: ShellKind
  readonly title: string
  readonly description?: string
  readonly children: ReactNode
}

/** A page with something to open the dialog from, so focus has somewhere real to come from and go back to. */
function Opener({ label, presentation, shellKind, title, description, children }: OpenerProps) {
  const [open, setOpen] = useState(false)

  return (
    <div className="dialogs-story-page">
      <p>
        Open the dialog, then press <kbd>Escape</kbd> or <kbd>Tab</kbd> around it. Focus comes back
        to the control you opened it from.
      </p>
      <div>
        <Button
          onClick={() => {
            setOpen(true)
          }}
          variant="primary"
        >
          {label}
        </Button>
      </div>
      <Dialog
        footer={
          <>
            <Button
              onClick={() => {
                setOpen(false)
              }}
            >
              Cancel
            </Button>
            <Button
              onClick={() => {
                setOpen(false)
              }}
              variant="primary"
            >
              Save
            </Button>
          </>
        }
        onClose={() => {
          setOpen(false)
        }}
        open={open}
        title={title}
        {...(presentation === undefined ? {} : { presentation })}
        {...(shellKind === undefined ? {} : { shellKind })}
        {...(description === undefined ? {} : { description })}
      >
        {children}
      </Dialog>
    </div>
  )
}

/** The default. On a phone-width viewport it is a sheet; wider, it is centred. */
export const Auto: Story = {
  render: () => (
    <Opener
      description="Anything typed here is added to the job card."
      label="Add an alteration note"
      title="Alteration note"
    >
      <p>Narrow the viewport below 768 px and open it again to see the sheet.</p>
    </Opener>
  ),
}

/** Forced to the sheet, whatever the width — a picker over a tablet's master-detail layout. */
export const Sheet: Story = {
  render: () => (
    <Opener label="Choose a phase" presentation="sheet" title="Move to which phase?">
      <p>The footer sits clear of the system gesture bar, which nothing may occupy.</p>
    </Opener>
  ),
}

/** Forced to the centred dialog. */
export const Centre: Story = {
  render: () => (
    <Opener label="Edit the note" presentation="centre" title="Alteration note">
      <p>Centred, because on a desktop the pointer is already where the eye is.</p>
    </Opener>
  ),
}

/** A drawer: the list behind stays visible and in place, and stays `inert` while it is open. */
export const AsADrawer: Story = {
  render: () => {
    function DrawerOpener() {
      const [open, setOpen] = useState(false)
      return (
        <div className="dialogs-story-page">
          <p>A filter panel opens without taking away the queue a person is working through.</p>
          <div>
            <Button
              onClick={() => {
                setOpen(true)
              }}
              variant="primary"
            >
              Filters
            </Button>
          </div>
          <Drawer
            footer={
              <Button
                onClick={() => {
                  setOpen(false)
                }}
                variant="primary"
              >
                Apply
              </Button>
            }
            onClose={() => {
              setOpen(false)
            }}
            open={open}
            title="Filters"
          >
            <p>Branch, phase, due date, and whether the job is on hold.</p>
          </Drawer>
        </div>
      )
    }

    return <DrawerOpener />
  },
}

/** A sheet opened from a story pinned to the phone shell, for review at the reflow floor. */
export const OnAPhone: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: () => (
    <Opener label="Add a note" shellKind="phone" title="Alteration note">
      <p>The sheet is full width, and its footer stacks so Cancel and Save cannot be confused.</p>
    </Opener>
  ),
}

/** A bottom sheet asked for by name. */
export const AsABottomSheet: Story = {
  render: () => {
    function SheetOpener() {
      const [open, setOpen] = useState(false)
      return (
        <div className="dialogs-story-page">
          <div>
            <Button
              onClick={() => {
                setOpen(true)
              }}
              variant="primary"
            >
              Row actions
            </Button>
          </div>
          <BottomSheet
            onClose={() => {
              setOpen(false)
            }}
            open={open}
            title="Job J-CBE01-2627-000512-01"
          >
            <p>Print the label, move the phase, or open the job card.</p>
          </BottomSheet>
        </div>
      )
    }

    return <SheetOpener />
  },
}

/** The pseudo-locale, at 40% growth. Watch the title and the footer, which grow first. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <Opener
      description="Anything typed here is added to the job card."
      label="Add an alteration note"
      shellKind="phone"
      title="Alteration note"
    >
      <p>The sheet keeps its shape at 40% growth and at the 320 px reflow floor.</p>
    </Opener>
  ),
}
