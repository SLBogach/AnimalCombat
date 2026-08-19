# Combat Test Plan WP-08 v0.1 — Decisions

> Статус: `IMPLEMENTED / LOCAL VERIFIED; CI PENDING`; WP-08 — `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`.
>
> Это утверждённая blocking acceptance matrix. `OPEN-WP08-01..17` закрыты и реализованы; тестовые методы покрывают все `107` уникальных blocking ID. Local gates от `2026-08-19` green; GitHub Actions matrix ожидает выполнения на финальном head.

## 1. Назначение

Test Plan задаёт exact pass/fail для:

- action catalog и fixed availability order;
- common decision predicates и first rejection reason;
- fixed-point Tactic/Situation/Synergy/Counter/Variety/Opportunity weights;
- `OnlyLegalAction`, `HardOpportunity`, zero fallback и weighted Decision RNG;
- repeat history и opportunity debt;
- no-hidden-knowledge и общий phase-5 snapshot;
- atomic action commit, costs, cooldowns, timings и lifecycle;
- `DecisionMade`, `ActionCommitted`, `AttackPrepared` и diagnostic DecisionTrace;
- replay semantics, determinism, safety, regression, architecture и coverage.

Статус `COMPLETED` запрещён, пока все blocking cases §14 не green локально и в Windows/Linux CI.

## 2. Источники и precedence

- Combat Design Specification v0.1: §2 (`Decision point`, `Action`, `Commit`), §§4.2–4.3, 7.2, 9.1–9.5, 10.5, 14.1–14.2, 15, приложение A.
- Combat Lab Technical Design v0.1: §§8.1–8.3, 12–13.2, 15.1–15.2, 21.2–21.4, 23.2, приложения B/E.
- Combat Event & Replay Schema v0.1: §§7–8.3, 9.2, 10.1–10.2, 11–12.2, 19.3–19.4, 21.2–21.4.
- Canonical balance config v0.1 — actions/tactics/passive/gear/settings values and Stable IDs.
- [WP-08 Brief](./WP-08_Brief.md) — утверждённые scope, algorithms и decisions.
- [Decisions](./Decisions.md) — approval/closure state.

CDS combat rule → DATA number/ID → Replay Schema wire/integrity → TDD architecture → этот Test Plan pass/fail. Existing code не разрешает конфликт сам по себе.

## 3. Scope boundary

Blocking WP-08 проверяет decision/commit и generic lifecycle. Следующее запрещено считать частью WP-08 pass:

- phase 7–10 combat intents или Resolution RNG;
- hit/miss, Block/Dodge/Counter outcome, damage/stagger/control;
- forced movement, wall impact, grab/throw/swap;
- effects/triggers и fighter-specific resource/passive reactions;
- Unity changes.

Combat action может пройти `Startup → Active → Recovery → null`, но его Active phase до WP-09 не наносит урон и не двигает бойца/цель.

## 4. Approval and implementation state

Exact решения `OPEN-WP08-01..17` из Brief утверждены. Поэтому:

- этот Test Plan является нормативной blocking matrix;
- `Decisions.md` помечает каждый пункт `CLOSED`;
- WP-08 имеет статус `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`;
- production implementation и тесты для всех `107` уникальных §14 IDs присутствуют;
- `ContractVersions.Engine` повышен до `battle.core/0.3.0`, current fixtures созданы отдельно от immutable historical fixtures;
- consolidated local run green: locked restore, Release build/test, filtered inventory, generated/target/historical replay и coverage gates;
- статус `COMPLETED` запрещён до green `windows-latest`/`ubuntu-latest` × Debug/Release на финальном head.

## 5. DATA и validation oracle

### 5.1 Required settings

```text
fp_scale                         = 1000
multiplier_min                   = 250
multiplier_max                   = 3000
decision_weight_max              = 100000000
repeat_same_action_fp            = 550
repeat_same_category_fp          = 800
opportunity_growth_fp            = 250
opportunity_cap_fp               = 2500
hard_opportunity_misses          = 4
default_perception_delay_ticks   = 5
```

Runtime читает точные canonical keys с prefixes `global.sim.`/`global.ai.`. Alias и numeric default запрещены.

`global.ai.default_perception_delay_ticks` remains a required, range-validated v0.1 compatibility key, but cannot replace the required selected-tactic `perception_delay_ticks`; a missing tactic value is rejected rather than defaulted.

Exact integer domains before cross-field validation:

| Key | Inclusive domain | Out-of-domain code/path |
|---|---|---|
| `global.sim.fp_scale` | `1..Int32.MaxValue` | zero/negative: `ZeroDivisor`; above Int32: `NumericOutOfRange`; `$.settings.global.sim.fp_scale` |
| `global.sim.multiplier_min` | `0..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.sim.multiplier_max` | `1..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.sim.decision_weight_max` | `1..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.ai.repeat_same_action_fp` | `0..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.ai.repeat_same_category_fp` | `0..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.ai.opportunity_growth_fp` | `0..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.ai.opportunity_cap_fp` | `1..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.ai.hard_opportunity_misses` | `0..Int32.MaxValue` | `NumericOutOfRange`; exact key path |
| `global.ai.default_perception_delay_ticks` | `0..Int32.MaxValue` | `NumericOutOfRange`; exact key path |

“Exact key path” means `$.settings.<full dotted key>`; there is no alias or coercion.

### 5.2 Required relationships

- `0 < fp_scale`;
- `0 <= multiplier_min <= fp_scale <= multiplier_max`;
- both repeat multipliers and global opportunity cap are inside `multiplier_min..multiplier_max`;
- all tactic/repeat/passive/gear decision multipliers inside global bounds;
- `0 <= base_weight <= decision_weight_max`;
- `max_consecutive_uses >= 1`;
- `opportunity_growth_fp >= 0`;
- `fp_scale <= action.opportunity_cap_fp <= global opportunity cap`;
- action hard misses is `0` (disabled) or `1..global hard threshold`;
- selected tactic perception delay `>=0`; required tactic value is authoritative;
- full diagnostic checked catalog size `<=256`;
- per-fighter maximum legal decision set size `<=128`;
- `candidate_count × decision_weight_max <= Int32.MaxValue` for every reachable build/mode catalog, or setup rejects before Begin;
- hit schedule is unique/sorted, matches configured hit count/primitives and lies within Active schedule;
- tag token comparison is ordinal exact; duplicate tokens reject; token order does not alter meaning.

