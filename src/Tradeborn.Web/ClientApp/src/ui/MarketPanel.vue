<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { MarketBoardDto, MarketQuoteDto } from '@/api/client'

/**
 * The market.
 *
 * The one place in the game with dense numbers, and it is a slide-over the player opens
 * deliberately rather than something on the main screen (ART_DIRECTION.md §7). Quotes arrive
 * sorted by value, so bread sits at the top where a browsing player cannot miss that it is
 * worth thirty times a log.
 */
const props = defineProps<{
  board: MarketBoardDto | null
  open: boolean
  busy: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'sell', payload: { resource: string; quantity: number }): void
}>()

const selected = ref<string | null>(null)
const quantity = ref(0)

const quote = computed<MarketQuoteDto | null>(
  () => props.board?.quotes.find((q) => q.resource === selected.value) ?? null,
)

const maxSellable = computed(() => {
  if (!quote.value || !props.board) return 0
  return Math.min(quote.value.held, props.board.orderLimit)
})

/** Projected proceeds. Advisory — the server recomputes and can refuse (SECURITY_MODEL.md T2). */
const projection = computed(() => {
  if (!quote.value || quantity.value <= 0 || !props.board) return null

  const gross = quote.value.sellPriceCent * quantity.value
  const fee = Math.floor((gross * props.board.feePercent) / 100)
  return { gross, fee, net: gross - fee }
})

// Selecting a different good must not carry the previous quantity across — selling 200 bread
// because that was the wood slider position would be an expensive surprise.
watch(selected, () => {
  quantity.value = Math.min(maxSellable.value, maxSellable.value)
})

watch(
  () => props.open,
  (isOpen) => {
    if (isOpen && !selected.value && props.board?.quotes.length) {
      selected.value = props.board.quotes.find((q) => q.held > 0)?.resource ?? null
      quantity.value = maxSellable.value
    }
  },
)

