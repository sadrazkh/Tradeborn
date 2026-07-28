import type { AbstractEngine } from '@babylonjs/core/Engines/abstractEngine'
import type { AgentRenderer } from '../entities/AgentRenderer'
import type { PerformanceMonitor } from './PerformanceMonitor'

export type QualityPreset = 'low' | 'medium' | 'high'

interface PresetSettings {
  /** 1.0 = native. Higher values render fewer pixels. */
  hardwareScaling: number
  citizens: number
  carts: number
}

/**
 * Quality presets and automatic downgrade, per docs/architecture/PERFORMANCE_BUDGET.md §7.
 */
const PRESETS: Record<QualityPreset, PresetSettings> = {
  // Low keeps a few citizens rather than none. The budget document originally specified
  // zero, written before instancing was in place; 20 citizens turn out to cost ~2 draw calls
  // in total, so removing them buys almost no frame time and costs the single strongest
  // "this city is alive" cue on exactly the devices that most need to feel good.
  // Resolution scale is the lever that actually pays on mobile.
  low: { hardwareScaling: 1 / 0.75, citizens: 6, carts: 2 },
  medium: { hardwareScaling: 1, citizens: 12, carts: 4 },
  high: { hardwareScaling: 1, citizens: 20, carts: 6 },
}

const DOWNGRADE_ORDER: QualityPreset[] = ['high', 'medium', 'low']

export class QualityManager {
  private current: QualityPreset = 'medium'
  private timer: number | null = null
  private belowFloorSince: number | null = null

  constructor(
    private readonly engine: AbstractEngine,
    private readonly perf: PerformanceMonitor,
    private readonly citizens: AgentRenderer,
    private readonly carts: AgentRenderer,
  ) {}

  /**
   * Mobile starts at Low. Detecting by pointer capability rather than user-agent string:
   * what matters is whether this is a touch device with a modest GPU, and UA sniffing is
   * both unreliable and a maintenance burden.
   */
  start(): void {
    const isTouchPrimary = window.matchMedia('(pointer: coarse)').matches
    this.apply(isTouchPrimary ? 'low' : 'medium')

    this.timer = window.setInterval(() => this.evaluate(), 1000)
  }

  apply(preset: QualityPreset): void {
    this.current = preset
    const settings = PRESETS[preset]

    // Cap by device pixel ratio as well: on a 3x screen, rendering at native resolution
    // costs ~9x the fragment work for a difference nobody sees at this camera distance.
    const dprCap = Math.min(window.devicePixelRatio || 1, 2)
    this.engine.setHardwareScalingLevel(settings.hardwareScaling / dprCap)

    this.citizens.setVisibleCount(settings.citizens)
    this.carts.setVisibleCount(settings.carts)
  }

  /**
   * Steps down after a sustained dip, never up.
   *
   * Automatic upgrades are deliberately absent: a scene that oscillates between presets
   * looks worse than one that is simply a tier too low, and the oscillation is far more
   * noticeable than the missing detail. Stepping back up is a user action.
   */
  private evaluate(): void {
    const floor = window.matchMedia('(pointer: coarse)').matches ? 25 : 50
    const index = DOWNGRADE_ORDER.indexOf(this.current)
    if (index === DOWNGRADE_ORDER.length - 1) return // already at the lowest preset

    const p95 = this.perf.p95Fps
    if (p95 === 0) return // not enough samples yet

    if (p95 >= floor) {
      this.belowFloorSince = null
      return
    }

    const now = performance.now()
    this.belowFloorSince ??= now

    if (now - this.belowFloorSince >= 3000) {
      const next = DOWNGRADE_ORDER[index + 1]!
      console.info(
        `[Tradeborn] p95 FPS ${p95} below floor ${floor} for 3s — dropping quality to "${next}".`,
      )
      this.apply(next)
      this.belowFloorSince = null
    }
  }

  get preset(): QualityPreset {
    return this.current
  }

  dispose(): void {
    if (this.timer !== null) window.clearInterval(this.timer)
    this.timer = null
  }
}
