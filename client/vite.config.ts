import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../server/wwwroot',
    emptyOutDir: true,
    rolldownOptions: {
      output: {
        codeSplitting: true,
      },
    },
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5297',
    },
  },
});
