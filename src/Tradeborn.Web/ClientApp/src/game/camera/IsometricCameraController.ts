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

/** Pointer travel in pixels above which a gesture counts as a drag rather than a tap. */
const DRAG_THRESHOLD_PX = 8

export class IsometricCameraController {
  readonly camera: ArcRotateCamera
  private snapHandle: number | null = null
  private focusHandle: number | null = null
  private downX = 0
  private downY = 0

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
    this.camera.panningDistanceLimit = worldExtent / 2 + 6

    this.camera.useNaturalPinchZoom = true

    this.attachSnapOnRelease()
  }

  /**
   * Eases alpha to the nearest 45° after a drag.
   *
   * Two details here are load-bearing and were both bugs first:
   *
   * 1. It only runs after an actual DRAG. Snapping on every `pointerup` meant a plain click
   *    to select a building also swung the camera — which breaks "the camera never moves on
   *    a tap" (SCENE_GUIDELINES.md §2) and reads as the scene jumping under your finger.
   *
   * 2. It zeroes the camera's inertial offsets, and keeps zeroing them for the duration of
   *    the tween. ArcRotateCamera keeps adding `inertialAlphaOffset` to `alpha` in its own
   *    `_checkInputs` after release; with this tween also writing `alpha` every frame there
   *    were two writers fighting, which produced visible oscillation.
   */
  private attachSnapOnRelease(): void {
    this.canvas.addEventListener('pointerdown', (event) => {
      this.downX = event.clientX
      this.downY = event.clientY
    })

    const onRelease = (event: PointerEvent) => {
      const travelled = Math.hypot(event.clientX - this.downX, event.clientY - this.downY)
      if (travelled <= DRAG_THRESHOLD_PX) {
        return // a tap, not a drag — leave the camera alone
      }

      this.snapToNearestIncrement()
    }

    this.canvas.addEventListener('pointerup', onRelease)
    this.canvas.addEventListener('pointercancel', onRelease)
  }

  private snapToNearestIncrement(): void {
    this.cancelSnap()

    const target = Math.round(this.camera.alpha / SNAP_INCREMENT) * SNAP_INCREMENT
    const start = this.camera.alpha
    const delta = target - start

    // Hand control back to the camera's own inertia if there is nothing to correct.
    if (Math.abs(delta) < 0.001) return

    const durationMs = 260
    const startedAt = performance.now()

    const step = () => {
      // Suppress the camera's own inertia so this tween is the only writer of alpha.
      this.camera.inertialAlphaOffset = 0
      this.camera.inertialBetaOffset = 0

      const t = Math.min(1, (performance.now() - startedAt) / durationMs)
      const eased = 1 - Math.pow(1 - t, 3) // ease-out cubic
      this.camera.alpha = start + delta * eased

      this.snapHandle = t < 1 ? requestAnimationFrame(step) : null
    }

    this.snapHandle = requestAnimationFrame(step)
  }

  private cancelSnap(): void {
    if (this.snapHandle !== null) {
      cancelAnimationFrame(this.snapHandle)
      this.snapHandle = null
    }
  }

  /** Smoothly move the camera target to a world position. */
  focusOn(position: Vector3, durationMs = 500): void {
    if (this.focusHandle !== null) cancelAnimationFrame(this.focusHandle)

    const start = this.camera.target.clone()
    const startedAt = performance.now()

    const step = () => {
      // Panning inertia is the same two-writer hazard as the alpha snap above.
      this.camera.inertialPanningX = 0
      this.camera.inertialPanningY = 0

      const t = Math.min(1, (performance.now() - startedAt) / durationMs)
      const eased = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2
      this.camera.setTarget(Vector3.Lerp(start, position, eased))

      this.focusHandle = t < 1 ? requestAnimationFrame(step) : null
    }

    this.focusHandle = requestAnimationFrame(step)
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
    this.cancelSnap()
    if (this.focusHandle !== null) cancelAnimationFrame(this.focusHandle)
    this.camera.dispose()
  }
}
