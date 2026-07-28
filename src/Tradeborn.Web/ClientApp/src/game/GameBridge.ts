import { Scene } from '@babylonjs/core/scene'
import { Color3, Color4 } from '@babylonjs/core/Maths/math.color'
import { Vector3 } from '@babylonjs/core/Maths/math.vector'
import { DirectionalLight } from '@babylonjs/core/Lights/directionalLight'
import { HemisphericLight } from '@babylonjs/core/Lights/hemisphericLight'
import type { AbstractEngine } from '@babylonjs/core/Engines/abstractEngine'

import { createEngine } from './engine/EngineBootstrap'
import { PerformanceMonitor } from './engine/PerformanceMonitor'
import { IsometricCameraController } from './camera/IsometricCameraController'
import { MaterialLibrary, PALETTE } from './assets/MaterialLibrary'
import { ProceduralModelRegistry } from './assets/ModelRegistry'
import { PlotGrid } from './world/PlotGrid'
import { TerrainRenderer } from './world/TerrainRenderer'
import { BuildingRenderer } from './entities/BuildingRenderer'
import { SelectionSystem } from './systems/SelectionSystem'
import { installTestBridge, removeTestBridge } from './debug/TestBridge'
import type { CityDto, PerfSample, RendererBackend, SelectionInfo } from './types'

/**
 * The single seam between Vue and Babylon (ARCHITECTURE.md §7, SCENE_GUIDELINES.md §4).
 *
 * CRITICAL: no Babylon object created here may ever be placed inside a Vue `ref` or
 * `reactive`. Vue would deep-proxy the entire scene graph, which is the single most likely
 * cause of catastrophic frame drops in this project (RISKS.md R-10). Vue holds a reference
 * to this class only, and this class exposes plain data.
 *
 * Nothing in `game/` imports from `vue`.
 */
export class GameBridge {
  private engine: AbstractEngine | null = null
  private scene: Scene | null = null
  private camera: IsometricCameraController | null = null
  private materials: MaterialLibrary | null = null
  private terrain: TerrainRenderer | null = null
  private buildings: BuildingRenderer | null = null
  private selection: SelectionSystem | null = null
  private perf: PerformanceMonitor | null = null

  private keyLight: DirectionalLight | null = null
  private fillLight: HemisphericLight | null = null

  private elapsed = 0
  private backend: RendererBackend = 'webgl2'
  private started = false
  private resizeObserver: ResizeObserver | null = null
  private onResize = (): void => this.engine?.resize()

  /** Simulated time of day, 0..1. Kept here so the debug overlay can scrub it. */
  timeOfDay = 0.36

  async start(canvas: HTMLCanvasElement, city: CityDto): Promise<void> {
    if (this.started) return
    this.started = true

    const handle = await createEngine(canvas)
    this.engine = handle.engine
    this.backend = handle.backend

    const scene = new Scene(this.engine)
    this.scene = scene
    scene.clearColor = Color4.FromHexString(`${PALETTE.water}ff`)

    // Skipping pointer-move picking is a meaningful win: without it Babylon raycasts on
    // every mouse move even when nothing listens.
    scene.skipPointerMovePicking = true
    scene.autoClearDepthAndStencil = false

    this.materials = new MaterialLibrary(scene)
    const grid = new PlotGrid(city.gridSize)

    this.setupLighting(scene)

    this.camera = new IsometricCameraController(scene, canvas, grid.worldExtent)

    this.terrain = new TerrainRenderer(scene, this.materials, grid)
    this.terrain.buildBaseGround()
    this.terrain.build(city.plots)

    const models = new ProceduralModelRegistry(scene, this.materials)
    this.buildings = new BuildingRenderer(scene, this.materials, models, grid)
    this.buildings.render(city.buildings)

    this.selection = new SelectionSystem(scene, this.materials, this.buildings, grid)
    this.selection.attach()

    this.perf = new PerformanceMonitor(this.engine, scene)
    this.perf.start()

    this.applyTimeOfDay(this.timeOfDay)

    this.engine.runRenderLoop(() => {
      const delta = (this.engine?.getDeltaTime() ?? 16) / 1000
      this.elapsed += delta
      this.buildings?.update(delta, this.elapsed)
      this.selection?.update(this.elapsed)
      scene.render()
    })

    window.addEventListener('resize', this.onResize)

    // A window `resize` listener alone is not enough: the canvas can change size without
    // the window doing so — a hidden pane becoming visible, a devtools split, a CSS layout
    // change. When that happens Babylon keeps its stale backing buffer (which defaults to
    // 300x150 if the canvas was measured while hidden) and the scene renders at the wrong
    // resolution, stretched. Observing the element itself is the only reliable fix.
    this.resizeObserver = new ResizeObserver(() => this.engine?.resize())
    this.resizeObserver.observe(canvas)

    if (__TRADEBORN_DEBUG__) installTestBridge(this)
  }

