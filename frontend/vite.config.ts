import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Backend routes are themselves rooted at /api/... (see [Route("api/[controller]")]
      // in the controllers), so the prefix must be forwarded as-is, not stripped.
      '/api': {
        target: 'http://localhost:5132',
        changeOrigin: true
      }
    }
  }
})
