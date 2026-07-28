<script setup lang="ts">
import { computed } from 'vue'
import type { QuestBoardDto } from '@/api/client'

/**
 * The contextual tutorial.
 *
 * One step at a time, never a list. PLAYER_JOURNEY.md is explicit about this: the tutorial
 * teaches by doing, and showing all seven steps at once turns it into a checklist the player
 * reads instead of a game they play.
 *
 * When a step is finished the card becomes the reward — that moment is the payoff, so it takes
 * over the card rather than being a badge on the next instruction.
 */
const props = defineProps<{
  board: QuestBoardDto | null
  busy: boolean
}>()

const emit = defineEmits<{ (e: 'claim', questId: string): void }>()

const quest = computed(() => props.board?.current ?? null)
const claimable = computed(() => quest.value?.isClaimable ?? false)

const progressLabel = computed(() =>
  props.board ? `${props.board.claimed} / ${props.board.total}` : '',
)
</script>

<template>
  <Transition name="quest">
    <div v-if="quest" class="quest tb-panel" :class="{ done: claimable }">
      <div class="head">
        <span class="eyebrow">{{ claimable ? 'Reward ready' : 'Next step' }}</span>
        <span class="count">{{ progressLabel }}</span>
      </div>

      <div class="title">{{ quest.title }}</div>
      <div v-if="!claimable" class="hint">{{ quest.hint }}</div>

      <div class="reward">
        <span class="coins">{{ quest.rewardCoins }}c</span>
        <span class="xp">{{ quest.rewardXp }} XP</span>
      </div>

      <button
        v-if="claimable"
        class="claim"
        :disabled="busy"
        @click="emit('claim', quest.id)"
      >
        Collect
      </button>
    </div>
  </Transition>
</template>

<style scoped>
.quest {
  grid-column: 1;
  grid-row: 2;
  align-self: start;
  justify-self: start;
  padding: 14px 16px;
  min-width: 214px;
  max-width: 248px;
}

/* A finished step earns a gold edge. The colour is a reward signal, never decorative. */
.quest.done {
  border-color: rgba(240, 180, 41, 0.55);
}

.head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
}

.eyebrow {
  font-size: 10px;
  letter-spacing: 0.13em;
  text-transform: uppercase;
  font-weight: 700;
  color: var(--tb-text-dim);
}

.quest.done .eyebrow {
  color: var(--tb-gold);
}

.count {
  font-size: 11px;
  color: var(--tb-text-dim);
}

.title {
  margin-top: 8px;
  font-size: 15px;
  font-weight: 650;
  line-height: 1.3;
}

.hint {
  margin-top: 5px;
  font-size: 12px;
  color: var(--tb-text-dim);
  line-height: 1.4;
}

.reward {
  display: flex;
  gap: 8px;
  margin-top: 10px;
  font-size: 11px;
}

.coins,
.xp {
  padding: 3px 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.07);
}

.coins {
  color: var(--tb-gold);
}

.xp {
  color: var(--tb-success);
}

.claim {
  margin-top: 12px;
  width: 100%;
  background: var(--tb-gold);
  color: #16202e;
  border: 0;
  border-radius: 10px;
  padding: 10px 14px;
  font-weight: 700;
  font-size: 13px;
  cursor: pointer;
  /* Comfortably above the 44px touch minimum (ART_DIRECTION.md §7). */
  min-height: 44px;
}

.claim:disabled {
  opacity: 0.55;
  cursor: default;
}

.quest-enter-active,
.quest-leave-active {
  transition:
    opacity 0.2s ease,
    transform 0.2s ease;
}
.quest-enter-from,
.quest-leave-to {
  opacity: 0;
  transform: translateX(-12px);
}
</style>
