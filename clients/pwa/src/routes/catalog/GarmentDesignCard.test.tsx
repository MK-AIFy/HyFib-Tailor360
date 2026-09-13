import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AppIntlProvider } from '../../i18n/IntlProvider'
import { expectNoAccessibilityViolations } from '../../design-system/testing/axe'
import { stubFetch } from '../../auth/testing/fixtures'
import type { FetchStub } from '../../auth/testing/fixtures'
import {
  aGarmentDesignSelectionSnapshot,
  aGarmentDesignSnapshot,
} from '../../catalog/testing/fixtures'
import { GarmentDesignCard } from './GarmentDesignCard'

let transport: FetchStub

beforeEach(() => {
  transport = stubFetch()
})

afterEach(() => {
  vi.restoreAllMocks()
})

function renderCard(props: Partial<Parameters<typeof GarmentDesignCard>[0]> = {}) {
  return render(
    <AppIntlProvider locale="en-IN">
      <GarmentDesignCard snapshot={aGarmentDesignSnapshot()} {...props} />
    </AppIntlProvider>,
  )
}

it('renders a two-year-old snapshot with no catalogue request at all', async () => {
  const snapshot = aGarmentDesignSnapshot({
    catalogVersionNumber: 3,
    categoryLabel: 'Blouse',
    serviceTypeLabel: 'Pattern work',
    selections: [
      aGarmentDesignSelectionSnapshot({
        groupCode: 'neckline',
        groupLabel: 'Neckline',
        optionCode: 'ROUND',
        optionLabel: 'Round',
      }),
    ],
    conditionalNotes: ['Cut the lining 5 mm wider at the armhole.'],
    instructions: 'Customer asked for a deeper back.',
  })

  const { container } = renderCard({ snapshot })

  expect(screen.getByRole('heading', { name: 'Pattern work' })).toBeInTheDocument()
  expect(screen.getByText('Neckline')).toBeInTheDocument()
  expect(screen.getByText('Round')).toBeInTheDocument()
  expect(screen.getByText('Cut the lining 5 mm wider at the armhole.')).toBeInTheDocument()
  expect(screen.getByText('Customer asked for a deeper back.')).toBeInTheDocument()

  // A two-year-old snapshot renders from nothing but its own value — no fetch of any kind.
  expect(transport.calls).toHaveLength(0)

  await expectNoAccessibilityViolations(container)
})

it('prints without a live catalogue lookup', async () => {
  const user = userEvent.setup()
  const print = vi.spyOn(window, 'print').mockImplementation(() => {})
  renderCard()

  await user.click(screen.getByRole('button', { name: 'Print' }))

  expect(print).toHaveBeenCalledTimes(1)
  expect(transport.calls).toHaveLength(0)
})

it('omits the print control when told to, for an embedded read-only view', () => {
  renderCard({ printable: false })

  expect(screen.queryByRole('button', { name: 'Print' })).not.toBeInTheDocument()
})

it('says there are no standing instructions rather than printing an empty section', () => {
  renderCard({ snapshot: aGarmentDesignSnapshot({ conditionalNotes: [], instructions: null }) })

  expect(screen.queryByText('Standing instructions')).not.toBeInTheDocument()
})

it('renders every option a multiple-choice group carries, even though they share a group code', () => {
  // Two selections from the same group are two rows, and each needs its own React key —
  // `groupCode` alone collides between them.
  renderCard({
    snapshot: aGarmentDesignSnapshot({
      selections: [
        aGarmentDesignSelectionSnapshot({
          groupCode: 'trims',
          groupLabel: 'Trims',
          optionCode: 'LACE',
          optionLabel: 'Lace edging',
        }),
        aGarmentDesignSelectionSnapshot({
          groupCode: 'trims',
          groupLabel: 'Trims',
          optionCode: 'ZARI',
          optionLabel: 'Zari border',
        }),
      ],
    }),
  })

  expect(screen.getByText('Lace edging')).toBeInTheDocument()
  expect(screen.getByText('Zari border')).toBeInTheDocument()
})