Compiler/schema errors reject config. Config-dependent applicability error in Engine returns `BattleResult.Rejected` before `journal.Begin`. No malformed decision value may become `Draw`, fallback or infrastructure exception.

Stable validation oracle for every required setting key `K`:

- missing `K` alone → compiler code `MissingRequiredConfigKey`, path `$.settings`;
- JSON string, numeric string or non-minimal/non-integral number → `InvalidInteger`, path `$.settings.K`;
- integer outside its declared WP-08 range → the exact code from the domain table, path `$.settings.K`;
- broken global multiplier relationship → `NumericOutOfRange`, path `$.settings`;
- invalid action opportunity cap/threshold → `NumericOutOfRange`, path `$.actions[<action_id>].<field>`;
- duplicate/invalid tag token → new stable code `InvalidTagSet`, path `$.<catalog>[<stable_id>].tags`;
- invalid schedule → new stable code `InvalidHitSchedule`, path `$.actions[<action_id>].hit_schedule`;
- reachable impact-timing overflow → `DecisionTimingOverflowRisk`, path `$.actions[<action_id>].hit_schedule`;
- unknown additional `sys_*` action → `InvalidSystemAction`, path `$.actions[<action_id>]`;
- incompatible inferred target/movement pair → new stable code `AmbiguousTargetProfile`, path `$.actions[<action_id>].movement_mode`;
- reachable legal-weight sum risk → Engine applicability code `DecisionWeightSumOverflowRisk`, path `$.mode_rules.allowed_action_ids`.

Tests mutate one key/entity at a time and assert exact code/path plus `journal.BeginCount=0`; localized message text is not part of the contract.

### 5.3 Config cases

| ID | Input | Pass |
|---|---|---|
| `WP08-CFG-001` | Generated canonical config | All decision profiles materialize; `24` actions, `4` tactics; no warning/default. |
| `WP08-CFG-002` | Each required setting `K` missing alone | `MissingRequiredConfigKey` at `$.settings`, Begin `0`. |
| `WP08-CFG-003` | Each required `K` as string/`1.5` and its applicable boundary−1/boundary+1 from the domain table | `InvalidInteger`, `ZeroDivisor` or `NumericOutOfRange` at exact `$.settings.K`; Begin `0`. |
| `WP08-CFG-004` | Each global multiplier/action opportunity relationship broken alone | Exact `NumericOutOfRange` path from map; no clamp repairs DATA; Begin `0`. |
| `WP08-CFG-005` | Reachable catalog with `candidate_count × decision_weight_max = Int32.MaxValue+1` | `DecisionWeightSumOverflowRisk` at `$.mode_rules.allowed_action_ids`; Begin and Decision RNG calls `0`. |
| `WP08-CFG-006` | Duplicate/invalid tag; duplicate/out-of-order/out-of-Active schedule entry | `InvalidTagSet` or `InvalidHitSchedule` at exact entity field; Begin `0`. |
| `WP08-CFG-007` | No-hit `Follow` (Self inference) and hit-scheduled `Retreat` (Opponent inference) | Both `AmbiguousTargetProfile` at exact movement field; no ActionId default; Begin `0`. |
| `WP08-CFG-008` | `hard_opportunity_misses=0` | Valid and means hard override disabled. |

## 6. Candidate catalog and availability oracle

### 6.1 Checked catalog

For actor:

```text
checked = all System
        + all Basic owned by actor.animal
        + all Special owned by actor.animal
checked = ordinal_sort(ActionId)
```

Mode and selected-special loadout are predicates, not pre-sort filters. Поэтому DiagnosticReplay видит rejected catalog entries; StandardReplay публикует только legal IDs.

Production legal set contains:

- exactly one WP-07 system action after neutral-band/headroom rules;
- allowlisted Basics actor animal;
- only the two selected allowlisted Specials;
- only candidates that pass all following predicates.

### 6.2 Predicate order and codes

| Order | Predicate | First rejection code |
|---:|---|---|
| 1 | Actor `DecisionReady`, category permitted | `ActorNotDecisionReady` / `CategoryUnavailable` |
| 2 | Mode allowlist | `ActionNotAllowedByMode` |
| 3 | Owner/slot/loadout | `WrongOwner`, `WrongSlot`, `ActionNotInLoadout` |
| 4 | Cooldown is zero/absent | `CooldownActive` |
| 5 | Enough Energy | `InsufficientEnergy` |
| 6 | Enough unique resource | `InsufficientResource` |
| 7 | Required target exists and not Defeated | `TargetUnavailable`, `TargetDefeated` |
| 8 | Inclusive decision range or WP-07 system band | `OutOfDecisionRange`, `SystemBandUnavailable` |
| 9 | Required movement direction has body-aware headroom | `NoMovementHeadroom` |
| 10 | Counter telegraph observed after perception delay | `TelegraphNotObserved` |
| 11 | Two-pass repeat cap | `MaxConsecutiveUses` |

Only first code is kept. A later predicate cannot replace it.

### 6.3 Target/range compatibility

Target derivation for current `combat.balance/0.1`:

```text
System                                      => Opponent
non-System and (hit_count > 0 or schedule)  => Opponent
otherwise                                   => Self
```

Any ambiguous row fails setup.

Allowed inferred-target/movement pairs for non-System actions are exact; System actions keep the WP-07 exception and target Opponent for replay compatibility:

| Inferred target | Allowed `movement_mode` | Ambiguous examples |
|---|---|---|
| Opponent | `None`, `Approach`, `Follow`, `Push`, `Pull`, `Swap` | hit-scheduled `Retreat` or `Adaptive` |
| Self | `None`, `Approach`, `Retreat`, `Adaptive` | no-hit `Follow`, `Push`, `Pull` or `Swap` |

