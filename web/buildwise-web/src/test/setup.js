import '@testing-library/jest-dom/vitest'
import { vi } from 'vitest'

// jsdom has no scrolling implementation; browser geometry is tested in Playwright.
window.scrollTo = vi.fn()
