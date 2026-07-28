import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { InstancedMesh } from '@babylonjs/core/Meshes/instancedMesh'
import { Vector3 } from '@babylonjs/core/Maths/math.vector'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Meshes/Builders/boxBuilder'
import '@babylonjs/core/Meshes/Builders/cylinderBuilder'
import '@babylonjs/core/Meshes/Builders/sphereBuilder'

import type { MaterialLibrary, PaletteKey } from '../assets/MaterialLibrary'
import { PLOT_GAP, PLOT_HEIGHT, PLOT_SIZE, PlotGrid } from './PlotGrid'
import type { PlotDto, TerrainKind } from '../types'

/**
 * Renders the plot grid and scenery props.
 *
 * Performance strategy (PERFORMANCE_BUDGET.md §1): one master mesh per terrain type with
 * `createInstance()` per plot. All instances of a master share a single draw call, and —
 * unlike thin instances — picking works out of the box, which the SelectionSystem needs.
 *
 * 64 plots across 3 terrain types therefore cost 3 draw calls, not 64.
 */

const TERRAIN_MATERIAL: Record<TerrainKind, PaletteKey> = {
  grass: 'grassLight',
  dirt: 'dirt',
  stone: 'stone',
}

export class TerrainRenderer {
  private readonly masters = new Map<TerrainKind, Mesh>()
  private readonly instances: InstancedMesh[] = []
  private readonly props: Mesh[] = []
  private treeMaster: Mesh | null = null
  private trunkMaster: Mesh | null = null

  constructor(
    private readonly scene: Scene,
    private readonly materials: MaterialLibrary,
    private readonly grid: PlotGrid,
  ) {}

  build(plots: PlotDto[]): void {
    this.buildMasters()

    for (const plot of plots) {
      const master = this.masters.get(plot.terrain)
      if (!master) continue

      const instance = master.createInstance(`plot_${plot.col}_${plot.row}`)
      const centre = this.grid.toWorld(plot.col, plot.row)
      instance.position.set(centre.x, -PLOT_HEIGHT / 2, centre.z)

      // Locked plots sit slightly lower and darker so the playable area reads instantly.
      if (!plot.unlocked) {
        instance.position.y -= 0.22
        instance.scaling.y = 0.8
      }

      instance.isPickable = true
      instance.metadata = { kind: 'plot', col: plot.col, row: plot.row, unlocked: plot.unlocked }
      instance.freezeWorldMatrix()
      this.instances.push(instance)
    }

    this.scatterTrees(plots)
  }

  private buildMasters(): void {
    const top = PLOT_SIZE - PLOT_GAP

    for (const terrain of Object.keys(TERRAIN_MATERIAL) as TerrainKind[]) {
      const master = MeshBuilder.CreateBox(
        `plotMaster_${terrain}`,
        { width: top, depth: top, height: PLOT_HEIGHT },
        this.scene,
      )
      master.material = this.materials.get(TERRAIN_MATERIAL[terrain])
      // The master itself is never drawn — only its instances are.
      master.setEnabled(false)
      master.isPickable = false
      this.masters.set(terrain, master)
    }
  }

  /**
   * Scatters trees on locked grass plots so the unplayable border reads as wilderness
   * rather than as missing content. Deterministic placement (hashed from coordinates) so
   * the scene is identical on every load and screenshots are stable.
   */
  private scatterTrees(plots: PlotDto[]): void {
    this.trunkMaster = MeshBuilder.CreateCylinder(
      'treeTrunkMaster',
      { height: 1.1, diameterTop: 0.22, diameterBottom: 0.32, tessellation: 6 },
      this.scene,
    )
    this.trunkMaster.material = this.materials.get('timberDark')
    this.trunkMaster.setEnabled(false)
    this.trunkMaster.isPickable = false

    this.treeMaster = MeshBuilder.CreateSphere(
      'treeCanopyMaster',
      { diameter: 1.6, segments: 4 },
      this.scene,
    )
    this.treeMaster.material = this.materials.get('grassDark')
    this.treeMaster.setEnabled(false)
    this.treeMaster.isPickable = false

    for (const plot of plots) {
      if (plot.unlocked || plot.terrain !== 'grass') continue

      const hash = (plot.col * 73856093) ^ (plot.row * 19349663)
      const count = Math.abs(hash) % 3
      const centre = this.grid.toWorld(plot.col, plot.row)

      for (let i = 0; i < count; i++) {
        const jitterX = (((Math.abs(hash >> (i * 3 + 1)) % 100) / 100) - 0.5) * 2.2
        const jitterZ = (((Math.abs(hash >> (i * 3 + 5)) % 100) / 100) - 0.5) * 2.2
        const scale = 0.75 + ((Math.abs(hash >> (i + 2)) % 50) / 100)

        const trunk = this.trunkMaster.createInstance(`trunk_${plot.col}_${plot.row}_${i}`)
        trunk.position.set(centre.x + jitterX, 0.55 * scale, centre.z + jitterZ)
        trunk.scaling.setAll(scale)
        trunk.isPickable = false
        trunk.freezeWorldMatrix()
        this.instances.push(trunk)

        const canopy = this.treeMaster.createInstance(`canopy_${plot.col}_${plot.row}_${i}`)
        canopy.position.set(centre.x + jitterX, 1.5 * scale, centre.z + jitterZ)
        canopy.scaling.set(scale, scale * 1.15, scale)
        canopy.isPickable = false
        canopy.freezeWorldMatrix()
        this.instances.push(canopy)
      }
    }
  }

  /** A soft ground plane under the grid so the city does not float in the void. */
  buildBaseGround(): void {
    const extent = this.grid.worldExtent + 24
    const ground = MeshBuilder.CreateBox(
      'baseGround',
      { width: extent, depth: extent, height: 1 },
      this.scene,
    )
    ground.position = new Vector3(0, -PLOT_HEIGHT - 0.5, 0)
    ground.material = this.materials.get('grassDark')
    ground.isPickable = false
    ground.freezeWorldMatrix()
    this.props.push(ground)
  }

  dispose(): void {
    for (const instance of this.instances) instance.dispose()
    for (const master of this.masters.values()) master.dispose()
    for (const prop of this.props) prop.dispose()
    this.treeMaster?.dispose()
    this.trunkMaster?.dispose()
    this.instances.length = 0
    this.props.length = 0
    this.masters.clear()
  }
}
