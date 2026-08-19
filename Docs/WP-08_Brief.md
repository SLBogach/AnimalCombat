# WP-08 Brief — Decisions

> Статус: `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`.
>
> Все `OPEN-WP08-01..17` закрыты и реализованы. В коде присутствуют тесты для всех `107` уникальных blocking ID; финальная GitHub Actions matrix ещё не выполнялась на текущем head, поэтому статус не `COMPLETED`.

## 1. Результат этапа

WP-08 должен заменить узкий выбор одного system action общим детерминированным decision pipeline:

- собрать полный catalog кандидатов бойца в ordinal `ActionId` order;
- применить фиксированный availability pipeline и сохранить первую причину отказа;
- вычислить fixed-point веса `Tactic → Situation → Synergy → Counter → Variety → Opportunity`;
- выполнить `OnlyLegalAction`, `HardOpportunity`, `ZeroWeightFallback` или ровно один unbiased Decision RNG draw;
- выбрать действия обоих бойцов из одного immutable decision view;
- атомарно зафиксировать action, costs, cooldown, timings, direction и target;
- emit корректные `DecisionMade`, `ActionCommitted`, cost/lifecycle и observable telegraph events;
- добавить DecisionTrace для diagnostic replay без изменения canonical event chain;
- сохранить поведение WP-06/WP-07, historical replay bytes и границу `Battle.Core → Battle.Contracts`.

WP-08 заканчивается выбором и commit. Attack/defense/grab intents, impact geometry, Resolution RNG, damage, stagger и forced movement остаются WP-09.

## 2. Нормативные источники и прочитанный scope

Использованы только связанные с WP-08 разделы оригинальных документов:

- Combat Design Specification v0.1:
  - §2 — `Decision point`, `Action`, `Commit`;
  - §4.2–4.3 — decision phase, общий snapshot и frozen commit values;
  - §7.2 — четыре tactics;
  - §9.1–9.5 — Action contract, availability, weights, knowledge, repeat/opportunity;
  - §10.5 — frozen startup/recovery через ActionSpeed;
  - §14.1–14.2 — `DecisionMade`, reason codes и RNG explainability;
  - §15 — zero-weight и commit edge cases;
  - приложение A — canonical selector pseudocode.
- Combat Lab Technical Design v0.1:
  - §8.1–8.3 — Decision stream и bounded PCG32 draw;
  - §12–12.2 — phase 5, snapshot discipline и atomic commit;
  - §13.1–13.2 — candidate pipeline и no hidden knowledge;
  - §15.1–15.2 — profile/draft contract;
  - §21.2–21.4 — determinism/CI/coverage;
  - §23.2 — `WP-08 Decisions: Availability, weights, opportunity, commit`;
  - приложения B/E — engine loop и source precedence.
- Combat Event & Replay Schema v0.1:
  - §§7–8.3 — envelope, frame, order и causality;
  - §9.2 — decision/action lifecycle catalog;
  - §§10.1–10.2 — `DecisionMade`, commit и phase rules;
  - §§11.1–11.3 — RNG provenance/consumption;
  - §§12.1–12.2 — standard explainability и diagnostic DecisionTrace;
  - §§19.3–19.4 и 21.2–21.4 — writer, collections и conformance.
- Canonical [combat.balance.v0.1.json](../CombatLab/config/generated/combat.balance.v0.1.json) — числа, tags и Stable IDs.
- [Implementation Status](./Implementation_Status.md), [Decisions](./Decisions.md), [Index](./Index.md), завершённые [WP-07 Brief](./WP-07_Brief.md) и [Combat Test Plan WP-07](./Combat_Test_Plan_WP-07_v0.1.md) — текущая implementation boundary.

Source order: CDS gameplay semantics → canonical balance DATA/Stable IDs → Replay Schema wire/integrity → TDD architecture → Test Plan WP-08 exact pass/fail. Уже написанный код не является нормативным источником.

## 3. Текущее состояние проекта

### 3.1 Реализованный production seam

