import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// The SPA builds into the ASP.NET project's wwwroot so that Kestrel serves it directly.
// wwwroot is gitignored — it is build output, never source. See ADR-002.
//
// Modes:
//   development  `npm run dev`         debug bridge ON  (HMR)
//   debug        `npm run build:debug` debug bridge ON  (built artefact, used by E2E)
//   production   `npm run build`       debug bridge OFF (shipped)
export default defineConfig(({ mode }) => ({
  plugins: [vue()],

  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },

  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    // Budgets from docs/architecture/PERFORMANCE_BUDGET.md §4. Vite warns above this;
    // CI enforces the hard limit separately.
    chunkSizeWarningLimit: 1000,
    rollupOptions: {
      output: {
        // Babylon goes in its own chunk so the HUD shell can paint before the engine
        // has downloaded — this is what keeps First Contentful Paint under 1.2 s.
        manualChunks(id) {
          if (id.includes('node_modules/@babylonjs')) return 'babylon'
          if (id.includes('node_modules/vue') || id.includes('node_modules/pinia')) return 'vendor'
          return undefined
        },
      },
    },
  },

  server: {
    port: 5173,
    strictPort: true,
    // During `npm run dev` the API is served by Kestrel on 5084.
    proxy: {
      '/api': { target: 'http://localhost:5084', changeOrigin: true },
      '/health': { target: 'http://localhost:5084', changeOrigin: true },
    },
  },

  define: {
    // Gates the window.__tradeborn test bridge (SCENE_GUIDELINES.md §9) and the debug
    // overlay. Stripped entirely from production builds so neither ships to players.
    __TRADEBORN_DEBUG__: JSON.stringify(mode !== 'production'),
  },
}))
