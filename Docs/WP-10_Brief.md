# WP-10 Brief — Effects

> Статус: `IN PROGRESS — LOCAL ACCEPTANCE PASSED / CI PENDING`; `132/132` blocking IDs исполнены локально.
>
> Подготовлено `2026-10-03` на baseline `81b1488` (локальный `master`: WP-09 completed + Unity Replay Viewer checkpoint).
> Владелец утвердил `OPEN-WP10-01..28` 2026-10-03; все проектные решения `CLOSED`.
> [Combat Test Plan WP-10 v0.1](./Combat_Test_Plan_WP-10_v0.1.md) — утверждённая обязательная blocking matrix. Physical DATA и execution gates закрываются только фактическими проверками.

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

## 4. Версии и DATA migration — утверждённое решение

### 4.1 Version contract

- новый Engine: `battle.core/0.5.0`;
- новый баланс: schema `combat.balance/0.2`, config `v0.2`;
- event/replay/rejection/presentation остаются `0.1`, RNG/ordering/mode versions сохраняются;
- `.0.5` принимает только explicit `.0.2` config; `.0.1` не дополняется runtime defaults;
- config loader/compiler продолжают читать `.0.1` для historical artifacts/tools;
- replay verifier читает `0.1.x–0.5.x` по version-specific semantic policies; старый replay не пересимулируется новым Engine для проверки SHA.

Причина миграции: обязательные bounds и новый catalog меняют строгий balance contract. Предлагается создать отдельные `config/source/Combat_Balance_Workbook_v0.2.xlsx`, `config/generated/combat.balance.v0.2.{json,map.csv,validation.json,manifest.json}` и `schemas/balance/v0.2/combat.balance.schema.json`. Source v0.1, generated v0.1 и historical fixtures сохраняются byte-for-byte. Новый XLSX строится из копии v0.1 с добавленными DATA/JSON Map rows; generated файлы создаёт exporter, ручное изменение запрещено.

### 4.2 Новые settings

Все числа утверждены владельцем `2026-10-03`, внесены в separate workbook v0.2 и проверены exporter/compiler/loader. Независимый Core materializer, pre-Begin wiring и consumer-specific arithmetic proof реализованы; соответствующие blocking cases и critical coverage gate прошли локально.

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

## 9. OPEN-WP10: утверждённые точные решения

Все строки `CLOSED`: владелец принял решения и матрицу 2026-10-03. Physical DATA/code/test gates закрываются только после фактического исполнения. Числа и правила ниже теперь утверждены, а не runtime defaults.

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
| `BLOCK-WP10-APPROVAL-01` | CLOSED | Owner принял OPEN-WP10-01..28 и Test Plan как blocking 2026-10-03 |
| `BLOCK-WP10-DATA-01` | CLOSED | 15 bound pairs physical source/map/schema/compiled parity green |
| `BLOCK-WP10-DATA-02` | CLOSED | Queue limits/instance cap physical source/map/compiled parity green |
| `BLOCK-WP10-DATA-03` | CLOSED | Five Effect Rules и explicit metadata/registry валидированы; strict Core materialization, pre-Begin integration и consumer arithmetic proof проверены локально |
| `BLOCK-WP10-DATA-04` | CLOSED | Knockdown/interrupt DATA и duplicate-duration validation green |
| `BLOCK-WP10-ARTIFACT-01` | CLOSED | Native fresh-process reproduction; v0.2 0-error/0-warning export/loader/schema/manifest parity, v0.1 pins unchanged |
| `BLOCK-WP10-HISTORY-01` | CLOSED | Десять replay0.1–0.4 SHA/verify cases green до и после bump; historical bytes/policies сохранены, public Engine0.5 использует отдельные goldens |
| `BLOCK-WP10-TOOLING-01` | CLOSED | Owner разрешил `.NET/OpenXML мигратор` 2026-10-03; alternative workflow реализован и проверен |

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

## 13. Previous checkpoint — versioned v0.2 setup, 2026-10-04

После physical DATA/tooling, atomic/control runtime реализованы remaining decision/grab consumers. Phase5 снимает immutable effect inputs вместе с общим snapshot; только Situation получает matching factors по точным тегам block/punish/wall_impact, с общим canonical source order до floor. Grab availability читает frozen opponent protection; effective signed GrabPriority снимается при intent collection и используется conflict resolver/payload без пересчёта. Mass/ControlResistance читаются при resolution-group freeze. Дополнительных RNG draws нет.

