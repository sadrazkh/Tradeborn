<script setup lang="ts">
import { computed, ref } from 'vue'
import type { BuildOption } from './uiTypes'

/**
 * The build control.
 *
 * Deliberately not a catalogue: it shows the handful of buildings the player can actually
 * place right now, with cost shown against what they hold. ART_DIRECTION.md §7 — no tables,
 * no forms on the main screen.
 */

const props = defineProps<{
  options: BuildOption[]
  balanceCoins: number
  wood: number
  cityLevel: number
  placing: boolean
  busy: boolean
}>()

const emit = defineEmits<{
  (e: 'pick', definitionId: string): void
  (e: 'cancel'): void
}>()

const open = ref(false)

const available = computed(() =>
  props.options.map((option) => ({
    ...option,
    locked: props.cityLevel < option.unlockCityLevel,
    affordable: props.balanceCoins >= option.costCoins && props.wood >= option.costWood,
  })),
)

function pick(definitionId: string) {
  open.value = false
  emit('pick', definitionId)
}
</script>

<template>
  <div class="build">
    <Transition name="menu">
      <div v-if="open && !placing" class="menu tb-panel">
        <button
          v-for="option in available"
          :key="option.definitionId"
          class="option"
          :class="{ disabled: option.locked || !option.affordable }"
          :disabled="option.locked || !option.affordable || busy"
          @click="pick(option.definitionId)"
        >
          <span class="label">{{ option.label }}</span>
          <span class="cost">
            <span :class="{ short: balanceCoins < option.costCoins }">{{ option.costCoins }}c</span>
            <span v-if="option.costWood" :class="{ short: wood < option.costWood }">
              {{ option.costWood }} wood
            </span>
          </span>
          <!-- The reason is stated, not merely implied by a greyed-out button. -->
          <span v-if="option.locked" class="reason">City level {{ option.unlockCityLevel }}</span>
        </button>
      </div>
    </Transition>

    <button v-if="placing" class="primary cancel" @click="emit('cancel')">
      Cancel placement
    </button>
    <button v-else class="primary" :disabled="busy" @click="open = !open">
      {{ open ? 'Close' : 'Build' }}
    </button>
  </div>
</template>

<style scoped>
.build {
  grid-column: 2;
  grid-row: 3;
  justify-self: center;
  align-self: end;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}

.primary {
  background: var(--tb-gold);
  color: #16202e;
  border: 0;
  border-radius: 999px;
  padding: 12px 30px;
  font-weight: 700;
  font-size: 15px;
  cursor: pointer;
  box-shadow: var(--tb-shadow);
  /* Touch target well above the 44px minimum (ART_DIRECTION.md §7). */
  min-height: 46px;
}

.primary:disabled {
  opacity: 0.55;
  cursor: default;
}

.cancel {
  background: transparent;
  color: var(--tb-text);
  border: 1px solid var(--tb-border);
}

.menu {
  padding: 8px;
  display: grid;
  gap: 4px;
  min-width: 250px;
  max-height: 44vh;
  overflow-y: auto;
}

.option {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 4px 10px;
  align-items: center;
  background: transparent;
  border: 0;
  border-radius: 10px;
  padding: 10px 12px;
  color: var(--tb-text);
  font-size: 14px;
  text-align: left;
  cursor: pointer;
}

.option:hover:not(.disabled) {
  background: rgba(255, 255, 255, 0.07);
}

.option.disabled {
  opacity: 0.45;
  cursor: default;
}

.label {
  font-weight: 600;
}

.cost {
  display: flex;
  gap: 8px;
  font-size: 12px;
  color: var(--tb-text-dim);
}

.cost .short {
  color: var(--tb-warning);
}

.reason {
  grid-column: 1 / -1;
  font-size: 11px;
  color: var(--tb-warning);
}

.menu-enter-active,
.menu-leave-active {
  transition:
    opacity 0.15s ease,
    transform 0.15s ease;
}
.menu-enter-from,
.menu-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
