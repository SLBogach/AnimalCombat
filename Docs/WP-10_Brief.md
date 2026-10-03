# WP-10 Brief — Effects

> Статус: `PREPARED / AWAITING APPROVAL`.
>
> Подготовлено `2026-10-03` на baseline `81b1488` (локальный `master`: WP-09 completed + Unity Replay Viewer checkpoint).
> Решения `OPEN-WP10-01..28` имеют статус `PROPOSED`, ещё не `CLOSED`.
> [Combat Test Plan WP-10 v0.1](./Combat_Test_Plan_WP-10_v0.1.md) — предложенная обязательная blocking matrix; execution не начинался.

## 1. Результат и границы

WP-10 из TDD §23.2: `Triggers, stacking, expiry, anti-loop`; acceptance: cycle/cap/control suites.

Этап подключает к существующей симуляции typed effect profiles, детерминированные правила активации, пять stacking policies, точные expiration boundaries, derived-stat recomputation и общие control protections. События `EffectAdded/Removed` становятся результатом реальных authoritative mutations.

Входит:

- DATA/schema migration для stat clamp и trigger/control metadata;
- pre-start materialization и validation полного reachable effect graph;
- ApplyEffect/RemoveEffect, duration, refresh/replace/strongest/stacks;
- очередь triggers, causal lineage, cooldown/activation budgets, cycle guards;
- effect modifiers к runtime stats, damage/defense/decision channels;
- GuardBreak effect, control fatigue/immunity, grab lockout, knockdown/wakeup;
- Armored/Unstoppable и сохранение current-group UninterruptibleImpact;
- атомарность effect closure вместе с исходной resolution group;
- replay semantics, historical artifacts, deterministic golden scenarios и CI gates.

WP-11 сохраняет полный запуск fighter passives, Rage/Tempo/Grip gain/decay/tiers, resource-specific conditions, signature/finisher/punish kit bonuses и explicit target DATA review. WP-10 не включает consumables, healing/lifesteal, periodic damage, summons, normalized-rating rules, batch/deployment и Unity authoring. Сам факт merge Viewer не означает `WP-UI-01 COMPLETED`: его отдельные acceptance gates сохраняются.

## 2. Прочитанные нормативные источники

Прочитаны только связанные разделы оригинальных документов, включая их таблицы:

- CDS: §§4.2–4.3 (expiry/snapshot), §§6.1–6.2 (stats и bounds), §§8.1–8.2 (control/interrupt profiles), §9.6 (primitives), §§10.2–10.6 (effect consumers), §12 полностью (stacking/anti-loop), §15 (effect expiry/defeated edges), §§17.1–17.2 (DATA ownership), приложение B.
- TDD: §7.1 (signed floor/overflow), §§12–12.2 (12 phases/snapshots), §14 полностью (EffectIntent, triggers/caps), §§21.1–21.4 (tests/coverage), §§23.1–23.2 (gates/WP-10), приложение B.
- Replay Schema: §§7–8 (frames/order/causality), §9.5 и §10.4 (effect/control/resource payloads), §21.2 (semantic verification); machine schemas `CombatLab/schemas/replay/v0.1` и существующие typed contracts.
- [Implementation Status](./Implementation_Status.md), [Decisions](./Decisions.md), [Index](./Index.md), завершённые [WP-09 Brief](./WP-09_Brief.md) и [Test Plan WP-09](./Combat_Test_Plan_WP-09_v0.1.md).
- `CombatLab/config/generated/combat.balance.v0.1.json`, JSON Map, manifest, `BalanceV01Schema`, exporter/compiler/materializers и текущие runtime/replay seams.

Source precedence: CDS gameplay → Workbook/compiled DATA numbers и IDs → Replay wire semantics → TDD architecture → утверждённый Test Plan. Ни существующий код, ни предлагаемые числа ниже не заменяют owner approval.

## 3. Baseline и найденные gaps

Локальная ветка подготовки: `feature/wp-10-effects`, исходный commit `81b1488`. В ней есть completed WP-09 и Viewer checkpoint. Проверка GitHub `2026-10-03` показала `origin/master=e610c76`; локальный master опережал remote на один commit. Синхронизация remote — отдельное Git-действие, не DATA/code blocker подготовки.

Исходный Engine: `battle.core/0.4.0`; balance `combat.balance/0.1`; replay/event `0.1`; RNG `pcg32/1`; ordering `tick-pipeline/1`; mode `mode.rules/0.1` с `NormalizationMode.None`.

Уже есть:

- deterministic initialization/decisions/movement/resolution и atomic ResolutionPlan;
- EffectFrame и EffectAdded/Removed payloads, пятизначный EffectStackPolicy;
- десять effect descriptions, шесть passive profiles, девять gear profiles;
- fatigue/lockout/immunity global settings и lookup `1000|750|500|250`;
- ProgressStamp с effect projection, event-cap preflight, historic fixtures и target probe.

Недостаёт:

