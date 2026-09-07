import type { Meta, StoryObj } from '@storybook/react-vite'
import { CashierPaymentScreen } from './CashierPayment'
import { DeliveryDispatchScreen } from './DeliveryDispatch'
import { InventoryStockEntryScreen } from './InventoryStockEntry'
import { JourneyFrame } from './JourneyFrame'
import { MeasurementWizardScreen } from './MeasurementWizard'
import { OwnerDashboardScreen } from './OwnerDashboard'
import { ReceptionIntakeScreen } from './ReceptionIntake'
import { TailorMasterWorkboardScreen } from './TailorMasterWorkboard'
import { TailorQueueScreen } from './TailorQueue'
import './journeys.css'

/**
 * The eight reference journeys of issue #50, one per role, against synthetic data.
 *
 * ## What these are for
 *
 * The #50 blueprint calls for "reference journeys per role in Storybook against synthetic data …
 * used for role walkthroughs and the device/orientation/zoom matrix", and
 * docs/nfr/a11y-checklist.md section 6.8 names the same eight `A11Y-RJ-01` to `A11Y-RJ-08`. Three of
 * them — Reception, Cashier and Delivery — are walked under a priority-zero record rather than
 * twice under a second name; the other five have their own records. Either way, this is the screen
 * the record is walked *against*, which is why the journeys exist before the modules that will
 * eventually own these screens do.
 *
 * ## Why each story pins a device
 *
 * `AppShell` chooses phone, tablet or desktop by measuring its own container, which is right in the
 * application and useless in a Storybook frame as wide as the browser. Every story therefore names
 * both the shell and the frame width, and the widths are the five `expectNoHorizontalOverflow`
 * asserts at. Pinning them is what lets a reviewer put the phone and desktop journeys side by side,
 * and what stops #52's visual baselines moving with the window.
 *
 * The device each role gets is not arbitrary: the counter roles get the tablet, the two roles that
 * work with a garment in one hand get the phone, and the two that read get the desktop. The last
 * three stories re-run the phone-shaped journeys at the 320 px reflow floor, because that is where a
 * layout that merely looks tight becomes a layout that scrolls sideways.
 *
 * ## What to do with them
 *
 * The four toolbar controls all apply. The pseudo-locale is the 40% text-growth check — including
 * over the fixture text, which is most of what a queue actually contains; the sunlight theme is the
 * contrast check; 150% is the product's own text-size preference; and compact density collapses
 * back to 44 px targets on a coarse pointer, which the story also demonstrates.
 *
 * Every name, number, measurement and address is synthetic and comes from
 * docs/prd/walkthroughs.md section 1.2. None of it is a real customer.
 */
