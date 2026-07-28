/**
 * Shapes shared between the Vue layer and the Babylon layer.
 *
 * Rule (docs/art-direction/SCENE_GUIDELINES.md §4): everything crossing the Vue/Babylon
 * boundary is plain data. No mesh, material, or scene reference ever appears here.
 */

export type TerrainKind = 'grass' | 'dirt' | 'stone'

export type BuildingState = 'UnderConstruction' | 'Idle' | 'Producing' | 'Halted'

export interface PlotDto {
  col: number
  row: number
  terrain: TerrainKind
  unlocked: boolean
}

export interface BuildingDto {
  id: string
  definitionId: string
  col: number
  row: number
  level: number
  state: BuildingState
  haltReason?: string | null
  /** ISO instant when in-flight work lands. Null when nothing is in flight. */
  completesAtUtc?: string | null
  /** Level once the in-flight work finishes; equal to `level` when idle. */
  pendingLevel?: number
  /**
   * Build progress 0..1 at the response's `serverTimeUtc`.
   *
   * Sent so the renderer can jump straight to the correct construction stage on load rather
   * than replaying from zero. It is then interpolated against the synchronised server clock,
   * never against `Date.now()` (REALTIME_AND_TIME_MODEL.md §7).
   */
  constructionProgress?: number
}

export interface ResourceBalanceDto {
  resource: string
  quantity: number
  capacity: number
}

export interface OfflineSummaryDto {
  since: string
  produced: ResourceBalanceDto[]
  haltedBuildings: string[]
}

/**
 * A load on the road.
 *
 * Absolute server instants rather than a progress fraction, so a client loading mid-journey
 * places the cart where it actually is instead of restarting the trip.
 */
export interface TransportDto {
  id: string
  fromBuildingId: string
  resource: string
  quantity: number
  departedAtUtc: string
  arrivesAtUtc: string
}

export interface PlayerProgressDto {
  level: number
  xp: number
  xpToNextLevel: number
  cityLevel: number
}

export interface CityDto {
  name: string
  gridSize: number
  serverTimeUtc: string
  balanceCoins: number
  capacityPerResource: number
  plots: PlotDto[]
  buildings: BuildingDto[]
  resources: ResourceBalanceDto[]
  transports: TransportDto[]
  progress: PlayerProgressDto
  offlineSummary?: OfflineSummaryDto | null
}

/** What the HUD shows when the player selects something in the world. */
export interface SelectionInfo {
  kind: 'building' | 'plot'
  id: string
  title: string
  subtitle: string
  state?: BuildingState
  col: number
  row: number
  level?: number
  /** Present for buildings — lets the panel look up the recipe and rate. */
  definitionId?: string
  haltReason?: string | null
  completesAtUtc?: string | null
  pendingLevel?: number
}

export type RendererBackend = 'webgpu' | 'webgl2' | 'webgl1'

export interface PerfSample {
  fps: number
  drawCalls: number
  triangles: number
  meshes: number
}