- production effect runtime и expiry/trigger/stack operations;
- `stat.<id>.min/max` — обязательство `OPEN-WP07-13`;
- queue depth/size и runtime effect-instance limits;
- explicit effect priorities, refresh semantics, semantic roles и bindings;
- DATA для knockdown stages, incoming interrupt strength и profile filters;
- immutable dynamic stat/channel snapshots и consumer wiring;
- effect/control replay semantic verifier и WP-10 matrix/CI inventory.

Существующий `ModifierPipeline` выполняет последовательные Add/Multiply/Override; смешанные операции могут расходиться с CDS §6.2. WP-10 вводит формулу §5.2 с явным version bump. Current pipeline также допускает неизвестный stat через `continue`; reachable WP-10 targets обязаны отклоняться pre-start, а не пропускаться.

## 4. Версии и DATA migration — предложенное решение

### 4.1 Version contract

- новый Engine: `battle.core/0.5.0`;
- новый баланс: schema `combat.balance/0.2`, config `v0.2`;
- event/replay/rejection/presentation остаются `0.1`, RNG/ordering/mode versions сохраняются;
- `.0.5` принимает только explicit `.0.2` config; `.0.1` не дополняется runtime defaults;
- config loader/compiler продолжают читать `.0.1` для historical artifacts/tools;
- replay verifier читает `0.1.x–0.5.x` по version-specific semantic policies; старый replay не пересимулируется новым Engine для проверки SHA.

Причина миграции: обязательные bounds и новый catalog меняют строгий balance contract. Предлагается создать отдельные `config/source/Combat_Balance_Workbook_v0.2.xlsx`, `config/generated/combat.balance.v0.2.{json,map.csv,validation.json,manifest.json}` и `schemas/balance/v0.2/combat.balance.schema.json`. Source v0.1, generated v0.1 и historical fixtures сохраняются byte-for-byte. Новый XLSX строится из копии v0.1 с добавленными DATA/JSON Map rows; generated файлы создаёт exporter, ручное изменение запрещено.

### 4.2 Новые settings

Все числа — предложения для стартового DATA review, не уже утверждённый баланс.

| Key | Предложение | Unit | Validation |
|---|---:|---|---|
| `global.control.max_trigger_depth` | 8 | causal levels | `1..32` |
| `global.control.max_triggers_per_tick` | 128 | admitted nodes/tick | `1..4096` |
| `global.control.max_effect_instances_per_fighter` | 32 | occupied stack groups | `1..128`, structural wire cap `128` |
| `global.control.knockdown_fall_ticks` | 2 | ticks | `1..100` |
| `global.control.knockdown_grounded_ticks` | 6 | ticks | `1..100` |
| `global.control.knockdown_getup_ticks` | 3 | ticks | `1..100` |

Existing authoritative values сохраняются: fatigue threshold `3`, decay `100`, immunity `25`, grab lockout `20`, wakeup immunity `25`, max hold `12`, stun bounds `3..20`, FP scale `1000`. Дублированные global/effect durations обязаны совпадать, иначе validation error; ни одно поле не является fallback другого.

### 4.3 Stat bounds

Для каждого строки добавить оба обязательных settings `stat.<id>.min` и `stat.<id>.max` в Global Config и JSON Map.

| Runtime stat | DATA id | Min | Max | Unit |
|---|---|---:|---:|---|
| MaxHealth | `max_health` | 1 | 10000 | health |
| MaxEnergy | `max_energy` | 0 | 10000 | energy |
| EnergyRegen | `energy_regen` | 0 | 1000 | energy/tick |
| Power | `power` | 0 | 1000 | rating |
| Armor | `armor` | 0 | 2000 | rating |
| Precision | `precision` | 0 | 1000 | rating |
| Evasion | `evasion` | 0 | 1000 | rating |
| Guard | `guard` | 0 | 1000 | rating |
| GuardBreak | `guard_break` | 0 | 1000 | rating |
| MoveSpeed | `move_speed` | 1 | 500 | distance/tick |
| ActionSpeed | `action_speed` | 1 | 500 | rating |
| Initiative | `initiative` | 0 | 1000 | rating |
| ControlPower | `control_power` | 0 | 1000 | rating |
| ControlResistance | `control_resistance` | 0 | 1000 | rating |
| Mass | `mass` | 1 | 2000 | mass rating |

CollisionRadius/StaggerThreshold/UniqueResourceMax остаются immutable validated structural/fighter DATA в WP-10. Runtime effects на MaxHealth/MaxEnergy запрещены: изменение maxima/current values требует отдельного решения, не может скрыто уменьшать HP вне DamageApplied или создавать лечение. Static base/gear maxima проходят initialization clamp.

Global Config rows заполняются всеми текущими столбцами: Key/Group/Description RU/Value/Data Type/Unit/Min/Max/Status/Notes RU. Group: `Stats`/`Control`; Data Type: `int`; Status после approval: `FIXED`; Notes содержат ссылку `OPEN-WP10-05` или `OPEN-WP10-13/20`, а не новое независимое правило. Bounds для значений min/max используют те же допустимые домены; JSON Map должен ссылаться на реальную Value cell и включать `Required=1`, `Include Runtime=1`.

