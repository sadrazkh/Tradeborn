import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import { PointerEventTypes } from '@babylonjs/core/Events/pointerEvents'
import type { Scene } from '@babylonjs/core/scene'
import type { Observer } from '@babylonjs/core/Misc/observable'
import type { PointerInfo } from '@babylonjs/core/Events/pointerEvents'

import '@babylonjs/core/Culling/ray'
import '@babylonjs/core/Meshes/Builders/boxBuilder'
import '@babylonjs/core/Meshes/Builders/torusBuilder'

import type { MaterialLibrary } from '../assets/MaterialLibrary'
import type { BuildingRenderer } from '../entities/BuildingRenderer'
import { PLOT_SIZE, PlotGrid } from '../world/PlotGrid'
import type { BuildingDto, SelectionInfo } from '../types'

const BUILDING_LABELS: Record<string, string> = {
  town_hall: 'Town Hall',
  market: 'Market',
  lumber_camp: 'Lumber Camp',
  farm: 'Farm',
  warehouse: 'Warehouse',
  sawmill: 'Sawmill',
  mill: 'Mill',
  bakery: 'Bakery',
}

/**
 * Pointer selection for buildings and plots.
 *
 * Picking happens on pointer UP only, never on move (SCENE_GUIDELINES.md §5) — picking
 * every frame is one of the easiest ways to lose 10 fps for no visible benefit.
 *
 * A drag is distinguished from a tap by pointer travel distance, so orbiting the camera
 * never selects anything.
 */
export class SelectionSystem {
  private observer: Observer<PointerInfo> | null = null
  private ring: Mesh | null = null
  private downX = 0
  private downY = 0
  private selectedId: string | null = null

  private readonly listeners = new Set<(info: SelectionInfo | null) => void>()

  constructor(
    private readonly scene: Scene,
    private readonly materials: MaterialLibrary,
    private readonly buildings: BuildingRenderer,
    private readonly grid: PlotGrid,
  ) {}

  attach(): void {
    this.buildRing()

    this.observer = this.scene.onPointerObservable.add((info) => {
      if (info.type === PointerEventTypes.POINTERDOWN) {
        this.downX = this.scene.pointerX
        this.downY = this.scene.pointerY
        return
      }
      if (info.type !== PointerEventTypes.POINTERUP) return

      const travelled = Math.hypot(this.scene.pointerX - this.downX, this.scene.pointerY - this.downY)
      if (travelled > 8) return // camera drag, not a selection

      this.pickAtPointer()
    })
  }

  private pickAtPointer(): void {
    const hit = this.scene.pick(this.scene.pointerX, this.scene.pointerY, (mesh) => mesh.isPickable)

    if (!hit?.hit || !hit.pickedMesh) {
      this.select(null)
      return
    }

    const meta = hit.pickedMesh.metadata as
      | { kind: 'building'; buildingId: string; col: number; row: number }
      | { kind: 'plot'; col: number; row: number; unlocked: boolean }
      | undefined

    if (!meta) {
      this.select(null)
      return
    }

    if (meta.kind === 'building') {
      const dto = this.buildings.get(meta.buildingId)
      if (!dto) return this.select(null)

      this.select(describe(dto))
      return
    }

    this.select({
      kind: 'plot',
      id: `plot_${meta.col}_${meta.row}`,
      title: meta.unlocked ? 'Empty plot' : 'Locked plot',
      subtitle: meta.unlocked ? 'Ready to build' : 'Raise your city level to unlock',
      col: meta.col,
      row: meta.row,
    })
  }

  /**
   * A gold ring on the ground marks the selection. Chosen over Babylon's HighlightLayer
   * deliberately: the effect layer costs a full-screen pass, which is a poor trade on
   * mobile for an outline that a ring communicates just as clearly at this camera angle.
   */
  private buildRing(): void {
    this.ring = MeshBuilder.CreateTorus(
      'selectionRing',
      { diameter: PLOT_SIZE * 0.92, thickness: 0.16, tessellation: 32 },
      this.scene,
    )
    this.ring.material = this.materials.get('gold', { emissive: true })
    this.ring.isPickable = false
    this.ring.setEnabled(false)
  }

  private select(info: SelectionInfo | null): void {
    this.selectedId = info?.id ?? null

    if (info && this.ring) {
      const centre = this.grid.toWorld(info.col, info.row)
      this.ring.position.set(centre.x, 0.32, centre.z)
      this.ring.setEnabled(true)
    } else {
      this.ring?.setEnabled(false)
    }

    for (const listener of this.listeners) listener(info)
  }

  /** Programmatic selection — used by the tutorial and by E2E tests. */
  selectBuilding(buildingId: string): void {
    const dto = this.buildings.get(buildingId)
    if (!dto) return
    this.select(describe(dto))
  }

  /** Re-emits the current selection so the panel picks up a changed building state. */
  refresh(): void {
    if (this.selectedId) this.selectBuilding(this.selectedId)
  }

  onSelectionChanged(listener: (info: SelectionInfo | null) => void): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  get current(): string | null {
    return this.selectedId
  }

  /** Slow pulse keeps the ring from reading as a static decal. */
  update(elapsedSeconds: number): void {
    if (!this.ring?.isEnabled()) return
    const pulse = 1 + Math.sin(elapsedSeconds * 3) * 0.035
    this.ring.scaling.set(pulse, 1, pulse)
  }

  dispose(): void {
    if (this.observer) this.scene.onPointerObservable.remove(this.observer)
    this.ring?.dispose()
    this.listeners.clear()
  }
}

/** One place that turns a building into the shape the HUD panel consumes. */
function describe(dto: BuildingDto): SelectionInfo {
  return {
    kind: 'building',
    id: dto.id,
    title: BUILDING_LABELS[dto.definitionId] ?? dto.definitionId,
    subtitle: `Level ${dto.level}`,
    state: dto.state,
    col: dto.col,
    row: dto.row,
    level: dto.level,
    definitionId: dto.definitionId,
    haltReason: dto.haltReason ?? null,
    completesAtUtc: dto.completesAtUtc ?? null,
    pendingLevel: dto.pendingLevel ?? dto.level,
  }
}