- `Battle.Config` materializes и валидирует typed AI/action/tactic/passive/gear decision profiles, target/range inference, bounds и reachable overflow risks без runtime defaults.
- `Battle.Core.Decisions` содержит Stable-ID-ordered catalog, fixed first-rejection availability, шесть weight stages, selection precedence, repeat/opportunity state и unbiased Decision RNG provenance.
- `DecisionBatchSnapshot` является immutable общим phase-5 view обоих бойцов; selector и frozen A/B descriptors не читают mutable runtime state после snapshot.
- Atomic commit preflight исключает partial decision batch; costs, cooldowns, timers, direction и target применяются из frozen descriptors в утверждённом A→B event/submutation order.
- Generic non-System lifecycle выпускает `AttackPrepared` только при непустом hit schedule и не выполняет WP-09 resolution, damage или forced movement.
- Optional diagnostic sink публикует `DecisionTrace` и `decision.batch-snapshot/0.1` commitment вне canonical event chain.
- `Battle.Replay` связывает selection mode, weights, RNG nullability/index/result, chosen candidate, timings, target/direction, costs, telegraph и lifecycle; malformed/tampered package возвращает typed verification failure без exception.
- `ContractVersions.Engine = battle.core/0.3.0`; остальные wire/determinism versions сохранены.

### 3.2 Safety boundaries

- `InvalidSystemAction` (`$.actions[<action_id>]`) запрещает неизвестный дополнительный `sys_*` action.
- `DecisionTimingOverflowRisk` (`$.actions[<action_id>].hit_schedule`) запрещает reachable impact timing overflow до `journal.Begin`.
- Diagnostic checked catalog ограничен `256` кандидатами; legal decision set ограничен `128`. Это разные limits: diagnostics сохраняет полный checked catalog, Core выбирает только из bounded legal set.
- Weight stages, legal-weight sum, action timers, repeat/opportunity/decision counters и absolute impact ticks используют checked arithmetic и typed failures вместо wraparound.
- `UnityClient`, historical replay bytes и canonical generated balance artifacts не изменены.

## 4. Scope

### 4.1 Входит

- typed action/tactic decision profiles, materialized до `journal.Begin`;
- catalog: system actions, animal Basic actions и ровно два выбранных Specials;
- mode/loadout/owner/state/cooldown/cost/target/range/headroom/telegraph/repeat predicates;
- первая rejection reason в fixed order;
- fixed-point modifier stages и final clamp;
- Decision RNG selection и exact provenance;
- repeat history, opportunity debt и HardOpportunity;
- общий phase-5 decision batch snapshot;
- atomic commit, costs, cooldown, frozen timings/direction/target;
- generic combat action phase lifecycle и `AttackPrepared` marker;
- standard `DecisionMade` explainability и diagnostic DecisionTrace;
- replay semantic/tamper validation;
- WP-08 golden fixture, determinism, regression, safety, architecture и coverage gates.

### 4.2 Не входит

- strike/defense/counter/grab/forced-move intent creation, sorting или resolution — WP-09;
- Block/Dodge chances, Resolution RNG, hit/miss, damage, stagger, wall impact, throw, double KO — WP-09;
- effect trigger/stack/expiry execution и утверждённый WP-07 stat-clamp defer — WP-10;
- полный набор fighter-specific wall/state/effect/passive predicates, resource gains и passive reactions — WP-11;
- periodic Energy/unique-resource generation; WP-08 применяет только commit costs и cooldown decrement;
- Unity playback/runtime, batch, deployment и production rollout;
- изменение `UnityClient`.

## 5. Current DATA snapshot

Canonical config остаётся `combat.balance/0.1`, config hash:

`sha256:0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`.

Catalog содержит `24` actions, `4` tactics и `3` fighters.

| Setting | v0.1 |
|---|---:|
| `global.sim.fp_scale` | `1000` |
| `global.sim.multiplier_min` | `250` |
| `global.sim.multiplier_max` | `3000` |
| `global.sim.decision_weight_max` | `100000000` |
| `global.ai.repeat_same_action_fp` | `550` |
| `global.ai.repeat_same_category_fp` | `800` |
| `global.ai.opportunity_growth_fp` | `250` |
| `global.ai.opportunity_cap_fp` | `2500` |
| `global.ai.hard_opportunity_misses` | `4` |
| `global.ai.default_perception_delay_ticks` | `5` |