### 4.4 Effect/gear metadata и registry

В `.0.2` каждому effect требуются `priority` (proposal `0`), `refresh_rule` (`ResetDuration` или `KeepLonger`; existing profiles: `ResetDuration`), `semantic_role` (`None|ControlFatigue|ControlImmunity|GrabLockout|WakeupImmunity|GuardBreak`). Пять special roles уникальны и ссылаются на existing effect IDs через DATA, без switches по animal/action/effect ID. Каждому gear требуется explicit `priority=0`.

Для Refresh/AddStacks group должен иметь один EffectId; compatible Replace/StrongestWins groups могут иметь несколько profiles. Один fighter имеет максимум один occupant каждой StackGroup и одну public entry каждого EffectId. Group policy/modifier target compatibility проверяется заранее.

Target registry:

- WP-10 runtime stats: Power/Armor/Precision/Evasion/Guard/GuardBreak/MoveSpeed/ActionSpeed/Initiative/ControlPower/ControlResistance/Mass/EnergyRegen;
- WP-10 channels: DamageTaken/DamageDealt/BlockChanceOffset/DodgeChanceOffset/BlockWeight/PunishWeight/WallActionWeight/GrabPriority/HardControlDuration/HardControlAllowed/GrabAllowed/KnockdownAllowed;
- neutral multiply channel — explicit FP scale в typed registry; neutral additive — `0`, allow flag — `1`; это семантические identity operations, а не отсутствующий DATA default;
- known WP-11-only targets (GripGain, RageGain, TempoPerUnit, DecayGraceTicks, SignatureWeight и resource kit targets) могут присутствовать в dormant profiles. Reachable WP-10 activation на такие targets отклоняется `UnsupportedEffectModifierTarget`; текущие passives автоматически не активируются.

Flag channels допускают только Override `0/1`; HardControlDuration в fatigue берёт один multiplier из lookup по stack count и не умножает дополнительно `value1`. Stat targets принимают Add/Multiply/Override, weight/damage channels — Multiply, defense chance offsets/GrabPriority — Add. Неверная operation/target pair — pre-start rejection.

### 4.5 EffectRules catalog

В `.0.2` добавить root catalog `effect_rules`, source sheet `Effect Rules`, namespace `effect_rule` в JSON Map. Минимальный typed record:

```text
rule_id: StableId
owner_kind: Global | Action | Effect
owner_id: existing ActionId/EffectId; literal global только для Global
trigger: BattleStart | DamageTaken | DamageDealt | Blocked | Dodged |
         GuardBreak | ControlEnded | FatigueThresholdReached | GrabEnded |
         Knockdown | WakeupCompleted | EffectAdded | EffectRemoved | EndOfTick
recipient: Self | Opponent
condition: Always | PositiveDamage | NormalHit | LivingTarget
primitive: ApplyEffect | RemoveEffect
effect_id: existing effect StableId
priority: int32
internal_cooldown_ticks: nonnegative int32
max_activations_per_tick: positive int32
max_activations_per_battle: positive int32
once_per_event: true
```

`Self` для Global — semantic subject hook (например, бывший controlled fighter); Action — owner исходного committed action; Effect — владелец subscribed effect. Opponent — другой fighter. Conditions — закрытый typed vocabulary, без eval/scripts/free-form expressions. Unknown trigger/condition/owner/primitive/ref отклоняется. No I/O/parsing в runtime.

Минимальный production catalog: global ControlEnded→fatigue; FatigueThresholdReached→control immunity; GrabEnded→grab lockout бывшей цели; WakeupCompleted→wakeup immunity; GuardBreak→guard-broken defender. Предлагаемые RuleIds: `rule_control_fatigue`, `rule_control_immunity`, `rule_grab_lockout`, `rule_wakeup_immunity`, `rule_guard_broken`. Priority `0`, cooldown `0`, caps `1/tick`, `999/battle`, once_per_event `true`, condition LivingTarget. Full passive/gear trigger bindings добавляются в WP-11, не выводятся сейчас из их text fields.

### 4.6 Interrupt DATA

Добавить action fields `hit_interrupt_strength`, `hit_interrupt_min_strength`, `hit_interruptible_phases`, `protected_phases`, `ignored_control_categories`.

- strength vocabulary: `0=None`, `1=Light`, `2=Medium`, `3=Heavy`;
- Armored threshold proposal `3`, остальные thresholds `1`;
- ordinary combat actions interruptible в `Startup`; movement actions — `Startup|Active`; Recovery обычным hit не отменяется;
- Armored/Unstoppable protection phases proposal `Startup|Active`;
- Unstoppable ignores `Stun|Knockdown` в protected phases; Grab/Defeat не игнорирует;
- остальные profiles имеют explicit empty protected/ignored lists.

