/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}

/** Injected by vite.config.ts `define`. False in production builds. */
declare const __TRADEBORN_DEBUG__: boolean