Strict v0.2 materializer и conservative consumer proof подключены в `BattleSetupFactory` для explicit Engine0.5/balance0.2. Оба билда, reachable graph, initial state/geometry и arithmetic проверяются до Begin. V0.2 initial stats используют aggregate Add/product/ordered Override/clamp; legacy0.4 sequential semantics сохранены. Proof охватывает damage/armor, control/fatigue/timelines, force/wall, fixed system versus scaled combat timing, decision/weight sum, additive grab priority, Int32 geometry/speed/combat-move pair и closure/terminal-cleanup reserve arithmetic. Потенциальный graph не отвергается по малому max_events; actual batch preflight сохраняет atomic failure semantics. Unknown tokens/refs, bounds/roles/groups/lookups/durations, flag non-integer0/1 и overflow дают sorted typed rejection без journal writes. Real external compiled v0.2 input используется full Simulate без definition injection; этот versioned путь пока internal, public Engine/CLI остаются0.4/balance0.1 до replay0.5/golden gates.

Исполнены `69/132` blocking IDs: новые `DATA-002/004/005/006/007/010/011`, `MOD-014`; точный список — Test Plan checkpoint. Versioned setup slice добавил21 unit test:17 real v0.2 setup/lifecycle/rejection cases и4 proof guard tests. Всего296 WP10 tests (239U/48C/9I). Release/Debug full suite1203 (776 Core +377 Conformance +50 Integration), no failures/skips; locked restore/build0 warnings/errors. Generated v0.1/v0.2 и actual-target/historical0.4 checks green, старые bytes не изменены. Legacy selected critical gates100%, fresh combined Core line92.26%; consumer proof line100%/branch97.36%, full WP10 critical coverage не закрыт. I/C effect/control/freeze cases, replay0.5 verifier/current+effect goldens, full132 inventory, остальные63 cases, WP10 process/TFM/OS/remote CI ещё обязательны; WP-10 не COMPLETED. [WP-10 Migration](./WP-10_Migration.md) содержит file list и границу результата; подробный статус — [Implementation Status](./Implementation_Status.md).

## 14. Previous checkpoint — full-loop integration, 2026-10-04

Synthetic integration configs компилируются production v0.2 compiler и проходят strict setup → full Engine0.5 loop → canonical journal, без definition/state injection. Test-only friend access и read-only observer позволяют проверять derived stats/frozen segments на phase barriers. Journal0.5 наследует event role policy0.3/0.4; standalone semantic verifier0.5 этим не включается. Public Engine/CLI остаются0.4 до replay/golden gates.

Добавлены27 integration tests /22 новых blocking IDs: `MOD-008/012`, `EXP-003/006/008/009`, `TRG-002/011/012/014`, `CTRL-002/004`, `KDN-003/005/006/008`, `INT-005/006/007`, `SAFE-006/007/009`. Проверены точные expiry/closure/end-tick границы, old same-group/new next-group Armor, frozen timing/segment speed/intent order, control fatigue/immunity/knockdown/wakeup, throw/wall ordering, cancellation costs/cooldown/current-group protection, trigger-free terminal cleanup, HP0 suppression и invalid event-cap cleanup reserve.

Итого91/132 IDs, осталось41; source traits91/unique91/no duplicates/no unknown. WP10323 tests (239U/48C/36I); full Release/Debug1230 (776 Core/377 Conformance/77 Integration),0 failures/skips. Locked restore/build green,0 warnings/errors. WP04 generated Release, WP10 fresh-process generated Release/Debug и WP09 actual-target/historical Release/Debug green. Historical/source/generated hashes не менялись. Legacy critical gates100%, combined Core line92.36%; full WP10 critical coverage/inventory/process/TFM/profile/culture/OS/remote CI и replay0.5/current+effect goldens остаются blocking. UnityClient/посторонние changes сохранены; git commit/push не выполнялись. File list и test command — [WP-10 Migration](./WP-10_Migration.md).

## 15. Previous checkpoint — remaining cases + replay0.5, 2026-10-04

