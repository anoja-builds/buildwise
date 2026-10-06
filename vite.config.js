import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// BuildWise – Component 1 (Material Request & Approval Management)
// Dev server proxies /api to the ASP.NET Core backend once it exists.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
