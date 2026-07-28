<script setup lang="ts">
import type { Toast } from './uiTypes'

/**
 * Transient feedback for command outcomes.
 *
 * A refusal is shown here rather than in a modal: the player should be told why and be able
 * to carry straight on. A modal for "you cannot afford that" would stop the game to deliver
 * information the world could have conveyed anyway.
 */
defineProps<{ toasts: Toast[] }>()
</script>

<template>
  <div class="stack" role="status" aria-live="polite">
    <TransitionGroup name="toast">
      <div v-for="toast in toasts" :key="toast.id" class="toast tb-panel" :class="toast.tone">
        <span class="mark" aria-hidden="true">{{ toast.tone === 'ok' ? '✓' : '!' }}</span>
        {{ toast.text }}
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.stack {
  grid-column: 2;
  grid-row: 2;
  justify-self: center;
  align-self: end;
  display: flex;
  flex-direction: column;
  gap: 8px;
  pointer-events: none;
  margin-bottom: 12px;
}

.toast {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  font-size: 13px;
  max-width: 340px;
}

.mark {
  display: grid;
  place-items: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  font-size: 11px;
  font-weight: 700;
  color: #16202e;
  flex: none;
}

.toast.ok .mark {
  background: var(--tb-success);
}

.toast.warn .mark {
  background: var(--tb-warning);
}

.toast-enter-active,
.toast-leave-active {
  transition:
    opacity 0.2s ease,
    transform 0.2s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(10px);
}
</style>
