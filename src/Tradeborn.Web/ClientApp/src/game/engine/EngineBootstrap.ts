import { Engine } from '@babylonjs/core/Engines/engine'
import { WebGPUEngine } from '@babylonjs/core/Engines/webgpuEngine'
import type { AbstractEngine } from '@babylonjs/core/Engines/abstractEngine'
import type { RendererBackend } from '../types'

/**
 * Creates the rendering engine, preferring WebGPU and falling back to WebGL2.
 *
 * Per RISKS.md R-07, WebGL2 is the DEFAULT path and WebGPU is opt-in: mobile Safari's
 * WebGPU support is still uneven, and nothing in the design requires it. The query
 * parameter `?webgpu=1` opts in; `?webgpu=0` forces the fallback so the WebGL2 path can be
 * tested deliberately rather than by accident.
 */
export interface EngineHandle {
  engine: AbstractEngine
  backend: RendererBackend
}

export async function createEngine(canvas: HTMLCanvasElement): Promise<EngineHandle> {
  const params = new URLSearchParams(window.location.search)
  const preference = params.get('webgpu')

  if (preference === '1') {
    const gpu = await tryWebGPU(canvas)
    if (gpu) return gpu
    console.warn('[Tradeborn] WebGPU requested but unavailable — falling back to WebGL.')
  }

  const engine = new Engine(canvas, true, {
    preserveDrawingBuffer: true,
    stencil: true,
    antialias: true,
    powerPreference: 'high-performance',
    // Guards against a lost context silently freezing the scene on mobile.
    doNotHandleContextLost: false,
  })

  // Cap device pixel ratio: on a 3x phone screen, rendering at native resolution costs
  // ~9x the fragment work for a difference nobody sees at this camera distance.
  engine.setHardwareScalingLevel(1 / Math.min(window.devicePixelRatio || 1, 2))

  return {
    engine,
    backend: engine.webGLVersion >= 2 ? 'webgl2' : 'webgl1',
  }
}

async function tryWebGPU(canvas: HTMLCanvasElement): Promise<EngineHandle | null> {
  try {
    const supported = await WebGPUEngine.IsSupportedAsync
    if (!supported) return null

    const engine = new WebGPUEngine(canvas, {
      antialias: true,
      stencil: true,
    })
    await engine.initAsync()
    engine.setHardwareScalingLevel(1 / Math.min(window.devicePixelRatio || 1, 2))

    return { engine, backend: 'webgpu' }
  } catch (error) {
    console.warn('[Tradeborn] WebGPU initialisation failed:', error)
    return null
  }
}
