<script setup lang="ts">
import { computed } from 'vue'
import type { SelectionInfo } from '@/game/types'
import { haltExplanation, ratePerHourAtLevel, recipeFor } from './recipes'

const props = defineProps<{
  selection: SelectionInfo | null
  busy: boolean
}>()

const emit = defineEmits<{
  (e: 'setProduction', payload: { buildingId: string; active: boolean }): void
  (e: 'upgrade', buildingId: string): void
}>()

const recipe = computed(() =>
  props.selection?.definitionId ? recipeFor(props.selection.definitionId) : null,
)

const rate = computed(() =>
  props.selection?.definitionId
    ? ratePerHourAtLevel(props.selection.definitionId, props.selection.level ?? 1)
    : 0,
)

const halt = computed(() => haltExplanation(props.selection?.haltReason))

const isBuilding = computed(() => props.selection?.kind === 'building')
const isUnderConstruction = computed(() => props.selection?.state === 'UnderConstruction')
const isRunning = computed(() => props.selection?.state === 'Producing' || props.selection?.state === 'Halted')

/** Upgrading shows the *projected* rate, never a bare cost (PLAYER_JOURNEY.md 4:20). */
const upgradeDelta = computed(() => {
  const selection = props.selection
  if (!selection?.definitionId || !selection.level) return null
  if (selection.level >= 3) return null

  const next = ratePerHourAtLevel(selection.definitionId, selection.level + 1)
  return next > 0 ? { from: rate.value, to: next } : null
})

const stateLabel = computed(() => {
  switch (props.selection?.state) {
    case 'UnderConstruction':
      return props.selection.pendingLevel && props.selection.level && props.selection.pendingLevel > props.selection.level
        ? 'Upgrading'
        : 'Under construction'
    case 'Producing':
      return 'Producing'
    case 'Halted':
      return 'Stopped'
    case 'Idle':
      return 'Not running'
    default:
      return ''
  }
})

const stateTone = computed(() => {
  switch (props.selection?.state) {
    case 'Producing':
      return 'producing'
    case 'Halted':
      return 'halted'
    case 'UnderConstruction':
      return 'building'
    default:
      return 'idle'
  }
})
</script>

<template>
  <Transition name="card">
    <div v-if="selection" class="card tb-panel">
      <div class="title">{{ selection.title }}</div>
      <div class="subtitle">{{ selection.subtitle }}</div>

      <div v-if="selection.state" class="state" :class="stateTone">
        <!-- Shape + text, never colour alone (ART_DIRECTION.md §9). -->
        <span class="dot" aria-hidden="true"></span>
        {{ stateLabel }}
      </div>

      <!-- Why it stopped, stated plainly. "Storage is full" is a goal; a red dot is not. -->
      <div v-if="halt" class="halt">{{ halt }}</div>

      <div v-if="recipe && !isUnderConstruction" class="chain">
        <div class="flow">
          <template v-if="recipe.inputs.length">
            <span v-for="input in recipe.inputs" :key="input.resource" class="chip">
              {{ input.qty }} {{ input.resource }}
            </span>
            <span class="arrow" aria-hidden="true">→</span>
          </template>
          <span v-for="output in recipe.outputs" :key="output.resource" class="chip out">
            {{ output.qty }} {{ output.resource }}
          </span>
        </div>
        <div class="rate">{{ rate }} / hour</div>
      </div>

      <div v-if="isBuilding && !isUnderConstruction" class="actions">
        <button
          v-if="recipe"
          class="action"
          :class="isRunning ? 'pause' : 'start'"
          :disabled="busy"
          @click="emit('setProduction', { buildingId: selection.id, active: !isRunning })"
        >
          {{ isRunning ? 'Pause' : 'Start production' }}
        </button>

        <button
          v-if="upgradeDelta"
          class="action upgrade"
          :disabled="busy"
          @click="emit('upgrade', selection.id)"
        >
          Upgrade
          <span class="delta">{{ upgradeDelta.from }} → {{ upgradeDelta.to }}/h</span>
        </button>
      </div>

      <div class="coords">Plot {{ selection.col }}, {{ selection.row }}</div>
    </div>
  </Transition>
</template>

<style scoped>
.card {
  grid-column: 3;
  grid-row: 2;
  align-self: center;
  justify-self: end;
  padding: 16px 18px;
  min-width: 232px;
  max-width: 280px;
}

.title {
  font-size: 17px;
  font-weight: 650;
  letter-spacing: -0.01em;
}

.subtitle {
  color: var(--tb-text-dim);
  font-size: 13px;
  margin-top: 2px;
}

.state {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  margin-top: 12px;
  font-size: 12px;
  font-weight: 600;
  padding: 4px 10px 4px 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.06);
}

.dot {
  width: 7px;
  height: 7px;
  border-radius: 2px;
  background: currentColor;
}

.state.producing {
  color: var(--tb-success);
}
.state.halted {
  color: var(--tb-warning);
}
.state.building {
  color: var(--tb-gold);
}
.state.idle {
  color: var(--tb-text-dim);
}

.halt {
  margin-top: 8px;
  font-size: 12px;
  color: var(--tb-warning);
}

.chain {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--tb-border);
}

.flow {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
}

.chip {
  background: rgba(255, 255, 255, 0.07);
  border-radius: 6px;
  padding: 3px 7px;
  font-size: 11px;
}

.chip.out {
  background: rgba(240, 180, 41, 0.16);
  color: var(--tb-gold);
}

.arrow {
  color: var(--tb-text-dim);
  font-size: 12px;
}

.rate {
  margin-top: 6px;
  font-size: 12px;
  color: var(--tb-text-dim);
}

.actions {
  display: grid;
  gap: 6px;
  margin-top: 12px;
}

.action {
  border: 0;
  border-radius: 10px;
  padding: 9px 12px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
  color: #16202e;
  min-height: 40px;
}

.action:disabled {
  opacity: 0.55;
  cursor: default;
}

.action.start {
  background: var(--tb-success);
}

.action.pause {
  background: rgba(255, 255, 255, 0.12);
  color: var(--tb-text);
}

.action.upgrade {
  background: var(--tb-gold);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.delta {
  font-size: 11px;
  font-weight: 600;
  opacity: 0.75;
}

.coords {
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--tb-border);
  color: var(--tb-text-dim);
  font-size: 11px;
}

.card-enter-active,
.card-leave-active {
  transition:
    opacity 0.16s ease,
    transform 0.16s ease;
}
.card-enter-from,
.card-leave-to {
  opacity: 0;
  transform: translateX(12px);
}
</style>
