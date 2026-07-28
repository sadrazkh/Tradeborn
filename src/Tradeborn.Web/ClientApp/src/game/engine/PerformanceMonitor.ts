import { SceneInstrumentation } from '@babylonjs/core/Instrumentation/sceneInstrumentation'
import type { AbstractEngine } from '@babylonjs/core/Engines/abstractEngine'
import type { Scene } from '@babylonjs/core/scene'
import type { PerfSample } from '../types'

/**
 * Rolling performance sampler backing the debug overlay, the quality auto-adjust, and the
 * E2E budget assertions in docs/architecture/PERFORMANCE_BUDGET.md §8.
 *
 * Draw calls come from `SceneInstrumentation`, which is the only accurate source. Counting
 * active meshes instead would be badly misleading here: instances of a master mesh are
 * separate entries in `getActiveMeshes()` but collapse into a *single* draw call, which is
 * the entire point of the instancing strategy in PERFORMANCE_BUDGET.md §1.
 *
 * Samples are taken on an interval rather than per frame — reading counters every frame is
 * itself measurable overhead.
 */
export class PerformanceMonitor {
  private readonly window: number[] = []
  private readonly windowSize = 120
  private lastSample: PerfSample = { fps: 0, drawCalls: 0, triangles: 0, meshes: 0 }
  private timer: number | null = null
  private instrumentation: SceneInstrumentation | null = null

  constructor(
    private readonly engine: AbstractEngine,
    private readonly scene: Scene,
  ) {}

  start(intervalMs = 500): void {
    this.instrumentation = new SceneInstrumentation(this.scene)
    this.instrumentation.captureFrameTime = true

    this.timer = window.setInterval(() => this.sampleNow(), intervalMs)
    this.sampleNow()
  }

  private sampleNow(): void {
    const fps = this.engine.getFps()
    if (Number.isFinite(fps) && fps > 0) {
      this.window.push(fps)
      if (this.window.length > this.windowSize) this.window.shift()
    }

    let triangles = 0
    let visibleMeshes = 0
    for (const mesh of this.scene.meshes) {
      if (!mesh.isEnabled() || !mesh.isVisible) continue
      visibleMeshes++
      triangles += mesh.getTotalIndices() / 3
    }

    this.lastSample = {
      fps: Number.isFinite(fps) ? Math.round(fps) : 0,
      drawCalls: this.instrumentation?.drawCallsCounter.current ?? 0,
      triangles: Math.round(triangles),
      meshes: visibleMeshes,
    }
  }

  get sample(): PerfSample {
    return this.lastSample
  }

  /** The budget is written against the p95 floor, not the average. */
  get p95Fps(): number {
    if (this.window.length === 0) return 0
    const sorted = [...this.window].sort((a, b) => a - b)
    return Math.round(sorted[Math.floor(sorted.length * 0.05)] ?? 0)
  }

  dispose(): void {
    if (this.timer !== null) window.clearInterval(this.timer)
    this.instrumentation?.dispose()
    this.instrumentation = null
    this.window.length = 0
  }
}
