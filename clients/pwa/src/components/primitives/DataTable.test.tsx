import { describe, expect, it } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { DataTable } from './DataTable'
import type { DataTableColumn } from './DataTable'
import { IconButton } from './IconButton'
import { StatusBadge } from './StatusBadge'

interface Job {
  readonly jobNumber: string
  readonly customer: string
  readonly due: string
  readonly amount: string
}

const jobs: readonly Job[] = [
  {
    jobNumber: 'J-CBE01-2627-000512-01',
    customer: 'Lakshmi Narayanan',
    due: '12-09-2026',
    amount: '2,450.00',
  },
  {
    jobNumber: 'J-CBE01-2627-000513-01',
    customer: 'Meena Sundaram',
    due: '14-09-2026',
    amount: '1,20,000.00',
  },
]

const columns: readonly DataTableColumn<Job>[] = [
  { id: 'job', header: 'Job number', cell: (job) => job.jobNumber, primary: true },
  { id: 'customer', header: 'Customer', cell: (job) => job.customer },
  { id: 'due', header: 'Due', cell: (job) => job.due, hideWhenNarrow: true },
  { id: 'amount', header: 'Amount', cell: (job) => job.amount, numeric: true },
]

function renderTable(overrides: Partial<Parameters<typeof DataTable<Job>>[0]> = {}) {
  return renderWithProviders(
    <DataTable<Job>
      caption="Jobs due this week"
      columns={columns}
      rows={jobs}
      rowKey={(job) => job.jobNumber}
      rowLabel={(job) => `job ${job.jobNumber}`}
      {...overrides}
    />,
  )
}

describe('DataTable', () => {
  it('is a table, and says so explicitly rather than relying on the element', () => {
    const { getByRole } = renderTable()

    // The card layout sets display:block on the table elements, which strips their implicit
    // semantics in every browser. The explicit roles are what keep the cells associated with their
    // column headers in both layouts; without them the card layout announces unlabelled fragments.
    expect(getByRole('table', { name: 'Jobs due this week' })).toBeInTheDocument()
    expect(getByRole('columnheader', { name: 'Job number' })).toBeInTheDocument()
    expect(getByRole('cell', { name: 'Lakshmi Narayanan' })).toBeInTheDocument()
  })

  it('keeps every column header in the accessibility tree, in both layouts', () => {
    const { getAllByRole } = renderTable()

    // The card layout clips the header row rather than removing it: display:none would take the
    // headers out of the tree and every cell would lose its association.
    expect(getAllByRole('columnheader')).toHaveLength(columns.length)
  })

  it('scrolls inside its own named, focusable region so the page never scrolls sideways', () => {
    const { getByRole } = renderTable()
    const region = getByRole('region', { name: 'Jobs due this week' })

    // 1.4.10 Reflow, and the rule that a scrollable region must be reachable from a keyboard: the
    // counter desktop does not always have a mouse.
    expect(region).toHaveAttribute('tabindex', '0')
  })

  it('repeats the column name inside each cell without announcing it twice', () => {
    const { container } = renderTable()
    const labels = container.querySelectorAll('.data-table__cell-label')

    expect(labels.length).toBeGreaterThan(0)
    for (const label of labels) {
      // Shown only in the card layout, where the header row is off screen; hidden from assistive
      // technology because the cell is already associated with its columnheader by role.
      expect(label).toHaveAttribute('aria-hidden', 'true')
    }
  })

  it('marks the identifying column, which becomes the card heading when it narrows', () => {
    const { container } = renderTable()

    expect(container.querySelector('[data-column="job"][data-primary="true"]')).not.toBeNull()
  })

  it('marks the columns that drop out of the card layout', () => {
    const { container } = renderTable()

    expect(
      container.querySelector('[data-column="due"][data-hide-when-narrow="true"]'),
    ).not.toBeNull()
  })

  it('names each row action group with the row it belongs to', () => {
    const { getByRole } = renderTable({
      rowActions: (job) => (
        <IconButton name="receipt" label={`Print label, job ${job.jobNumber}`} />
      ),
    })

    // Checklist item A11Y-60: eleven identical "Print" buttons in a queue is a custody error waiting
    // to happen.
    expect(
      getByRole('group', { name: 'Actions for job J-CBE01-2627-000512-01' }),
    ).toBeInTheDocument()
  })

  it('says so when there is nothing to show, rather than rendering an empty box', () => {
    const { getByText } = renderTable({ rows: [] })

    expect(getByText('Nothing to show yet.')).toBeInTheDocument()
  })

  it('takes a caller-supplied empty state', () => {
    const { getByText } = renderTable({
      rows: [],
      empty: 'No jobs are due this week. Check the next week instead.',
    })

    expect(getByText('No jobs are due this week. Check the next week instead.')).toBeInTheDocument()
  })

  it('keeps the caption in the accessibility tree even when it is hidden visually', () => {
    const { getByRole, container } = renderTable({ hideCaption: true })

    expect(getByRole('table', { name: 'Jobs due this week' })).toBeInTheDocument()
    expect(container.querySelector('caption')).toHaveClass('visually-hidden')
  })

  it('translates its own words, which the pseudo-locale makes visible', () => {
    const { container } = renderWithProviders(
      <DataTable<Job>
        caption="Jobs"
        columns={columns}
        rows={[]}
        rowKey={(job) => job.jobNumber}
        rowLabel={(job) => job.jobNumber}
      />,
      { locale: 'en-XA' },
    )

    expect(container.textContent).toMatch(/⟦.+⟧/)
  })

  it('has no accessibility violations', async () => {
    const { container } = renderTable({
      rowActions: (job) => (
        <>
          <IconButton name="receipt" label={`Print label, job ${job.jobNumber}`} />
          <IconButton name="chevron-right" label={`Open job ${job.jobNumber}`} />
        </>
      ),
    })

    await expectNoAccessibilityViolations(container)
  })

  it('renders a status badge inside a cell without losing the cell semantics', () => {
    const withStatus: readonly DataTableColumn<Job>[] = [
      ...columns,
      { id: 'status', header: 'Status', cell: () => <StatusBadge status="overdue" /> },
    ]
    const { getAllByRole } = renderWithProviders(
      <DataTable<Job>
        caption="Jobs"
        columns={withStatus}
        rows={jobs}
        rowKey={(job) => job.jobNumber}
        rowLabel={(job) => job.jobNumber}
      />,
    )

    expect(getAllByRole('cell', { name: /Overdue/ })).toHaveLength(jobs.length)
  })
})