function coins(cent: number): string {
  return (cent / 100).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** How far the price sits between floor and ceiling, for the trend bar. */
function pricePosition(q: MarketQuoteDto): number {
  const span = q.ceilingCent - q.floorCent
  if (span <= 0) return 50
  return Math.round(((q.sellPriceCent - q.floorCent) / span) * 100)
}

function trendClass(q: MarketQuoteDto): string {
  if (q.sellPriceCent > q.basePriceCent) return 'up'
  if (q.sellPriceCent < q.basePriceCent) return 'down'
  return 'flat'
}

/** A sparkline path from recent price points, normalised to its own range. */
function sparkline(points: { priceCent: number }[]): string {
  if (points.length < 2) return ''

  const values = points.map((p) => p.priceCent)
  const min = Math.min(...values)
  const max = Math.max(...values)
  const span = max - min || 1

  return values
    .map((v, i) => {
      const x = (i / (values.length - 1)) * 100
      const y = 20 - ((v - min) / span) * 18
      return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
}

function confirmSell() {
  if (!selected.value || quantity.value <= 0) return
  emit('sell', { resource: selected.value, quantity: quantity.value })
}
</script>

<template>
  <Transition name="slide">
    <aside v-if="open" class="market tb-panel" aria-label="Market">
      <header class="head">
        <div>
          <div class="title">Market</div>
          <div class="sub">{{ board?.feePercent ?? 3 }}% fee · max {{ board?.orderLimit ?? 0 }} per order</div>
        </div>
        <button class="close" aria-label="Close market" @click="emit('close')">✕</button>
      </header>

      <ul class="quotes">
        <li
          v-for="q in board?.quotes ?? []"
          :key="q.resource"
          class="quote"
          :class="{ active: q.resource === selected, empty: q.held === 0 }"
        >
          <button class="pick" @click="selected = q.resource; quantity = Math.min(q.held, board?.orderLimit ?? 0)">
            <span class="name">{{ q.resource }}</span>
            <span class="held">{{ q.held.toLocaleString() }} held</span>

            <svg class="spark" viewBox="0 0 100 20" preserveAspectRatio="none" aria-hidden="true">
              <path :d="sparkline(q.history)" fill="none" stroke="currentColor" stroke-width="1.5" />
            </svg>

            <span class="price" :class="trendClass(q)">{{ coins(q.sellPriceCent) }}c</span>
          </button>

          <!-- Where the price sits between floor and ceiling: shape, not just colour. -->
          <div class="range" :title="`${coins(q.floorCent)} – ${coins(q.ceilingCent)}`">
            <span class="fill" :style="{ width: pricePosition(q) + '%' }"></span>
          </div>
        </li>
      </ul>

      <div v-if="quote" class="order">
        <label class="row">
          <span>Sell</span>
          <input
            v-model.number="quantity"
            type="range"
            min="0"
            :max="maxSellable"
            :disabled="maxSellable === 0 || busy"
          />
          <b>{{ quantity.toLocaleString() }}</b>
        </label>

        <div v-if="projection" class="breakdown">
          <span>Gross <b>{{ coins(projection.gross) }}c</b></span>
          <span class="fee">Fee −{{ coins(projection.fee) }}c</span>
          <span class="net">You get <b>{{ coins(projection.net) }}c</b></span>
        </div>

        <button
          class="confirm"
          :disabled="quantity <= 0 || busy || maxSellable === 0"
          @click="confirmSell"
        >
          {{ maxSellable === 0 ? `No ${quote.resource} in storage` : `Sell ${quantity} ${quote.resource}` }}
        </button>
      </div>
    </aside>
  </Transition>
</template>

<style scoped>
.market {
  grid-column: 3;
  grid-row: 1 / 4;
  align-self: stretch;
  justify-self: end;
  width: min(330px, 92vw);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  overflow-y: auto;
}

.head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.title {
  font-size: 17px;
  font-weight: 650;
}

.sub {
  color: var(--tb-text-dim);
  font-size: 11px;
  margin-top: 2px;
}

.close {
  background: transparent;
  border: 0;
  color: var(--tb-text-dim);
  font-size: 15px;
  cursor: pointer;
  min-width: 32px;
  min-height: 32px;
}

.quotes {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 6px;
}

.quote {
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.05);
  padding: 4px 4px 7px;
}

.quote.active {
  background: rgba(240, 180, 41, 0.14);
  outline: 1px solid var(--tb-border);
}

.quote.empty .name,
.quote.empty .held {
  opacity: 0.5;
}

.pick {
  width: 100%;
  display: grid;
  grid-template-columns: 1fr auto;
  grid-template-areas: 'name price' 'held spark';
  gap: 2px 8px;
  align-items: center;
  background: transparent;
  border: 0;
  color: var(--tb-text);
  padding: 7px 8px 4px;
  cursor: pointer;
  text-align: left;
}

.name {
  grid-area: name;
  font-weight: 600;
  font-size: 14px;
  text-transform: capitalize;
}

.held {
  grid-area: held;
  font-size: 11px;
  color: var(--tb-text-dim);
}

.spark {
  grid-area: spark;
  width: 60px;
  height: 16px;
  color: var(--tb-text-dim);
}

.price {
  grid-area: price;
  font-weight: 700;
  font-size: 14px;
}

.price.up {
  color: var(--tb-success);
}
.price.down {
  color: var(--tb-warning);
}

.range {
  height: 3px;
  margin: 0 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.1);
  overflow: hidden;
}

.range .fill {
  display: block;
  height: 100%;
  background: var(--tb-gold);
}

.order {
  margin-top: auto;
  padding-top: 12px;
  border-top: 1px solid var(--tb-border);
  display: grid;
  gap: 10px;
}

.row {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 10px;
  align-items: center;
  font-size: 13px;
}

.row input {
  width: 100%;
  accent-color: var(--tb-gold);
}

.breakdown {
  display: grid;
  gap: 3px;
  font-size: 12px;
  color: var(--tb-text-dim);
}

.breakdown .fee {
  color: var(--tb-warning);
}

.breakdown .net b {
  color: var(--tb-gold);
}

.confirm {
  background: var(--tb-gold);
  color: #16202e;
  border: 0;
  border-radius: 12px;
  padding: 12px;
  font-weight: 700;
  font-size: 14px;
  cursor: pointer;
  min-height: 46px;
}

.confirm:disabled {
  opacity: 0.5;
  cursor: default;
}

.slide-enter-active,
.slide-leave-active {
  transition:
    opacity 0.18s ease,
    transform 0.18s ease;
}
.slide-enter-from,
.slide-leave-to {
  opacity: 0;
  transform: translateX(20px);
}
</style>