Migration заполняет strength по следующей DATA review table (не runtime inference): Bear paw=1, body=2, crushing=3, earthbreaker=3, fury=2, rampage=3; Gorilla hammer=2, shove=2, overhead=3; Kangaroo lead=1, push=2, flying=2, tail-counter=2, barrage=1. Остальные actions=`0`. Existing 24 IDs не меняются. Списки используют pipe-separated enum tokens, canonical ordering, без duplicate/unknown tokens.

## 5. Runtime semantics

### 5.1 Initialization

Materialize все reachable profiles/rules/bounds/phase filters до `journal.Begin`. Проверить references, allowlists owners, group compatibility, positive durations, stack cap `1..255`, signed values, rule counts, all timer/counter/graph arithmetic. Неизвестный target/недостающий обязательный DATA key возвращает sorted typed Rejected, journal пуст, draws `0`.

Initial state атомарен для обоих fighters. BattleStarted — sequence `0`; только после него допускается BattleStart closure. Инициализация читает base/mode/gear; dormant selected passive не исполняется до WP-11. Mode остаётся None; extra effect allowlist не добавляется: reachable rules разрешены только для allowed loaded owners, Global — обязательные rules режима.

### 5.2 Stat formula и ordering

Каждый recompute начинается из immutable base, а не из ранее modified value:

```text
ordered sources = layer, Priority ASC, SourceStableId ordinal, modifier ordinal
layers = BaseAnimal, ModeNormalization, Gear, PassiveInitialization,
         PermanentEffect, TemporaryEffect
additive = checked sum(all Add values; AddStacks uses checked value*stacks)
multiplier = product_sorted(all Multiply; mathematical floor at each fold)
derived = mul_fp(checked(base + additive), multiplier)
apply Override values in canonical source order; last explicit Override wins
derived = clamp(derived, required stat min, required stat max)
```

Из CDS применяется aggregated additive/product formula; отдельная семантика Override предложена явно, поскольку CDS не задаёт конфликт Overrides. Умножение AddStacks повторяется `stacks` раз в canonical modifier ordinal; Override применяется один раз. Signed floor использует WP-02 `FixedMath`, не C# truncate-to-zero. Overflow/risky reachable combinations отклоняются до старта; corrupted runtime arithmetic — FailedInvariant с rollback.

Recompute invalidated derived/channel cache после фактического add/remove/refresh/stack mutation; unmodified tick не делает накопительного умножения. Maxima clamp только в setup; эффект не меняет shape/HP/maxima.

### 5.3 Snapshot/freeze и barriers

Порядок двенадцати фаз сохраняется. Phase 1 capture нужен для timers/progress; общий decision snapshot для phase 5 строится после phases 2–4. Это продолжает WP-08 closed snapshot rule, а не возвращает решения к stale pre-expiry values.

| Boundary | Effect work |
|---|---|
| After BattleStarted | BattleStart closure после atomic setup |
| Phase 2 | ExpireBeforeTick; control timer transitions; associated closures; recompute до decisions |
| Phase 5 | Commit costs/timings не пересчитываются; effect consequences только после freeze обоих descriptors |
| Phase 6 | Effect MoveSpeed не меняет уже frozen system movement segment |
| Phases 9–10, per group | Base trade/defense/damage/control/spatial plan → complete trigger closure → defeat, одним atomic plan |
| Phase 12 nonterminal | EndOfTick closure → ExpireAfterTick closure → recompute → next tick |
| Terminal | Final cleanup; новые normal triggers запрещены |

Все impacts одной equal trade group читают один pre-group stats/channel snapshot. Effect, вызванный ударом A, не меняет ответный удар B в той же group, но виден следующей group того же tick. ActionSpeed freezes startup/recovery при commit; Active/HitSchedule неизменны. Initiative берётся на intent-collection boundary; effect closure не пересортирует уже ordered groups. EnergyRegen channel materialized, а periodic gain/state multiplier execution остаётся WP-11.

### 5.4 Lifetime

Effect durations положительны; indefinite/duration `0` не входят в WP-10. Для применения на tick `t` с duration `D`: `end_exclusive=t+D`, `ticks_remaining=max(0,end_exclusive-current_tick)`.

- ExpireBeforeTick: удалить в phase 2 tick `t+D`; impacts `t..t+D-1` видят эффект.
- ExpireAfterTick: удалить в phase 12 tick `t+D-1`, после outcome check; effect действует на этот tick полностью.
- D=1 after-effect может added и removed в том же tick, если closure закончена до phase 12.
- ResetDuration: новый end=`current_tick+incoming_D`; KeepLonger: max(old end, current+incoming_D).
- Timer advance сам по себе не emit EffectAdded; EffectFrame показывает актуальное remaining.
- Если expiry closure добавила новый D=1 After-effect, уже due на текущей phase-12 boundary, повторить bounded due-expiry drain до отсутствия due effects. Нельзя оставить его активным на следующий tick из-за однократной stale enumeration.

### 5.5 Stacking

