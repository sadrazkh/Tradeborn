import type { GameBridge } from '../GameBridge'

/**
 * `window.__tradeborn` — the reason Playwright can meaningfully test a <canvas>.
 *
 * Without this, an E2E test can only assert that a canvas element exists. With it,
 * "the sawmill appeared at plot (3,4) and is producing" becomes an ordinary assertion.
 * See SCENE_GUIDELINES.md §9 and TEST_STRATEGY.md §6.
 *
 * Every value returned is plain JSON — no mesh, scene, or engine reference escapes.
 * Stripped from production builds by the `__TRADEBORN_DEBUG__` define in vite.config.ts.
 */
export interface TradebornTestBridge {
  ready(): boolean
  renderer(): string
  fps(): number
  p95Fps(): number
  drawCalls(): number
  triangles(): number
  buildings(): Array<{ id: string; definitionId: string; col: number; row: number; level: number; state: string }>
  selection(): string | null
  select(buildingId: string): void
  cameraState(): unknown
  timeOfDay(): number
  setTimeOfDay(value: number): void
}

declare global {
  interface Window {
    __tradeborn?: TradebornTestBridge
  }
}

export function installTestBridge(bridge: GameBridge): void {
  window.__tradeborn = {
    ready: () => bridge.isReady,
    renderer: () => bridge.rendererBackend,
    fps: () => bridge.performance.fps,
    p95Fps: () => bridge.p95Fps,
    drawCalls: () => bridge.performance.drawCalls,
    triangles: () => bridge.performance.triangles,
    buildings: () => bridge.listBuildings().map((b) => ({ ...b })),
    selection: () => bridge.currentSelection,
    select: (id: string) => bridge.selectBuilding(id),
    cameraState: () => bridge.cameraState,
    timeOfDay: () => bridge.timeOfDay,
    setTimeOfDay: (value: number) => bridge.applyTimeOfDay(value),
  }
}

export function removeTestBridge(): void {
  delete window.__tradeborn
}
