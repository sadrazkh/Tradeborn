<script setup lang="ts">
import { markRaw, onBeforeUnmount, onMounted, ref, shallowRef } from 'vue'
import { GameBridge } from '@/game/GameBridge'
import {
  claimQuest,
  fetchCity,
  fetchMarket,
  fetchQuests,
  newIdempotencyKey,
  sell,
  setProduction,
  startConstruction,
  startUpgrade,
  tryRefresh,
  type MarketBoardDto,
  type QuestBoardDto,
} from '@/api/client'
import type {
  OfflineSummaryDto,
  PerfSample,
  RendererBackend,
  ResourceBalanceDto,
  SelectionInfo,
} from '@/game/types'
import DebugOverlay from '@/ui/DebugOverlay.vue'
import SelectionCard from '@/ui/SelectionCard.vue'
import BuildBar from '@/ui/BuildBar.vue'
import ToastStack from '@/ui/ToastStack.vue'
import OfflineRecap from '@/ui/OfflineRecap.vue'
import MarketPanel from '@/ui/MarketPanel.vue'
import QuestTracker from '@/ui/QuestTracker.vue'
import type { BuildOption, Toast } from '@/ui/uiTypes'
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
const cityLevel = ref(1)
const placing = ref(false)
const commandBusy = ref(false)
const toasts = ref<Toast[]>([])
const offlineSummary = ref<OfflineSummaryDto | null>(null)
const market = ref<MarketBoardDto | null>(null)
const marketOpen = ref(false)
const playerLevel = ref(1)
const playerXp = ref(0)
const xpToNextLevel = ref(100)
const quests = ref<QuestBoardDto | null>(null)
let toastId = 0

/**
 * Costs shown in the build menu mirror the seed data.
 *
 * The server remains the authority — it recomputes every cost and can refuse — so these are
 * an affordance that stops the player guessing, not a source of truth (SECURITY_MODEL.md §3).
 */
const buildOptions: BuildOption[] = [
  { definitionId: 'lumber_camp', label: 'Lumber Camp', costCoins: 150, costWood: 20, unlockCityLevel: 1 },
  { definitionId: 'farm', label: 'Farm', costCoins: 150, costWood: 20, unlockCityLevel: 1 },
  { definitionId: 'warehouse', label: 'Warehouse', costCoins: 250, costWood: 40, unlockCityLevel: 1 },
  { definitionId: 'sawmill', label: 'Sawmill', costCoins: 400, costWood: 60, unlockCityLevel: 2 },
  { definitionId: 'mill', label: 'Mill', costCoins: 400, costWood: 60, unlockCityLevel: 2 },
  { definitionId: 'bakery', label: 'Bakery', costCoins: 900, costWood: 40, unlockCityLevel: 3 },
]

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
    offlineSummary.value = city.offlineSummary ?? null
    void refreshQuests()
    playerLevel.value = city.progress.level
    playerXp.value = city.progress.xp
    xpToNextLevel.value = city.progress.xpToNextLevel
    cityLevel.value = city.progress.cityLevel

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

/**
 * Opens the market, refreshing prices first.
 *
 * Prices are global and move whenever anyone sells, so a board cached from five minutes ago
 * would quote numbers the server will not honour.
 */
async function openMarket() {
  marketOpen.value = true
  await refreshMarket()
}

async function refreshMarket() {
  try {
    market.value = await fetchMarket(abort.signal)
  } catch (error) {
    if ((error as Error).name === 'AbortError') return
    console.error('[Tradeborn] Could not load the market', error)
    toast('Could not load market prices.', 'warn')
  }
}

/**
 * Sells goods and reconciles against the server's answer.
 *
 * Everything shown afterwards — proceeds, balance, XP, the new price — comes from the
 * response. The client never computes what the player earned.
 */