Range/headroom:

- Opponent `None|Push|Pull|Swap`: inclusive `hit_range_min..max` surface gap;
- Opponent `Approach|Follow`: inclusive `preferred_range_min..max` start gap;
- Self `Approach`: gap above preferred maximum and inward headroom;
- Self `Retreat`: outward headroom;
- Self `Adaptive`: outside inclusive preferred band, with headroom in selected direction;
- Self `None`: no range predicate;
- system actions retain exact WP-07 availability;
- WP-09 always rechecks geometry at impact; a legal commit never guarantees hit.

### 6.4 Availability cases

| ID | Check | Pass |
|---|---|---|
| `WP08-CAT-001` | Bear catalog composition | Three System + all Bear Basic/Special checked, ordinal; only selected two Specials may become legal. |
| `WP08-CAT-002` | ModeRules allowlists, config catalog rows and backing dictionary insertion shuffled | Identical candidates/scores/events/digest after their defined canonical sort. `FighterBuildSnapshot.SpecialActionIds` is explicitly excluded: build order is canonical input and changing it may change input digest. |
| `WP08-CAT-003` | Wrong animal/slot/unselected Special/mode exclusion | Exact first rejection; not in Standard legal list. |
| `WP08-AVL-001` | Actor not decision-ready | No decision point; direct evaluator returns first state rejection. |
| `WP08-AVL-002` | Cooldown/cost boundary | `0` legal; `1` cooldown or one-unit deficit illegal with exact code. |
| `WP08-AVL-003` | Target null/Defeated | Illegal before range/telegraph. |
| `WP08-AVL-004` | Range min/max/min−1/max+1 | Inclusive bounds exact, body-aware surface gap. |
| `WP08-AVL-005` | Self movement at wall | Headroom zero rejects; one unit legal. |
| `WP08-AVL-006` | Counter delay `d` | elapsed `d−1` illegal, `d` legal; uncommitted opponent candidate invisible. |
| `WP08-AVL-007` | Predicate has several failures | Only earliest fixed code stored. |
| `WP08-AVL-008` | System regression | WP-07 neutral band/headroom truth table unchanged. |
| `WP08-AVL-009` | No legal candidate after required fallback validation | Deterministic invariant; predicates are not weakened. |

## 7. Fixed-point weights

### 7.1 Stage order

```text
weight = clamp(base, 0, decision_weight_max)
weight = mul_fp(weight, clamp(Tactic))
weight = mul_fp(weight, clamp(Situation))
weight = mul_fp(weight, clamp(Synergy))
weight = mul_fp(weight, clamp(Counter))
weight = mul_fp(weight, clamp(Variety))
weight = mul_fp(weight, clamp(Opportunity))
weight = clamp(weight, 0, decision_weight_max)
```

Each multiply uses checked `Int64` and floor semantics already fixed by WP-02. Reordering stages or performing one final division is failure.

Reference vector:

| Step | Value |
|---|---:|
| Base | `1000` |
| Tactic `1250` | `1250` |
| Situation `800` | `1000` |
| Synergy `1100` | `1100` |
| Counter `900` | `990` |
| Variety `550` | `544` |
| Opportunity `1500` | `816` |

### 7.2 Tactic mapping

Tactic submultipliers fold from `fp_scale` in exact order:

1. `approach_fp` for `approach`;
2. `block_fp` for `block`;
3. `dodge_fp` for `dodge`;
4. `grab_fp` for `grab`;
5. `heavy_fp` for `heavy`;
6. `light_fp` for `light`;
7. `resource_generator_fp` for exact `*_generator` or `rhythm`;
8. `resource_spender_fp` when `resource_cost>0`;
9. `retreat_fp` for `retreat`;
10. `signature_fp` for `signature`.

`counter_fp`, context fields and repeat penalty are not multiplied here.

### 7.3 Situation/Synergy/Counter

- Situation suborder: low health → self wall → target wall → target recovery.
- Low-health only exists when an exact animal threshold key exists; no global numeric default.
- Wall checks use `global.arena.wall_zone_size` and current center/body-safe state.
- Target-wall multiplier applies only to exact `position|knockback|wall_impact|grab` opportunities.
- Self-wall multiplier applies only to exact `retreat|dodge|position|grab` escape/reversal actions.
- Synergy: selected passive tag intersection, then Offense/Defense/Utility gear intersections; passive uses `weight_multiplier_fp`, gear uses `normalized_value`.
- Counter: candidate has exact `counter` tag and observed public telegraph after tactic perception delay.
- Missing/nonmatching stage is identity `fp_scale`.

### 7.4 Weight cases

| ID | Check | Pass |
|---|---|---|
| `WP08-WGT-001` | Reference six-stage vector | Final `816`, exact intermediate floors. |
| `WP08-WGT-002` | Stage order permutation | Test proves different result and rejects permutation. |
| `WP08-WGT-003` | Multiplier below/above bounds | Stage clamp exact; invalid canonical DATA still rejected pre-start. |
| `WP08-WGT-004` | Final exceeds max | Exact `decision_weight_max`, no wrap. |
| `WP08-WGT-005` | Base zero | Final zero through all positive modifiers. |
| `WP08-WGT-006` | Direct calculator corruption vector: base `Int32.MaxValue`, first stage `3000`, scale `1000` | Exact `DecisionArithmeticOverflow` engine invariant at `Decisions`; no wrapped/clamped result. Valid request/config risks are already rejected only by `WP08-CFG-005`. |
| `WP08-WGT-007` | `tactic_pressure` paw jab | Tactic `light_fp=1150`; neutral other stages; final `1150`. |
| `WP08-WGT-008` | `tactic_pressure` sys retreat | Tactic `retreat_fp=750`; `450×750/1000=337`. |
| `WP08-WGT-009` | Multi-tag tactic fold | Exact listed suborder and floor after each fold. |
| `WP08-WGT-010` | Situation boundaries | Wall/recovery/low-HP predicates at exact boundary. |
| `WP08-WGT-011` | Passive/gear tag intersection | Ordinal exact tags only; current gear `1000` is neutral, not skipped default. |
| `WP08-WGT-012` | Counter knowledge | Same public view gives same multiplier; hidden future action change gives no difference. |
| `WP08-WGT-013` | Illegal candidate | Diagnostic modifiers empty, final weight `0`, excluded from sum. |