| Policy | Exact rule |
|---|---|
| Reject | Occupied group: no mutation/event/expiry extension |
| Refresh | Retain one profile/stack; update expiry по refresh_rule; emit EffectAdded только при real mutation |
| Replace | EffectRemoved(Replaced) → EffectAdded, один atomic plan |
| StrongestWins | Compare incoming/existing по explicit CompareKey; bigger signed Value1/DurationTicks/StackCount wins; stable identity ASC breaks exact tie; winner replaces atomically |
| AddStacks | count=min(count+1,cap), обновить expiry по refresh_rule; at cap refresh допускается, same count+same expiry — no-op |

Stable identity: `(EffectId, source-owner StableId, source-action StableId-or-empty, RuleId)` ordinal. Exact same identity/strength retains existing; weaker effect не refresh-ит сильный. Missing CompareKey/несовместимый compare domain/group — rejected. Public effects сортируются по EffectId, internal operations — priority/source/ordinal. Removal всегда пересчитывает из base и surviving sources.

### 5.6 Trigger closure

Hook имеет semantic subject, canonical earlier SourceEventId, root event и depth. EndOfTick использует последний earlier canonical event текущей battle как source; hook не создаёт отдельный gameplay event. Hook occurrence identity для реального события — его EventId; для EndOfTick — `(EndOfTick,tick,subject fighter)` вместе с earlier SourceEventId. Root closure/once keys используют occurrence identity: два тихих тика с одним и тем же earlier event остаются двумя разными EndOfTick opportunities. ControlEnded emitted internally ровно один раз для lifecycle: StateChanged выхода из Stunned/KnockedDown или GrabEnded для Grabbed; defeat не порождает normal ControlEnded.

FIFO causal levels; внутри уровня key `(source sequence ASC, RulePriority ASC, RuleId ordinal, owner StableId ordinal, recipient StableId ordinal)`. Никакой hash-map/registration/object allocation order не влияет на результат. Root depth=`0`, admitted child depth=`parent+1`; `depth > max` — FailedInvariant.

Перед enqueue: owner/condition/living-target → ancestor/root-cycle guard → once-per-event → rule cooldown/caps → effect application cooldown/caps. Runtime owner — fighter: для Global это semantic subject, для Action — owner action, для Effect — subscribed fighter. Rule key `(ownerFighterId,RuleId)`; effect budget `(recipient,EffectId)`; once key `(ownerFighterId,RuleId,hook occurrence identity,recipient)`. Поэтому global rule с cap1/tick может сработать по одному разу для A и B, а не только для первого обработанного fighter. Ready iff `tick>=next_allowed_tick`; successful admission reserves rule budget/cooldown в preview. Effect budget расходуется только при effect mutation. Stack-rejected node расходует admitted rule budget, но не effect budget и не gameplay progress. Normal budget/condition rejection — bounded diagnostic, без canonical marker.

Один `(owner,RuleId,recipient)` может исполниться максимум один раз в root causal closure; A→B→A semantic cycle подавляется, а не выдаёт новые effects бесконечно. Это не silent hard-cap drop: suppression имеет diagnostic reason `TriggerCycleSuppressed`. Acyclic цепочка сверх DATA depth/tick cap — fatal. Hard tick counter считает admitted nodes, включая no-op; reset только на следующем tick. Queue ограничена remaining tick budget.

### 5.7 Generic consumers

- DamageDealt/DamageTaken входят в ordered ContextDamageModifiers до ArmorRatio/MinDamage и Block chip; пример в Test Plan. Wall damage остаётся direct WP-09 wall formula, без неописанного бонуса.
- BlockChanceOffset/DodgeChanceOffset входят перед existing chance clamp; eligible defense draws остаются только Resolution ChanceCheck.
- BlockWeight/PunishWeight/WallActionWeight — дополнительный Situation factor для exact existing tags `block`/`punish`/`wall_impact` соответственно, canonical modifier order; Synergy/Counter/Variety/Opportunity не дублируются.
- GrabPriority additive снимается на intent collection; force всё ещё использует effective Mass, а control — effective ControlPower/Resistance.
- Effect rules/stacking/expiry/fatigue/knockdown/interrupts не расходуют RNG.

GuardBreak hook существует только после failed eligible active Block у incoming `guard_break` action. Успешный Block, no active Block, miss, Dodge и counter не применяют guard-broken effect. Source — соответствующий AttackHit(reason=GuardBreak), effect closure после complete group.

### 5.8 Control fatigue/immunity и grab lockout

ControlEnded живого fighter добавляет одну fatigue stack. Lookup index `0..3` даёт `1000/750/500/250`; таблица обязана содержать `stack_cap+1` entries. Fatigue expires целиком; AddStacks refresh сбрасывает его lifetime по DATA. При crossing `old<threshold && new>=threshold` добавляется control immunity; at-cap refresh не создаёт новый immunity interval.

При immunity: damage разрешён; stagger response логируется; threshold meter reset-to-zero сохраняется, но новый Stunned/KnockedDown/Grabbed не начинается. Prevented control — StateChanged(old==new, duration=0, immunity_result=Prevented) с reason, unchanged state; action не отменяется этим prevented control. Наличие immunity нельзя обойти system fallback.

