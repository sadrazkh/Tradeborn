import { ArcRotateCamera } from '@babylonjs/core/Cameras/arcRotateCamera'
import { Vector3 } from '@babylonjs/core/Maths/math.vector'
import type { Scene } from '@babylonjs/core/scene'

import '@babylonjs/core/Cameras/Inputs/arcRotateCameraPointersInput'
import '@babylonjs/core/Cameras/Inputs/arcRotateCameraKeyboardMoveInput'
import '@babylonjs/core/Cameras/Inputs/arcRotateCameraMouseWheelInput'

/**
 * Isometric-feeling camera per docs/art-direction/SCENE_GUIDELINES.md §2.
 *
 * Elevation (beta) is LOCKED at 55°. This is a design decision, not a limitation: it
 * guarantees every building silhouette is authored for the angle it is seen at, and removes
 * a whole class of "the player found a bad camera angle" bugs.
 *
 * Rotation (alpha) is free while dragging but snaps to 45° increments on release, so the
 * city always settles into a readable orientation.
 */

const BETA_LOCKED = (55 * Math.PI) / 180
const SNAP_INCREMENT = Math.PI / 4
const RADIUS_MIN = 18
const RADIUS_MAX = 60
const RADIUS_DEFAULT = 34

export class IsometricCameraController {
  readonly camera: ArcRotateCamera
  private snapHandle: number | null = null

  constructor(
    scene: Scene,
    private readonly canvas: HTMLCanvasElement,
    worldExtent: number,
  ) {
    this.camera = new ArcRotateCamera(
      'mainCamera',
      -Math.PI / 4,
      BETA_LOCKED,
      RADIUS_DEFAULT,
      Vector3.Zero(),
      scene,
    )

    this.camera.attachControl(canvas, true)

    // Narrow FOV flattens perspective so the scene reads as isometric while keeping just
    // enough depth cue that it does not look like a flat orthographic diagram.
    this.camera.fov = 0.5

    this.camera.lowerBetaLimit = BETA_LOCKED
    this.camera.upperBetaLimit = BETA_LOCKED

    this.camera.lowerRadiusLimit = RADIUS_MIN
    this.camera.upperRadiusLimit = RADIUS_MAX

    // Weighted, not floaty. This is most of what makes the camera feel good.
    this.camera.inertia = 0.85
    this.camera.angularSensibilityX = 1400
    this.camera.angularSensibilityY = 1400
    this.camera.wheelPrecision = 12
    this.camera.pinchPrecision = 60
    this.camera.panningInertia = 0.85
    this.camera.panningSensibility = 90

    // Clamp panning so the player cannot lose the city off-screen.
    const limit = worldExtent / 2 + 6
    this.camera.panningDistanceLimit = limit

    this.camera.useNaturalPinchZoom = true

    this.attachSnapOnRelease()
  }

  /**
   * On pointer release, ease alpha to the nearest 45°. Implemented with a small manual
   * tween rather than Babylon's Animation system to avoid pulling in the animation module
   * for one effect.
   */
  private attachSnapOnRelease(): void {
    const onRelease = () => {
      if (this.snapHandle !== null) cancelAnimationFrame(this.snapHandle)

      const target = Math.round(this.camera.alpha / SNAP_INCREMENT) * SNAP_INCREMENT
      const start = this.camera.alpha
      const delta = target - start
      if (Math.abs(delta) < 0.001) return

      const durationMs = 260
      const startedAt = performance.now()

      const step = () => {
        const t = Math.min(1, (performance.now() - startedAt) / durationMs)
        // ease-out cubic
        const eased = 1 - Math.pow(1 - t, 3)
        this.camera.alpha = start + delta * eased
        if (t < 1) {
          this.snapHandle = requestAnimationFrame(step)
        } else {
          this.snapHandle = null
        }
      }
      this.snapHandle = requestAnimationFrame(step)
    }

    this.canvas.addEventListener('pointerup', onRelease)
    this.canvas.addEventListener('pointercancel', onRelease)
  }

  /** Smoothly move the camera target to a world position. */
  focusOn(position: Vector3, durationMs = 500): void {
    const start = this.camera.target.clone()
    const startedAt = performance.now()

    const step = () => {
      const t = Math.min(1, (performance.now() - startedAt) / durationMs)
      const eased = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
      this.camera.setTarget(Vector3.Lerp(start, position, eased))
      if (t < 1) requestAnimationFrame(step)
    }
    requestAnimationFrame(step)
  }

  get state() {
    return {
      alpha: this.camera.alpha,
      beta: this.camera.beta,
      radius: this.camera.radius,
      target: {
        x: this.camera.target.x,
        y: this.camera.target.y,
        z: this.camera.target.z,
      },
    }
  }

  dispose(): void {
    if (this.snapHandle !== null) cancelAnimationFrame(this.snapHandle)
    this.camera.dispose()
  }
}
