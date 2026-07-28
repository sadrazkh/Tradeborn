/**
 * Shared UI types.
 *
 * These live outside the components because a Vue `<script setup>` block cannot export
 * types — everything in it is component-local by design.
 */

export interface BuildOption {
  definitionId: string
  label: string
  costCoins: number
  costWood: number
  unlockCityLevel: number
}

export interface Toast {
  id: number
  text: string
  tone: 'ok' | 'warn'
}