async function onSell(payload: { resource: string; quantity: number }) {
  if (commandBusy.value) return
  commandBusy.value = true

  try {
    const result = await sell(payload.resource, payload.quantity, newIdempotencyKey())

    if (!result.accepted) {
      toast(result.refusalMessage ?? 'That sale was refused.', 'warn')
      return
    }

    balanceCoins.value = result.balanceCoins
    resources.value = result.resources
    playerLevel.value = result.playerLevel
    playerXp.value = result.playerXp
    xpToNextLevel.value = result.xpToNextLevel

    const net = (result.netCent / 100).toFixed(2)
    toast(`Sold ${result.quantitySold} ${result.resource} for ${net}c.`)

    if (result.levelsGained > 0) {
      toast(`Level ${result.playerLevel}!`)
    }

    // The sale moved the price, so the board the player is looking at is now stale.
    await refreshMarket()
  } catch (error) {
    console.error('[Tradeborn] Sale failed', error)
    toast('Could not reach the server. Nothing was sold.', 'warn')
  } finally {
    commandBusy.value = false
  }
}

/**
 * Re-reads the tutorial board.
 *
 * Called after anything that could finish a step. Completion is derived from city state on the
 * server, so the client never decides a quest is done — it asks.
 */
async function refreshQuests() {
  try {
    quests.value = await fetchQuests(abort.signal)
  } catch (error) {
    if ((error as Error).name === 'AbortError') return
    // A failed quest refresh must not break play; the next action retries it.
    console.warn('[Tradeborn] Could not refresh quests', error)
  }
}

/**
 * Collects a finished quest's reward.
 *
 * The level-up is celebrated separately from the reward: crossing a level is a bigger moment
 * than the coins that caused it, and folding both into one line would bury it.
 */
async function onClaimQuest(questId: string) {
  if (commandBusy.value) return
  commandBusy.value = true

  try {
    const result = await claimQuest(questId, newIdempotencyKey())

    if (!result.accepted) {
      toast(result.refusalMessage ?? 'That reward could not be collected.', 'warn')
      void refreshQuests()
      return
    }

    balanceCoins.value = result.balanceCoins
    playerLevel.value = result.playerLevel
    playerXp.value = result.playerXp
    xpToNextLevel.value = result.xpToNextLevel
    quests.value = result.board

    toast(`+${result.rewardCoins}c  ·  +${result.rewardXp} XP`)

    if (result.levelsGained > 0) {
      toast(`Level ${result.playerLevel}!`)
    }
  } catch (error) {
    console.error('[Tradeborn] Claim failed', error)
    toast('Could not reach the server.', 'warn')
  } finally {
    commandBusy.value = false
  }
}

function woodHeld(): number {
  return resources.value.find((r) => r.resource === 'wood')?.quantity ?? 0
}

function toast(text: string, tone: 'ok' | 'warn' = 'ok') {
  const id = ++toastId
  toasts.value = [...toasts.value, { id, text, tone }]
  window.setTimeout(() => {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }, 4000)
}

/** Enters placement mode. The build only happens once the player confirms a plot. */
function beginPlacement(definitionId: string) {
  placing.value = true
  bridge.value?.beginPlacement(definitionId, (candidate) => {
    placing.value = false
    void confirmBuild(definitionId, candidate.col, candidate.row)
  })
}

function cancelPlacement() {
  placing.value = false
  bridge.value?.cancelPlacement()
}

/**
 * Switches a building's production on or off.
 *
 * The building's own state comes back from the server and is applied to the scene, so the
 * panel and the 3D city can never disagree about whether something is running.
 */
async function onSetProduction(payload: { buildingId: string; active: boolean }) {
  if (commandBusy.value) return
  commandBusy.value = true

  try {
    const result = await setProduction(payload.buildingId, payload.active, newIdempotencyKey())

    if (!result.accepted) {
      toast(result.refusalMessage ?? 'That could not be changed.', 'warn')
      return
    }

    if (result.building) {
      bridge.value?.updateBuilding(result.building)
      toast(payload.active ? 'Production started.' : 'Production paused.')
      void refreshQuests()
    }
  } catch (error) {
    console.error('[Tradeborn] Production toggle failed', error)
    toast('Could not reach the server.', 'warn')
  } finally {
    commandBusy.value = false
  }
}