Action rows уже содержат `base_weight`, costs, cooldown, phase bounds, hit/preferred range, tags/category, `max_consecutive_uses`, `hard_opportunity_misses`, `opportunity_cap_fp`, `movement_mode`, `track_target` и hit schedule. Tactic rows содержат action-tag multipliers, contextual multipliers, `repeat_penalty_fp` и `perception_delay_ticks`.

DATA gap: CDS §9.1 называет `TargetSelector` обязательным Action field, но canonical v0.1 не содержит отдельного `target_selector`. Предлагаемая v0.1 compatibility policy зафиксирована в `OPEN-WP08-04`; скрытые branches по конкретному ActionId запрещены.

## 6. Утверждённые решения OPEN-WP08

| OPEN | Статус | Точное утверждённое решение |
|---|---|---|
| `OPEN-WP08-01` | `CLOSED` | Этот Brief задаёт утверждённый scope, а [Combat Test Plan WP-08](./Combat_Test_Plan_WP-08_v0.1.md) является blocking exact pass/fail matrix. После реализации всех `107` unique IDs WP-08 имеет статус `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`. |
| `OPEN-WP08-02` | `CLOSED` | Checked catalog строится в ordinal `ActionId` order: все три System actions; все Basic actor animal; все Special actor animal. Mode/loadout являются predicates, поэтому rejected entries остаются в diagnostic trace. Production legal set содержит один WP-07 system candidate, allowlisted Basics и ровно два выбранных allowlisted Specials, прошедших predicates. Mode/config collections canonical-sort; порядок двух `FighterBuildSnapshot.SpecialActionIds` остаётся canonical input и не обещает одинаковый input digest при перестановке. |
| `OPEN-WP08-03` | `CLOSED` | Phase 5 начинает работу с одним immutable `DecisionBatchSnapshot`, снятым после phases 2–4 и до первого draw/commit. Оба actor contexts входят в него одновременно; selector не читает mutable `BattleState`. Это не новая tick phase и не меняет `tick-pipeline/1`. Phase-1 pre-tick snapshot и observer trace сохраняются. |
| `OPEN-WP08-04` | `CLOSED` | В `combat.balance/0.1` target выводится без ActionId branches: System → Opponent; non-system с `hit_count>0` или non-empty `hit_schedule` → Opponent; иначе Self. Opponent inference допускает `None/Approach/Follow/Push/Pull/Swap`, Self — `None/Approach/Retreat/Adaptive`; любая иная inferred-target/movement pair отклоняется как `AmbiguousTargetProfile` до старта. Opponent stationary/Push/Pull/Swap проверяет inclusive hit range; Opponent Approach/Follow — inclusive preferred start range; Self Approach/Retreat/Adaptive проверяет direction/headroom, Self None legal at any gap. Impact range повторно проверит WP-09. `CurrentGrabTarget` отложен до WP-09; explicit DATA target field пересматривается в WP-11. |
| `OPEN-WP08-05` | `CLOSED` | Predicate order: actor state/category → mode → owner/slot/loadout → cooldown → Energy → unique resource → target existence/Defeated → decision/system range → movement headroom → observed telegraph/perception → MaxConsecutiveUses. Сохраняется только первый stable rejection code; отвергнутый WP-07 system profile использует `SystemBandUnavailable` либо `NoMovementHeadroom`. Target-state/wall/effect/grab-control predicates без generic DATA остаются typed extension seams WP-09/WP-11, без action-specific hardcode в WP-08. |
| `OPEN-WP08-06` | `CLOSED` | Weight считается checked sequential `FixedMath.Mul` в порядке `Base × Tactic × Situation × Synergy × Counter × Variety × Opportunity`; floor выполняется после каждого stage. Stage multiplier clamp: `[multiplier_min,multiplier_max]`; final clamp: `[0,decision_weight_max]`. Sum считается checked `Int64`, обязан помещаться в `Int32` до RNG. Любой риск, достижимый из valid request/config, отклоняется до `BattleStarted`; `DecisionArithmeticOverflow` runtime invariant остаётся только guard/test для повреждённого internal calculator input, silent wrap запрещён. |
| `OPEN-WP08-07` | `CLOSED` | Tactic stage использует matching action tags в фиксированном порядке `approach, block, dodge, grab, heavy, light, resource_generator, resource_spender, retreat, signature`; `resource_generator` означает exact `*_generator` или `rhythm`, `resource_spender` — positive `resource_cost`. `counter_fp` принадлежит Counter stage; `low_hpfp`, `self_wall_fp`, `target_recovery_fp`, `target_wall_fp` — Situation; `repeat_penalty_fp` — Variety. Multiple matches fold от `fp_scale` в указанном порядке. Exact source key `low_hpfp` не переименовывается. |
| `OPEN-WP08-08` | `CLOSED` | Situation применяет только DATA-backed contexts: target recovery; target/self wall zone для matching position/escape tags; animal low-HP threshold, если такой key существует. Preferred/hit ranges в WP-08 влияют на availability; отдельная несуществующая distance multiplier не выдумывается. Synergy использует tag intersection с selected passive `weight_multiplier_fp` и gear `normalized_value` в Offense→Defense→Utility order; текущие gear values `1000` нейтральны. |
| `OPEN-WP08-09` | `CLOSED` | Counter multiplier применяется только к candidate с exact `counter` tag, когда opponent уже committed observable telegraph и elapsed ticks ≥ selected tactic `perception_delay_ticks`. Required tactic value имеет приоритет; global default не является runtime fallback. Opponent uncommitted candidate, future action и direct AnimalId bonus недоступны. |
| `OPEN-WP08-10` | `CLOSED` | Selection precedence: one legal → `OnlyLegalAction`; затем emergency suppresses hard override; затем HardOpportunity; затем sum=0 system fallback; иначе `WeightedRng`. При двух и более legal candidates и positive sum выполняется draw даже если positive weight только у одного. Candidates/cumulative intervals — ordinal ActionId; interval `[prefix,prefix+weight)`. Decision draws/emission остаются A→B; отсутствие draw A не резервирует индекс. |
| `OPEN-WP08-11` | `CLOSED` | Variety хранит только immediate consecutive ActionId/category history, потому что DATA не задаёт window. Same action: `repeat_same_action_fp`; same category: `repeat_same_category_fp`; если применён любой repeat — ещё `tactic.repeat_penalty_fp`, в этом порядке. History обновляется на commit и не откатывается при interruption. MaxConsecutive блокирует action только если после остальных predicates существует хотя бы один legal candidate не на своём cap; all-at-cap не создаёт empty set. |
| `OPEN-WP08-12` | `CLOSED` | Opportunity debt ведётся per Special ActionId. Legal-but-not-selected increment; nonlegal unchanged; selected commit resets even if later interrupted. `multiplier=min(action_cap,global_cap,fp_scale+debt×growth)`. `hard_opportunity_misses=0` отключает hard override; positive action value capped global threshold. Hard действует при накопленных prior misses `>= threshold` и может выбрать legal Special с final weight `0`: hard override предшествует zero-sum fallback. Multiple hard: debt DESC, final weight DESC, ActionId ASC, no RNG. Emergency is a typed input seam; WP-09 supplies threat detection, WP-08 proves that its presence suppresses hard override but не вводит новый selection mode. |
| `OPEN-WP08-13` | `CLOSED` | Commit descriptors обоих actors полностью создаются до mutation; между descriptor freeze и концом batch нет rule evaluation. Exact batch events и previewed Decision RNG state проходят event-cap preflight до первого phase-5 emit/mutation; недостаточная capacity оставляет gameplay/RNG/history неизменными и ведёт к reserved terminal invalid event. Canonical projection применяет authoritative submutations A→B: Decisions A/B, ActionCommitted A/B, затем cost events A energy/resource, B energy/resource, затем AttackPrepared A/B. Каждый event снимает frames из своей submutation; поэтому target frame commit B может уже содержать public committed state A, но никогда не меняет frozen B descriptor. Costs списываются один раз, cooldown ставится на commit и впервые декрементируется в phase 3 следующего tick. Combat startup/recovery freeze использует CDS §10.5; Active/HitSchedule не ускоряются; `FixedTiming` использует identity. Periodic resource gains остаются WP-11. |
| `OPEN-WP08-14` | `CLOSED` | Opponent commit target/direction freeze по decision snapshot; Self имеет null target/target frame/target position, direction вычисляется только для movement profile. `AttackPrepared` emit для non-empty hit schedule: telegraph tick=commit tick, impact ticks=`commit+startup+schedule`, direction locked, source=`ActionCommitted`. Generic combat lifecycle использует actor-only `ActionPhaseChanged`, exact reasons `StartupCompleted`/`ActiveCompleted`/`RecoveryCompleted` и lifecycle-anchor chain из §8; WP-07 movement chain/reasons не меняются. Phases 7–10 не создают combat intents и не меняют HP/position. |
| `OPEN-WP08-15` | `CLOSED` | Existing canonical event/replay shape достаточна. Constructors и semantic validator усиливаются: sorted legal IDs, count/list equality, chosen membership, mode/weight/RNG rules, `Decision/NextInt`, range `0..weight_sum`, raw/result/normalized checks. Legal diagnostic candidate несёт ровно шесть folded stage traces (`Tactic..Opportunity`), illegal — none, поэтому schema cap `16` не нарушается. Diagnostic-only typed sink публикует DecisionTrace и общий `decision.batch-snapshot/0.1` digest из §9; Standard remains `diagnostics=null`; canonical events/input/final digest совпадают. |
| `OPEN-WP08-16` | `CLOSED` | Behavior bump: `battle.core/0.2.0 → battle.core/0.3.0`. `combat.event/0.1`, `combat.replay/0.1`, `combat.balance/0.1`, `pcg32/1`, `tick-pipeline/1` сохраняются. Historical `0.1.0/0.2.0` fixtures immutable; current wait `0.3.0` и weighted `decision_weighted_l1` создаются отдельными versioned artifacts. |
| `OPEN-WP08-17` | `CLOSED` | WP-08 не вводит action-specific switch по Bear/Kangaroo/Gorilla. Generic target/range/tag behavior входит сейчас; resolution prerequisites — WP-09, effects/stat clamp — WP-10, полный fighter-kit availability/resource/passive semantics и explicit target DATA review — WP-11. |

