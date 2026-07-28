import { MeshBuilder } from '@babylonjs/core/Meshes/meshBuilder'
import type { Mesh } from '@babylonjs/core/Meshes/mesh'
import type { InstancedMesh } from '@babylonjs/core/Meshes/instancedMesh'
import { Vector3 } from '@babylonjs/core/Maths/math.vector'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Meshes/Builders/boxBuilder'

import type { MaterialLibrary, PaletteKey } from '../assets/MaterialLibrary'
import type { RoadNetwork, RoadTile } from '../world/RoadNetwork'

/**
 * Citizens and carts moving along the road network.
 *
 * These are purely cosmetic — no economy depends on them — but they are the single biggest
 * "this city is alive" cue for their cost, which is why they belong in Phase 2 alongside
 * the terrain rather than waiting for logistics in Phase 5.
 *
 * Performance rules that are not optional here (PERFORMANCE_BUDGET.md §1,
 * SCENE_GUIDELINES.md §3):
 * - Every agent is an instance of one master mesh, so 20 citizens cost 2 draw calls, not 40.
 * - The whole pool is allocated up front and never grows during play.
 * - `update` mutates pre-allocated scratch vectors; it allocates nothing per frame.
 */

interface Agent {
  body: InstancedMesh
  accent: InstancedMesh
  from: RoadTile
  to: RoadTile
  previous: RoadTile | null
  /** 0..1 progress along the current edge. */
  t: number
  speed: number
  /** Per-agent phase so the walk cycle does not move in lockstep. */
  phase: number
  seed: number
}

export interface AgentKind {
  bodySize: { w: number; h: number; d: number }
  accentSize: { w: number; h: number; d: number }
  accentOffsetY: number
  bodyColour: PaletteKey
  accentColour: PaletteKey
  /** World units per second. */
  speed: number
  /** Vertical bob amplitude; carts do not bob. */
  bob: number
  laneOffset: number
}

export const CITIZEN: AgentKind = {
  bodySize: { w: 0.32, h: 0.75, d: 0.32 },
  accentSize: { w: 0.34, h: 0.32, d: 0.34 },
  accentOffsetY: 0.54,
  bodyColour: 'roofBlue',
  accentColour: 'plaster',
  speed: 1.5,
  bob: 0.06,
  laneOffset: 0.9,
}

export const CART: AgentKind = {
  bodySize: { w: 0.85, h: 0.5, d: 1.35 },
  accentSize: { w: 0.9, h: 0.28, d: 0.5 },
  accentOffsetY: 0.42,
  bodyColour: 'timber',
  accentColour: 'timberDark',
  speed: 3.2,
  bob: 0,
  laneOffset: -0.9,
}

export class AgentRenderer {
  private readonly agents: Agent[] = []
  private bodyMaster: Mesh | null = null
  private accentMaster: Mesh | null = null

  // Scratch vectors, reused every frame. See the allocation rule above.
  private readonly fromPos = new Vector3()
  private readonly toPos = new Vector3()

  constructor(
    private readonly scene: Scene,
    private readonly materials: MaterialLibrary,
    private readonly roads: RoadNetwork,
    private readonly kind: AgentKind,
    private readonly name: string,
  ) {}

  spawn(count: number): void {
    if (this.roads.isEmpty || count <= 0) return

    this.bodyMaster = MeshBuilder.CreateBox(`${this.name}BodyMaster`, toBoxOptions(this.kind.bodySize), this.scene)
    this.bodyMaster.material = this.materials.get(this.kind.bodyColour)
    this.bodyMaster.setEnabled(false)
    this.bodyMaster.isPickable = false

    this.accentMaster = MeshBuilder.CreateBox(`${this.name}AccentMaster`, toBoxOptions(this.kind.accentSize), this.scene)
    this.accentMaster.material = this.materials.get(this.kind.accentColour)
    this.accentMaster.setEnabled(false)
    this.accentMaster.isPickable = false

    for (let i = 0; i < count; i++) {
      // Deterministic spread so the scene looks identical on every load and screenshots
      // stay comparable between runs.
      const seed = (i * 2654435761) >>> 0
      const from = this.roads.tileAt(i * 3 + (seed % 7))
      const to = this.roads.nextTile(from, null, seed)

      const body = this.bodyMaster.createInstance(`${this.name}_${i}`)
      const accent = this.accentMaster.createInstance(`${this.name}_a_${i}`)
      body.isPickable = false
      accent.isPickable = false

      this.agents.push({
        body,
        accent,
        from,
        to,
        previous: null,
        t: ((seed % 100) / 100),
        speed: this.kind.speed * (0.8 + ((seed >> 8) % 40) / 100),
        phase: ((seed >> 16) % 628) / 100,
        seed,
      })
    }
  }

  update(deltaSeconds: number, elapsedSeconds: number): void {
    for (const agent of this.agents) {
      this.roads.positionToRef(agent.from, this.fromPos)
      this.roads.positionToRef(agent.to, this.toPos)

      const distance = Vector3.Distance(this.fromPos, this.toPos) || 1
      agent.t += (deltaSeconds * agent.speed) / distance

      while (agent.t >= 1) {
        agent.t -= 1
        agent.previous = agent.from
        agent.from = agent.to
        agent.seed = (agent.seed * 1103515245 + 12345) >>> 0
        agent.to = this.roads.nextTile(agent.from, agent.previous, agent.seed)

        this.roads.positionToRef(agent.from, this.fromPos)
        this.roads.positionToRef(agent.to, this.toPos)
      }

      const x = this.fromPos.x + (this.toPos.x - this.fromPos.x) * agent.t
      const z = this.fromPos.z + (this.toPos.z - this.fromPos.z) * agent.t

      // Face the direction of travel, and offset sideways so agents keep to a lane instead
      // of walking down the exact centre line of the road.
      const dx = this.toPos.x - this.fromPos.x
      const dz = this.toPos.z - this.fromPos.z
      const heading = Math.atan2(dx, dz)
      const offsetX = Math.cos(heading) * this.kind.laneOffset
      const offsetZ = -Math.sin(heading) * this.kind.laneOffset

      const bob = this.kind.bob === 0
        ? 0
        : Math.abs(Math.sin(elapsedSeconds * 7 + agent.phase)) * this.kind.bob

      const baseY = this.kind.bodySize.h / 2 + bob

      agent.body.position.set(x + offsetX, baseY, z + offsetZ)
      agent.body.rotation.y = heading

      agent.accent.position.set(x + offsetX, baseY + this.kind.accentOffsetY, z + offsetZ)
      agent.accent.rotation.y = heading
    }
  }

  /** Used by QualityManager to thin the crowd on weaker hardware. */
  setVisibleCount(count: number): void {
    for (let i = 0; i < this.agents.length; i++) {
      const visible = i < count
      this.agents[i]!.body.setEnabled(visible)
      this.agents[i]!.accent.setEnabled(visible)
    }
  }

  get count(): number {
    return this.agents.length
  }

  dispose(): void {
    for (const agent of this.agents) {
      agent.body.dispose()
      agent.accent.dispose()
    }
    this.agents.length = 0
    this.bodyMaster?.dispose()
    this.accentMaster?.dispose()
    this.bodyMaster = null
    this.accentMaster = null
  }
}

/** The kit uses w/h/d for brevity; Babylon's builder wants width/height/depth. */
function toBoxOptions(size: { w: number; h: number; d: number }) {
  return { width: size.w, height: size.h, depth: size.d }
}
