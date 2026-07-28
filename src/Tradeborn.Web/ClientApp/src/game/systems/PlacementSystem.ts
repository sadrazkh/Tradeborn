import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { TransformNode } from '@babylonjs/core/Meshes/transformNode'
import { PointerEventTypes, type PointerInfo } from '@babylonjs/core/Events/pointerEvents'
import type { Observer } from '@babylonjs/core/Misc/observable'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Culling/ray'
import '@babylonjs/core/Meshes/Builders/boxBuilder'
import '@babylonjs/core/Meshes/Builders/planeBuilder'

import type { MaterialLibrary } from '../assets/MaterialLibrary'
import type { ModelRegistry } from '../assets/ModelRegistry'
import { PLOT_SIZE, type PlotGrid } from '../world/PlotGrid'
import type { BuildingDto, PlotDto } from '../types'

export type PlacementRejection = 'occupied' | 'locked' | 'none'

export interface PlacementCandidate {
  col: number
  row: number
  valid: boolean
  reason: PlacementRejection
}

/**
 * Ghost-preview placement mode (SCENE_GUIDELINES.md §5).
 *
 * The validity shown here is a **UX affordance only**. The server validates independently
 * and its answer is the one that counts (SECURITY_MODEL.md §3) — this exists so the player
 * is not asked to guess, not to decide anything.
 */
export class PlacementSystem {
  private ghost: TransformNode | null = null
  private pad: Mesh | null = null
  private marker: Mesh | null = null
  private observer: Observer<PointerInfo> | null = null

  private active = false
  private definitionId = ''
  private candidate: PlacementCandidate | null = null

  private readonly plotIndex = new Map<string, PlotDto>()
  private readonly occupied = new Set<string>()

  private readonly listeners = new Set<(candidate: PlacementCandidate | null) => void>()
  private confirmHandler: ((candidate: PlacementCandidate) => void) | null = null

  constructor(
    private readonly scene: Scene,
    private readonly materials: MaterialLibrary,
    private readonly models: ModelRegistry,
    private readonly grid: PlotGrid,
  ) {}

  setWorld(plots: PlotDto[], buildings: BuildingDto[]): void {
    this.plotIndex.clear()
    this.occupied.clear()
    for (const plot of plots) this.plotIndex.set(key(plot.col, plot.row), plot)
    for (const building of buildings) this.occupied.add(key(building.col, building.row))
  }

  /** Records a plot as taken after the server confirms a build. */
  markOccupied(col: number, row: number): void {
    this.occupied.add(key(col, row))
  }

  attach(): void {
    this.observer = this.scene.onPointerObservable.add((info) => {
      if (!this.active) return

      if (info.type === PointerEventTypes.POINTERMOVE) {
        this.moveGhostToPointer()
        return
      }

      if (info.type === PointerEventTypes.POINTERUP && this.candidate?.valid) {
        this.confirmHandler?.(this.candidate)
        this.stop()
      }
    })
  }

  begin(definitionId: string, onConfirm: (candidate: PlacementCandidate) => void): void {
    this.stop()

    this.active = true
    this.definitionId = definitionId
    this.confirmHandler = onConfirm

    const model = this.models.create(definitionId, 1)
    this.ghost = model.root
    // The ghost must never intercept picks, or it would shadow the plot underneath it.
    for (const mesh of this.ghost.getChildMeshes(false)) {
      mesh.isPickable = false
      mesh.visibility = 0.55
    }

    this.pad = MeshBuilder.CreateBox(
      'placementPad',
      { width: PLOT_SIZE * 0.9, height: 0.12, depth: PLOT_SIZE * 0.9 },
      this.scene,
    )
    this.pad.isPickable = false

    // A glyph above the ghost, not just a colour. State must never be conveyed by hue
    // alone (ART_DIRECTION.md §9) — a red-green ghost is invisible to a large minority.
    this.marker = MeshBuilder.CreateBox('placementMarker', { width: 0.7, height: 0.16, depth: 0.16 }, this.scene)
    this.marker.isPickable = false

    this.moveGhostToPointer()
  }

  stop(): void {
    this.active = false
    this.candidate = null
    this.confirmHandler = null

    this.ghost?.dispose(false, true)
    this.pad?.dispose()
    this.marker?.dispose()
    this.ghost = null
    this.pad = null
    this.marker = null

    this.emit(null)
  }

  get isActive(): boolean {
    return this.active
  }

  get currentDefinitionId(): string {
    return this.definitionId
  }

  private moveGhostToPointer(): void {
    if (!this.ghost || !this.pad || !this.marker) return

    const hit = this.scene.pick(this.scene.pointerX, this.scene.pointerY, (mesh) => mesh.isPickable)
    if (!hit?.hit || !hit.pickedPoint) return

    const plot = this.grid.fromWorld(hit.pickedPoint)
    if (!plot) return

    const evaluated = this.evaluate(plot.col, plot.row)
    const centre = this.grid.toWorld(plot.col, plot.row)

    this.ghost.position.set(centre.x, 0, centre.z)
    this.pad.position.set(centre.x, 0.06, centre.z)
    this.marker.position.set(centre.x, 3.2, centre.z)

    const tone = evaluated.valid ? 'success' : 'danger'
    this.pad.material = this.materials.getGhost(tone)
    this.marker.material = this.materials.get(tone, { emissive: true })

    // A bar for valid, a cross for invalid — readable without colour.
    this.marker.rotation.z = evaluated.valid ? 0 : Math.PI / 4
    this.marker.scaling.set(1, 1, evaluated.valid ? 1 : 3.5)

    if (
      this.candidate?.col !== evaluated.col ||
      this.candidate?.row !== evaluated.row ||
      this.candidate?.valid !== evaluated.valid
    ) {
      this.candidate = evaluated
      this.emit(evaluated)
    }
  }

  private evaluate(col: number, row: number): PlacementCandidate {
    const plot = this.plotIndex.get(key(col, row))

    if (!plot || !plot.unlocked) {
      return { col, row, valid: false, reason: 'locked' }
    }
    if (this.occupied.has(key(col, row))) {
      return { col, row, valid: false, reason: 'occupied' }
    }
    return { col, row, valid: true, reason: 'none' }
  }

  onCandidateChanged(listener: (candidate: PlacementCandidate | null) => void): () => void {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  private emit(candidate: PlacementCandidate | null): void {
    for (const listener of this.listeners) listener(candidate)
  }

  dispose(): void {
    if (this.observer) this.scene.onPointerObservable.remove(this.observer)
    this.stop()
    this.listeners.clear()
  }
}

function key(col: number, row: number): string {
  return `${col},${row}`
}