## 8. Selection and RNG

### 8.1 Precedence

1. One legal candidate → `OnlyLegalAction`, no RNG, even with weight `0`.
2. With multiple legal candidates, an emergency marker suppresses HardOpportunity override; ordinary weighting continues.
3. Otherwise HardOpportunity-ready candidate wins without RNG.
4. Otherwise total `0` → fixed legal system fallback, no RNG.
5. Otherwise exactly one `Decision.NextInt(0, weight_sum, NextInt)`.

Two or more legal candidates with only one positive weight still consume one draw. Zero weights make empty intervals but remain in the public legal list.

### 8.2 Interval oracle

For sorted weights `[3,0,2]`:

```text
Action A: [0,3)
Action B: [3,3)
Action C: [3,5)
draw 0/2 => A
draw 3/4 => C
```

Selection is `first cumulative_end > draw`.

### 8.3 RNG cases

| ID | Check | Pass |
|---|---|---|
| `WP08-SEL-001` | One legal positive | OnlyLegal; configured chosen/sum; Decision index unchanged. |
| `WP08-SEL-002` | One legal zero | OnlyLegal `0/0`; no RNG, not ZeroFallback. |
| `WP08-SEL-003` | Multiple legal, sum zero | System priority fallback; chosen `0`, sum `0`, no RNG. |
| `WP08-SEL-004` | Multiple legal, one positive | One Decision draw; positive candidate selected. |
| `WP08-SEL-005` | Interval boundaries | `0`, prefix−1, prefix, sum−1 select exact candidate. |
| `WP08-SEL-006` | Candidate insertion order shuffled | Same sorted intervals/draw/chosen. |
| `WP08-SEL-007` | A and B both draw | A consumes index `0`, B `1`; exact raw/result/provenance. |
| `WP08-SEL-008` | A no-draw, B draw | B consumes index `0`; no reserved draw. |
| `WP08-SEL-009` | Decision choice | Resolution stream index/state unchanged. |
| `WP08-SEL-010` | Weighted provenance tamper | Wrong stream/operation/range/result/normalized/index rejected. |
| `WP08-SEL-011` | HardOpportunity | Deterministic choice, mode HardOpportunity, RNG null/index unchanged. |
| `WP08-SEL-012` | Multiple hard candidates | debt DESC, final weight DESC, ActionId ASC exact. |

## 9. Variety and opportunity

### 9.1 Repeat

Immediate commit history only:

```text
same ActionId  => × global.ai.repeat_same_action_fp
same category  => × global.ai.repeat_same_category_fp
any repeat     => × tactic.repeat_penalty_fp
```

Order is same action → same category → tactic. History updates on commit, including system action and interrupted combat action.

MaxConsecutive two-pass rule must never remove every base-legal candidate solely because all are at cap.

### 9.2 Opportunity

Per Special ActionId:

```text
debt starts 0
legal and not selected => debt + 1
not legal              => unchanged
selected commit        => 0

opportunity_multiplier = min(
    action.opportunity_cap_fp,
    global.ai.opportunity_cap_fp,
    fp_scale + debt * global.ai.opportunity_growth_fp)
```

Hard is disabled at action value `0`. Positive action threshold is capped by global threshold. Hard becomes ready when prior debt `>= effective_threshold`.

### 9.3 History/debt cases

| ID | Check | Pass |
|---|---|---|
| `WP08-VAR-001` | Same action/category | Exact three multipliers and floor order. |
| `WP08-VAR-002` | Different action same category | Category+tactic only. |
| `WP08-VAR-003` | Different category | Variety identity; action/category counters reset. |
| `WP08-VAR-004` | Candidate reaches MaxConsecutive with alternative | Candidate rejected with exact code. |
| `WP08-VAR-005` | All base-legal candidates capped | None removed by cap; penalties still apply. |
| `WP08-OPP-001` | Special legal but not selected | Debt increments once after frozen decision. |
| `WP08-OPP-002` | Special illegal for resource/range | Debt unchanged. |
| `WP08-OPP-003` | Selected then interrupted later | Debt reset at commit and remains reset. |
| `WP08-OPP-004` | Growth/cap vector | `1000,1250,1500,…` capped by min(action/global). |
| `WP08-OPP-005` | Hard threshold `4` | Four prior misses: next eligible decision Hard; no draw. |
| `WP08-OPP-006` | Action hard `0` | Debt/multiplier may grow, Hard never activates. |
| `WP08-OPP-007` | Emergency seam true | Hard override suppressed; ordinary selection semantics/RNG apply. |
| `WP08-OPP-008` | Both actors have debt changes | Shared snapshot; A update cannot change B current score. |

## 10. Shared snapshot, commit and lifecycle

### 10.1 Phase-5 snapshot

At phase 5 entry, after expiry/resource/phase-end and before any decision draw:

- capture both current public frames and decision metadata atomically;
- actor that completed Recovery in phase 4 appears `DecisionReady`;
- cooldown reaching zero in phase 3 is legal now;
- both actors reference the same snapshot identity;
- scoring/selection never reads mutable state after capture;
- commit A cannot alter B candidates, weights, frozen descriptor target or `DecisionMade` snapshot target frame; this does not describe the later `ActionCommitted B` envelope frame.

### 10.2 Frozen timing

```text
speed_multiplier = clamp(
    fp_scale + (ActionSpeed - speed_baseline) * speed_slope,
    speed_min,
    speed_max)

startup = clamp(div_fp(startup_base, speed_multiplier), startup_min, startup_max)
recovery = clamp(div_fp(recovery_base, speed_multiplier), recovery_min, recovery_max)
active = configured active_ticks
```

