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
  quality(): string
  setQuality(preset: 'low' | 'medium' | 'high'): void
  agents(): { citizens: number; carts: number }

  /**
   * Placement preview. Exposed here rather than behind a HUD button because confirming a
   * placement needs the server-side construction command that arrives in Phase 3 — a button
   * that leads nowhere would be worse than none. This lets Phase 2 demonstrate and test the
   * system without shipping a dead control.
   */
  beginPlacement(definitionId: string): void
  cancelPlacement(): void
  isPlacing(): boolean
  lastCandidate(): { col: number; row: number; valid: boolean; reason: string } | null
}

declare global {
  interface Window {
    __tradeborn?: TradebornTestBridge
  }
}

export function installTestBridge(bridge: GameBridge): void {
  let lastCandidate: { col: number; row: number; valid: boolean; reason: string } | null = null
  const unsubscribe = bridge.onPlacementCandidateChanged((candidate) => {
    lastCandidate = candidate ? { ...candidate } : null
  })
  teardown = unsubscribe

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
    quality: () => bridge.qualityPreset,
    setQuality: (preset) => bridge.setQualityPreset(preset),
    agents: () => bridge.agentCounts,

    beginPlacement: (definitionId: string) => {
      // No-op confirm: Phase 2 proves the preview, Phase 3 supplies the command.
      bridge.beginPlacement(definitionId, () => {})
    },
    cancelPlacement: () => bridge.cancelPlacement(),
    isPlacing: () => bridge.isPlacing,
    lastCandidate: () => lastCandidate,
  }
}

let teardown: (() => void) | null = null

export function removeTestBridge(): void {
  teardown?.()
  teardown = null
  delete window.__tradeborn
}