Новые17 IDs: `STACK-007/011/012`, `TRG-003/007/013`, `CTRL-008/010/012`, `EVT-001..008`. Теперь108/132, осталось24; source traits108/unique108/no duplicate/no unknown. Полностью представлены stack/trigger/control matrix sections. Добавлены85 tests (7U/60C/18I); WP10408 (246U/108C/54I), полный Release/Debug suite1315 (783 Core/437 Conformance/95 Integration),0 failures/skips. Restore/build green,0 warnings/errors; пустой Performance project без новых tests.

Strongest compare/tie/group domain, guards/independent budgets, replacement при capacity32/128 и atomic overflow rollback проверены. Full-loop grab lockout для разных actions, failed-block guard-break consumers и max-hold/release/throw/lethal endings используют strict compiled v0.2 без injected definitions.

Standalone verifier0.5 теперь включён: explicit balance0.2 metadata, public effect membership/causality/no RNG/stack/lifetime/expiry/latest cause/replacement, prevented control, actionless knockdown stages/ready boundary, sparse advisory keyframes и terminal cleanup; decision/resolution validators составлены version-specifically.60 C tests включают rehashed semantic tampering,9 I scenarios — positive production roundtrip. Private rule/stats/cooldown proof не подменяется публичными deltas: `EVT-009` ещё открыт. Найденные stale countdown в timeout cleanup/control before-frame исправлены только effects projection; legacy policy/bytes сохранены. Public Engine/CLI всё ещё0.4/balance0.1; current+effect goldens ещё не созданы.

WP04 generated Release, WP10 generated Release/Debug, WP09 actual-target/historical0.4 Release/Debug и legacy critical gates green. Это не WP10 determinism release evidence. Fresh Core+I line92.45%; effect replay witness line98%/branch91.44%, full WP10 critical coverage не100%. Matrix/thresholds не изменены. Остаток: `BASE-001/003..006`, `DATA-008`, `EVT-009`, `SAFE-008`, `DET-001..008`, `REG-003..006`, `GOLD-001..004`; remote CI/OS и полный132 inventory обязательны. Unity/посторонние changes сохранены, git commit/push не выполнялись. Подробные проверки/file list — [Migration](./WP-10_Migration.md) и [Status](./Implementation_Status.md).

## 16. Previous checkpoint — public Engine0.5, goldens и release gates, 2026-10-04

Все `132/132` blocking IDs исполнены локально, inventory discovery green: нет duplicate/unknown IDs и skips. Public `new CombatEngine()` производит `battle.core/0.5.0` и требует explicit `combat.balance/0.2`; historical compiler/loader/replay policies сохранены. Старые unit/integration harnesses и target probe явно выбирают internal historical Engine0.4, поэтому прежние fixtures проверяют прежнюю семантику, а не новые defaults.

Добавлены CLI `run-demo`, каталог девяти explicit synthetic scenarios, девять `.0.5` replay, девять canonical config sidecars и отдельный `wp10.engine-0.5.0.manifest.json`. Historical файлы не заменены. Output публикуется только через CreateNew после schema/semantic verification; existing replay/config paths не перезаписываются. Gear/passive-specific triggers не включены автоматически. Используемый demo config отличается от production balance явно и компилируется настоящим compiler.

Закрыты последние24 IDs: BASE/version/contracts/architecture/inventory, DATA-008, config-aware EVT-009, SAFE-008, DET-001..008, REG-003..006, GOLD-001..004. Конфигурационные budgets/stats EVT-009 доказываются pinned config + independent arithmetic oracle + re-simulation, не выводятся из публичных frames. Effects сохраняют original resolution group для expiry lineage; только EffectAdded/Removed0.5 не трактуются как повторное открытие impact group. Rehashed negative tests подтверждают запрет reopen/cross-tick для настоящих impacts.

Locked restore green; Release/Debug build0 warnings/errors; полные suites1443 (864 Core/469 Conformance/110 Integration),0 failures/skips. WP10:536 tests (327U/140C/69I). Новый строгий `verify-wp10-coverage.ps1` проверяет целые critical math/store/queue/control/runtime/atomic classes, включая generated closure/iterator branches, и effect replay policy/witnesses:100% branch. Combined Core line92.74%. Parser/adapter scaffolding не является этим critical scope; coverage collection не исключает новые файлы. Legacy WP02/03/06/07/08/09 gates не ослаблены и green.