`FixedTiming` uses identity speed multiplier. Active and hit schedule are never sped up. All values freeze once.

### 10.3 Mutation/emission order

```text
DecisionMade A
DecisionMade B
ActionCommitted A
ActionCommitted B
ResourceChanged A Energy? / UniqueResource?
ResourceChanged B Energy? / UniqueResource?
AttackPrepared A?
AttackPrepared B?
```

Commit descriptors are fully prepared before first mutation. Costs apply once; cooldown starts once and first decrements in next tick phase 3.

Selection draws are first evaluated on a preview copy of the Decision stream. After descriptors and exact derived-event count are known, the whole phase-5 batch must fit while preserving the final `BattleEnded` slot. Only then are the preview RNG state and batch committed. Insufficient capacity emits the deterministic terminal invalid path from the pre-batch state: no DecisionMade/ActionCommitted/cost/telegraph from that batch, no RNG-index advance, and no repeat/opportunity/cooldown/resource/action mutation.

No rule evaluation occurs after descriptor freeze inside the commit batch. `ActionCommitted` submutations/events run A then B and each frame pair is taken from that exact authoritative submutation: B's unchanged target frame may therefore contain A's already committed public action state, while B's descriptor/decision remains snapshot-frozen. `ActionCommitted` changes action/state/phase/timer/cooldown but not resource scales. Cost `ResourceChanged` events then mutate Energy followed by unique resource. Zero delta emits no resource event.

### 10.4 Target/direction/telegraph

- Opponent descriptor and `DecisionMade`: target ID/frame and target position come from decision snapshot; direction freezes toward opponent unless movement profile explicitly Retreat. `ActionCommitted` envelope target before/after frames instead follow the authoritative A→B submutation rule in §10.3 and never feed the descriptor.
- Self action: target ID/frame/position null; movement direction only when applicable.
- `AttackPrepared` exists only for non-empty hit schedule.
- `telegraph_tick=commit_tick`.
- absolute impact ticks=`commit_tick+startup_ticks+relative_schedule_tick`.
- `direction_locked=true`; `track_target` may affect future WP-09 geometry, not frozen commit direction.

Generic non-System combat lifecycle uses actor-only `ActionPhaseChanged`: target ID/frames, RNG and resolution group are null; action/decision IDs are preserved; related IDs contain exactly the direct source. Startup→Active is `StartupCompleted` sourced by `ActionCommitted`. Active→Recovery is `ActiveCompleted` sourced by the latest generic lifecycle event, or by `ActionCommitted` after a direct-to-Active commit. Recovery→null is `RecoveryCompleted` sourced by Active→Recovery. Zero Recovery produces direct Active→null with `ActiveCompleted`. Simultaneous transitions use InitiativeOrder. WP-07 movement continues to use `MovementCompleted` sourced by `MoveEnded` and is byte/semantic-regression protected.

### 10.5 Commit/lifecycle cases

| ID | Check | Pass |
|---|---|---|
| `WP08-SNP-001` | Recovery completes in phase 4 | Phase-5 frame/readiness is DecisionReady, not stale Recovery. |
| `WP08-SNP-002` | Cooldown reaches zero in phase 3 | Candidate legal in same tick decision phase. |
| `WP08-SNP-003` | Commit A mutates state | B legal IDs/weights/frames match precommit shared view. |
| `WP08-CMT-001` | Both choose | Descriptors frozen together; Decisions A/B then authoritative commit submutations A/B regardless InitiativeOrder; exact sequential frame rule above. |
| `WP08-CMT-002` | Energy/resource exact cost | One atomic deduction each, correct event order/delta/frame. |
| `WP08-CMT-003` | Cost zero | No ResourceChanged; commit payload still records zero. |
| `WP08-CMT-004` | Cooldown | Set at commit; not decremented in same tick; availability exact next ticks. |
| `WP08-CMT-005` | ActionSpeed min/baseline/max/bounds | Exact frozen startup/recovery floor+clamp; Active unchanged. |
| `WP08-CMT-006` | Cost/resource changes after commit | Action continues; no revalidation/refund. |
| `WP08-CMT-007` | Opponent vs Self target | Exact IDs/frames/position/direction/null semantics. |
| `WP08-CMT-008` | Hit schedule | One AttackPrepared, exact absolute sorted impact ticks. |
| `WP08-LIFE-001` | Combat lifecycle | Startup→Active→Recovery→null and zero-Recovery branch in phase 4; exact actor-only frames, reasons, source/related chain and timers above. |
| `WP08-LIFE-002` | Active phase before WP-09 | No intent, HP, position, stagger, effect or Resolution RNG mutation. |
| `WP08-LIFE-003` | WP-07 movement action | Existing movement lifecycle/event oracle unchanged. |
| `WP08-LIFE-004` | Terminal during action | No cleanup event after BattleEnded; frozen final frame retained. |

## 11. Event, diagnostics and replay conformance

### 11.1 DecisionMade mode rules

| Mode | Required semantics |
|---|---|
| `WeightedRng` | `candidate_count>=2`, sum `>0`, chosen weight `>0`, RNG=`Decision/NextInt`, range `0..sum`. |
| `OnlyLegalAction` | count/list `1`, chosen is sole ID, chosen weight=sum, RNG null. |
| `ZeroWeightFallback` | count `>=2`, sum/chosen `0`, chosen fixed legal system priority, RNG null. |
| `HardOpportunity` | count `>=2`, chosen legal (final weight may be `0`), RNG null, `HardOpportunity` reason first. |

All modes require sorted unique legal IDs, count equality and chosen membership.

### 11.2 Standard explainability

- envelope `source_event_id` for both decisions points to the pre-batch causal root, never to the other decision;
- commit source is corresponding decision;
- cost/telegraph source is corresponding commit;
- `resolution_group_id=null` in all WP-08 decision/commit/lifecycle events;
- legal diagnostic candidate always has exactly six folded stage traces in `Tactic, Situation, Synergy, Counter, Variety, Opportunity` order, including identities; illegal candidate has none;
- standard dominant modifiers include only chosen non-identity folded stages, absolute deviation DESC then stage order, max six;
- selection reason first, remaining reason codes stable/unique/max eight;
- before/after roles match actor/optional target exactly.

