import { afterEach } from 'vitest'
import { cleanup } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'

// Unmount and clear the DOM between tests so repeated render() calls don't accumulate.
afterEach(() => {
  cleanup()
})