Детерминизм: четыре effect goldens ×100 in-process; ×10 fresh CLI processes на каждый scenario; profile/culture/catalog permutation/mirror/RNG checks green. Обе конфигурации совпадают с девятью fixed-metadata goldens; actual loaded netstandard2.1/net10.0 dependencies проверяются WP10 target probe. WP04 generated Release, WP10 native generated Release/Debug и historical actual-target gate green.

**Осталось для COMPLETED:** отправить WP10 branch/PR и получить green Windows/Linux × Debug/Release CI с новыми inventory/target/coverage steps. Remote CI в этой сессии не запускался; локальный Windows run не является Linux evidence. Functional cases и local coverage больше не являются незавершёнными пунктами. UnityClient/посторонние changes сохранены; git commit/push не выполнялись. Подробный file list и demo commands — [Migration](./WP-10_Migration.md).

## 17. Previous checkpoint — resumed final verification, 2026-10-05

После остановки сессии сохранённая реализация восстановлена по checkpoint, без повторной миграции DATA или замены fixtures. Заново выполнены locked restore, Release/Debug build и полные suites: по1443 passed (864U/469C/110I),0 failed/skipped, сборки0 warnings/errors. Отдельный Release `WorkPackage=WP10` завершился536 passed (327U/140C/69I); inventory132/132 green. Actual-target/process/golden/determinism проверки входят в выполненные integration suites.

Повторно прошли WP04 generated Release, WP10 native generated Release/Debug и все legacy/WP10 coverage gates на сохранённых финальных reports: critical branches100%, combined Core line92.74%. Новая CLI smoke-пара `artifacts/replays/wp10-knockdown-20261005-8a1f5305.{json,config.json}` совпала побайтно с pinned golden/sidecar; outcome FighterAWin, end_tick47. Smoke outputs ignored, не заменяют fixtures и не предназначены для commit.

Production-код при возобновлении менять не потребовалось; уточнены текущие DATA/history пометки и execution evidence в Docs. Статус `LOCAL ACCEPTANCE PASSED / CI PENDING` сохраняется: необходимы четыре remote Windows/Linux × Debug/Release green jobs. UnityClient/посторонние changes сохранены, commit/push не выполнялись.

## 18. Previous checkpoint — CI schema portability fix, 2026-10-05

Remote CI выявил OS-dependent schema formatting: Git blobs LF, Windows exporter/default `JsonWriterOptions.NewLine` и local pins CRLF. Генератор schema теперь явно LF, checkout policy покрывает обе balance versions, SHA tests закрепляют существующие LF Git bytes. Historical schema blob, workbook/config/replay contents и hashes не изменены; два новых regression executions проверяют LF/no BOM/no final newline. Это исправление сериализации/checkout, не изменение approved gameplay/DATA/version contract.

Locked restore; Release/Debug build0 warnings/errors; по1445 tests (864U/471C/110I),0 failed/skipped; affected suites7/7 в обеих конфигурациях. Generated WP04/WP10 Release/Debug green. Git filters при `core.autocrlf=true/false` сохраняют оба pinned schema SHA; conformance/inventory132/132, actual-target/process/historical/golden checks green. Saved coverage для unchanged critical Core/Replay scopes100%, combined Core line92.74%. Для COMPLETED нужен новый remote Windows/Linux × Debug/Release green run после push fix. UnityClient unchanged, commit/push не выполнялись.

## 19. Current checkpoint — CI target hash helper fix, 2026-10-05

Следующий Windows Debug CI прошёл schema/conformance, но target integration child host не нашёл `Get-FileHash`. Gate теперь использует .NET file stream/SHA256, сохраняя byte-exact manifest/golden comparisons. DET004 выполняется в normal host и с намеренно запрещённым Get-FileHash; оба варианта проверяют9 goldens на обеих actual target dependencies и обязательный success marker. Матрица/числа/producer/hashes/fixtures unchanged, skips/relaxed checks не добавлены.

Locked restore; Release/Debug build0 warnings/errors; full suites по1446 passed (864U/471C/111I),0 failures/skips. Targeted DET0042/2 в обеих конфигурациях green; full suite/inventory132/132 и generated WP04/WP10 Release/Debug green. Saved critical Core/Replay coverage100%, Core line92.74%. Remote confirmation для нового fix commit pending; UnityClient unchanged, commit/push не выполнялись.
