import { act } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { RouterProvider, createMemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { renderWithProviders } from '../../design-system/testing/renderWithProviders'
import { PAGE_HEADING_ATTRIBUTE, RouteAnnouncer } from './RouteAnnouncer'

/** A minimal shell: the announcer, the main landmark, and whatever the route puts inside it. */
function shellAround(children: ReactNode) {
  return (
    <>
      <RouteAnnouncer />
      <main id="main-content">{children}</main>
    </>
  )
}

function renderRouter(initial = '/') {
  const router = createMemoryRouter(
    [
      { path: '/', element: shellAround(<h1>Home</h1>) },
      { path: '/orders', element: shellAround(<h1>Orders due today</h1>) },
      {
        path: '/scan',
        element: shellAround(
          <>
            <h1>Ignore me</h1>
            <h2 {...{ [PAGE_HEADING_ATTRIBUTE]: 'true' }}>Scan a job</h2>
          </>,
        ),
      },
      { path: '/headless', element: shellAround(<p>No heading at all.</p>) },
    ],
    { initialEntries: [initial] },
  )

  const result = renderWithProviders(<RouterProvider router={router} />)
  return { ...result, router }
}

afterEach(() => {
  document.title = ''
})

describe('RouteAnnouncer', () => {
  it('names the screen in the document title', () => {
    // 2.4.2 Page Titled and checklist item A11Y-01: "HyFib Tailor360" on nine screens tells nobody
    // where they are.
    renderRouter('/')

    expect(document.title).toBe('Home — HyFib Tailor360')
  })

  it('says nothing on the first render', () => {
    // The browser has just loaded a document and the screen reader is already reading it. A second
    // announcement here is the chatter checklist item A11Y-43 fails a screen for.
    const { getByTestId } = renderRouter('/')

    expect(getByTestId('route-announcer')).toHaveTextContent('')
  })

  it('moves focus to the new screen heading on navigation', async () => {
    const { router, findByRole } = renderRouter('/')

    await act(async () => {
      await router.navigate('/orders')
    })

    const heading = await findByRole('heading', { level: 1, name: 'Orders due today' })
    expect(document.activeElement).toBe(heading)
    expect(heading).toHaveAttribute('tabindex', '-1')
    expect(document.title).toBe('Orders due today — HyFib Tailor360')
  })

  it('leaves the live region empty when a heading took the focus', async () => {
    // Announced once, never twice: a focus move already announces the heading.
    const { router, getByTestId } = renderRouter('/')

    await act(async () => {
      await router.navigate('/orders')
    })

    expect(getByTestId('route-announcer')).toHaveTextContent('')
  })

  it('prefers the heading a screen has nominated', async () => {
    const { router, findByRole } = renderRouter('/')

    await act(async () => {
      await router.navigate('/scan')
    })

    const heading = await findByRole('heading', { level: 2, name: 'Scan a job' })
    expect(document.activeElement).toBe(heading)
    expect(document.title).toBe('Scan a job — HyFib Tailor360')
  })

  it('announces instead when a screen has no heading to focus', async () => {
    // The graceful degradation, not the design: a screen with no heading is a defect in that screen,
    // and a person should still be told that the screen changed (checklist item A11Y-45).
    const { router, getByTestId } = renderRouter('/')

    await act(async () => {
      await router.navigate('/headless')
    })

    expect(getByTestId('route-announcer').textContent).toContain('Navigated to')
  })
})
