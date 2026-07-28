import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import { TransformNode } from '@babylonjs/core/Meshes/transformNode'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Meshes/Builders/boxBuilder'
import '@babylonjs/core/Meshes/Builders/cylinderBuilder'

import type { MaterialLibrary, PaletteKey } from './MaterialLibrary'

/**
 * Procedural building meshes — the "now" half of ADR-006.
 *
 * Every building is composed from the modular kit in ART_DIRECTION.md §5:
 *   base → body → roof → accents → one signature moving part.
 *
 * Nothing here is downloaded. All geometry is original work generated from Babylon
 * primitives, which is what makes the copyright register in ART_DIRECTION.md §10 empty.
 *
 * When authored assets arrive, `GltfModelRegistry` implements the same interface and
 * gameplay code does not change.
 */
export interface ModelRegistry {
  create(definitionId: string, level: number): BuiltModel
}

export interface BuiltModel {
  root: TransformNode
  /** Nodes spun by the AnimationCoordinator when the building is producing. */
  spinners: TransformNode[]
  /** Approximate height, used to anchor UI and effects above the building. */
  height: number
}

export class ProceduralModelRegistry implements ModelRegistry {
  private counter = 0

  constructor(
    private readonly scene: Scene,
    private readonly materials: MaterialLibrary,
  ) {}

  create(definitionId: string, level: number): BuiltModel {
    const root = new TransformNode(`bld_${definitionId}_${this.counter++}`, this.scene)
    const spinners: TransformNode[] = []

    // Level scaling is subtle in footprint but obvious in height and detail, so an
    // upgrade reads from across the city (ART_DIRECTION.md §5).
    const s = 1 + (level - 1) * 0.18

    let height: number
    switch (definitionId) {
      case 'town_hall':
        height = this.townHall(root, s)
        break
      case 'market':
        height = this.market(root, s)
        break
      case 'lumber_camp':
        height = this.lumberCamp(root, s)
        break
      case 'warehouse':
        height = this.warehouse(root, s)
        break
      case 'sawmill':
        height = this.sawmill(root, s, spinners)
        break
      default:
        height = this.placeholder(root, s)
        break
    }

    if (level >= 2) this.addLevelAccents(root, level, height)

    return { root, spinners, height }
  }

  // -------------------------------------------------------------------------------------
  // Kit primitives
  // -------------------------------------------------------------------------------------

  private box(
    parent: TransformNode,
    material: PaletteKey,
    size: { w: number; h: number; d: number },
    position: { x?: number; y?: number; z?: number },
    options: { emissive?: boolean; rotY?: number; rotZ?: number } = {},
  ): Mesh {
    const mesh = MeshBuilder.CreateBox(
      `${parent.name}_box`,
      { width: size.w, height: size.h, depth: size.d },
      this.scene,
    )
    mesh.parent = parent
    mesh.position.set(position.x ?? 0, position.y ?? 0, position.z ?? 0)
    mesh.material = this.materials.get(material, { emissive: options.emissive ?? false })
    if (options.rotY) mesh.rotation.y = options.rotY
    if (options.rotZ) mesh.rotation.z = options.rotZ
    return mesh
  }

  private cyl(
    parent: TransformNode,
    material: PaletteKey,
    size: { h: number; dTop: number; dBottom: number; sides?: number },
    position: { x?: number; y?: number; z?: number },
    options: { rotX?: number; rotZ?: number } = {},
  ): Mesh {
    const mesh = MeshBuilder.CreateCylinder(
      `${parent.name}_cyl`,
      {
        height: size.h,
        diameterTop: size.dTop,
        diameterBottom: size.dBottom,
        tessellation: size.sides ?? 8,
      },
      this.scene,
    )
    mesh.parent = parent
    mesh.position.set(position.x ?? 0, position.y ?? 0, position.z ?? 0)
    mesh.material = this.materials.get(material)
    if (options.rotX) mesh.rotation.x = options.rotX
    if (options.rotZ) mesh.rotation.z = options.rotZ
    return mesh
  }

