<script setup lang="ts">
import { markRaw, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import { GameBridge } from '@/game/GameBridge'
import { fetchCity, tryRefresh } from '@/api/client'
import type { PerfSample, RendererBackend, ResourceBalanceDto, SelectionInfo } from '@/game/types'
import DebugOverlay from '@/ui/DebugOverlay.vue'
import SelectionCard from '@/ui/SelectionCard.vue'
import AuthScreen from '@/ui/AuthScreen.vue'

const canvas = ref<HTMLCanvasElement | null>(null)

/**
 * CRITICAL (RISKS.md R-10): the bridge is held in `shallowRef` and wrapped in `markRaw`.
 * A plain `ref(bridge)` would make Vue deep-proxy every mesh, material, and observer in the
 * scene graph — the single most likely cause of catastrophic frame drops in this project.
 */
const bridge = shallowRef<GameBridge | null>(null)

const status = ref<'loading' | 'auth' | 'ready' | 'error'>('loading')
const errorMessage = ref('')
const cityName = ref('')
const balanceCoins = ref(0)
const resources = ref<ResourceBalanceDto[]>([])
const backend = ref<RendererBackend>('webgl2')
const selection = ref<SelectionInfo | null>(null)
const perf = ref<PerfSample>({ fps: 0, drawCalls: 0, triangles: 0, meshes: 0 })
const p95 = ref(0)
const timeOfDay = ref(0.36)
const showDebug = ref(__TRADEBORN_DEBUG__)

let unsubscribeSelection: (() => void) | null = null
let perfTimer: number | null = null
let dayTimer: number | null = null
const abort = new AbortController()

onMounted(async () => {
  // The SPA holds no access token after a page load, so it asks the server whether the
  // HttpOnly refresh cookie still represents a session. This is what makes a refresh
  // restore the game rather than bounce the player to a login screen (ADR-007).
  const restored = await tryRefresh()
  if (!restored) {
    status.value = 'auth'
    return
  }

  await startGame()
})

async function startGame() {
  status.value = 'loading'

  try {
    const city = await fetchCity(abort.signal)
    cityName.value = city.name
    balanceCoins.value = city.balanceCoins
    resources.value = city.resources

    if (!canvas.value) throw new Error('Canvas element was not mounted')

    const instance = markRaw(new GameBridge())
    await instance.start(canvas.value, city)
    bridge.value = instance

    backend.value = instance.rendererBackend
    unsubscribeSelection = instance.onSelectionChanged((info) => {
      selection.value = info
    })

    // Poll plain-data samples rather than making the engine reactive.
    perfTimer = window.setInterval(() => {
      perf.value = instance.performance
      p95.value = instance.p95Fps
    }, 500)

    // One in-game day every four real minutes, purely so the lighting rig can be evaluated
    // without scrubbing by hand. Ticked at 4 Hz rather than 10 Hz: the transition is a lerp,
    // so a faster tick buys no smoothness, and a quicker cycle reads as the light flickering
    // rather than as time passing.
    const dayTickMs = 250
    const dayLengthMs = 4 * 60 * 1000
    dayTimer = window.setInterval(() => {
      timeOfDay.value = (timeOfDay.value + dayTickMs / dayLengthMs) % 1
      instance.applyTimeOfDay(timeOfDay.value)
    }, dayTickMs)

    status.value = 'ready'
  } catch (error) {
    if ((error as Error).name === 'AbortError') return
    console.error('[Tradeborn] Failed to start', error)
    errorMessage.value = error instanceof Error ? error.message : 'Unknown error'
    status.value = 'error'
  }
}

onBeforeUnmount(() => {
  abort.abort()
  if (perfTimer !== null) window.clearInterval(perfTimer)
  if (dayTimer !== null) window.clearInterval(dayTimer)
  unsubscribeSelection?.()
  bridge.value?.dispose()
  bridge.value = null
})

function onTimeOfDayChanged(value: number) {
  timeOfDay.value = value
  bridge.value?.applyTimeOfDay(value)
}

function reload() {
  window.location.reload()
}
</script>

<template>
  <canvas id="renderCanvas" ref="canvas" touch-action="none"></canvas>

  <!-- Not signed in -->
  <AuthScreen v-if="status === 'auth'" @authenticated="startGame" />

  <!-- Loading state -->
  <div v-else-if="status === 'loading'" class="overlay">
    <div class="loader tb-panel">
      <div class="mark">TRADEBORN</div>
      <div class="bar"><span></span></div>
      <div class="hint">Preparing your city…</div>
    </div>
  </div>

  <!-- Error state -->
  <div v-else-if="status === 'error'" class="overlay">
    <div class="loader tb-panel">
      <div class="mark error">Could not start</div>
      <div class="hint">{{ errorMessage }}</div>
      <button class="retry" @click="reload">Try again</button>
    </div>
  </div>

  <!-- HUD -->
  <div v-else class="tb-hud">
    <DebugOverlay
      v-if="showDebug"
      :backend="backend"
      :perf="perf"
      :p95="p95"
      :time-of-day="timeOfDay"
      @update:time-of-day="onTimeOfDayChanged"
    />

    <div class="city-name tb-panel">
      <span class="dot" aria-hidden="true"></span>
      {{ cityName }}
      <span class="coins">{{ balanceCoins.toLocaleString() }}<i>c</i></span>
    </div>

    <div v-if="resources.length" class="resources tb-panel">
      <span v-for="r in resources" :key="r.resource" class="res" :class="{ full: r.quantity >= r.capacity }">
        <b>{{ r.quantity.toLocaleString() }}</b>
        <span class="label">{{ r.resource }}</span>
      </span>
    </div>

    <SelectionCard :selection="selection" />

    <div class="hint-bar tb-panel">
      Drag to orbit · Scroll to zoom · Click a building or plot
    </div>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  display: grid;
  place-items: center;
  background: #16202e;
}