Stun duration: existing ratio/fatigue/floor/clamp order; ActionCancelled только когда control действительно разрешён. New control не продлевает действующую immunity. Existing same-source action не refresh-ит hard control: once-per-action/control lifecycle; `MultiHitControlProfile` не вводится и повторные entries не обходят защиту.

GrabEnded живой бывшей цели добавляет lockout с role GrabLockout; runtime existing EndExclusiveTick и effect expiry согласованы. Нельзя иметь расходящиеся два независимых таймера. В `.0.5` effect с ExpireAfterTick, добавленный t с D=20, препятствует grab до конца t+19; phase-2 availability на t+20 разрешает grab. MaxHold/release/throw WP-09 сохраняются.

### 5.9 Knockdown

Разрешённый `knockdown` consequence инициируется один раз после complete damage/force/throw, до group defeat, если цель жива и нет immunity/Unstoppable protection. Durations каждого stage: `max(1,mul_fp(mul_fp(base_stage,ControlRatio),fatigue))`, checked sum; stun_min/max не подменяет knockdown stage bounds.

Без modifiers: Fall `[t,t+2)`, Grounded `[t+2,t+8)`, GetUp `[t+8,t+11)`; Ready в phase 2 `t+11`. Основной state всё время KnockedDown; internal stages проецируются StateChanged(old==new) с reasons `KnockdownGrounded`/`KnockdownGetUp`. ActionId/ActionPhase остаются null после отмены; ActionPhase.GetUp не используется без committed action. StateTicksRemaining — remaining total до Ready.

Удар по уже KnockedDown возможен только с exact `ground_hit` tag (на всех трёх stages). Остальные impacts — AttackMissed(InvalidTarget), no defense RNG. GroundHit не refresh-ит knockdown того же action. При wakeup: StateChanged→DecisionReady, fatigue/lockout consequences по rules и wakeup immunity до нового decision snapshot. Lethal GroundHit имеет precedence над wakeup, никакой resurrection.

### 5.10 Interrupt filters

Отдельно проверяются ordinary HitInterrupt и hard-control category. Armored игнорирует только ordinary hit strength ниже threshold в protected phases, damage всё равно проходит. Unstoppable игнорирует explicit Stun/Knockdown categories в protected phases; Block/Dodge rules, damage, Grab и Defeat сохраняются. Outside protected phases действуют общие filters.

Counter/hard control/defeat остаются явными причинами. Cancel не возвращает cost/cooldown, удаляет future schedule entries. UninterruptibleImpact защищает только уже созданный intent текущей group; не защищает будущие hits и не отменяет defeat.

## 6. Atomicity, progress и terminal

Исходный group plan включает всю bounded trigger closure, effects, derived cache, control timers, rule/effect budgets/visited sets, RNG previews и drafts. Exact count preflight выполняется до первой authoritative mutation/Append. Event-cap reserve учитывает BattleEnded плюс planned active-effect cleanup. Overflow/trigger cap/instance cap/event cap откатывает всю текущую group/phase closure; ранее committed groups сохраняются.

Runtime instance cap — occupied groups/fighter; replacement at cap legal, создание нового group at cap fatal, не silent reject. Queues/diagnostic chains ограничены DATA и не становятся глобальным mutable state.

Progress: реальные изменения stats/effect membership/stacks/expiry, timer countdown/control stage и существующие state/resources/position mutations. RNG/events, queue/counters, suppressed/rejected/no-op application не считаются progress. Технический watchdog возвращает FailedInvariant, не Draw.

Terminal path не запускает EndOfTick/passive/reapplication rules. Remove surviving effects один раз с `BattleEnded` reason в canonical priority/ID/fighter order, recompute, final frames без active effects, затем ровно один BattleEnded и journal.Complete. Cleanup не активирует removal triggers и не меняет уже выбранный outcome. Defeated/HP=0 не принимает late ApplyEffect; explicit terminal removals разрешены.

Failure codes proposal: `EffectTriggerDepthExceeded`, `EffectTriggerTickCapExceeded`, `EffectInstanceCapExceeded`, `EffectArithmeticOverflow`, `EffectInvalidMutation`; сохраняются existing event-cap/watchdog codes. Pre-start codes: `MissingStatBounds`, `InvalidStatBounds`, `InvalidEffectReference`, `InvalidEffectGroup`, `InvalidEffectLookup`, `UnsupportedEffectModifierTarget`, `InvalidEffectRule`, `InvalidInterruptProfile`, `EffectArithmeticOverflowRisk`. Errors имеют stable JSON path, sorted order; messages не являются assert contract.

## 7. Events и replay compatibility

EffectAdded/Removed actor — affected fighter; target — source fighter для opponent-source, null для self/global. Envelope effect_id обязателен. SourceEventId — earlier real cause; related IDs unique ordinal. Originating ActionId/DecisionId/ResolutionGroupId сохраняются только когда действительно относятся к source action/group. Expiry source — latest successful application event; terminal removal source — earlier terminal cause. RNG=null.