## 7. Canonical decision algorithms

### 7.1 Candidate/read model

```text
phase5_view = capture_both_after_phase4_before_draw_or_commit()

for actor in [fighter_a, fighter_b]:
    checked = system_actions
            + actor_animal_basic_actions
            + actor_animal_special_actions
    checked = checked.sorted_by(ActionId ordinal)
    scores[actor] = evaluate_all(checked, phase5_view)

select A with Decision stream if required
select B with the next Decision index if required
freeze both commit descriptors
preflight exact batch capacity and commit previewed Decision RNG state
emit DecisionMade A/B
apply/emit commits A/B and derived commit events
```

`DecisionBatchSnapshot` содержит public frames обоих бойцов, actor cooldowns, energy/resource, action/category history, opportunity counters, selected build/tactic refs и observable committed-action timestamps. Он не содержит uncommitted candidate другого actor. Frozen B descriptor and both `DecisionMade` frame pairs come from this view; later authoritative `ActionCommitted B` envelope frames follow the ordered event-submutation rule below and are not descriptor inputs.

### 7.2 Availability

Surface gap используется из WP-07 body-aware geometry. Bounds inclusive.

MaxConsecutive — двухпроходное правило:

1. вычислить base legal set без repeat cap;
2. пометить кандидаты на cap;
3. reject capped candidate только если существует хотя бы один base-legal non-capped alternative;
4. если все base-legal candidates capped, cap никого не удаляет, а Variety penalties продолжают действовать.