  /** A gable roof made from two tilted slabs — cheaper and crisper than a prism. */
  private gableRoof(
    parent: TransformNode,
    material: PaletteKey,
    width: number,
    depth: number,
    y: number,
    pitch = 0.62,
  ): void {
    const slabLength = depth / 2 / Math.cos(pitch)
    for (const sign of [1, -1]) {
      const slab = this.box(
        parent,
        material,
        { w: width, h: 0.14, d: slabLength },
        { y: y + (Math.sin(pitch) * slabLength) / 2, z: (sign * depth) / 4 },
      )
      slab.rotation.x = sign * pitch
    }
  }

  /** A hip roof approximated by a tapered box stack — reads as civic and distinct. */
  private hipRoof(
    parent: TransformNode,
    material: PaletteKey,
    width: number,
    depth: number,
    y: number,
    steps = 3,
  ): void {
    for (let i = 0; i < steps; i++) {
      const t = i / steps
      this.box(
        parent,
        material,
        { w: width * (1 - t * 0.75), h: 0.26, d: depth * (1 - t * 0.75) },
        { y: y + i * 0.24 },
      )
    }
  }

  // -------------------------------------------------------------------------------------
  // Buildings — each must be identifiable from its silhouette alone (ART_DIRECTION.md §5)
  // -------------------------------------------------------------------------------------

  /** Silhouette cue: tallest, symmetrical, topped by a clock tower. */
  private townHall(root: TransformNode, s: number): number {
    this.box(root, 'stone', { w: 3.0 * s, h: 0.32, d: 2.6 * s }, { y: 0.16 })
    this.box(root, 'plaster', { w: 2.7 * s, h: 1.5 * s, d: 2.3 * s }, { y: 1.05 * s })
    this.box(root, 'timber', { w: 2.8 * s, h: 0.18, d: 2.4 * s }, { y: 1.82 * s })
    this.box(root, 'plaster', { w: 2.2 * s, h: 1.1 * s, d: 1.9 * s }, { y: 2.46 * s })

    this.hipRoof(root, 'roofBlue', 2.6 * s, 2.3 * s, 3.08 * s)

    // Clock tower — the distinguishing accent.
    this.box(root, 'plaster', { w: 0.6 * s, h: 1.2 * s, d: 0.6 * s }, { y: 4.15 * s })
    this.box(root, 'gold', { w: 0.42 * s, h: 0.42 * s, d: 0.1 }, { y: 4.5 * s, z: 0.31 * s })
    this.hipRoof(root, 'roofBlue', 0.8 * s, 0.8 * s, 4.78 * s, 2)

    // Door and windows
    this.box(root, 'timberDark', { w: 0.6 * s, h: 0.9 * s, d: 0.1 }, { y: 0.77 * s, z: 1.16 * s })
    for (const x of [-0.85, 0.85]) {
      this.box(
        root,
        'windowGlow',
        { w: 0.34 * s, h: 0.42 * s, d: 0.08 },
        { x: x * s, y: 1.15 * s, z: 1.16 * s },
        { emissive: true },
      )
    }
    return 5.4 * s
  }