Before/after содержат effect membership/stacks/remaining и current frames той же mutation. No-op/rejected stack не emit canonical event. Replace — Removed→Added, не два Added. Frame entries уникальны и ordinal по EffectId, structural limit=128.

Добавить version-specific EffectReplaySemanticValidator для `.0.5` (composition с decision/movement/resolution semantics). Он проверяет local membership deltas, policies, expiry boundaries, null/roles/lineage, immunity/control durations и terminal cleanup. Standalone replay не может доказать отсутствующие private stats/cooldown/cycle predicates: их проверяет config-aware producer conformance/re-simulation с той же version и config hash. Нельзя обещать полный gameplay proof из public frames.

## 8. Historical pin policy

Исходный config SHA `0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`, workbook SHA `bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2`.

До Engine bump закрепить current `.0.4` файлы как historical, без переименования/перезаписи:

| Fixture в `fixtures/replay/v0.1` | File SHA-256 |
|---|---|
| `wait-equal-l1.engine-0.4.0.json` | `732b442056083d6dc2a3d2439219116199ec707bd5854ce4c365629cdfcd6c08` |
| `decision-weighted-l1.engine-0.4.0.json` | `ffbed61a784e72b20f43dc6b3d5f89c4954d2e65714ab747dc6c42c6e76d815b` |
| `resolution-basic-l1.engine-0.4.0.json` | `c56685b7b9fae47abd1b0cb503b9d2b46cfda4a890c73cc82226810626f70c99` |
| `resolution-double-ko-l1.engine-0.4.0.json` | `1bee7887f1603c0f95e2f48950a54549ff17dd34edb67dd85d51415f39bb188f` |
| `resolution-wall-grab-l1.engine-0.4.0.json` | `25ee1cbe58c0fa1df40076ca69c79ba2dde0e00b427d4c4b7017a8a65e28843c` |

Historical `.0.1–.0.3`, source machine fixture package и его manifest также сохраняются. Новые `.0.5` fixtures — отдельные файлы; actual input/final/file hashes вычислить после implementation и записать в manifest/tests/status, сейчас не выдумывать. Unity bundled `.0.4` copies не обновлять в WP-10.

## 9. OPEN-WP10: точные решения для утверждения

Все строки `PROPOSED`. Approval переводит проектные вопросы в CLOSED, но physical DATA/code/test gates закрываются только после фактического исполнения.

| ID | Предлагаемое решение | Ссылка |
|---|---|---|
| `OPEN-WP10-01` | Test Plan — обязательная blocking matrix; inventory traits, no Skip; code после approval | Test Plan §§1,6 |
| `OPEN-WP10-02` | Baseline local `81b1488`, WP-09 completed; Viewer checkpoint независим; remote sync отдельно | §3 |
| `OPEN-WP10-03` | Generic effects/control — WP-10; full passives/resources — WP-11 | §1 |
| `OPEN-WP10-04` | Engine `.0.5`, balance `.0.2`, новые versioned DATA files; wire/RNG/ordering прежние | §4.1 |
| `OPEN-WP10-05` | 15 обязательных stat min/max pairs и шесть queue/knockdown settings с точными proposals | §§4.2–4.3 |
| `OPEN-WP10-06` | Aggregate Add → sorted FP product → Override → clamp; signed floor; recompute from base | §5.2 |
| `OPEN-WP10-07` | Shared post-expiry decision snapshot, frozen commit/segment/ordered intents, next-group effects | §5.3 |
| `OPEN-WP10-08` | Explicit effect_rules catalog, closed typed vocabulary; пять generic production bindings | §4.5 |
| `OPEN-WP10-09` | 12 phases unchanged, bounded closure до defeat; end-tick then after-expiry | §5.3 |
| `OPEN-WP10-10` | Positive D; before at t+D, after end t+D-1; ResetDuration/KeepLonger | §5.4 |
| `OPEN-WP10-11` | Exact five stacking policies, same-ID Refresh/AddStacks, atomic replace | §5.5 |
| `OPEN-WP10-12` | One group occupant; explicit CompareKey and stable source identity tie-break | §§4.4,5.5 |
| `OPEN-WP10-13` | Depth 8/tick 128/instances 32; FIFO levels и full deterministic queue key | §§4.2,5.6 |
| `OPEN-WP10-14` | Explicit once/cooldown/caps; rule admission vs effect mutation budgets раздельны | §5.6 |
| `OPEN-WP10-15` | Same-root node repetition suppressed diagnostically; hard DATA limits fatal | §5.6 |
| `OPEN-WP10-16` | Typed stat/damage/chance/tag-weight/grab channels, no extra RNG, reserved WP-11 targets unreachable | §§4.4,5.7 |
| `OPEN-WP10-17` | Guard-broken только после failed eligible Block + guard_break tag | §5.7 |
| `OPEN-WP10-18` | Fatigue on ControlEnded, lookup 0..3, threshold crossing immunity, no immunity refresh | §5.8 |
| `OPEN-WP10-19` | Grab lockout бывшей цели, один authoritative interval, no same-source refresh | §5.8 |
| `OPEN-WP10-20` | Fall/Grounded/GetUp, fatigue per-stage, GroundHit-only, wakeup immunity | §5.9 |
| `OPEN-WP10-21` | Explicit incoming strengths/phases/ignored categories; armored threshold 3; costs retained | §§4.6,5.10 |
| `OPEN-WP10-22` | Affected actor/source target, earlier lineage, exact mutation frames; no-op silent canonical | §7 |
| `OPEN-WP10-23` | Whole effect closure preflight/rollback, reserve cleanup+terminal, no partial Append/RNG | §6 |
| `OPEN-WP10-24` | Authoritative progress only, typed failures, bounded diagnostic capture | §6 |
| `OPEN-WP10-25` | Terminal cleanup without triggers, no late effects, BattleEnded last | §6 |
| `OPEN-WP10-26` | Pin `.0.4` before bump, preserve every historical/config/source artifact, new `.0.5` goldens | §8 |
| `OPEN-WP10-27` | Unit/conformance/integration inventory, target/process/profile/OS/coverage gates | Test Plan §7 |
| `OPEN-WP10-28` | .0.1 config reader retained; .0.5 requires .0.2; no new passive/resources/Unity work | §§1,4.1,4.4 |

