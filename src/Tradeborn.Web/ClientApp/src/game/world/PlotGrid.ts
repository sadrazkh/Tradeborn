import { Vector3 } from '@babylonjs/core/Maths/math.vector'

/**
 * The single source of truth for plot ↔ world-space conversion.
 *
 * SCENE_GUIDELINES.md §1: no other file performs this arithmetic. Every renderer that
 * needs a world position asks here, so changing the grid scale is a one-line change.
 */
export const PLOT_SIZE = 4
export const PLOT_GAP = 0.18
export const PLOT_HEIGHT = 0.5

export class PlotGrid {
  constructor(readonly size: number) {}

  /** Centre of a plot in world space, at ground level (top face of the plot slab). */
  toWorld(col: number, row: number): Vector3 {
    return new Vector3(
      col * PLOT_SIZE + PLOT_SIZE / 2 - this.centreOffset,
      0,
      row * PLOT_SIZE + PLOT_SIZE / 2 - this.centreOffset,
    )
  }

  /** Writes into an existing vector — used on hot paths to avoid per-frame allocation. */
  toWorldToRef(col: number, row: number, ref: Vector3): void {
    ref.set(
      col * PLOT_SIZE + PLOT_SIZE / 2 - this.centreOffset,
      0,
      row * PLOT_SIZE + PLOT_SIZE / 2 - this.centreOffset,
    )
  }

  /** Nearest plot to a world position, or null if outside the grid. */
  fromWorld(position: Vector3): { col: number; row: number } | null {
    const col = Math.floor((position.x + this.centreOffset) / PLOT_SIZE)
    const row = Math.floor((position.z + this.centreOffset) / PLOT_SIZE)
    if (col < 0 || row < 0 || col >= this.size || row >= this.size) return null
    return { col, row }
  }

  /** Half the grid width, so the city is centred on the origin. */
  private get centreOffset(): number {
    return (this.size * PLOT_SIZE) / 2
  }

  get worldExtent(): number {
    return this.size * PLOT_SIZE
  }
}
