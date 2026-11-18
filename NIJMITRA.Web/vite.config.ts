import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import { cpSync } from 'node:fs'
import { resolve } from 'node:path'

/** Publishes pdf.js character maps and standard fonts (used by the in-app PDF viewer) at /pdfjs/ in the build output. */
const pdfjsAssets = () => {
  let outDir = 'dist'
  return {
    name: 'nijmitra-pdfjs-assets',
    configResolved(c: { build: { outDir: string }; root: string }) { outDir = resolve(c.root, c.build.outDir) },
    closeBundle() {
      for (const d of ['cmaps', 'standard_fonts']) cpSync(resolve(__dirname, 'node_modules/pdfjs-dist', d), resolve(outDir, 'pdfjs', d), { recursive: true })
    },
  }
}

export default defineConfig({
  plugins: [
    react(),
    pdfjsAssets(),
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      registerType: 'prompt',
      injectRegister: false,
      includeAssets: ['branding/favicon.svg', 'branding/favicon.ico', 'branding/apple-touch-icon.png', 'theme-init.js'],
      injectManifest: { globPatterns: ['**/*.{js,css,html,ico,woff2}', 'branding/{favicon.svg,mark.webp,logo-light.webp,logo-dark.webp,pwa-192.png}'], maximumFileSizeToCacheInBytes: 5 * 1024 * 1024 },
      manifest: {
        id: '/',
        name: 'NijMitra — Aapka Personal Digital Saathi',
        short_name: 'NijMitra',
        description: 'अपनी पूरी निजी दुनिया अब एक ही App में — finance, documents, passwords, Udhar, diary, reminders & chat.',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'any',
        background_color: '#FFFFFF',
        theme_color: '#0B3D91',
        lang: 'en-IN',
        categories: ['productivity', 'finance', 'lifestyle'],
        icons: [
          { src: '/branding/pwa-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/branding/pwa-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: '/branding/pwa-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
          { src: '/branding/favicon.svg', sizes: 'any', type: 'image/svg+xml' }
        ],
        shortcuts: [
          { name: 'Add expense', url: '/finance/expenses?new=1', icons: [{ src: '/branding/pwa-192.png', sizes: '192x192' }] },
          { name: 'Chat', url: '/chat', icons: [{ src: '/branding/pwa-192.png', sizes: '192x192' }] }
        ]
      },
      devOptions: { enabled: false }
    })
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: false },
      '/hubs': { target: 'http://localhost:5080', ws: true }
    }
  },
  build: {
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      output: {
        manualChunks: (id: string) => {
          if (id.includes('@fullcalendar')) return 'calendar'
          if (id.includes('recharts') || id.includes('d3-')) return 'charts'
          if (id.includes('@tiptap') || id.includes('prosemirror')) return 'editor'
          if (id.includes('node_modules')) return 'vendor'
        }
      }
    }
  },
  test: { environment: 'jsdom' }
} as never)