### 11.3 Diagnostic overlay

DiagnosticReplay must contain one DecisionTrace per canonical `DecisionMade`:

- decision/tick/sequence/actor exact;
- all checked candidates ordinal;
- legal/first rejection/base/six folded stage modifiers-or-empty/final exact;
- public legal candidates exactly match trace candidates with `legal=true`;
- chosen candidate final weight matches payload;
- both decisions from the same phase-5 batch carry one identical snapshot digest: SHA-256 of ASCII `decision.batch-snapshot/0.1`, one terminating NUL byte, then canonical JSON containing battle ID, engine version, master seed, config hash; full ModeRules identity/settings/five sorted allowlists; tick; InitiativeOrder; Decision next index; and A/B-ordered fighter projections with public frame, build refs, positive ActionId-sorted cooldowns, immediate action/category history+streaks, ActionId-sorted selected-Special debt, observable telegraph/tick and emergency flag;
- standalone verification enforces digest format and same-batch equality; config-aware producer conformance recomputes the exact typed Core projection through the Replay-owned canonical writer/hash;
- no diagnostics in StandardReplay;
- Standard and Diagnostic canonical events, input digest, final digest and summary byte-equivalent.

### 11.4 Replay cases

| ID | Check | Pass |
|---|---|---|
| `WP08-CON-001` | Valid four selection modes | Contract constructor, writer, schema and semantic verifier green. |
| `WP08-CON-002` | Count/list/chosen tamper and rehash | Semantic rejection. |
| `WP08-CON-003` | Mode/weight/RNG nullability tamper and rehash | Semantic rejection with stable path/code. |
| `WP08-CON-004` | RNG raw/result/normalized/range/index tamper | Semantic rejection. |
| `WP08-CON-005` | Unsorted legal IDs/modifiers/reasons | Constructor or semantic rejection. |
| `WP08-CON-006` | Commit/telegraph target, timing or frame inconsistency | Config-free verifier rejects cross-event contradictions; exact DATA cost/timing values are asserted by config-aware engine conformance tests, not guessed by standalone ReplayVerifier. |
| `WP08-CON-007` | Broken decision→commit→telegraph/cost cause | Semantic rejection. |
| `WP08-CON-008` | Standard profile with diagnostics | Schema/semantic rejection. |
| `WP08-CON-009` | Diagnostic trace missing/extra/mismatched | Semantic rejection. |
| `WP08-CON-010` | Standard vs Diagnostic | Same canonical event bytes/digests/summary. |
| `WP08-CON-011` | SummaryOnly | Same decisions/RNG counters/summary; no replay publication. |
| `WP08-CON-012` | Historical replay set | Existing bytes/hashes and verifier results unchanged. |

## 12. Golden `decision_weighted_l1`

### 12.1 Fixture input

- canonical generated config with only `global.arena.start_position_a=4000`, `global.arena.start_position_b=5540`, `battle.time_limit_ticks=1`; all other settings retain canonical values;
- battle/replay IDs `battle-wp08-decision-weighted-l1` / `replay-wp08-decision-weighted-l1`; engine `battle.core/0.3.0`; `StandardReplay`; master seed `0`;
- ModeRules id/version/normalization `decision_weighted_l1_v01` / `mode.rules/0.1` / `None`;
- exact ordinal mode allowlists: animal `[bear]`; actions `[bear_earthbreaker,bear_fury_maul,bear_paw_jab,sys_retreat,sys_wait]`; passive `[bear_thick_hide]`; gear `[gear_defense_reinforced_hide,gear_offense_power_wraps,gear_utility_sprint_soles]`; tactic `[tactic_pressure]`;
- builds `fighter_a/A` and `fighter_b/B`: Bear, build ID null, selected Specials in build order `[bear_earthbreaker,bear_fury_maul]`, `bear_thick_hide`, Offense/Defense/Utility=`gear_offense_power_wraps`/`gear_defense_reinforced_hide`/`gear_utility_sprint_soles`, `tactic_pressure`;
- centers `4000/5540` give body-aware surface gap `500` and positive outward headroom; derived Bear ActionSpeed/Initiative remain `85`/`85`; existing `StatThenSeededHash` at seed `0` yields `[fighter_a,fighter_b]` from exact scores `7606814898323161814` / `13543846652233914625`;
- both Specials are checked but fail unique-resource at initial value `0`; `sys_approach` fails mode and `sys_wait` fails system band, leaving paw and retreat legal.

### 12.2 Candidate/weight oracle

Both actors:

| Action | Weight interval |
|---|---|
| `bear_paw_jab` | `1150`, `[0,1150)` |
| `sys_retreat` | `337`, `[1150,1487)` |

`legal_action_ids=[bear_paw_jab,sys_retreat]`, `candidate_count=2`, `weight_sum=1487`.

### 12.3 RNG oracle

| Actor | stream/index | raw_u32 | range/result | normalized_fp | chosen |
|---|---|---:|---|---:|---|
| A | `Decision/0` | `2879411843` | `[0,1487) / 1400` | `941` | `sys_retreat` |
| B | `Decision/1` | `495049527` | `[0,1487) / 461` | `310` | `bear_paw_jab` |

Resolution index remains `0`.

### 12.4 Event oracle