System availability сохраняет строгую WP-07 truth table: ниже neutral band при положительном outward headroom legal только Retreat, ниже band при нулевом headroom legal только Wait, внутри inclusive band legal только Wait, выше band legal только Approach. Mode exclusion не «ремонтируется» другим system action: если обязательный кандидат запрещён mode, Core получает no-legal typed invariant/rejection. Поэтому при согласованном mode в production legal set присутствует ровно один system candidate, который конкурирует с combat actions.

### 7.3 Weight

```text
weight = clamp(base_weight, 0, decision_weight_max)
for stage in [Tactic, Situation, Synergy, Counter, Variety, Opportunity]:
    m = clamp(stage_multiplier, multiplier_min, multiplier_max)
    weight = FixedMath.Mul(weight, m, fp_scale)
weight = clamp(weight, 0, decision_weight_max)
```

Reference floor vector:

```text
base = 1000
stages = [1250, 800, 1100, 900, 550, 1500]
result = 816
```

Each `CandidateScore` immutable: ActionId, legal, first rejection, base, ordered modifiers, final weight, opportunity/repeat facts. For a legal candidate, diagnostic modifiers are exactly six folded/clamped stage multipliers in order `Tactic, Situation, Synergy, Counter, Variety, Opportunity`, including identity `fp_scale`; atomic tag/context/repeat factors remain calculator test inputs, not additional wire items. An illegal candidate has no modifiers, `final_weight=0` and никогда не участвует в sum/draw.