  /**
   * Three-light rig from ART_DIRECTION.md §4. The rim light is not optional — it is what
   * stops flat-shaded low-poly geometry reading as a diagram.
   */
  private setupLighting(scene: Scene): void {
    this.keyLight = new DirectionalLight('key', new Vector3(-0.55, -0.78, 0.32), scene)
    this.keyLight.intensity = 1.1

    this.fillLight = new HemisphericLight('fill', new Vector3(0, 1, 0), scene)
    this.fillLight.intensity = 0.55
    this.fillLight.groundColor = Color3.FromHexString('#6B7A5A')

    const rim = new DirectionalLight('rim', new Vector3(0.62, -0.42, -0.55), scene)
    rim.intensity = 0.25
    rim.diffuse = Color3.FromHexString('#A9C4E8')
  }

  /**
   * Interpolates sky and light colour across the day. Cheap (three colour assignments)
   * and it is the single strongest "this city is alive" cue for its cost.
   */
  applyTimeOfDay(t: number): void {
    this.timeOfDay = ((t % 1) + 1) % 1

    const stops = [
      { at: 0.0, sky: '#2C3E60', key: '#8FA8D8', ambient: '#3E4A6B', intensity: 0.45 },
      { at: 0.25, sky: '#FFD5A0', key: '#FFC98A', ambient: '#8FA5C4', intensity: 0.9 },
      { at: 0.5, sky: '#A8D8F0', key: '#FFF6E0', ambient: '#B8D4E8', intensity: 1.15 },
      { at: 0.75, sky: '#E8956B', key: '#FF9E5E', ambient: '#7E88B0', intensity: 0.85 },
      { at: 1.0, sky: '#2C3E60', key: '#8FA8D8', ambient: '#3E4A6B', intensity: 0.45 },
    ]

    let a = stops[0]!
    let b = stops[stops.length - 1]!
    for (let i = 0; i < stops.length - 1; i++) {
      if (this.timeOfDay >= stops[i]!.at && this.timeOfDay <= stops[i + 1]!.at) {
        a = stops[i]!
        b = stops[i + 1]!
        break
      }
    }

    const span = b.at - a.at || 1
    const f = (this.timeOfDay - a.at) / span

    const sky = Color3.Lerp(Color3.FromHexString(a.sky), Color3.FromHexString(b.sky), f)
    const key = Color3.Lerp(Color3.FromHexString(a.key), Color3.FromHexString(b.key), f)
    const ambient = Color3.Lerp(Color3.FromHexString(a.ambient), Color3.FromHexString(b.ambient), f)

    if (this.scene) this.scene.clearColor = new Color4(sky.r, sky.g, sky.b, 1)
    if (this.keyLight) {
      this.keyLight.diffuse = key
      this.keyLight.intensity = a.intensity + (b.intensity - a.intensity) * f
    }
    if (this.fillLight) this.fillLight.diffuse = ambient
  }

  // ---- Plain-data surface consumed by Vue ------------------------------------------------

  get rendererBackend(): RendererBackend {
    return this.backend
  }

  get performance(): PerfSample {
    return this.perf?.sample ?? { fps: 0, drawCalls: 0, triangles: 0, meshes: 0 }
  }

  get p95Fps(): number {
    return this.perf?.p95Fps ?? 0
  }

  get isReady(): boolean {
    return this.scene !== null && this.started
  }

  onSelectionChanged(listener: (info: SelectionInfo | null) => void): () => void {
    return this.selection?.onSelectionChanged(listener) ?? (() => {})
  }

  selectBuilding(id: string): void {
    this.selection?.selectBuilding(id)
  }

  get currentSelection(): string | null {
    return this.selection?.current ?? null
  }

  listBuildings() {
    return this.buildings?.all() ?? []
  }

  get cameraState() {
    return this.camera?.state ?? null
  }

  focusOnPlot(col: number, row: number, gridSize: number): void {
    const grid = new PlotGrid(gridSize)
    this.camera?.focusOn(grid.toWorld(col, row))
  }

  dispose(): void {
    window.removeEventListener('resize', this.onResize)
    this.resizeObserver?.disconnect()
    this.resizeObserver = null
    if (__TRADEBORN_DEBUG__) removeTestBridge()

    this.perf?.dispose()
    this.selection?.dispose()
    this.buildings?.dispose()
    this.terrain?.dispose()
    this.camera?.dispose()
    this.materials?.dispose()
    this.scene?.dispose()
    this.engine?.dispose()

    this.started = false
  }
}
