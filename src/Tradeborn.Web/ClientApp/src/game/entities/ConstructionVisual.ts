import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import { TransformNode } from '@babylonjs/core/Meshes/transformNode'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Meshes/Builders/boxBuilder'
import '@babylonjs/core/Meshes/Builders/cylinderBuilder'

import type { MaterialLibrary } from '../assets/MaterialLibrary'

/**
 * Scaffolding, crane and staged reveal for a building under construction.
 *
 * SCENE_GUIDELINES.md §6: construction is a scene, not a progress bar. The four stages below
 * are the beat that PLAYER_JOURNEY.md calls the most important in the tutorial — if this
 * lands, players stay.
 *
 * Progress comes from the **server**: `(serverNow - startedAt) / (completesAt - startedAt)`,
 * where `serverNow` is the synchronised clock, never `Date.now()`. A client whose clock is
 * wrong sees the same construction as everyone else.
 */
export class ConstructionVisual {
  readonly root: TransformNode
  private readonly crane: TransformNode
  private readonly scaffolds: Mesh[] = []
  private lastStage = -1

  constructor(
    scene: Scene,
    materials: MaterialLibrary,
    private readonly buildingRoot: TransformNode,
    height: number,
    x: number,
    z: number,
  ) {
    this.root = new TransformNode(`construction_${buildingRoot.name}`, scene)
    this.root.position.set(x, 0, z)

    // Cleared ground pad — the first thing that says "work is starting here".
    const pad = MeshBuilder.CreateBox('cPad', { width: 3.5, height: 0.16, depth: 3.5 }, scene)
    pad.parent = this.root
    pad.position.y = 0.08
    pad.material = materials.get('dirt')
    pad.isPickable = false

    // Scaffolding poles at the corners, revealed stage by stage.
    for (const [dx, dz] of [[-1.5, -1.5], [1.5, -1.5], [-1.5, 1.5], [1.5, 1.5]] as const) {
      const pole = MeshBuilder.CreateBox(
        'cPole',
        { width: 0.14, height: Math.max(1.4, height * 0.85), depth: 0.14 },
        scene,
      )
      pole.parent = this.root
      pole.position.set(dx, Math.max(1.4, height * 0.85) / 2, dz)
      pole.material = materials.get('timberDark')
      pole.isPickable = false
      pole.setEnabled(false)
      this.scaffolds.push(pole)
    }

    // Horizontal planks between the poles.
    for (const y of [0.9, 1.9]) {
      if (y > height) break
      for (const [dx, dz, rotY] of [[0, -1.5, 0], [0, 1.5, 0], [-1.5, 0, Math.PI / 2], [1.5, 0, Math.PI / 2]] as const) {
        const plank = MeshBuilder.CreateBox('cPlank', { width: 3.1, height: 0.1, depth: 0.18 }, scene)
        plank.parent = this.root
        plank.position.set(dx, y, dz)
        plank.rotation.y = rotY
        plank.material = materials.get('timber')
        plank.isPickable = false
        plank.setEnabled(false)
        this.scaffolds.push(plank)
      }
    }

    // Crane: mast plus jib. It rotates slowly, which is what reads as "work in progress"
    // from across the city without needing particles.
    this.crane = new TransformNode('cCrane', scene)
    this.crane.parent = this.root
    this.crane.position.set(2.1, 0, -2.1)

    const mast = MeshBuilder.CreateBox(
      'cMast',
      { width: 0.22, height: height + 1.6, depth: 0.22 },
      scene,
    )
    mast.parent = this.crane
    mast.position.y = (height + 1.6) / 2
    mast.material = materials.get('metal')
    mast.isPickable = false

    const jib = MeshBuilder.CreateBox('cJib', { width: 3.2, height: 0.16, depth: 0.16 }, scene)
    jib.parent = this.crane
    jib.position.set(1.2, height + 1.4, 0)
    jib.material = materials.get('warning')
    jib.isPickable = false

    const cable = MeshBuilder.CreateCylinder('cCable', { height: 1.0, diameter: 0.05 }, scene)
    cable.parent = this.crane
    cable.position.set(2.5, height + 0.9, 0)
    cable.material = materials.get('timberDark')
    cable.isPickable = false

    const load = MeshBuilder.CreateBox('cLoad', { width: 0.45, height: 0.4, depth: 0.45 }, scene)
    load.parent = this.crane
    load.position.set(2.5, height + 0.3, 0)
    load.material = materials.get('timber')
    load.isPickable = false
  }

  /**
   * Applies the visual state for a progress value in 0..1.
   *
   * Stages are discrete rather than a smooth scale because a building that grows continuously
   * reads as a loading bar wearing a hat. Discrete jumps read as construction milestones.
   */
  update(progress: number, deltaSeconds: number): void {
    const clamped = Math.min(Math.max(progress, 0), 1)
    const stage = clamped < 0.25 ? 0 : clamped < 0.6 ? 1 : clamped < 0.9 ? 2 : 3

    this.crane.rotation.y += deltaSeconds * 0.35

    if (stage === this.lastStage) return
    this.lastStage = stage

    // Stage 0: cleared ground only. 1: frame. 2: walls going up. 3: nearly done.
    const scaffoldVisible = stage >= 1
    for (const mesh of this.scaffolds) mesh.setEnabled(scaffoldVisible)

    this.crane.setEnabled(stage < 3)

    // The building itself rises through the scaffolding.
    const reveal = [0.05, 0.35, 0.75, 1][stage]!
    this.buildingRoot.scaling.set(1, reveal, 1)
    this.buildingRoot.setEnabled(stage > 0)
  }

  /** Restores the finished building and tears the site down. */
  complete(): void {
    this.buildingRoot.scaling.set(1, 1, 1)
    this.buildingRoot.setEnabled(true)
    this.dispose()
  }

  dispose(): void {
    this.root.dispose(false, true)
  }
}