### 7.4 Selection

```text
legal = scores.where(Legal).sorted_by(ActionId)
require legal.count >= 1

if legal.count == 1:
    OnlyLegalAction, no draw
elif no emergency and any HardOpportunity-ready:
    choose debt DESC, weight DESC, ActionId ASC; no draw
elif sum(legal.FinalWeight) == 0:
    choose first legal system action by approach > retreat > wait; no draw
else:
    rng = Decision.NextInt(0, sum, NextInt)
    chosen = first candidate with cumulative_end > rng.result
```

Zero-weight candidates remain in `legal_action_ids` and `candidate_count`, but occupy empty cumulative intervals.

### 7.5 Repeat and opportunity

Opportunity debt increments after both selections are frozen and before the next decision point. It cannot influence the second actor through mutation because each actor owns its own counters and both scores came from one immutable view.

An unavailable Special neither gains nor loses debt. `OnlyLegalAction`, system fallback and ordinary weighted selections still update every fully legal Special. Selected Special resets on commit.

## 8. Commit, lifecycle and events

Combat timing:

```text
speed_multiplier = clamp(
    fp_scale + (derived_action_speed - speed_baseline) * speed_slope,
    speed_min,
    speed_max)

startup = clamp(FixedMath.Div(startup_base, speed_multiplier, fp_scale), startup_min, startup_max)
recovery = clamp(FixedMath.Div(recovery_base, speed_multiplier, fp_scale), recovery_min, recovery_max)
active = configured active_ticks
```

Commit event projection:

- `DecisionMade` uses phase-5 immutable frames before mutation;
- both commit descriptors are frozen first; no availability, weight, target or timing rule is evaluated again inside the batch;
- selection runs against a preview copy of Decision RNG; exact Decision/Commit/cost/telegraph event count must fit before the preview state is committed and before the first phase-5 event/gameplay/history mutation;
- insufficient capacity raises the stable event-limit invariant and uses the already-reserved `BattleEnded` slot; no partial decision/commit batch is visible;
- `ActionCommitted` mutates action/state/phase/timer/cooldown, but cost scales remain unchanged in its frame;
- commit submutations/events run A then B; each before/after pair comes from that authoritative submutation, so commit B may show A's already committed public state as its unchanged target frame;
- following `ResourceChanged` applies Energy, then unique resource, actor A then B;
- `AttackPrepared` is a marker with equal before/after frames;
- all post-commit events point back through `source_event_id` to `ActionCommitted`, which points to `DecisionMade`;
- `resolution_group_id=null` and no Resolution RNG in WP-08.

Generic combat lifecycle uses phase 4 and InitiativeOrder for simultaneous transitions. It may reach `Startup → Active → Recovery → null`, but Active does not emit hit/defense/grab intents before WP-09.

Exact generic combat `ActionPhaseChanged` projection:

- actor is the performer; target ID and target before/after frames are null, while the committed target remains recoverable through the source chain;
- actor before/after frames, `action_id` and `decision_id` are present and preserve the same committed action;
- RNG and `resolution_group_id` are null; `related_event_ids` contains exactly the direct `source_event_id`;
- Startup→Active uses reason `StartupCompleted` and source=`ActionCommitted`;
- Active→Recovery uses reason `ActiveCompleted` and source=the latest prior generic lifecycle event, or `ActionCommitted` when the action entered Active directly;
- Recovery→null uses reason `RecoveryCompleted` and source=the Active→Recovery event;
- if configured Recovery is zero, Active transitions directly to null with `ActiveCompleted`; no zero-timer Recovery frame is created.

This chain is only for non-System combat actions. Existing WP-07 movement keeps `MovementCompleted` and `MoveEnded` as its Active→Recovery source; WP-08 must not rewrite that oracle.

## 9. Replay and diagnostics

`DecisionMade` standard payload:

