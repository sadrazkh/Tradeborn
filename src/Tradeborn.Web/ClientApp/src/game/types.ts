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

export interface CityDto {
  name: string
  gridSize: number
  serverTimeUtc: string
  balanceCoins: number
  capacityPerResource: number
  plots: PlotDto[]
  buildings: BuildingDto[]
  resources: ResourceBalanceDto[]
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
}

export type RendererBackend = 'webgpu' | 'webgl2' | 'webgl1'

export interface PerfSample {
  fps: number
  drawCalls: number
  triangles: number
  meshes: number
}