.loader {
  padding: 28px 34px;
  text-align: center;
  min-width: 280px;
}

.mark {
  font-size: 20px;
  font-weight: 700;
  letter-spacing: 0.18em;
  color: var(--tb-gold);
}

.mark.error {
  color: var(--tb-danger);
  letter-spacing: 0;
}

.bar {
  height: 3px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.1);
  margin: 18px 0 12px;
  overflow: hidden;
}

.bar span {
  display: block;
  height: 100%;
  width: 40%;
  border-radius: 999px;
  background: var(--tb-gold);
  animation: slide 1.1s ease-in-out infinite;
}

@keyframes slide {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(250%);
  }
}

.hint {
  color: var(--tb-text-dim);
  font-size: 13px;
}

.retry {
  margin-top: 16px;
  background: var(--tb-gold);
  color: #16202e;
  border: 0;
  border-radius: 10px;
  padding: 9px 18px;
  font-weight: 650;
  font-size: 13px;
  cursor: pointer;
}

.city-name {
  grid-column: 2;
  grid-row: 1;
  justify-self: center;
  align-self: start;
  padding: 8px 18px;
  font-size: 14px;
  font-weight: 600;
  letter-spacing: 0.02em;
  display: flex;
  align-items: center;
  gap: 9px;
}

.city-name .dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--tb-success);
}

.city-name .coins {
  margin-left: 6px;
  padding-left: 12px;
  border-left: 1px solid var(--tb-border);
  color: var(--tb-gold);
  font-weight: 700;
}

.city-name .coins i {
  font-style: normal;
  opacity: 0.6;
  margin-left: 2px;
  font-size: 11px;
}

.resources {
  grid-column: 3;
  grid-row: 1;
  align-self: start;
  justify-self: end;
  padding: 9px 14px;
  display: flex;
  gap: 16px;
  font-size: 12px;
}

.res {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1px;
}

.res .label {
  color: var(--tb-text-dim);
  font-size: 10px;
  text-transform: capitalize;
}

/* Storage full is a design signal, not a failure — warn, never alarm. */
.res.full b {
  color: var(--tb-warning);
}

.hint-bar {
  grid-column: 2;
  grid-row: 3;
  justify-self: center;
  align-self: end;
  padding: 8px 16px;
  font-size: 12px;
  color: var(--tb-text-dim);
}

@media (max-width: 720px) {
  .hint-bar {
    font-size: 11px;
  }
}
</style>
