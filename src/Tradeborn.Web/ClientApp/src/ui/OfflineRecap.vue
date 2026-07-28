<script setup lang="ts">
import { computed } from 'vue'
import type { OfflineSummaryDto } from '@/game/types'

/**
 * "While you were away".
 *
 * Framing is the whole point (PLAYER_JOURNEY.md, GAME_VISION.md P3). This reports what was
 * *produced* and what *stopped and why* — never what was "lost". Same facts, opposite
 * feeling: a full warehouse is a goal, not a punishment.
 */
const props = defineProps<{ summary: OfflineSummaryDto | null }>()
const emit = defineEmits<{ (e: 'dismiss'): void }>()

const away = computed(() => {
  if (!props.summary) return ''
  const ms = Date.now() - Date.parse(props.summary.since)
  const minutes = Math.round(ms / 60_000)
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'}`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'}`
  const days = Math.round(hours / 24)
  return `${days} day${days === 1 ? '' : 's'}`
})

const produced = computed(() => props.summary?.produced.filter((p) => p.quantity > 0) ?? [])
</script>

<template>
  <Transition name="recap">
    <div v-if="summary" class="backdrop">
      <div class="recap tb-panel">
        <div class="eyebrow">While you were away</div>
        <div class="headline">Your city kept working for {{ away }}.</div>

        <ul v-if="produced.length" class="produced">
          <li v-for="item in produced" :key="item.resource">
            <b>+{{ item.quantity.toLocaleString() }}</b>
            <span>{{ item.resource }}</span>
          </li>
        </ul>

        <!--
          The hook. A building that stopped is the reason to come back in, so it is stated
          as something to act on rather than buried.
        -->
        <div v-if="summary.haltedBuildings.length" class="halted">
          {{ summary.haltedBuildings.length }}
          {{ summary.haltedBuildings.length === 1 ? 'building needs' : 'buildings need' }}
          your attention.
        </div>

        <button class="dismiss" @click="emit('dismiss')">Back to the city</button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.backdrop {
  position: fixed;
  inset: 0;
  display: grid;
  place-items: center;
  background: rgba(10, 15, 23, 0.55);
  backdrop-filter: blur(3px);
  z-index: 20;
}

.recap {
  padding: 28px 32px;
  max-width: 380px;
  text-align: center;
}

.eyebrow {
  font-size: 11px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--tb-gold);
  font-weight: 700;
}

.headline {
  margin-top: 10px;
  font-size: 18px;
  font-weight: 650;
  line-height: 1.35;
}

.produced {
  list-style: none;
  margin: 18px 0 0;
  padding: 0;
  display: grid;
  gap: 6px;
}

.produced li {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  padding: 8px 12px;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.06);
  font-size: 14px;
}

.produced b {
  color: var(--tb-success);
}

.produced span {
  color: var(--tb-text-dim);
  font-size: 12px;
}

.halted {
  margin-top: 14px;
  font-size: 13px;
  color: var(--tb-warning);
}

.dismiss {
  margin-top: 22px;
  width: 100%;
  background: var(--tb-gold);
  color: #16202e;
  border: 0;
  border-radius: 12px;
  padding: 12px 18px;
  font-weight: 700;
  font-size: 14px;
  cursor: pointer;
  min-height: 46px;
}

.recap-enter-active,
.recap-leave-active {
  transition: opacity 0.22s ease;
}
.recap-enter-from,
.recap-leave-to {
  opacity: 0;
}
</style>