  /** Silhouette cue: low, wide, open, striped awning. */
  private market(root: TransformNode, s: number): number {
    this.box(root, 'dirt', { w: 3.2 * s, h: 0.22, d: 2.8 * s }, { y: 0.11 })

    // Corner posts — the open frame reads as a stall, not a building.
    for (const x of [-1.3, 1.3]) {
      for (const z of [-1.1, 1.1]) {
        this.box(root, 'timber', { w: 0.2 * s, h: 1.5 * s, d: 0.2 * s }, { x: x * s, y: 0.97 * s, z: z * s })
      }
    }

    this.box(root, 'timber', { w: 3.0 * s, h: 0.16, d: 2.6 * s }, { y: 1.78 * s })

    // Striped awning — alternating slats give the market its identity at silhouette size.
    const slats = 6
    for (let i = 0; i < slats; i++) {
      const z = (-1.1 + (2.2 / (slats - 1)) * i) * s
      this.box(
        root,
        i % 2 === 0 ? 'roofRed' : 'plaster',
        { w: 3.1 * s, h: 0.12, d: (2.2 / slats) * s },
        { y: 1.94 * s, z },
      )
    }

    // Goods crates
    this.box(root, 'timberDark', { w: 0.55 * s, h: 0.5 * s, d: 0.55 * s }, { x: -0.9 * s, y: 0.47 * s, z: 0.6 * s })
    this.box(root, 'timberDark', { w: 0.45 * s, h: 0.4 * s, d: 0.45 * s }, { x: -0.9 * s, y: 0.92 * s, z: 0.6 * s })
    this.box(root, 'timber', { w: 0.6 * s, h: 0.55 * s, d: 0.6 * s }, { x: 1.0 * s, y: 0.5 * s, z: -0.5 * s })

    return 2.3 * s
  }

  /** Silhouette cue: smallest, asymmetric, with a stacked log pile beside it. */
  private lumberCamp(root: TransformNode, s: number): number {
    this.box(root, 'dirt', { w: 2.8 * s, h: 0.2, d: 2.6 * s }, { y: 0.1 })

    // Off-centre hut — asymmetry distinguishes it from the warehouse at a glance.
    this.box(root, 'timber', { w: 1.5 * s, h: 1.15 * s, d: 1.4 * s }, { x: -0.55 * s, y: 0.78 * s })
    this.gableRoof(root, 'roofSlate', 1.7 * s, 1.6 * s, 1.34 * s)
    this.box(
      root,
      'windowGlow',
      { w: 0.3 * s, h: 0.32 * s, d: 0.08 },
      { x: -0.55 * s, y: 0.9 * s, z: 0.71 * s },
      { emissive: true },
    )

    // Log pile — three rows, the signature accent.
    for (let row = 0; row < 3; row++) {
      const count = 3 - row
      for (let i = 0; i < count; i++) {
        this.cyl(
          root,
          'timberDark',
          { h: 1.5 * s, dTop: 0.3 * s, dBottom: 0.3 * s, sides: 6 },
          {
            x: (0.75 + row * 0.16) * s,
            y: (0.35 + row * 0.3) * s,
            z: (-0.6 + i * 0.32) * s,
          },
          { rotZ: Math.PI / 2 },
        )
      }
    }

    // Chopping block and axe handle
    this.cyl(root, 'timber', { h: 0.5 * s, dTop: 0.4 * s, dBottom: 0.44 * s }, { x: -0.2 * s, y: 0.45 * s, z: 0.95 * s })
    return 2.1 * s
  }

  /** Silhouette cue: long, low, uninterrupted slate roof and large doors. */
  private warehouse(root: TransformNode, s: number): number {
    this.box(root, 'stone', { w: 3.4 * s, h: 0.26, d: 2.4 * s }, { y: 0.13 })
    this.box(root, 'plaster', { w: 3.2 * s, h: 1.35 * s, d: 2.2 * s }, { y: 0.94 * s })

    // Timber banding breaks up the long wall.
    for (const x of [-1.0, 0, 1.0]) {
      this.box(root, 'timberDark', { w: 0.16 * s, h: 1.35 * s, d: 2.24 * s }, { x: x * s, y: 0.94 * s })
    }

    this.gableRoof(root, 'roofSlate', 3.5 * s, 2.5 * s, 1.6 * s, 0.5)

    // Big cargo doors — the identity cue.
    this.box(root, 'timber', { w: 1.1 * s, h: 1.0 * s, d: 0.1 }, { y: 0.76 * s, z: 1.12 * s })
    this.box(root, 'metal', { w: 1.16 * s, h: 0.12, d: 0.12 }, { y: 1.28 * s, z: 1.14 * s })

    // Crates outside
    this.box(root, 'timberDark', { w: 0.5 * s, h: 0.45 * s, d: 0.5 * s }, { x: 1.25 * s, y: 0.48 * s, z: 0.9 * s })
    return 2.8 * s
  }