- legal IDs sorted ordinal, unique;
- `candidate_count == legal_action_ids.Count`;
- chosen ID belongs to legal list;
- weights are post-clamp integers;
- `rng` only for `WeightedRng` and exactly `Decision/NextInt/[0,weight_sum)`;
- dominant modifiers: non-identity modifiers chosen action, `abs(multiplier-fp_scale)` DESC, stage order tie, maximum eight;
- reason code order: selection mode first, then unique dominant reasons.

Diagnostic DecisionTrace:

- exactly one trace per `DecisionMade` in DiagnosticReplay;
- sequence/tick/decision/actor match the canonical event;
- all checked candidates sorted ActionId, including unavailable;
- fixed first rejection, base, exactly six folded stage modifiers for legal candidates (none for illegal) and final weight;
- both decisions from one phase-5 batch use the same snapshot digest. It is SHA-256 over ASCII `decision.batch-snapshot/0.1`, one terminating NUL byte, then canonical JSON of: battle ID, engine version, master seed, config hash; complete ModeRules ID/version/normalization and five sorted allowlists; tick; InitiativeOrder; Decision next index; and both fighters in A/B order with public frame, selected build refs, positive cooldowns sorted by ActionId, immediate action/category history and streaks, selected-Special opportunity debts sorted by ActionId, observable committed telegraph/tick, and emergency flag;
- `Battle.Core` supplies that typed immutable projection through the diagnostic port; `Battle.Replay` owns canonical JSON/hash. The standalone replay verifier checks digest shape and same-batch equality, while config-aware producer conformance recomputes it exactly;
- overlay is outside input/event/final digest and cannot alter selection or summary.

## 10. Reference scenario `decision_weighted_l1`

Approved golden uses generated canonical config with only three explicit setting overrides: `global.arena.start_position_a=4000`, `global.arena.start_position_b=5540`, `battle.time_limit_ticks=1`. All other settings, including event/watchdog caps, remain canonical. Exact request/profile:

- IDs/profile: battle=`battle-wp08-decision-weighted-l1`, replay=`replay-wp08-decision-weighted-l1`, engine=`battle.core/0.3.0`, journal=`StandardReplay`, `master_seed=0`;
- ModeRules: id=`decision_weighted_l1_v01`, version=`mode.rules/0.1`, normalization=`None`;
- mode allowlists: animals=`[bear]`; actions=`[bear_earthbreaker,bear_fury_maul,bear_paw_jab,sys_retreat,sys_wait]`; passives=`[bear_thick_hide]`; gear=`[gear_defense_reinforced_hide,gear_offense_power_wraps,gear_utility_sprint_soles]`; tactics=`[tactic_pressure]` (each list canonicalized ordinal);
- build A/B: `fighter_a/A` and `fighter_b/B`, animal=`bear`, build ID null, Specials in build order `[bear_earthbreaker,bear_fury_maul]`, passive=`bear_thick_hide`, gear slots Offense/Defense/Utility=`gear_offense_power_wraps`/`gear_defense_reinforced_hide`/`gear_utility_sprint_soles`, tactic=`tactic_pressure`;
- centers `4000/5540` give body-aware surface gap `500`; both outward directions have headroom; equal derived Initiative `85` uses existing `StatThenSeededHash`, and seed `0` yields order `[fighter_a,fighter_b]` (scores `7606814898323161814` / `13543846652233914625`);
- both Specials fail unique-resource predicate at the initial resource `0`; `sys_approach` fails mode, `sys_wait` fails system band, so only `bear_paw_jab` and `sys_retreat` are legal;
- weights: `bear_paw_jab=1150`, `sys_retreat=337`, sum `1487`.

Expected Decision draws:

| Actor | RNG index | raw_u32 | result | normalized_fp | Chosen |
|---|---:|---:|---:|---:|---|
| A | `0` | `2879411843` | `1400` | `941` | `sys_retreat` |
| B | `1` | `495049527` | `461` | `310` | `bear_paw_jab` |

Expected canonical semantic order:

```text
BattleStarted
DecisionMade A (WeightedRng index 0)
DecisionMade B (WeightedRng index 1)
ActionCommitted A (sys_retreat)
ActionCommitted B (bear_paw_jab)
AttackPrepared B (impact tick 3)
TimeoutReached
DrawDeclared(TimeoutEqualHealthFraction)
BattleEnded
```