const meta = {
  title: 'Journeys/Reference journeys',
  parameters: { layout: 'centered' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

/**
 * `A11Y-RJ-01` — Reception: find the customer, then intake.
 *
 * Walked as `A11Y-PZ-01` (order confirmation), whose steps 1 to 8 are this journey. A counter
 * tablet, because Reception has both hands and a customer in front of them.
 */
export const ReceptionIntake: Story = {
  render: () => (
    <JourneyFrame role="reception" route="/orders/new" shellKind="tablet" width="768">
      <ReceptionIntakeScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-02` — Measurement Staff: the measurement wizard.
 *
 * Its own record. The tablet again: a tape in one hand and the device flat on the counter.
 */
export const MeasurementWizard: Story = {
  render: () => (
    <JourneyFrame role="measurement-staff" route="/measurements" shellKind="tablet" width="768">
      <MeasurementWizardScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-03` — Tailor: scan in, work a phase, scan out.
 *
 * A phone, scanner-first, with the personal queue badged. This is the journey the offline queue is
 * for: completing a phase is queued when there is no signal, and the replay outcome is reported
 * rather than assumed.
 */
export const TailorScanToQueue: Story = {
  render: () => (
    <JourneyFrame
      badges={{ production: 3 }}
      role="tailor"
      route="/production"
      shellKind="phone"
      width="360"
    >
      <TailorQueueScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-04` — Tailor Master: the workboard, assignment, start production and QC.
 *
 * A desktop, because this is the one role whose work is a table of everything at once. Every row
 * control is a button; there is no drag handle in the journey to fall back from.
 */
export const TailorMasterWorkboard: Story = {
  render: () => (
    <JourneyFrame
      badges={{ workboard: 6 }}
      role="tailor-master"
      route="/workboard"
      shellKind="desktop"
      width="1280"
    >
      <TailorMasterWorkboardScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-05` — Inventory Clerk: receive, issue against a job, and count.
 *
 * A tablet at the rack. Every quantity carries its unit and every purchase its conversion, because a
 * clerk receiving in rolls and issuing in metres is where a stock ledger goes wrong silently.
 */
export const InventoryStockEntry: Story = {
  render: () => (
    <JourneyFrame
      badges={{ inventory: 2 }}
      role="inventory"
      route="/inventory"
      shellKind="tablet"
      width="768"
    >
      <InventoryStockEntryScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-06` — Cashier: taking a payment.
 *
 * Walked as `A11Y-PZ-05` (payment recording). A counter tablet, and the one journey where offline is
 * a refusal rather than a queue.
 */
export const CashierPayment: Story = {
  render: () => (
    <JourneyFrame
      badges={{ billing: 2 }}
      role="cashier"
      route="/billing"
      shellKind="tablet"
      width="768"
    >
      <CashierPaymentScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-07` — Delivery Staff: the branch queue through to a doorstep.
 *
 * Walked as `A11Y-PZ-03` (the dispatch gate). A phone, on a scooter, in the sun — try this one in
 * the high-contrast theme, which is what it was designed for.
 */
export const DeliveryQueueToDispatch: Story = {
  render: () => (
    <JourneyFrame
      badges={{ delivery: 3 }}
      role="delivery"
      route="/delivery"
      shellKind="phone"
      width="360"
    >
      <DeliveryDispatchScreen />
    </JourneyFrame>
  ),
}

/**
 * `A11Y-RJ-08` — Owner: the dashboard, its alerts and a report.
 *
 * Its own record — reports and alerts, which no other record opens. A desktop, and the only role
 * with no primary shop-floor action: this one is read, not done.
 */
export const OwnerDashboard: Story = {
  render: () => (
    <JourneyFrame badges={{ workboard: 2 }} role="owner" route="/" shellKind="desktop" width="1280">
      <OwnerDashboardScreen />
    </JourneyFrame>
  ),
}

/* The reflow floor ---------------------------------------------------------------------------- */

/**
 * The tailor's journey at 320 CSS px.
 *
 * 1.4.10 Reflow: no horizontal page scroll at 320 px, and a wide table scrolls inside its own
 * container instead of taking the page with it. 320 is not a device anybody carries — it is the
 * width a 640 px phone becomes at 200% zoom, which is the setting somebody actually turns on.
 */
export const TailorAtTheReflowFloor: Story = {
  render: () => (
    <JourneyFrame
      badges={{ production: 3 }}
      role="tailor"
      route="/production"
      shellKind="phone"
      width="320"
    >
      <TailorQueueScreen />
    </JourneyFrame>
  ),
}

/** The delivery journey at 320 CSS px, including the doorstep form and its two fields. */
export const DeliveryAtTheReflowFloor: Story = {
  render: () => (
    <JourneyFrame
      badges={{ delivery: 3 }}
      role="delivery"
      route="/delivery"
      shellKind="phone"
      width="320"
    >
      <DeliveryDispatchScreen />
    </JourneyFrame>
  ),
}

/**
 * The payment journey on a phone at the reflow floor.
 *
 * A counter tablet is where this journey belongs, but the form is the thing most likely to break the
 * floor — an amount field with a leading unit, a radio group and an error summary — so it is walked
 * at 320 px as well.
 */
export const CashierAtTheReflowFloor: Story = {
  render: () => (
    <JourneyFrame
      badges={{ billing: 2 }}
      role="cashier"
      route="/billing"
      shellKind="phone"
      width="320"
    >
      <CashierPaymentScreen />
    </JourneyFrame>
  ),
}
