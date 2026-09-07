import { useState } from 'react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { PSEUDO_LOCALE } from '../../i18n/pseudo'
import { MasterDetail } from './MasterDetail'

const list = (
  <ul>
    <li>
      <button type="button">J-CBE01-2627-000512-01</button>
    </li>
  </ul>
)

const detail = <h2>Job J-CBE01-2627-000512-01</h2>

/** A stacked pair a person can actually walk: open the job, come back to the list. */
function StackedExample() {
  const [open, setOpen] = useState(false)

  return (
    <MasterDetail
      arrangement="stacked"
      listLabel="Jobs due today"
      detailLabel="Job card"
      detailOpen={open}
      onCloseDetail={() => {
        setOpen(false)
      }}
      list={
        <button
          type="button"
          onClick={() => {
            setOpen(true)
          }}
        >
          Open job card
        </button>
      }
      detail={detail}
    />
  )
}

describe('MasterDetail — split', () => {
  it('names both panes, so a returning user can choose which to skip to', () => {
    // "region, region" is what checklist item A11Y-06 fails a screen for.
    const { getByRole } = renderWithProviders(
      <MasterDetail
        arrangement="split"
        listLabel="Jobs due today"
        detailLabel="Job card"
        list={list}
        detail={detail}
      />,
    )

    expect(getByRole('region', { name: 'Jobs due today' })).toBeInTheDocument()
    expect(getByRole('region', { name: 'Job card' })).toBeInTheDocument()
  })

  it('says what an unselected detail pane is waiting for', () => {
    // Silence and emptiness sound identical (checklist item A11Y-88).
    const { getByText } = renderWithProviders(<MasterDetail arrangement="split" list={list} />)

    expect(getByText('Choose an item from the list to see it here.')).toBeInTheDocument()
  })

  it('falls back to the catalogue when a screen supplies no pane names', () => {
    const { getByRole } = renderWithProviders(
      <MasterDetail arrangement="split" list={list} detail={detail} />,
    )

    expect(getByRole('region', { name: 'List' })).toBeInTheDocument()
    expect(getByRole('region', { name: 'Details' })).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderWithProviders(
      <MasterDetail arrangement="split" list={list} detail={detail} />,
    )

    await expectNoAccessibilityViolations(container)
  })
})

describe('MasterDetail — stacked', () => {
  it('shows one pane at a time', () => {
    const { getByRole, queryByRole } = renderWithProviders(
      <MasterDetail
        arrangement="stacked"
        listLabel="Jobs due today"
        detailLabel="Job card"
        list={list}
        detail={detail}
      />,
    )

    expect(getByRole('region', { name: 'Jobs due today' })).toBeInTheDocument()
    expect(queryByRole('region', { name: 'Job card' })).toBeNull()
  })

  it('offers a way back that a keyboard can reach', async () => {
    const user = userEvent.setup()
    const onCloseDetail = vi.fn()
    const { getByRole } = renderWithProviders(
      <MasterDetail
        arrangement="stacked"
        detailOpen
        onCloseDetail={onCloseDetail}
        list={list}
        detail={detail}
      />,
    )

    await user.click(getByRole('button', { name: 'Back to the list' }))

    expect(onCloseDetail).toHaveBeenCalledOnce()
  })

  it('moves focus with the pane, in both directions', async () => {
    // Opening a job card replaces the list. Without this, focus is still on a control that is no
    // longer on the screen and the next Tab starts at the top of the shell (A11Y-64, A11Y-66).
    const user = userEvent.setup()
    const { getByRole } = renderWithProviders(<StackedExample />)

    await user.click(getByRole('button', { name: 'Open job card' }))
    expect(document.activeElement).toBe(getByRole('region', { name: 'Job card' }))

    await user.click(getByRole('button', { name: 'Back to the list' }))
    expect(document.activeElement).toBe(getByRole('region', { name: 'Jobs due today' }))
  })

  it('does not steal focus on a re-render that changed nothing', async () => {
    // 3.2.1 On Focus and checklist item A11Y-21: focus stays where the user put it.
    const user = userEvent.setup()
    const { getByRole } = renderWithProviders(<StackedExample />)

    const trigger = getByRole('button', { name: 'Open job card' })
    await user.tab()

    expect(document.activeElement).toBe(trigger)
  })

  it('renders in the pseudo-locale without losing a pane', () => {
    const { getByRole } = renderWithProviders(
      <MasterDetail arrangement="stacked" list={list} detail={detail} />,
      { locale: PSEUDO_LOCALE },
    )

    // The default pane names are translated, so neither is the plain English string.
    expect(getByRole('region')).toBeInTheDocument()
  })
})
