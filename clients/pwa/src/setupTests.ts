import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Test globals are off, so Testing Library cannot install its own automatic clean-up; unmounting
// after every test keeps one test's DOM out of the next one's queries.
afterEach(() => {
  cleanup()
})
