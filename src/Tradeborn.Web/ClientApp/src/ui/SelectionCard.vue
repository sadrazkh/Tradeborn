<script setup lang="ts">
import { computed } from 'vue'
import type { SelectionInfo } from '@/game/types'

const props = defineProps<{ selection: SelectionInfo | null }>()

const STATE_LABEL: Record<string, string> = {
  UnderConstruction: 'Under construction',
  Idle: 'Idle',
  Producing: 'Producing',
  Halted: 'Halted',
}

const stateClass = computed(() => {
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

      <div v-if="selection.state" class="state" :class="stateClass">
        <!-- Shape + text, never colour alone (ART_DIRECTION.md §9). -->
        <span class="dot" aria-hidden="true"></span>
        {{ STATE_LABEL[selection.state] ?? selection.state }}
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
  min-width: 210px;
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
