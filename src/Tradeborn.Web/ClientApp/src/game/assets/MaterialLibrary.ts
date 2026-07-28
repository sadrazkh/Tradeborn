import { Color3 } from '@babylonjs/core/Maths/math.color'
import { StandardMaterial } from '@babylonjs/core/Materials/standardMaterial'
import type { Scene } from '@babylonjs/core/scene'

/**
 * The locked palette from docs/art-direction/ART_DIRECTION.md §3.
 *
 * These are the ONLY colours permitted in the 3D scene. Materials are created once and
 * shared — never `new StandardMaterial` per object (SCENE_GUIDELINES.md §3), because a
 * material per building would blow the material budget and defeat instancing.
 */
export const PALETTE = {
  // Terrain
  grassLight: '#8CC152',
  grassDark: '#6BA644',
  dirt: '#C9A87C',
  stone: '#9E9E93',
  water: '#4FA3C7',

  // Buildings
  timber: '#A9714B',
  timberDark: '#7C5134',
  plaster: '#E8DCC8',
  roofRed: '#C0563F',
  roofBlue: '#4A7BA7',
  roofSlate: '#5B6670',
  metal: '#8D949C',

  // Signal colours — never decorative
  gold: '#F0B429',
  success: '#4CAF7D',
  warning: '#E8A33D',
  danger: '#D9534F',

  // Emissive
  windowGlow: '#FFD98A',
} as const

export type PaletteKey = keyof typeof PALETTE

export class MaterialLibrary {
  private readonly cache = new Map<string, StandardMaterial>()

  constructor(private readonly scene: Scene) {}

  /**
   * Returns the shared material for a palette colour, creating it on first use.
   * `emissive` is used sparingly — window glow at night, and selection feedback.
   */
  get(key: PaletteKey, options: { emissive?: boolean } = {}): StandardMaterial {
    const cacheKey = `${key}:${options.emissive ? 'e' : ''}`
    const existing = this.cache.get(cacheKey)
    if (existing) return existing

    const colour = Color3.FromHexString(PALETTE[key])
    const material = new StandardMaterial(`mat_${cacheKey}`, this.scene)
    material.diffuseColor = colour

    // Stylised look: no specular highlights. Shine reads as "plastic" on flat-shaded
    // low-poly geometry and is the fastest way to make this style look cheap.
    material.specularColor = Color3.Black()

    if (options.emissive) {
      material.emissiveColor = colour.scale(0.85)
    }

    material.freeze()
    this.cache.set(cacheKey, material)
    return material
  }

  /** Semi-transparent variant used for placement ghosts. */
  getGhost(key: Extract<PaletteKey, 'success' | 'danger'>): StandardMaterial {
    const cacheKey = `ghost:${key}`
    const existing = this.cache.get(cacheKey)
    if (existing) return existing

    const material = new StandardMaterial(`mat_${cacheKey}`, this.scene)
    material.diffuseColor = Color3.FromHexString(PALETTE[key])
    material.emissiveColor = Color3.FromHexString(PALETTE[key]).scale(0.4)
    material.specularColor = Color3.Black()
    material.alpha = 0.55
    this.cache.set(cacheKey, material)
    return material
  }

  dispose(): void {
    for (const material of this.cache.values()) material.dispose()
    this.cache.clear()
  }
}
