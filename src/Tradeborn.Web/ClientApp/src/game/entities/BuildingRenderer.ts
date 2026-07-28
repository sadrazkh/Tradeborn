import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { AbstractMesh } from '@babylonjs/core/Meshes/abstractMesh'
import type { TransformNode } from '@babylonjs/core/Meshes/transformNode'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Meshes/Builders/boxBuilder'

import type { MaterialLibrary } from '../assets/MaterialLibrary'
import type { ModelRegistry, Spinner } from '../assets/ModelRegistry'
import type { PlotGrid } from '../world/PlotGrid'
import { ConstructionVisual } from './ConstructionVisual'
import type { BuildingDto } from '../types'

interface PlacedBuilding {
  dto: BuildingDto
  root: TransformNode
  spinners: Spinner[]
  height: number
  picker: Mesh
  halted: Mesh | null
  construction: ConstructionVisual | null
}

/**
 * Places building models on the grid and owns their per-building state visuals.
 *
 * Picking uses a single invisible box per building rather than the composite mesh tree:
 * a building is 20–40 small meshes, and making all of them pickable would make every
 * `scene.pick` walk hundreds of candidates. One forgiving picker box is faster and gives
 * a better feel (SCENE_GUIDELINES.md §5 — selection should not require precision).
 */
export class BuildingRenderer {
  private readonly placed = new Map<string, PlacedBuilding>()

  constructor(
    private readonly scene: Scene,
    private readonly materials: MaterialLibrary,
    private readonly models: ModelRegistry,
    private readonly grid: PlotGrid,
  ) {}

  render(buildings: BuildingDto[]): void {
    for (const dto of buildings) this.add(dto)
  }

  add(dto: BuildingDto): void {
    if (this.placed.has(dto.id)) return

    const { root, spinners, height } = this.models.create(dto.definitionId, dto.level)
    const centre = this.grid.toWorld(dto.col, dto.row)
    root.position.set(centre.x, 0, centre.z)

    const picker = MeshBuilder.CreateBox(
      `pick_${dto.id}`,
      { width: 3.4, height: Math.max(height, 1.5), depth: 3.4 },
      this.scene,
    )
    picker.position.set(centre.x, Math.max(height, 1.5) / 2, centre.z)
    picker.isVisible = false
    picker.isPickable = true
    picker.metadata = { kind: 'building', buildingId: dto.id, col: dto.col, row: dto.row }

    const entry: PlacedBuilding = { dto, root, spinners, height, picker, halted: null, construction: null }
    this.placed.set(dto.id, entry)

    if (dto.state === 'UnderConstruction') {
      entry.construction = new ConstructionVisual(
        this.scene, this.materials, root, height, centre.x, centre.z)
      // Applied immediately so a page load lands on the right stage instead of replaying
      // the build from zero.
      entry.construction.update(dto.constructionProgress ?? 0, 0)
    }

    if (dto.state === 'Halted') this.showHaltMote(entry)
  }

  /**
   * Warning mote above a halted building. Uses a shape + colour pair, never colour alone
   * (ART_DIRECTION.md §9 — state must be readable without colour vision).
   */
  private showHaltMote(entry: PlacedBuilding): void {
    const mote = MeshBuilder.CreateBox(
      `halt_${entry.dto.id}`,
      { width: 0.35, height: 0.9, depth: 0.35 },
      this.scene,
    )
    mote.position.set(entry.root.position.x, entry.height + 1.0, entry.root.position.z)
    mote.material = this.materials.get('warning', { emissive: true })
    mote.isPickable = false
    entry.halted = mote
  }

  /**
   * Advances continuous animations. Called once per frame from the render loop.
   *
   * `serverNowMs` is the synchronised server clock, never `Date.now()`. Driving construction
   * from the device clock would let a wrong clock show a finished building that the server
   * still considers half-built.
   *
   * SCENE_GUIDELINES.md §3: this must not allocate. `rotation[axis] +=` mutates the existing
   * Vector3 in place rather than creating a new one.
   */
  update(deltaSeconds: number, elapsedSeconds: number, serverNowMs: number): void {
    for (const entry of this.placed.values()) {
      if (entry.construction) {
        const completesAt = entry.dto.completesAtUtc
          ? Date.parse(entry.dto.completesAtUtc)
          : serverNowMs

        const total = Math.max(1, completesAt - this.startedAt(entry, completesAt))
        const progress = 1 - (completesAt - serverNowMs) / total

        if (progress >= 1) {
          entry.construction.complete()
          entry.construction = null
          entry.dto = { ...entry.dto, state: 'Producing' }
        } else {
          entry.construction.update(progress, deltaSeconds)
        }
        continue
      }

      if (entry.dto.state === 'Producing') {
        for (const spinner of entry.spinners) {
          // Mutates the existing Vector3 in place — allocating here would create thousands
          // of objects per second and produce GC stutter (SCENE_GUIDELINES.md §3).
          spinner.node.rotation[spinner.axis] += deltaSeconds * spinner.speed
        }
      }
      if (entry.halted) {
        // Gentle bob so the warning reads as active rather than as scenery.
        entry.halted.position.y = entry.height + 1.0 + Math.sin(elapsedSeconds * 2.2) * 0.14
        entry.halted.rotation.y += deltaSeconds * 1.2
      }
    }
  }

  /**
   * Reconstructs when a build started from the progress the server reported on load.
   *
   * The API sends `completesAtUtc` and a progress fraction rather than a start time, because
   * progress is what the renderer actually needs and it keeps the contract from implying the
   * client should compute elapsed time itself.
   */
  private startedAt(entry: PlacedBuilding, completesAt: number): number {
    const progress = entry.dto.constructionProgress ?? 0
    if (progress <= 0 || progress >= 1) return completesAt - 30_000
    return completesAt - (completesAt - this.loadedAtServerMs) / (1 - progress)
  }

  /** Server time when this city was loaded, used to anchor construction timelines. */
  loadedAtServerMs = 0

  get(id: string): BuildingDto | undefined {
    return this.placed.get(id)?.dto
  }

  all(): BuildingDto[] {
    return Array.from(this.placed.values(), (entry) => entry.dto)
  }

  /** Meshes belonging to a building, used by the selection outline. */
  meshesOf(id: string): AbstractMesh[] {
    const entry = this.placed.get(id)
    if (!entry) return []
    return entry.root.getChildMeshes(false)
  }

  rootOf(id: string): TransformNode | undefined {
    return this.placed.get(id)?.root
  }

  dispose(): void {
    for (const entry of this.placed.values()) {
      entry.construction?.dispose()
      entry.root.dispose(false, true)
      entry.picker.dispose()
      entry.halted?.dispose()
    }
    this.placed.clear()
  }
}
