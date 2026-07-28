/**
 * What each building makes, for display only.
 *
 * Mirrors the seed data in docs/economy/RESOURCE_GRAPH.md §4. The server owns the real rates
 * and enforces them; this exists so the building panel can explain a chain without an extra
 * round trip. If the two ever disagree, the server wins and the player sees the server's
 * numbers in their balances.
 */
export interface RecipeInfo {
  label: string
  inputs: { resource: string; qty: number }[]
  outputs: { resource: string; qty: number }[]
  /** Units produced per hour at level 1. */
  ratePerHour: number
}

const BASE_RATES: Record<string, RecipeInfo> = {
  lumber_camp: {
    label: 'Lumber Camp',
    inputs: [],
    outputs: [{ resource: 'wood', qty: 1 }],
    ratePerHour: 120,
  },
  farm: {
    label: 'Farm',
    inputs: [],
    outputs: [{ resource: 'grain', qty: 1 }],
    ratePerHour: 120,
  },
  sawmill: {
    label: 'Sawmill',
    inputs: [{ resource: 'wood', qty: 2 }],
    outputs: [{ resource: 'planks', qty: 1 }],
    ratePerHour: 60,
  },
  mill: {
    label: 'Mill',
    inputs: [{ resource: 'grain', qty: 2 }],
    outputs: [{ resource: 'flour', qty: 1 }],
    ratePerHour: 60,
  },
  bakery: {
    label: 'Bakery',
    inputs: [
      { resource: 'flour', qty: 2 },
      { resource: 'planks', qty: 1 },
    ],
    outputs: [{ resource: 'bread', qty: 1 }],
    ratePerHour: 30,
  },
}

const STORAGE: Record<string, string> = {
  town_hall: 'Town Hall',
  market: 'Market',
  warehouse: 'Warehouse',
}

export function recipeFor(definitionId: string): RecipeInfo | null {
  return BASE_RATES[definitionId] ?? null
}

export function labelFor(definitionId: string): string {
  return BASE_RATES[definitionId]?.label ?? STORAGE[definitionId] ?? definitionId
}

/**
 * Output per hour at a given level.
 *
 * Upgrades scale the cycle time down by 1.6× per level, so the rate scales up by the same
 * factor — see the reasoning in `Recipe.CycleMillisecondsAtLevel`.
 */
export function ratePerHourAtLevel(definitionId: string, level: number): number {
  const recipe = recipeFor(definitionId)
  if (!recipe) return 0
  return Math.floor(recipe.ratePerHour * Math.pow(1.6, level - 1))
}

/** Player-facing explanation of why a building stopped. */
export function haltExplanation(haltReason: string | null | undefined): string | null {
  switch (haltReason) {
    case 'NoInput':
      return 'Waiting for materials'
    case 'NoCapacity':
      return 'Storage is full'
    default:
      return null
  }
}