Pinned weighted fixture:

- fixture config: `sha256:26c53cf464539e2ebf1eb37f90d73715adb0842e29e6b7a9eeaede8336d49227`;
- input: `sha256:eaee293a90e5fc432ab1822965b3f632abc803bd79b23ae401a8fc9fd8a2b021`;
- final: `sha256:6ed4f34aa845096ee63d125d306fbef64ff469773e14389bfe1152146a007f3f`;
- file SHA-256: `1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0`;
- canonical events: `9`.

Pinned current `wait_equal_l1` fixture для `battle.core/0.3.0`:

- fixture config: `sha256:f7524a127ca0ec085562d1ca43fc91d384b7f713f1ddb323be53bc701f6d0dc3`;
- input: `sha256:4155833aa33fd60fee5f034dc8f4050afb957682af5141701d6dca463bbc7a08`;
- final: `sha256:bcc34972a33aadd5da02f3c5d3996ecd76c0037fbfe5e94e25cdf883ca9177f9`;
- file SHA-256: `8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd`;
- canonical events: `8`.

Historical `0.1.0`/`0.2.0` wait и `approach_band_l3` bytes не перезаписываются.

## 11. Реализованные изменения

1. Contracts/typed profiles:
   - усилены invariants `DecisionMadePayload`/`RngProvenance`;
   - добавлены diagnostic trace DTO и optional sink contract;
   - canonical event shape не изменён.
2. Config/setup:
   - materialized/validated AI settings, action/tactic/passive/gear decision profiles;
   - build refs и derived ActionSpeed сохраняются;
   - добавлены typed bounds, schedule, tag, system-action, candidate-count и overflow checks без defaults.
3. Core decision model:
   - реализованы `DecisionBatchSnapshot`, catalog, ordered availability и candidate scoring;
   - реализованы stage calculators, repeat/opportunity state и selector;
   - Decision RNG consumption/provenance соответствует утверждённой precedence.
4. Commit/lifecycle:
   - реализован frozen generic action descriptor;
   - A/B commits, costs/cooldowns, phase transitions и `AttackPrepared` выполняются атомарно;
   - WP-07 movement lifecycle и strict system truth table сохранены.
5. Replay:
   - усилена decision/lifecycle semantic validation и tamper rejection;
   - реализованы diagnostic overlay writer и Standard/Diagnostic canonical parity.
6. Acceptance:
   - добавлены unit/conformance/integration/determinism/safety/architecture/coverage tests для всех `107` уникальных blocking ID;
   - Engine повышен до `0.3.0`, historical fixtures сохранены, weighted golden и current wait добавлены отдельными pinned artifacts;
   - repository scripts/runsettings включают WP-08 target determinism и coverage gates.

Затронутые production areas:

- `CombatLab/src/Battle.Contracts/Events`, `Replay`, `Ports`;
- `CombatLab/src/Battle.Core/Decisions`, `Engine`, `Initialization`;
- `CombatLab/src/Battle.Config/Semantic`;
- `CombatLab/src/Battle.Replay/Journal`, `Verification`;
- соответствующие test projects, scripts и новые versioned fixtures.

`UnityClient`, WP-09 resolution files и existing historical fixture bytes не изменены.

## 12. Статус реализации

WP-08 имеет статус `LOCAL IMPLEMENTATION COMPLETE / CI PENDING`:

- owner approval `OPEN-WP08-01..17` и blocking decision gate сохранены;
- production implementation и тестовые методы для всех `107` unique blocking IDs присутствуют;
- `battle.core/0.3.0`, diagnostic/replay hardening и новые versioned fixtures добавлены;
- historical replay bytes, generated balance artifacts и `UnityClient` не изменены;
- local verification от `2026-08-19` green: locked restore; Release build `0` warnings/errors; full solution `875/875`; filtered `WorkPackage=WP08` `347/347`; generated, target-determinism, historical replay и все coverage gates green;
- GitHub Actions `windows-latest`/`ubuntu-latest` × Debug/Release для финального WP-08 head ещё не запускалась.

Статус `COMPLETED` запрещён до фактического green CI matrix. Следующий шаг — отправить ветку и подтвердить все четыре CI jobs.
