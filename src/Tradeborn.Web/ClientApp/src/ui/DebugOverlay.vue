<script setup lang="ts">
import { computed } from 'vue'
import type { PerfSample, RendererBackend } from '@/game/types'

const props = defineProps<{
  backend: RendererBackend
  perf: PerfSample
  p95: number
  timeOfDay: number
}>()

const emit = defineEmits<{ (e: 'update:timeOfDay', value: number): void }>()

// Budgets from docs/architecture/PERFORMANCE_BUDGET.md §2 and §3 (desktop targets).
const BUDGET = { fps: 50, drawCalls: 150, triangles: 150_000 }

const fpsClass = computed(() => (props.perf.fps >= BUDGET.fps ? 'ok' : 'warn'))
const drawClass = computed(() => (props.perf.drawCalls <= BUDGET.drawCalls ? 'ok' : 'warn'))
const triClass = computed(() => (props.perf.triangles <= BUDGET.triangles ? 'ok' : 'warn'))

const clock = computed(() => {
  const totalMinutes = Math.round(props.timeOfDay * 24 * 60)
  const h = Math.floor(totalMinutes / 60) % 24
  const m = totalMinutes % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`
})
</script>

<template>
  <div class="debug tb-panel">
    <div class="row header">
      <span class="badge">{{ backend.toUpperCase() }}</span>
      <span class="dim">Phase 0 prototype</span>
    </div>

    <div class="row">
      <span>FPS</span>
      <b :class="fpsClass">{{ perf.fps }}</b>
      <span class="dim">p95 {{ p95 }}</span>
    </div>
    <div class="row">
      <span>Draw calls</span>
      <b :class="drawClass">{{ perf.drawCalls }}</b>
      <span class="dim">/ {{ BUDGET.drawCalls }}</span>
    </div>
    <div class="row">
      <span>Triangles</span>
      <b :class="triClass">{{ perf.triangles.toLocaleString() }}</b>
      <span class="dim">/ {{ BUDGET.triangles.toLocaleString() }}</span>
    </div>
    <div class="row">
      <span>Meshes</span>
      <b>{{ perf.meshes }}</b>
    </div>

    <label class="row slider">
      <span>Time</span>
      <input
        type="range"
        min="0"
        max="1"
        step="0.005"
        :value="timeOfDay"
        aria-label="Time of day"
        @input="emit('update:timeOfDay', Number(($event.target as HTMLInputElement).value))"
      />
      <b>{{ clock }}</b>
    </label>
  </div>
</template>

<style scoped>
.debug {
  grid-column: 1;
  grid-row: 1;
  align-self: start;
  padding: 12px 14px;
  font-size: 12px;
  min-width: 236px;
  line-height: 1.7;
}

.row {
  display: grid;
  grid-template-columns: 74px auto 1fr;
  gap: 8px;
  align-items: center;
}

.header {
  grid-template-columns: auto 1fr;
  margin-bottom: 6px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--tb-border);
}

.badge {
  background: var(--tb-gold);
  color: #16202e;
  font-weight: 700;
  font-size: 10px;
  letter-spacing: 0.06em;
  padding: 2px 7px;
  border-radius: 999px;
}

.dim {
  color: var(--tb-text-dim);
  font-size: 11px;
}

b.ok {
  color: var(--tb-success);
}
b.warn {
  color: var(--tb-warning);
}

.slider {
  margin-top: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--tb-border);
  grid-template-columns: 74px 1fr auto;
}

.slider input {
  width: 100%;
  accent-color: var(--tb-gold);
}
</style>