| Seq | Tick | Event | Exact meaning |
|---:|---:|---|---|
| 0 | 0 | `BattleStarted` | Shared initial frames, initiative `[fighter_a,fighter_b]`, tie-break `StatThenSeededHash`, Decision/Resolution indices `0/0`. |
| 1 | 0 | `DecisionMade` A | WeightedRng, index `0`, retreat `337/1487`. |
| 2 | 0 | `DecisionMade` B | WeightedRng, index `1`, paw `1150/1487`; same snapshot identity. |
| 3 | 0 | `ActionCommitted` A | `sys_retreat`, direction away from B, frozen `1/5/1`, no costs/cooldown. |
| 4 | 0 | `ActionCommitted` B | `bear_paw_jab`, direction toward A, frozen `3/1/5`, costs/cooldown `0`. |
| 5 | 0 | `AttackPrepared` B | Telegraph tick `0`, impact ticks `[3]`, source event 4. |
| 6 | 1 | `TimeoutReached` | Equal Bear health fractions; no active tick 1 phases. |
| 7 | 1 | `DrawDeclared` | `TimeoutEqualHealthFraction`, source event 6. |
| 8 | 1 | `BattleEnded` | Draw, `9` canonical events. |

Final A frame: Retreat/Startup, remaining `1`; final B frame: AttackPrepare/Startup, remaining `3`. HP/energy/resource/positions unchanged. Decision next index `2`; Resolution next index `0`.

Implemented fixture state:

- a new versioned weighted fixture was generated without overwriting historical files;
- weighted pins: config `sha256:26c53cf464539e2ebf1eb37f90d73715adb0842e29e6b7a9eeaede8336d49227`, input `sha256:eaee293a90e5fc432ab1822965b3f632abc803bd79b23ae401a8fc9fd8a2b021`, final `sha256:6ed4f34aa845096ee63d125d306fbef64ff469773e14389bfe1152146a007f3f`, file `1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0`, events `9`;
- current `wait_equal_l1` for `battle.core/0.3.0` is a separate artifact: config `sha256:f7524a127ca0ec085562d1ca43fc91d384b7f713f1ddb323be53bc701f6d0dc3`, input `sha256:4155833aa33fd60fee5f034dc8f4050afb957682af5141701d6dca463bbc7a08`, final `sha256:bcc34972a33aadd5da02f3c5d3996ecd76c0037fbfe5e94e25cdf883ca9177f9`, file `8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd`, events `8`;
- historical Engine `0.1.0`/`0.2.0` wait fixtures and WP-07 `approach_band_l3` bytes remain immutable;
- schema/semantic/integrity, ×100 repetition and target/profile/culture variants are part of the final execution gates below; their outcome is not predeclared here.

## 13. Determinism, safety, regression and architecture

### 13.1 Determinism

`WP08-DET-007` is a pure decision-model mirror, not a canonical replay-digest comparison. Let `M=arena_min+arena_max`; transform old A→new B and old B→new A, position `p→M−p`, facing/commit direction Left↔Right, actor/target IDs and corresponding build/history/debt/cooldown entries A↔B. Evaluate corresponding actor contexts with the same explicitly injected bounded draw (or a no-draw mode), then normalize the mirrored result back with the inverse mapping. Candidate IDs, first rejection, six stage multipliers, final weights, chosen ActionId, target position and direction must be exact. Event sequence/EventId/digest are excluded because canonical Decision RNG consumption and emission intentionally remain A→B.

| ID | Variant | Pass |
|---|---|---|
| `WP08-DET-001` | Same process ×100 | Exact drafts, diagnostics, RNG, summary, final digest. |
| `WP08-DET-002` | Standard/Diagnostic/SummaryOnly | Same gameplay events/RNG/summary; overlay isolated. |
| `WP08-DET-003` | `en-US`, `ru-RU`, `tr-TR` | Exact canonical bytes/digests. |
| `WP08-DET-004` | `netstandard2.1` vs `net10.0` | Target semantic parity. |
| `WP08-DET-005` | Debug vs Release, Windows vs Linux | Pinned fixture/event-by-event equality. |
| `WP08-DET-006` | Catalog/dictionary insertion order | No change. |
| `WP08-DET-007` | Pure evaluator mirror above | Exact inverse-normalized scores/descriptor with same injected draw; no Side/FighterId/iteration bias; replay digest not compared. |

### 13.2 Safety/regression

| ID | Check | Pass |
|---|---|---|
| `WP08-SAFE-001` | Event cap one slot below/at exact decision batch requirement | Below: preflight leaves batch/RNG/gameplay/history absent and uses reserved BattleEnded; at boundary: complete batch plus terminal event fits. |
| `WP08-SAFE-002` | Zero-progress watchdog | Existing deterministic invariant semantics retained. |
| `WP08-SAFE-003` | Arithmetic/counter overflow | No silent wrap; pre-start rejection or deterministic invariant. |
| `WP08-REG-001` | WP-02/WP-03 vectors | Exact unchanged. |
| `WP08-REG-002` | WP-04 generated artifact reproducibility | Green; canonical base config/hash unchanged. |
| `WP08-REG-003` | WP-05 historical fixtures | Bytes/digests immutable; verifier green. |
| `WP08-REG-004` | WP-06 current/historical wait | Historical immutable; new engine-version wait added separately. |
| `WP08-REG-005` | WP-07 movement golden | Existing historical fixture immutable; movement semantic tests green. |
| `WP08-REG-006` | Timeout/terminal precedence | No decision/action event after terminal boundary. |

### 13.3 Architecture/scope

| ID | Check | Pass |
|---|---|---|
| `WP08-ARCH-001` | Project graph | `Battle.Core → Battle.Contracts+BCL` only; Replay/Config/Runner/Unity dependencies forbidden. |
| `WP08-ARCH-002` | Forbidden APIs | No float/decimal/System.Random/Guid/hash code/wall-clock/I/O/Unity API in gameplay. |
| `WP08-ARCH-003` | Snapshot discipline | Selector receives immutable decision view, not mutable BattleState. |
| `WP08-ARCH-004` | Phase/version | Twelve phases and `tick-pipeline/1` unchanged; Engine exactly `0.3.0`. |
| `WP08-ARCH-005` | No hidden hardcode | No switch/branch on animal or concrete combat ActionId; only Stable-ID lookup/data tags. |
| `WP08-ARCH-006` | Scope | Phases 7–10 remain resolution no-op; UnityClient diff empty. |

### 13.4 Coverage

Required:

- `100%` branch coverage for selection precedence, interval boundaries, repeat cap and HardOpportunity tie rules;
- `100%` branch coverage for ordered availability first-rejection logic;
- `100%` branch coverage for DecisionMade semantic mode/RNG validation;
- existing WP-02/WP-03/WP-06/WP-07 critical gates stay `100%`;
- Battle.Core line coverage remains `>=85%`.

Snapshot/golden assertions do not replace semantic unit tests.

## 14. Blocking acceptance matrix

All rows required:

| Family | Required cases |
|---|---|
| Config | `WP08-CFG-001..008` |
| Catalog/availability | `WP08-CAT-001..003`, `WP08-AVL-001..009` |
| Weights | `WP08-WGT-001..013` |
| Selection/RNG | `WP08-SEL-001..012` |
| Variety/opportunity | `WP08-VAR-001..005`, `WP08-OPP-001..008` |
| Snapshot/commit/lifecycle | `WP08-SNP-001..003`, `WP08-CMT-001..008`, `WP08-LIFE-001..004` |
| Replay | `WP08-CON-001..012` |
| Golden/determinism | §12 exact oracle, `WP08-DET-001..007` |
| Safety/regression | `WP08-SAFE-001..003`, `WP08-REG-001..006` |
| Architecture/coverage | `WP08-ARCH-001..006`, §13.4 |

One failed blocking case leaves WP-08 not completed.

## 15. Execution gates after implementation

Required local commands, run from repository root. WP-08 implementation includes `verify-wp08-target-determinism.ps1`, `verify-wp08-coverage.ps1` and `coverage.wp08-replay.runsettings`. The new runsettings includes `[Battle.Replay]*,[Battle.Contracts]*`; the existing `coverage.runsettings` remains the Core/Contracts source for all earlier coverage gates.

```powershell
Set-Location CombatLab
dotnet restore --locked-mode
dotnet build CombatLab.sln --configuration Release --no-restore
dotnet test CombatLab.sln --configuration Release --no-build
./scripts/verify-wp04-generated.ps1 -Configuration Release
./scripts/verify-wp06-target-determinism.ps1 -Configuration Release
./scripts/verify-wp07-target-determinism.ps1 -Configuration Release
./scripts/verify-wp08-target-determinism.ps1 -Configuration Release
dotnet test CombatLab.sln --configuration Release --no-build --no-restore --filter "WorkPackage=WP08"
$wp08CoverageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("combatlab-wp08-coverage-" + [Guid]::NewGuid().ToString("N"))
$wp08CoreCoverage = Join-Path $wp08CoverageRoot "core"
$wp08ReplayCoverage = Join-Path $wp08CoverageRoot "replay"
New-Item -ItemType Directory -Path $wp08CoreCoverage,$wp08ReplayCoverage | Out-Null
dotnet test tests/Battle.Core.UnitTests/Battle.Core.UnitTests.csproj --configuration Release --no-build --no-restore --collect:"XPlat Code Coverage" --settings coverage.runsettings --results-directory $wp08CoreCoverage
dotnet test tests/Battle.ConformanceTests/Battle.ConformanceTests.csproj --configuration Release --no-build --no-restore --filter "WorkPackage=WP08" --collect:"XPlat Code Coverage" --settings coverage.wp08-replay.runsettings --results-directory $wp08ReplayCoverage
./scripts/verify-wp02-coverage.ps1 -ResultsDirectory $wp08CoreCoverage
./scripts/verify-wp03-coverage.ps1 -ResultsDirectory $wp08CoreCoverage
./scripts/verify-wp06-coverage.ps1 -ResultsDirectory $wp08CoreCoverage
./scripts/verify-wp07-coverage.ps1 -ResultsDirectory $wp08CoreCoverage
./scripts/verify-wp08-coverage.ps1 -CoreResultsDirectory $wp08CoreCoverage -ReplayResultsDirectory $wp08ReplayCoverage
Set-Location ..
git diff --check
git status --short -- UnityClient
```

All WP-08 tests must carry xUnit trait `WorkPackage=WP08`; the filtered run is the exact fixture/schema/semantic/integrity gate, while the unfiltered solution run remains the regression gate. `git status --short -- UnityClient` must print nothing.

Required CI:

- `windows-latest` and `ubuntu-latest`;
- Debug and Release;
- all four jobs green on final WP-08 code/docs head.

## 16. Completion gate and current execution record

WP-08 may become `COMPLETED` only when:

- `OPEN-WP08-01..17` approved/closed;
- every §14 case implemented and green;
- Release restore/build/test and all repository gates green;
- Windows/Linux Debug/Release matrix green;
- `battle.core/0.3.0` current fixtures added, historical fixture bytes unchanged;
- exact weighted fixture digests pinned and verifier green;
- standard/diagnostic canonical parity proven;
- docs updated from `APPROVED / NOT EXECUTED` to executed facts;
- `UnityClient` unchanged.

Current record:

- implementation coverage: all `107` unique blocking IDs have corresponding automated tests;
- local consolidated verification from `2026-08-19`: locked restore green; Release build `0` warnings / `0` errors; full solution `875` passed / `0` failed / `0` skipped; filtered `WorkPackage=WP08` `347` passed / `0` failed / `0` skipped;
- coverage execution: Core `522` passed and WP-08 conformance `184` passed; WP-02, WP-03, WP-06, WP-07 and WP-08 coverage gates green, including required `100%` critical branch targets and Battle.Core line coverage `>=85%`;
- WP-04 generated reproducibility and WP-06/WP-07/WP-08 target-determinism gates green for Release; current fixtures match `netstandard2.1`/`net10.0`, historical SHA pins remain unchanged;
- current WP-08 wait pins: recorded and enforced by the fixture test/target gate;
- GitHub Actions Windows/Linux Debug/Release: pending on final WP-08 head;
- `UnityClient`: unchanged.

До выполнения всех completion conditions Test Plan имеет статус `IMPLEMENTED / LOCAL VERIFIED; CI PENDING`, а WP-08 — `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`, но не `COMPLETED`.
