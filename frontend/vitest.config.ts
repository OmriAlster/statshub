import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Dates are shown in the viewer's local time - pin one so date tests give
// the same result on every machine (and in CI).
process.env.TZ = 'Asia/Jerusalem'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
