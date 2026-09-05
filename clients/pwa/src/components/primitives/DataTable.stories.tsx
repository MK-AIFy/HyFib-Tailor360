import type { Meta, StoryObj } from '@storybook/react-vite'
import { DataTable } from './DataTable'
import type { DataTableColumn } from './DataTable'
import { IconButton } from './IconButton'
import { StatusBadge } from './StatusBadge'
import type { StatusKind } from './statuses'
import { useDemoText } from './demoText'
import './storybook.css'

interface Job {
  readonly jobNumber: string
  readonly customer: string
  readonly category: string
  readonly due: string
  readonly status: StatusKind
  readonly amount: string
}

/** Synthetic data. Nothing here is a real customer, and no screen in this issue talks to an API. */
const jobs: readonly Job[] = [
  {
    jobNumber: 'J-CBE01-2627-000512-01',
    customer: 'Lakshmi Narayanan',
    category: 'Blouse — Aari work',
    due: '12-09-2026',
    status: 'overdue',
    amount: '2,450.00',
  },
  {
    jobNumber: 'J-CBE01-2627-000513-01',
    customer: 'Meena Sundaram',
    category: 'Lehenga',
    due: '14-09-2026',
    status: 'in-progress',
    amount: '1,20,000.00',
  },
  {
    jobNumber: 'J-CBE01-2627-000514-01',
    customer: 'Anitha Devi',
    category: 'Salwar',
    due: '15-09-2026',
    status: 'ready',
    amount: '3,900.00',
  },
  {
    jobNumber: 'J-CBE01-2627-000515-02',
    customer: 'Priya Raghavan',
    category: 'Kids — party frock',
    due: '18-09-2026',
    status: 'held',
    amount: '1,150.00',
  },
]

function useColumns(): readonly DataTableColumn<Job>[] {
  const demo = useDemoText()
  return [
    { id: 'job', header: demo('Job number'), cell: (job) => job.jobNumber, primary: true },
    { id: 'customer', header: demo('Customer'), cell: (job) => demo(job.customer) },
    {
      id: 'category',
      header: demo('Category'),
      cell: (job) => demo(job.category),
      hideWhenNarrow: true,
    },
    { id: 'due', header: demo('Due'), cell: (job) => job.due },
    { id: 'status', header: demo('Status'), cell: (job) => <StatusBadge status={job.status} /> },
    { id: 'amount', header: demo('Amount'), cell: (job) => `₹${job.amount}`, numeric: true },
  ]
}

/**
 * The table that becomes cards.
 *
 * One DOM tree, restyled by a **container query** — so the switch happens at the width of the pane
 * the table is in rather than of the device, costs no JavaScript, and never re-mounts the collection
 * and throws away focus mid-task. Drag the Storybook viewport across 45rem on `Wide` and watch it
 * happen; then compare `Narrow`, which is the same table in a 24rem pane on whatever screen you are
 * reading this on.
 *
 * Every ARIA role is written out in the component, because `display: block` on a table element
 * strips its implicit semantics in every browser. Turn a screen reader on in both layouts: the
 * column headers are still associated with their cells.
 */
const meta = {
  title: 'Primitives/Data table',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

function JobsTable({ withActions = true }: { readonly withActions?: boolean }) {
  const demo = useDemoText()
  const columns = useColumns()

  return (
    <DataTable<Job>
      caption={demo('Garment jobs due this week')}
      columns={columns}
      rows={jobs}
      rowKey={(job) => job.jobNumber}
      rowLabel={(job) => `job ${job.jobNumber}`}
      {...(withActions
        ? {
            rowActions: (job) => (
              <>
                <IconButton
                  name="receipt"
                  size="dense"
                  label={demo(`Print label, job ${job.jobNumber}`)}
                />
                <IconButton
                  name="chevron-right"
                  size="dense"
                  label={demo(`Open job ${job.jobNumber}`)}
                />
              </>
            ),
          }
        : {})}
    />
  )
}

/** A desktop queue. */
export const Wide: Story = {
  render: () => (
    <div className="storybook-wide">
      <JobsTable />
    </div>
  ),
}

/** The same table in a narrow pane — a phone, or a master-detail list beside a detail view. */
export const Narrow: Story = {
  render: () => (
    <div className="storybook-narrow">
      <JobsTable />
    </div>
  ),
}

/** At the 320 px reflow floor of 1.4.10. The page must not scroll sideways here. */
export const ReflowFloor: Story = {
  globals: { viewport: { value: 'reflowFloor' } },
  render: () => <JobsTable />,
}

export const WithoutRowActions: Story = {
  render: () => (
    <div className="storybook-wide">
      <JobsTable withActions={false} />
    </div>
  ),
}

export const Empty: Story = {
  render: function EmptyStory() {
    const demo = useDemoText()
    const columns = useColumns()
    return (
      <DataTable<Job>
        caption={demo('Garment jobs due this week')}
        columns={columns}
        rows={[]}
        rowKey={(job) => job.jobNumber}
        rowLabel={(job) => job.jobNumber}
      />
    )
  },
}

/**
 * The pseudo-locale, at 40% growth, in a narrow pane.
 *
 * Column headings, customer names and both row-action names are all growing. What to look for: the
 * card layout's labels wrapping instead of clipping, and the identifying line still identifying.
 */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <div className="storybook-narrow">
      <JobsTable />
    </div>
  ),
}