  /** Silhouette cue: the rotating saw blade — the only building with a large disc. */
  private sawmill(root: TransformNode, s: number, spinners: TransformNode[]): number {
    this.box(root, 'stone', { w: 3.0 * s, h: 0.26, d: 2.6 * s }, { y: 0.13 })
    this.box(root, 'timber', { w: 2.2 * s, h: 1.4 * s, d: 2.0 * s }, { x: -0.3 * s, y: 0.96 * s })
    this.gableRoof(root, 'roofSlate', 2.4 * s, 2.2 * s, 1.64 * s)

    this.box(
      root,
      'windowGlow',
      { w: 0.5 * s, h: 0.36 * s, d: 0.08 },
      { x: -0.3 * s, y: 1.05 * s, z: 1.01 * s },
      { emissive: true },
    )

    // Saw blade on a pivot so the AnimationCoordinator can spin it while producing.
    const pivot = new TransformNode(`${root.name}_sawPivot`, this.scene)
    pivot.parent = root
    pivot.position.set(1.05 * s, 1.0 * s, 0)
    pivot.rotation.z = Math.PI / 2

    const blade = this.cyl(pivot, 'metal', { h: 0.08, dTop: 1.5 * s, dBottom: 1.5 * s, sides: 16 }, {})
    blade.rotation.x = Math.PI / 2

    // Teeth make the rotation legible — a smooth disc reads as static.
    for (let i = 0; i < 12; i++) {
      const angle = (i / 12) * Math.PI * 2
      this.box(
        pivot,
        'metal',
        { w: 0.16 * s, h: 0.1, d: 0.26 * s },
        { x: Math.cos(angle) * 0.8 * s, z: Math.sin(angle) * 0.8 * s },
        { rotY: -angle },
      )
    }
    spinners.push(pivot)

    // Support frame for the blade
    this.box(root, 'timberDark', { w: 0.18 * s, h: 1.1 * s, d: 0.18 * s }, { x: 1.05 * s, y: 0.55 * s, z: 0.55 * s })
    this.box(root, 'timberDark', { w: 0.18 * s, h: 1.1 * s, d: 0.18 * s }, { x: 1.05 * s, y: 0.55 * s, z: -0.55 * s })

    // Cut planks stacked by the blade
    for (let i = 0; i < 3; i++) {
      this.box(root, 'timber', { w: 1.0 * s, h: 0.1 * s, d: 0.6 * s }, { x: 0.6 * s, y: (0.31 + i * 0.11) * s, z: -1.0 * s })
    }
    return 2.9 * s
  }

  private placeholder(root: TransformNode, s: number): number {
    this.box(root, 'plaster', { w: 2.0 * s, h: 1.6 * s, d: 2.0 * s }, { y: 0.8 * s })
    this.gableRoof(root, 'roofRed', 2.2 * s, 2.2 * s, 1.6 * s)
    return 2.6 * s
  }

  /**
   * Level 2+ adds visible detail around the base so an upgrade is obvious without
   * reading a number (ART_DIRECTION.md §5).
   */
  private addLevelAccents(root: TransformNode, level: number, height: number): void {
    // A stone kerb around the plot edge.
    for (const [x, z] of [[-1.4, -1.4], [1.4, -1.4], [-1.4, 1.4], [1.4, 1.4]] as const) {
      this.box(root, 'stone', { w: 0.34, h: 0.3, d: 0.34 }, { x, y: 0.15, z })
    }

    if (level >= 3) {
      // A banner pole marks a fully upgraded building.
      this.box(root, 'timberDark', { w: 0.12, h: height * 0.5, d: 0.12 }, { x: 1.5, y: height * 0.25, z: 1.5 })
      this.box(root, 'gold', { w: 0.08, h: 0.5, d: 0.42 }, { x: 1.5, y: height * 0.45, z: 1.72 })
    }
  }
}
