# Player Journey

> The first 60 minutes, beat by beat. Every beat names what the player **learns**, what they
> **feel**, and what would make it fail.

## Design rule for the whole tutorial

> **Teach by doing, never by telling.**

No wall of text. No modal explaining mechanics. Each step is a single short line of copy,
an in-world highlight pointing at the thing, and a reward when done. If a step needs a
paragraph, the step is badly designed.

Maximum copy per step: **one sentence, ≤ 12 words.**

---

## Minute 0:00–0:30 — Arrival

**Sees:** Camera sweeps low over a small river-bend settlement — Town Hall, a market stall,
a few trees, empty plots catching the light. Birds, wind, distant chatter. HUD fades in
last.

**Learns:** This is a place, not a menu.
**Feels:** Curiosity. "This is mine."

**Fails if:** loading exceeds 4 s, or the HUD appears before the world (the interface must
not be the first impression).

---

## 0:30–1:00 — Camera (Quest 1a)

> *"Drag to look around your town."*

Gentle arrows hint the gesture. Completes on any camera movement. On mobile, the hint shows
a finger glyph instead.

**Learns:** Controls, and that the world is 3D and explorable.
**Feels:** Agency.

**Fails if:** the camera feels floaty, laggy, or inverts expectations. Inertia and the 45°
snap matter here more than anywhere else.

---

## 1:00–1:40 — First building (Quest 1)

> *"Your town needs wood. Build a Lumber Camp."*

An empty plot pulses gold. Tapping opens a compact build card — **not** a catalogue: only
the Lumber Camp is offered. Cost `150c + 20 wood` is shown against the player's `800c / 80
wood`.

Placement: ghost mesh snaps to plots, green when valid. Confirm → dust puff, thud, ground
flattens, crane appears.

**Learns:** Select → choose → place → pay. The whole build verb, in one interaction.
**Feels:** Commitment. The first mark on the world.

**Reward:** +50c, +20 XP.
**Fails if:** the build card looks like a form, or shows eight buildings the player cannot
afford or understand yet.

---

## 1:40–2:10 — Construction is a spectacle (Quest 2)

Construction runs 30 s through four visible stages: foundation → frame → walls → finish,
with a crane, workers, and dust. A slim ring timer sits above the building.

At completion: a burst, a wooden chime, and the camera nudges very slightly toward it.

> *"Start cutting wood."*

One tap on the building starts production. The saw turns. Smoke drifts. The wood counter
begins to climb, one unit every 30 s.

**Learns:** Buildings do work over time, and work is visible.
**Feels:** Satisfaction. This is the first "the world is alive" moment and the single most
important beat in the tutorial.

**Reward:** +50c, +20 XP.
**Fails if:** construction is a bar instead of a scene, or production has no motion. If this
beat lands, players stay. If it does not, nothing later rescues it.

---

## 2:10–2:50 — Storage (Quest 3)

> *"Wood needs somewhere to go. Build a Warehouse."*

Cost `250c + 40 wood` — affordable because production has been running and quest rewards
landed. A capacity indicator appears in the HUD and visibly grows.

**Learns:** Storage is finite and expandable. Introduces the constraint that will later
drive most upgrade decisions.
**Feels:** Preparedness.

**Reward:** +100c, +30 XP.

---

## 2:50–3:30 — Goods move (Quest 4)

The first cart loads at the Lumber Camp, drives the road to the Warehouse, and unloads.
The camera does **not** follow it — the player chooses to watch, which is what makes it feel
like a world rather than a cutscene.

**Learns:** Goods are physical and take time to move.
**Feels:** Delight. This is the beat that gets described to friends.

**Reward:** +100c, +30 XP.
**Fails if:** the cart teleports, clips through buildings, or arrives before it visually
should.

---

## 3:30–4:20 — First sale (Quest 5)

> *"Sell your wood at the Market."*

The Market pulses. Opening it slides in the only dense-number panel in the game: price,
quantity slider, projected total. Selling 60 wood → coins arc through the air to the HUD
counter, which rolls up. The wood price **visibly dips** on a small sparkline.

**Learns:** Goods → money, and *my actions move prices*. The second idea is the seed of the
entire game.
**Feels:** Reward, and the first flicker of strategy.

**Reward:** +200c, +50 XP → player level 2.
**Fails if:** the price dip is not noticed. It must be shown both as a number and as a
movement.

---

## 4:20–5:30 — Getting better (Quest 6)

> *"Upgrade your Lumber Camp."*

The upgrade card shows the projected delta explicitly: **120 → 192 wood/h**. Never a bare
cost with an unexplained benefit.

On completion the building is visibly taller and busier — recognisable from across the city
without reading anything.

**Learns:** Improvement is a lever, and its value is legible before committing.
**Feels:** Ownership. "My city is getting better."

**Reward:** +300c, +80 XP.

---

## 5:30–7:00 — The chain begins (Quest 7)

> *"Raw wood is cheap. Planks are not. Build a Sawmill."*

This copy does the heaviest lifting in the tutorial: it states the game's thesis in nine
words.

The Sawmill consumes wood and produces planks. The player sees wood leaving storage and
planks arriving — the first *connected* system.

**Learns:** Processing multiplies value. Chains exist.
**Feels:** Comprehension. "Oh — *that's* the game."

**Reward:** +400c, +100 XP → player level 3. Tutorial ends. Guidance stops.

---

## 7:00–20:00 — First self-directed play

No more quests. The player has ~1 000 coins and a working chain, and now discovers:

- Bread sells for 60. Planks sell for 10. **Why?**
- Bread needs flour *and* planks → a second chain (Farm → Mill).
- Building it takes ~10 min of income and three plots.

**Learns:** Goals are self-set from observed opportunity.
**Feels:** Intention. The transition from *following* to *playing*.

**Fails if:** the player does not notice the bread opportunity. Mitigation: the Market panel
sorts by price descending, so bread sits at the top, unmissable but unexplained.

---

## 20:00–60:00 — The first real decision

The bread chain comes online. Income roughly triples. Then the bottleneck appears:

> The Sawmill makes 60 planks/h. The Bakery eats 30. What do I do with the rest?

Sell them, bank them toward a second bakery, or upgrade elsewhere. The game asks its first
question it does not answer.

**Learns:** Optimisation, opportunity cost, specialisation.
**Feels:** Investment — the emotional state that produces day-two retention.

---

## Session end

Closing the tab is safe and stated as such. On return, a **Session Recap** animates what was
produced, what was delivered, and **what stopped and why** — the last item being the hook
that pulls the player back into the loop.

Framing rule: *"Your city produced 340 wood while you were away — and your warehouse filled
up."* Never *"You lost 120 wood because you were away."* Same fact, opposite feeling.

---

## Instrumentation

Each beat emits a telemetry event with a timestamp so drop-off is measurable rather than
guessed:

| Event | Healthy target |
|---|---|
| `tutorial_camera_moved` | > 98 % of starts |
| `tutorial_first_building_placed` | > 95 % |
| `tutorial_production_started` | > 92 % |
| `tutorial_first_delivery_seen` | > 90 % |
| `tutorial_first_sale` | > 85 %, median < 6 min |
| `tutorial_completed` | > 70 % |
| `self_directed_first_build` | > 55 % |
| `day2_return` | > 30 % |

Any beat losing more than 10 % of the players who reached it is a design defect, not a
funnel statistic.
