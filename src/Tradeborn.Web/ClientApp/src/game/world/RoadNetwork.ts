import { Vector3 } from '@babylonjs/core/Maths/math.vector'
import type { PlotGrid } from './PlotGrid'
import type { PlotDto } from '../types'

export interface RoadTile {
  col: number
  row: number
}

/**
 * The walkable/drivable graph, derived from plots whose terrain is `dirt`.
 *
 * Deliberately not a pathfinder. Agents perform a random walk across adjacent road tiles,
 * which at this camera distance is indistinguishable from purposeful movement and costs
 * nothing per frame. A* arrives in Phase 5, when a cart genuinely has to reach a *specific*
 * warehouse rather than merely look busy.
 */
export class RoadNetwork {
  private readonly tiles: RoadTile[] = []
  private readonly index = new Map<string, RoadTile[]>()

  constructor(
    plots: PlotDto[],
    private readonly grid: PlotGrid,
  ) {
    for (const plot of plots) {
      if (plot.terrain === 'dirt') {
        this.tiles.push({ col: plot.col, row: plot.row })
      }
    }

    // Adjacency is precomputed once. Recomputing it per agent per arrival would be a
    // per-frame cost for a graph that never changes.
    for (const tile of this.tiles) {
      this.index.set(key(tile), this.tiles.filter((other) => isAdjacent(tile, other)))
    }
  }

  get isEmpty(): boolean {
    return this.tiles.length === 0
  }

  get size(): number {
    return this.tiles.length
  }

  tileAt(i: number): RoadTile {
    return this.tiles[i % this.tiles.length]!
  }

  neighbours(tile: RoadTile): RoadTile[] {
    return this.index.get(key(tile)) ?? []
  }

  /**
   * Picks the next tile, preferring not to double back.
   * <paramref name="seed"/> keeps this deterministic so the scene replays identically.
   */
  nextTile(current: RoadTile, previous: RoadTile | null, seed: number): RoadTile {
    const options = this.neighbours(current)
    if (options.length === 0) return current

    const forward = previous
      ? options.filter((t) => t.col !== previous.col || t.row !== previous.row)
      : options

    const pool = forward.length > 0 ? forward : options
    return pool[Math.abs(seed) % pool.length]!
  }

  positionToRef(tile: RoadTile, ref: Vector3): void {
    this.grid.toWorldToRef(tile.col, tile.row, ref)
  }
}

function key(tile: RoadTile): string {
  return `${tile.col},${tile.row}`
}

function isAdjacent(a: RoadTile, b: RoadTile): boolean {
  const dc = Math.abs(a.col - b.col)
  const dr = Math.abs(a.row - b.row)
  return dc + dr === 1
}