async function onUpgrade(buildingId: string) {
  if (commandBusy.value) return
  commandBusy.value = true

  try {
    const result = await startUpgrade(buildingId, newIdempotencyKey())

    if (!result.accepted) {
      toast(result.refusalMessage ?? 'That upgrade was refused.', 'warn')
      return
    }

    balanceCoins.value = result.balanceCoins
    resources.value = result.resources
    if (result.building) {
      bridge.value?.updateBuilding(result.building)
      toast('Upgrade started.')
      void refreshQuests()
    }
  } catch (error) {
    console.error('[Tradeborn] Upgrade failed', error)
    toast('Could not reach the server. Nothing was charged.', 'warn')
  } finally {
    commandBusy.value = false
  }
}

/**
 * Sends the build and reconciles against the server's answer.
 *
 * The idempotency key is generated once per attempt, so a retry of this same build can never
 * charge twice. A refusal is not an error — it is the world explaining itself, so it is shown
 * as a message rather than thrown.
 */
async function confirmBuild(definitionId: string, col: number, row: number) {
  if (commandBusy.value) return
  commandBusy.value = true

  try {
    const result = await startConstruction(definitionId, col, row, newIdempotencyKey())

    if (!result.accepted) {
      toast(result.refusalMessage ?? 'That build was refused.', 'warn')
      return
    }

    // Reconcile from the server's numbers rather than guessing locally.
    balanceCoins.value = result.balanceCoins
    resources.value = result.resources
    if (result.building) {
      bridge.value?.addBuilding(result.building)
      toast('Construction started.')
      void refreshQuests()
    }
  } catch (error) {
    console.error('[Tradeborn] Build failed', error)
    toast('Could not reach the server. Nothing was charged.', 'warn')
  } finally {
    commandBusy.value = false
  }
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
      <span class="level" :title="`${xpToNextLevel} XP to level ${playerLevel + 1}`">
        Lv {{ playerLevel }}
      </span>
    </div>

    <div v-if="resources.length" class="resources tb-panel">
      <span v-for="r in resources" :key="r.resource" class="res" :class="{ full: r.quantity >= r.capacity }">
        <b>{{ r.quantity.toLocaleString() }}</b>
        <span class="label">{{ r.resource }}</span>
      </span>
    </div>

    <SelectionCard
      :selection="selection"
      :busy="commandBusy"
      @set-production="onSetProduction"
      @upgrade="onUpgrade"
    />

    <BuildBar
      :options="buildOptions"
      :balance-coins="balanceCoins"
      :wood="woodHeld()"
      :city-level="cityLevel"
      :placing="placing"
      :busy="commandBusy"
      @pick="beginPlacement"
      @cancel="cancelPlacement"
    />

    <button v-if="!marketOpen" class="market-button tb-panel" @click="openMarket">Market</button>

    <MarketPanel
      :board="market"
      :open="marketOpen"
      :busy="commandBusy"
      @close="marketOpen = false"
      @sell="onSell"
    />

    <QuestTracker :board="quests" :busy="commandBusy" @claim="onClaimQuest" />

    <ToastStack :toasts="toasts" />

    <OfflineRecap :summary="offlineSummary" @dismiss="offlineSummary = null" />

    <div class="hint-bar tb-panel">
      {{ placing ? 'Tap a highlighted plot to build · Esc to cancel' : 'Drag to orbit · Scroll to zoom · Click a building or plot' }}
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

.city-name .level {
  padding: 2px 8px;
  border-radius: 999px;
  background: rgba(240, 180, 41, 0.18);
  color: var(--tb-gold);
  font-size: 11px;
  font-weight: 700;
}

.market-button {
  grid-column: 3;
  grid-row: 1;
  align-self: start;
  justify-self: end;
  padding: 9px 18px;
  background: var(--tb-panel);
  color: var(--tb-text);
  border: 1px solid var(--tb-border);
  border-radius: 999px;
  font-size: 13px;
  font-weight: 650;
  cursor: pointer;
  min-height: 40px;
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