## 10. DATA blockers и programming gate

| Blocker | Текущий статус | Условие закрытия |
|---|---|---|
| `BLOCK-WP10-APPROVAL-01` | OPEN | Owner принимает OPEN-WP10-01..28 и Test Plan как blocking |
| `BLOCK-WP10-DATA-01` | OPEN | 15 stat bounds pairs утверждены, внесены в v0.2 XLSX/JSON Map/schema, валидированы |
| `BLOCK-WP10-DATA-02` | OPEN | Queue limits и runtime instance cap внесены и проверены |
| `BLOCK-WP10-DATA-03` | OPEN | Effect Rules, metadata/roles/refresh/priorities и target registry complete |
| `BLOCK-WP10-DATA-04` | OPEN | Knockdown/interrupt DATA complete; duplicate durations согласованы |
| `BLOCK-WP10-ARTIFACT-01` | OPEN | v0.2 0-error/0-warning export, manifest/hash/schema parity; v0.1 unchanged |
| `BLOCK-WP10-HISTORY-01` | OPEN | Automated .0.4 historical SHA/verify gate до version bump |

После approval разрешается DATA/tooling implementation, затем runtime только после validated DATA materialization. Не считать owner approval доказательством наличия XLSX keys/green tests. Ни один новый numeric fallback не разрешён.

## 11. План реализации и предполагаемые изменения

1. Зафиксировать `.0.4` historical pins/tests; добавить WP-10 inventory и negative DATA tests.
2. Создать v0.2 Workbook/JSON Map, `BalanceV02Schema`, compiler/exporter support effect_rules и parallel artifact verification; сохранить v0.1 path/tests.
3. Обновить Contracts: immutable effect rule catalog, typed targets/profiles/requests; source-compatible existing constructors; без зависимости на Core/Replay/Unity.
4. Добавить `Battle.Core/Effects`: materializer, registry, modifier evaluator, effect store, stack/expiry planner, trigger queue/guard/budgets, control filters/lifecycle. RuntimeBattleSettings/BattleSetup получают validated immutable profiles.
5. Интегрировать cache/snapshot/barriers в TickCoordinator, FighterRuntimeState и ResolutionPlan; связать generic consumer channels с Decisions/Resolution/Movement.
6. Добавить `Battle.Replay/Verification/EffectReplaySemanticValidator`, contracts/json/frame assertions, tamper tests и terminal cleanup.
7. Создать четыре effect/control goldens, current `.0.5` wait/decision/basic/double-KO/wall-grab, probe support и `verify-wp10-target-determinism.ps1`, generated/coverage gates.
8. Выполнить locked restore, Release/Debug build/test, process/TFM/culture/profile/determinism/historical/generated/coverage; Windows/Linux CI. Обновить status только по фактическим результатам.

Предполагаемые области: `Battle.Contracts/Config`, `Battle.Contracts/Events` (только validation существующего wire), `Battle.Config/{Schema,Compiler,Semantic}`, `CombatLab.Runner/Config/Export`, `Battle.Core/{Initialization,Effects,Engine,Decisions,Resolution,Safety}`, `Battle.Replay/Verification`, три test projects, probe/scripts, balance v0.2 files, workflow, Docs. `UnityClient` не входит в patch; чужие изменения ProjectSettings и untracked Unity files сохраняются.

## 12. Критерий завершения

Все blocking IDs из Test Plan реализованы, inventory clean, локальные и четыре Windows/Linux × Debug/Release CI jobs green, required critical branches 100%, Core line >=85%, historical/config pins прежние, v0.2 generated reproducible. Лишь тогда WP-10=COMPLETED. После него — WP-11 Fighters, а не автоматическая готовность всей игры/production backend.
