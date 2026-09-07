# WP-09 Brief — Resolution

> Статус: `IMPLEMENTED / LOCAL GATES GREEN`; GitHub Actions pending.
>
> [Combat Test Plan WP-09 v0.1](./Combat_Test_Plan_WP-09_v0.1.md) исполнен как обязательная exact blocking matrix. `OPEN-WP09-01..23` закрыты. Все локальные gates green; статус `COMPLETED` ожидает Windows/Linux × Debug/Release CI.
>
> `BLOCK-WP09-BASE-01 — CLOSED`: `feature/wp-09-resolution` синхронизирована с `origin/master@73a31bd` и содержит завершённый WP-08 baseline.

## 1. Результат этапа

WP-09 должен превратить подготовленные в WP-08 committed actions и hit schedules в полностью детерминированный combat resolution:

- собрать defense, counter, strike, grab и forced-movement intents в фазе 7;
- построить canonical resolution groups и порядок их обработки в фазе 8;
- разрешить geometry, Counter, Dodge, Block, damage и stagger атомарными mutation batches в фазе 9;
- применить forced movement, wall impact, grab, throw и короткий control в фазе 10;
- только после полного resolution group определить defeat, victory или Double KO в фазе 11;
- выпустить причинно связанный canonical event journal с достаточными данными для replay verification;
- сохранить детерминированность, event-cap atomicity, zero-progress safety и historical replay предыдущих Engine versions.

Нормативный результат WP-09 из Technical Design: `Defense, damage, stagger, force, double KO`; минимальный acceptance — CDS edge fixtures.

После WP-09 движок должен уметь реально обменивать HP в бою, разрешать базовую защиту и пространственные последствия удара. Полный effect engine, stat modifiers и уникальные механики бойцов остаются последующими этапами.

## 2. Нормативные источники и прочитанный scope

Для подготовки Brief использованы только связанные с WP-09 разделы оригинальных документов:

- Combat Design Specification v0.1:
  - §§4.2–4.3 — decision/impact snapshots и frozen commit values;
  - §§5.1, 5.3 — geometry и range semantics;
  - §§8.1–8.2 — state/control model;
  - §§10.1–10.8 — damage, defense, stagger, force, wall, grab, clash и defeat;
  - §15 и приложение B — edge cases и canonical resolution order.
- Combat Lab Technical Design v0.1:
  - §§8.1–8.3 — Resolution RNG и draw discipline;
  - §§12–12.2 — 12-фазный pipeline, snapshots и canonical intent key;
  - §§14.1–14.3 — intents, effects boundary и safety;
  - §§15, 21, 23.1–23.2 — event drafts, verification, coverage и WP-09 deliverable;
  - приложения B и E — loop и source precedence.
- Combat Event & Replay Schema v0.1:
  - §§8–11 — event order, resolution payloads, causality и RNG provenance;
  - §§14–15 — damage/control/spatial semantics;
  - §§19 и 21 — writer/verifier requirements и conformance.
- Canonical [combat.balance.v0.1.json](../CombatLab/config/generated/combat.balance.v0.1.json) — существующие DATA keys, значения, tags и Stable IDs. Текущий SHA-256 файла: `0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`.
- Завершённый WP-08 из `origin/master@73a31bd`: decision snapshot, generic combat lifecycle, immutable committed descriptor, `AttackPrepared`, Engine `battle.core/0.3.0` и historical replay boundary.
- Текущие [Implementation Status](./Implementation_Status.md), [Decisions](./Decisions.md) и [Index](./Index.md), а также завершённые [WP-08 Brief](./WP-08_Brief.md) и [Combat Test Plan WP-08](./Combat_Test_Plan_WP-08_v0.1.md).

Source precedence для WP-09:

1. CDS задаёт gameplay semantics и формулы.
2. Canonical balance DATA задаёт числа и Stable IDs.
3. Replay Schema задаёт wire representation, causality и integrity.
4. Technical Design задаёт архитектуру, порядок фаз и quality gates.
5. Подготовленный [Combat Test Plan WP-09](./Combat_Test_Plan_WP-09_v0.1.md) после owner approval задаёт exact pass/fail examples.

Существующий код является реализационным seam, но не заменяет нормативные источники.

## 3. Исходное состояние и обязательные prerequisites

### 3.1 Baseline после WP-08

Нормативный baseline WP-09 — завершённый WP-08:

- Engine version `battle.core/0.3.0`;
- один immutable decision view обоих бойцов в фазе 5;
- frozen target, direction, timings и absolute impact ticks;
- generic non-System lifecycle `Startup → Active → Recovery`;
- `AttackPrepared` для действий с hit schedule;
- отдельные Decision и Resolution RNG streams;
- 12 фаз `tick-pipeline/1`, event cap и zero-progress watchdog;
- historical Engine `0.1.0`, `0.2.0` и `0.3.0` fixtures;
- deterministic config, journal и replay verification.

`BLOCK-WP09-BASE-01 — CLOSED`: локальная ветка WP-09 находится на `73a31bd`, совпадает с `origin/master` и содержит полный WP-08-complete baseline.

### 3.2 Уже существующий wire vocabulary

`Battle.Contracts` и JSON Schema v0.1 уже предусматривают основные события WP-09:

- `ConflictResolved`;
- `AttackHit`, `AttackMissed`;
- `Blocked`, `Dodged`, `Countered`;
- `DamageApplied`, `StateChanged`;
- `KnockbackApplied`, `WallImpact`;
- `GrabStarted`, `GrabEnded`;
- `FinisherTriggered`, `FighterDefeated`;
- `RngProvenance` со stream `Resolution` и operations `ChanceCheck`/`TieBreak`;
- `FramePair`, `resolution_group_id`, `source_event_id` и `related_event_ids`.

Поэтому wire version не должна повышаться только из-за активации уже описанных payloads. Изменение event/replay schema допустимо лишь при доказанной невозможности выразить обязательную семантику существующим контрактом.

### 3.3 Недостающий production seam

После WP-08 фазы 7–10 ещё не выполняют combat resolution. В частности, отсутствуют:

- typed hit-schedule primitive после config materialization;
- resolution profiles и runtime stats, необходимые формулам;
- intent collection, group construction, conflict/defense/damage/control resolvers;
- consumed-state для hit groups и scheduled hits;
- атомарный `ResolutionPlan` с preview Resolution RNG;
- authoritative HP/stagger/control/grab mutations;
- group-aware defeat/Double-KO path;
- semantic replay validation damage/group/defense/defeat lineage;
- deterministic Stable ID factories для resolution entities.

Текущий immediate outcome path WP-08 не является допустимой реализацией WP-09: defeat должен происходить из lethal mutation lineage и только после полного resolution group.

## 4. Scope

### 4.1 Входит в WP-09

- typed immutable resolution DATA materialization до `journal.Begin`;
- сохранение kind, tick и ordinal каждого элемента hit schedule;
- internal `DefenseIntent`, `CounterIntent`, `StrikeIntent`, `GrabIntent` и `ForcedMoveIntent`;
- canonical grouping, ordering, conflict resolution и exact-tie handling;
- impact target/state/geometry validation;
- Counter, Dodge и Block eligibility/outcomes;
- deterministic damage, chip, armor и HP clamp;
- stagger accumulation, threshold и минимальный hard-control seam;
- strike trade, multi-hit consumption, defeat и Double KO;
- forced movement, body-aware arena clamp и wall consequences;
- короткий grab/throw lifecycle, если его exact DATA requirements закрыты;
- emergency-threat input для WP-08 HardOpportunity suppression;
- canonical events, frames, causality и Resolution RNG provenance;
- resolution-aware replay semantic validation;
- atomic event-cap/RNG/state commit и zero-progress integration;
- current Engine fixtures, historical replay pins и determinism gates;
- Unit, Conformance и Integration tests из отдельного Combat Test Plan WP-09.

### 4.2 Не входит в WP-09

- общий effect trigger/stack/refresh/expiry engine — WP-10;
- полный stat clamp и произвольные effect-derived modifiers — WP-10;
- fighter-specific passive/resource reactions и полные Bear/Kangaroo/Gorilla kits — WP-11;
- Punish/Exposed/Finisher bonus model без явных DATA rules — WP-11;
- batch/CLI/metrics — последующие этапы;
- projectiles, vertical axis, healing, lifesteal и длительный periodic-damage hold;
- случайный passive miss или общий random critical chance;
- Unity playback, presentation, VFX/HUD и любые изменения `UnityClient`.

### 4.3 Явная граница с Effects

WP-09 может создать минимальное authoritative combat state, необходимое для resolution: HP, stagger, hard-control timer/state, active grab и defeat. Generic effect records, stacking, immunities, fatigue application и arbitrary stat modifiers принадлежат WP-10.

Пока WP-10 не реализован:

- `ContextDamageModifiers` имеет явно заданное identity-значение `fp_scale`, а не скрытый fallback;
- `BlockMods` и `DodgeMods` равны явно заданному `0`;
- никаких ветвей по конкретному `AnimalId` или `ActionId` не допускается;
- unsupported semantics отклоняются typed validation error до `journal.Begin`, а не упрощаются молча.

## 5. Phase ownership и snapshot discipline

| Фаза | Владелец WP-09 | Обязанность |
|---:|---|---|
| 7 `CollectIntents` | `IntentCollector` | Из due active windows/schedule entries создать immutable intent buffers. Не менять gameplay state и не consume RNG. |
| 8 `SortIntents` | `IntentOrderer` | Построить canonical conflict/resolution groups и стабильный порядок. Authoritative RNG не менять; exact tie только выявить и включить его draw в последующий атомарный plan. |
| 9 `Resolve` | `ResolutionSystem` | Проверить impact geometry/eligibility, разрешить Counter/Dodge/Block/damage/stagger и построить атомарный mutation/event plan. |
| 10 `WallsAndGrabs` | `SpatialControlSystem` | Добавить forced movement, wall, grab/throw/control consequences в тот же causal group. |
| 11 `Outcome` | `OutcomeSystem` | После полного commit группы определить defeat; после всех групп tick определить victory/draw. |

Обязательные snapshot rules:

- все due intents одного tick собираются из одного phase-7 snapshot;
- frozen target и direction приходят из commit WP-08;
- position, gap, target state и defense stats читаются в момент impact;
- `track_target` может обновить live target position/gap, но не frozen direction;
- одна resolution group строится из одного pre-impact snapshot;
- следующая group видит полностью committed результат предыдущей;
- before/after frames снимаются непосредственно вокруг конкретной authoritative mutation;
- rule evaluation не повторяется между plan и commit.

WP-08 `Emergency` input для HardOpportunity suppression равен true только если на phase-5 snapshot существует opponent committed, non-consumed damaging/grab impact с absolute tick, равным текущему tick, target равным actor и уже valid live state/geometry. Проверка не прогнозирует phase-6 movement, не consume RNG и не читает future opponent choice.

## 6. Internal intent и group model

Internal types не являются wire contracts и не сериализуются в replay целиком.

| Intent | Создаётся когда | Минимальные immutable поля |
|---|---|---|
| `DefenseIntent` | defense window активен на impact tick | intent/action/actor IDs, defense kind, window, chance inputs, eligibility tags, local index |
| `CounterIntent` | due `counter` primitive наблюдает legal incoming strike | intent/action/actor/target IDs, priority, match data, local index |
| `StrikeIntent` | due damage hit-schedule entry | intent/impact/hit-group/action/actor/target IDs, tick/ordinal, frozen direction, profile |
| `GrabIntent` | due `grab`/`throw` primitive | intent/grab/action/actor/target IDs, kind, priority, range/control profile |
| `ForcedMoveIntent` | успешный hit/throw/effect требует displacement | intent/source/actor/target IDs, direction, force profile, wall permission |

Минимальная модель resolution group:

- один обычный scheduled hit создаёт одну group;
- каждый следующий hit multi-hit action создаёт отдельную group;
- равноприоритетные simultaneous strikes, образующие trade, входят в одну group;
- все valid impacts trade рассчитываются из общего pre-impact snapshot;
- одна цель может быть затронута данным `HitGroup` не более одного раза;
- group коммитится целиком и никогда не открывается повторно;
- defeat проверяется после всех её valid impacts и spatial consequences;
- defeat между multi-hit groups отменяет будущие hits defeated actor/target по явной причине;
- forced movement и wall consequence остаются в causal resolution group исходного impact.

## 7. Canonical ordering и exact ties

Нормативный intent key:

```text
ResolutionClass,
ActionPriority DESC,
InitiativeValue DESC,
SeededTieBreak only if exact tie,
ActorStableId ordinal,
ActionStableId ordinal,
HitGroupIndex,
IntentLocalIndex
```

Предлагаемая fixed class order для materialized profiles:

```text
Counter = 0
Defense = 1
Strike = 2
Grab = 3
ForcedMove = 4
```

Class order определяет stage/group scheduling, но не должен сам по себе подменять CDS conflict matrix. Например, равные legal strikes могут образовать trade вместо выбора одного победителя.

Предлагаемое отображение существующих DATA priorities:

- `action_priority` — canonical order внутри класса;
- `resolution_priority` — Counter/общий exclusive conflict priority;
- `clash_priority` — strike clash/trade comparison;
- `grab_priority` — grab conflict comparison.

Stable IDs не могут молча заменить обязательный seeded tie-break. Для exact tied exclusive conflict предлагается:

1. canonical-sort contenders по Stable ID tuple только для отображения draw result;
2. выполнить ровно один unbiased bounded draw из stream `Resolution`, operation `TieBreak`, range `[0, contender_count)`;
3. сохранить draw на `ConflictResolved`;
4. использовать Stable IDs только как последний deterministic key после записанного tie result.

Равный strike trade не расходует TieBreak draw. Точная conflict matrix и список exclusive conflicts должны быть зафиксированы blocking tests до кода.

## 8. Impact geometry и defense precedence

Для каждого impact применяется точный порядок:

```text
1. Invalid/Defeated target или consumed HitGroup → reject/cancel.
2. Невалидная live geometry → AttackMissed(OutOfRange).
3. Matching legal Counter → исходный strike отменён, counter запускается.
4. Eligible Dodge + успешный Resolution draw → Dodged.
5. Eligible Block + успешный Resolution draw → Blocked и chip damage.
6. Иначе → AttackHit, normal damage/stagger и разрешённый forced movement.
```

Geometry rules:

- `hit_range_min` и `hit_range_max` включительны;
- используется body-aware surface gap из WP-07;
- target/direction frozen на commit, live positions читаются на impact;
- движение за range до impact даёт `AttackMissed`, а не Block/Dodge attempt;
- passive random miss отсутствует;
- `Undodgeable` пропускает Dodge check, но не range validation;
- grab по умолчанию не блокируется Block;
- Dodge может избежать grab только при явной `EvadeGrab` eligibility и фактическом выходе из grab range.

RNG rules:

- defense draw выполняется только после valid geometry и eligibility;
- draw range `[0, fp_scale)`, успех iff `result < chance_fp`;
- недоступная защита, deterministic outcome и geometry miss draw не consume;
- успешный draw хранится в `Blocked`/`Dodged`;
- failed draw переносится в последующий outcome event с reason `BlockFailed`/`DodgeFailed`;
- один canonical event содержит не более одного draw;
- indices Resolution stream непрерывны с `0` и не зависят от diagnostic mode.

## 9. Fixed-point formulas

Все операции выполняются checked integer arithmetic. Floor применяется после каждой указанной операции; перегруппировка выражений запрещена.

```text
mul_fp(a, b) = floor(a * b / FP_SCALE)
div_fp(a, b) = floor(a * FP_SCALE / max(b, 1))
```

### 9.1 Damage

```text
PowerTerm = BaseDamage + mul_fp(EffectivePower, PowerRatio)
RawDamage = mul_fp(PowerTerm, product_sorted(ContextDamageModifiers))
ArmorRatio = div_fp(EffectiveArmor, EffectiveArmor + armor_k)
AfterArmor = mul_fp(RawDamage, FP_SCALE - ArmorRatio)
AppliedDamage = max(MinDamage, clamp(AfterArmor, DamageFloor, DamageCap))
```

В WP-09 без effect engine `product_sorted(ContextDamageModifiers) = FP_SCALE` как явная boundary policy.

`DamageBreakdown.final` предлагается определить как вычисленный `AppliedDamage` до HP clamp. Тогда:

```text
ActualHpLoss = hp_before - hp_after
overkill = AppliedDamage - ActualHpLoss
final = ActualHpLoss + overkill
lethal iff hp_after == 0
```

Только `DamageApplied` меняет HP. `AttackHit` подтверждает geometry/resolution, но HP не изменяет.

### 9.2 Block

```text
BlockChance = clamp(
    BlockBase + (Guard - AttackGuardBreak) * BlockSlope + BlockMods,
    BlockMin,
    BlockMax)

BlockedDamage = max(
    ChipMin,
    mul_fp(AppliedDamage, FP_SCALE - BlockReduction))
```

До появления explicit `OnBlocked` profiles успешный Block применяет только chip damage; обычные stagger и knockback подавляются. Exact GuardBreak semantics требуют отдельного DATA-решения, скрытая эвристика запрещена.

### 9.3 Dodge

```text
DodgeChance = clamp(
    DodgeBase + (Evasion - AttackerPrecision) * DodgeSlope + DodgeMods,
    DodgeMin,
    DodgeMax)
```

Успешный Dodge отменяет damage/control текущей group; будущие multi-hit groups проверяют geometry и defense заново.

### 9.4 Stagger и hard control

```text
ControlRatio = div_fp(
    ControlK + AttackerControlPower,
    ControlK + DefenderControlResistance)

StaggerGain = mul_fp(BaseStagger, ControlRatio)

StunTicks = clamp(
    mul_fp(
        mul_fp(BaseStunTicks, ControlRatio),
        ControlFatigueMultiplier),
    StunMin,
    StunMax)
```

`StunTicks` применяется только при пересечении `stagger_threshold`, а не на каждом hit. Proposed v0.1 policy: после срабатывания meter сбрасывается ровно в `0`; это state-transition semantics, а не настраиваемый balance modifier.

Обычный успешный hit без пересечения stagger threshold не прерывает action в WP-09. Counter отменяет соответствующий incoming intent, defeat отменяет будущие groups, а hard control после group отменяет текущий action. `UninterruptibleImpact` сохраняет уже созданный intent до конца текущей group, но не будущие multi-hit entries. Расширенные Light/Medium `Armored`/`Unstoppable` filters остаются WP-10 вместе с effect/control modifier pipeline.

### 9.5 Combat MoveSelf

Non-System action modes `Approach`, `Retreat`, `Adaptive` и `Follow` выполняются в существующей фазе 6 как voluntary `MoveSelf`, до сбора impact intents:

- `move_distance` — total action budget, а не distance-per-tick;
- budget распределяется по `active_ticks`: `q = move_distance / active_ticks`, `r = move_distance % active_ticks`; первые `r` active ticks получают `q + 1`, остальные `q`;
- направление заморожено при commit; `track_target` обновляет только live gap/stop predicate;
- damaging/grab `Approach` и `Follow` останавливаются на `hit_range_max`; non-impact `Approach` — на `preferred_range_max`;
- `Retreat` останавливается при `gap >= preferred_range_min` или у стены;
- `Adaptive` использует frozen direction WP-08: toward останавливается на `preferred_range_max`, away — на `preferred_range_min`;
- unused budget после достижения stop condition или стены теряется и не переносится;
- simultaneous MoveSelf использует существующие WP-07 atomic pair/separation rules;
- только nonzero committed position delta считается progress.

### 9.6 Force и wall

```text
ForceRatio = div_fp(
    ForceK + ActionKnockback,
    ForceK + DefenderMass)

RequestedMove = clamp(
    mul_fp(BaseKnockback, ForceRatio),
    KnockbackMin,
    KnockbackMax)

BlockedByWall = max(0, RequestedMove - ActualMove)

WallDamage = clamp(
    mul_fp(BlockedByWall, WallDamagePerUnit),
    WallDamageMin,
    WallDamageMax)
```

Forced movement:

- выполняется после damage batch;
- не является voluntary movement WP-07 и не создаёт movement-resource gains;
- использует body-aware legal bounds `[arena.min + radius, arena.max - radius]`;
- обычный clamp у стены не наносит damage/stagger;
- wall consequence разрешён только explicit `wall_impact` profile/tag;
- wall damage является отдельной direct HP mutation после `WallImpact`, без повторного armor pass;
- термины `ActionKnockback` и `BaseKnockback` в формуле v0.1 означают одно existing поле `base_knockback`; отдельное скрытое число не вводится;
- `Push` перемещает target по hit direction, `Pull` — к actor без crossing, `Swap` атомарно обменивает центры, только если обе конечные позиции legal для соответствующих radii;
- wall threshold v0.1 равен предикату `BlockedByWall > 0`, без нового numeric key;
- tagged WallImpact дополнительно применяет один `StaggerGain` из того же action `base_stagger`; отдельного wall-stagger multiplier в WP-09 нет.

## 10. Grab, throw и control boundary

Минимальный WP-09 lifecycle должен быть generic и DATA-driven:

- `grab` участвует в отдельном conflict stage;
- успешный grab создаёт authoritative `ActiveGrabId` и `GrabStarted`;
- throw/release/interruption закрывают тот же grab ровно один раз через `GrabEnded` с stable reason;
- defeated actor/target не может оставаться в active grab;
- grab lockout/immunity проверяются до RNG/conflict outcome;
- throw mutation и её forced movement входят в одну resolution group;
- costs/cooldown committed action никогда не возвращаются после miss/block/dodge/counter/interruption.

Proposed v0.1 lifecycle использует уже существующие `max_hold_ticks` и `grab_lockout_ticks`:

- grab, начатый на tick `s`, остаётся legal не дольше полуинтервала `[s, s + max_hold_ticks)`;
- `throw`/`wall` primitive, defeat или hard-control interruption завершает grab в своей group;
- grab без terminal primitive завершается при выходе action из Active;
- после release на tick `r` новый grab запрещён на `[r, r + grab_lockout_ticks)` и снова legal при `tick >= r + grab_lockout_ticks`;
- `throw`/`wall` без active matching grab отменяется без RNG и mutation;
- `knockdown` tag сохраняется в reasons/tags, но полный `Fall → Grounded → GetUp` lifecycle и wakeup immunity принадлежат WP-10.

Эти правила закрывают минимальный WP-09 grab seam без изменения canonical DATA. Full fighter-specific grab prerequisites и resource reactions остаются WP-11.

## 11. Atomic mutation, outcome и safety

### 11.1 Resolution plan

Предлагаемая архитектура — immutable `ResolutionPlan`:

1. собрать snapshot и intents;
2. preview Resolution RNG без изменения authoritative stream;
3. вычислить полный ordered event/mutation batch группы;
4. проверить arithmetic invariants, event cap и causality;
5. атомарно commit RNG state, gameplay mutations и события;
6. при failure не оставлять partial HP/stagger/position/grab/event/RNG changes;
7. завершить бой reserved terminal invalid event по существующей Engine safety policy.

### 11.2 Defeat и Double KO

- defeat проверяется только после полного commit resolution group;
- все valid impacts trade применяются до первого `FighterDefeated`;
- оба lethal impacts одной group дают Double KO/Draw;
- `Defeated` имеет приоритет над timeout на последнем active tick;
- после `BattleEnded` gameplay mutations и events запрещены;
- summary и `BattleEnded.payload` должны совпадать.

### 11.3 Event cap и watchdog

- event-cap preflight учитывает полный batch группы, включая spatial/control/outcome consequences;
- нехватка capacity не допускает частичную group;
- authoritative HP, stagger, position, state/control или grab mutation считается progress;
- marker-only event, failed/missed impact без mutation и RNG draw сами по себе progress не создают;
- zero-progress counter сбрасывается только после фактически committed authoritative progress.

## 12. Canonical events и causality

Обязательные правила:

- sequence начинается с `0`, непрерывен; tick не убывает;
- все события simultaneous trade имеют один `resolution_group_id` и расположены contiguous;
- group после закрытия не может появиться снова;
- `source_event_id` указывает на одну первичную более раннюю причину;
- `related_event_ids` содержит дополнительные более ранние причины, sorted ordinal;
- единственная допустимая forward reference — `FinisherTriggered.predicted_lethal_event_id` на будущий lethal event в той же group;
- attack/damage tags и cancelled intent IDs canonical-sort ordinal;
- один event содержит максимум один RNG draw;
- before/after frames соответствуют ровно одной описанной mutation;
- повторное consumption одного `HitGroup` запрещено.

Рекомендуемая defeat lineage:

```text
FighterDefeated
  → StateChanged(HealthDepleted)
  → DamageApplied
  → AttackHit | Blocked | WallImpact
  → lifecycle/conflict event
  → ActionCommitted
  → DecisionMade | system source
```

Для normal hit минимальный causal order:

```text
AttackHit → DamageApplied → optional StateChanged/KnockbackApplied/WallImpact
          → optional wall DamageApplied → optional Defeated chain
```

Для successful defense:

```text
Dodged

или

Blocked → DamageApplied(chip) → optional lethal outcome
```

`Battle.Replay` должен добавить `ResolutionReplaySemanticValidator`, активный для Engine `0.4.x`. Он проверяет config-free cross-field semantics: group contiguity, damage/lethal/overkill, frames, Resolution RNG, causality, hit-group consumption, trade/Double-KO ordering и terminal consistency. Формулы, зависящие от balance DATA, проверяются config-aware Core/Integration tests, чтобы не нарушать dependency graph.

## 13. DATA inventory и закрытие gaps

### 13.1 Уже доступно в `combat.balance/0.1`

| Область | Existing DATA |
|---|---|
| Fixed-point | `global.sim.fp_scale = 1000` |
| Damage | `armor_k = 200`, floor `1`, cap `600` |
| Block | slope `3`, bounds `100..900` |
| Dodge | slope `3`, bounds `50..850` |
| Control/force | `control_k = 150`, `force_k = 120`, stun bounds `3..20` |
| Arena | coordinates `0..10000`, wall zone `1200` |
| Fighter stats | health, power, armor, precision, evasion, guard, guard break, initiative, mass, control power/resistance, stagger threshold, radius |
| Action profile | ranges, damage/chip, defense chance/reduction, priorities, stagger/stun, knockback/wall fields, movement mode, tags, interrupt profile, hit schedule |
| Anti-loop data | fatigue lookup, control immunity, grab lockout, wakeup immunity durations |

Канонические edge candidates уже существуют: Block (`bear_guarded_advance`, `gorilla_high_guard`), Dodge (`kangaroo_slip_hop`), Counter (`kangaroo_tail_counter`), grab/throw/wall (`gorilla_clinch_check`, `gorilla_pivot_throw`, `gorilla_wall_breaker`), wall strikes (`bear_rampage_charge`, `kangaroo_flying_kick`) и multi-hit (`bear_fury_maul`, `kangaroo_tempo_barrage`).

### 13.2 Proposed v0.1 policies для найденных gaps

| Gap | Proposed exact policy |
|---|---|
| Typed primitive теряется после parsing | Сохранить `Hit/Counter/Grab/Throw/Wall`, tick и ordinal в immutable descriptor; public impact tick projection не меняется. |
| Нет explicit `ResolutionClass` | Materialized class выводится только из primitive/tags по §7; ID-specific branches запрещены. |
| Counter filter/chance | Любой observable `strike`, кроме grab, подходит при counter `resolution_priority >= incoming resolution_priority`; CDS имеет gameplay priority, поэтому success deterministic и RNG не consume. |
| GuardBreak | Effective attacker `guard_break` участвует в BlockChance для каждого blockable strike; exact `guard_break` tag добавляет reason при failed block. `effect_guard_broken` отложен до WP-10. |
| Stagger reset | После threshold reset ровно в `0`; fatigue/immunity modifiers отложены до WP-10 и в WP-09 равны identity/absent. |
| Armored/interrupt strength | Обычный hit не interrupt-ит action в WP-09; Counter, hard control, defeat и current-group `UninterruptibleImpact` обрабатываются явно. Advanced filters — WP-10. |
| `ActionKnockback` vs `BaseKnockback` | Оба имени означают existing `base_knockback`; formula использует это значение в обоих обозначенных местах. |
| Wall threshold/stagger | Threshold — `BlockedByWall > 0`; tagged impact повторно применяет один action-level `StaggerGain`; wall damage direct и без armor. |
| MoveSelf/Push/Pull/Swap | Truth table закреплена в §§9.5–9.6 и Test Plan; no ID branches/defaults. |
| Grab/knockdown | Grab использует existing max-hold/lockout boundaries §10; полный knockdown lifecycle отложен до WP-10. |
| Per-hit profiles | Каждый entry использует общий immutable action-level profile; overrides отложены. |

`BLOCK-WP09-DATA-01 — CLOSED`: перечисленные policies утверждены как нормативные для WP-09, а workbook/generated artifacts и `combat.balance/0.1` не меняются. Если при реализации обнаружится действительно отсутствующее обязательное число, работа останавливается и оформляется новое решение; runtime fallback запрещён.

## 14. Версионирование и historical compatibility

Предлагаемый version plan:

- Engine: `battle.core/0.3.0 → battle.core/0.4.0`, потому что одинаковый input впервые получает combat resolution и иной canonical journal;
- ordering: сохранить `tick-pipeline/1`, если 12 фаз и normative key не меняются;
- RNG: сохранить `pcg32/1`, если algorithm/stream derivation не меняются;
- event/replay: сохранить `combat.event/0.1` и `combat.replay/0.1`, если существующих payloads достаточно;
- balance: сохранить `combat.balance/0.1`; proposed WP-09 policies используют только существующие fields и state-transition semantics, поэтому workbook/generated artifacts не меняются.

Compatibility requirements:

- historical Engine `0.1.0`, `0.2.0`, `0.3.0` fixture bytes и digests immutable;
- новый current wait и combat resolution fixtures создаются отдельными `0.4.0` artifacts;
- WP-08 decision semantic rules наследуются Engine `0.4.x`, а не отключаются из-за predicate, ограниченного `0.3.x`;
- resolution semantic rules применяются только к `0.4.x`, чтобы не переинтерпретировать historical events;
- WP-08 target-determinism gate после bump должен проверять immutable `0.3.0` pins; current generation parity переносится в WP-09 gate;
- Standard/Diagnostic profiles имеют одинаковые canonical events, RNG indices и final digest.

## 15. Утверждённые решения OPEN-WP09

Все решения утверждены владельцем продукта `2026-09-07` и имеют статус `CLOSED`.

| OPEN | Статус | Предлагаемое точное решение |
|---|---|---|
| `OPEN-WP09-01` | `CLOSED` | Этот Brief определяет scope. Отдельный `Combat_Test_Plan_WP-09_v0.1.md` является обязательной exact blocking matrix; код начинается только после её утверждения. |
| `OPEN-WP09-02` | `CLOSED` | Единственный допустимый baseline реализации — WP-08-complete из `origin/master@73a31bd` или более новый descendant со всеми green gates. |
| `OPEN-WP09-03` | `CLOSED` | WP-09 владеет defense/damage/stagger/force/wall/grab/defeat seams; generic effects — WP-10, full fighter kits/passives/resources — WP-11. Identity/zero modifiers задаются явно, ID-specific branches запрещены. |
| `OPEN-WP09-04` | `CLOSED` | Engine повышается до `battle.core/0.4.0`; event/replay/RNG/ordering/balance versions сохраняются. Workbook/generated artifacts не меняются. |
| `OPEN-WP09-05` | `CLOSED` | Config один раз materializes typed immutable resolution profiles до `journal.Begin`; runtime не парсит strings и не применяет defaults. |
| `OPEN-WP09-06` | `CLOSED` | Hit schedule хранит typed kinds `Hit/Counter/Grab/Throw/Wall`, relative/absolute tick, ordinal и Stable HitGroup ID; public `impact_ticks` остаётся numeric projection. |
| `OPEN-WP09-07` | `CLOSED` | Фазы 7–11 и snapshot/read points закрепляются таблицей §5; одна group использует один pre-impact snapshot, следующая видит committed результат предыдущей. |
| `OPEN-WP09-08` | `CLOSED` | Один schedule entry — одна group; multi-hit entries раздельны; equal legal strike trade использует одну group и общий snapshot; once-per-target enforced; defeat между groups отменяет future hits. |
| `OPEN-WP09-09` | `CLOSED` | Canonical key и priority mapping закрепляются §§7. Exact tied exclusive conflict использует один bounded Resolution/TieBreak draw; equal strike trade draw не использует; Stable IDs не заменяют обязательный draw. |
| `OPEN-WP09-10` | `CLOSED` | Frozen direction/target берутся из commit; live body-aware gap/state проверяются на impact; TrackTarget обновляет только target position/gap; ranges inclusive. |
| `OPEN-WP09-11` | `CLOSED` | Precedence строго `invalid → geometry miss → Counter → Dodge → Block → hit`; Dodge/Block ChanceCheck range `[0,fp_scale)`, success `< chance`, draw только после eligibility/geometry. Matching Counter с достаточным `resolution_priority` deterministic по gameplay-priority CDS и не consume RNG; TDD own-draw rule резервируется для будущего probabilistic counter profile. |
| `OPEN-WP09-12` | `CLOSED` | Damage formula/floor order — §9.1; `DamageApplied` — единственная HP mutation; `final` — pre-HP-clamp AppliedDamage, `overkill=final-(before-after)`, lethal iff after=0. |
| `OPEN-WP09-13` | `CLOSED` | Successful Block применяет chip и подавляет normal stagger/knockback. Effective attacker GuardBreak участвует в chance formula; exact `guard_break` tag добавляет failed-block reason, но `effect_guard_broken` application остаётся WP-10. |
| `OPEN-WP09-14` | `CLOSED` | Stagger накапливается по §9.4; stun создаётся только на threshold, meter reset-to-zero. Обычный hit не interrupt-ит action; Counter/hard control/defeat и current-group `UninterruptibleImpact` входят в WP-09, advanced Armored/Unstoppable strength filters — WP-10. |
| `OPEN-WP09-15` | `CLOSED` | System voluntary movement остаётся WP-07; combat MoveSelf исполняется в phase 6 по total-budget distribution и stop truth table §9.5. Follow обновляет live gap, но не frozen direction; Push/Pull/Swap — phase-10 forced movement §9.6. |
| `OPEN-WP09-16` | `CLOSED` | `ActionKnockback` и `BaseKnockback` — одно existing поле `base_knockback`. Push/Pull используют exact force formula, Swap — atomic legal center exchange. Wall threshold `BlockedByWall > 0`; tagged wall stagger повторяет action-level StaggerGain; wall damage direct, без второго armor pass. |
| `OPEN-WP09-17` | `CLOSED` | Grab authoritatively mutates ActiveGrabId; terminal primitive/defeat/hard control/end Active закрывает его ровно один раз; `max_hold_ticks` и `grab_lockout_ticks` используют полуинтервалы §10. Throw/wall остаются в исходной group; полный KnockedDown lifecycle — WP-10. |
| `OPEN-WP09-18` | `CLOSED` | Costs/cooldowns WP-08 не возвращаются. Generic Effect lifecycle, fatigue/immunity application и special modifiers не реализуются в WP-09; используются только явно разрешённые minimal state seams. |
| `OPEN-WP09-19` | `CLOSED` | External IDs создаются culture-invariant: `intent:<decision_id>:<ordinal:D2>`, `impact:<decision_id>:<ordinal:D2>`, `hit-group:<decision_id>:<ordinal:D2>`, `resolution:<tick:D10>:<ordinal:D4>`, `damage:<resolution_id>:<ordinal:D2>`, `conflict:<resolution_id>:<ordinal:D2>`, `grab:<decision_id>:<ordinal:D2>`. Counters per-battle, checked, начинают с `0`. |
| `OPEN-WP09-20` | `CLOSED` | `ResolutionPlan` preview/preflight/commit атомарен для state, RNG и journal. Event cap считает полную group. Watchdog progress — только committed HP/stagger/position/control/grab mutation. |
| `OPEN-WP09-21` | `CLOSED` | Event order, frames, RNG, group contiguity и defeat lineage следуют §12. Новый resolution validator включён только для `0.4.x`; WP-08 semantics продолжают действовать для `0.4.x`. Finisher emit только при гарантированном current-plan lethal prediction. |
| `OPEN-WP09-22` | `CLOSED` | Test Plan обязан покрыть CDS edges, tamper, event-cap, watchdog, last-tick precedence, Standard/Diagnostic parity, ×100 repeat, culture/insertion/mirror, target frameworks, Debug/Release и Windows/Linux. Core line coverage остаётся `≥85%`, transition guards — 100% branch. |
| `OPEN-WP09-23` | `CLOSED` | `BLOCK-WP09-DATA-01` закрывается policies §13.2 без workbook/schema migration; `combat.balance/0.1` и generated bytes остаются прежними. Config validator проверяет existing relationships/overflow/tag/schedule consistency до `journal.Begin`; новое обязательное число требует отдельного решения и останавливает реализацию. |

## 16. Обязательные acceptance-направления

Exact IDs, входы, события и digests должны быть заданы в отдельном Combat Test Plan WP-09. Минимальная blocking matrix должна включать:

### 16.1 Unit

- typed schedule/profile materialization и invalid DATA;
- canonical ordering, class/priority mapping и exact tie;
- inclusive geometry boundaries и consumed HitGroup;
- successful/failed/no-draw Counter, Dodge и Block paths;
- damage/chip/armor/floor/cap/overkill/lethal arithmetic;
- checked overflow rejection;
- stagger threshold/reset/control timing;
- trade, multi-hit cancellation и Double KO;
- forced movement, wall clip/damage и body bounds;
- grab start/end/throw/interruption;
- atomic group event-cap boundary и watchdog progress.

### 16.2 Conformance/replay

- typed round-trip всех resolution payloads;
- group contiguity и no reopen;
- source/related/forward-reference rules;
- damage/frames/lethal/overkill consistency;
- Resolution RNG stream/operation/range/result/index;
- hit-group single consumption;
- no premature defeat inside trade;
- valid DoubleKO and terminal consistency;
- tamper cases с пересчитанным integrity;
- historical fixture SHA/digest immutability.

### 16.3 Integration/golden

- normal hit → damage → defeat → BattleEnded;
- out-of-range miss;
- Block success/failure и chip lethal boundary;
- Dodge success/failure и Undodgeable no-draw;
- Counter success/cancel path;
- equal strike trade/Double KO;
- multi-hit с отдельными groups и остановкой после defeat;
- knockback без стены, clip у стены и tagged WallImpact;
- grab start/end, throw и interruption;
- last-active-tick defeat before timeout;
- event cap до/ровно на границе group;
- current wait/decision behavior на Engine `0.4.0`;
- Standard/Diagnostic identical canonical replay;
- deterministic repeat/culture/insertion/mirror/TFM/OS gates.

## 17. Предлагаемая структура реализации

Production modules:

```text
Battle.Core/Resolution/
  ResolutionProfiles.cs
  ResolutionSetupMaterializer.cs
  ImpactIntentCollector.cs
  ImpactIntentOrderer.cs
  ConflictResolver.cs
  DefenseResolver.cs
  DamageResolver.cs
  ControlResolver.cs
  ForcedMovementResolver.cs
  GrabResolver.cs
  ResolutionPlan.cs

Battle.Core/Outcome/
  GroupOutcomeResolver.cs

Battle.Replay/
  ResolutionReplaySemanticValidator.cs
```

Названия файлов не являются публичным контрактом и могут быть скорректированы после аудита актуального WP-08 baseline. Архитектурные границы обязательны:

- `Battle.Core` зависит только от `Battle.Contracts` и BCL;
- internal dense handles/intents не выходят в wire contracts;
- `Battle.Replay` остаётся config-free;
- formulas/data-aware validation тестируется в Core/Integration;
- никаких Unity, wall-clock, collection-order или floating-point dependencies.

## 18. План подготовки и реализации

1. **Baseline:** `DONE` — `feature/wp-09-resolution` синхронизирована с `origin/master@73a31bd` и содержит WP-08-complete.
2. **Decision freeze:** `DONE` — `OPEN-WP09-01..23` приняты; `BLOCK-WP09-DATA-01` закрыт без DATA migration.
3. **Test Plan:** `APPROVED / BLOCKING` — [Combat Test Plan WP-09 v0.1](./Combat_Test_Plan_WP-09_v0.1.md) содержит unique blocking IDs, exact inputs/events/RNG/groups и traceability к Brief.
4. **Contracts/config:** `DONE` — typed resolution profiles/validation добавлены, wire/balance versions и canonical DATA сохранены.
5. **Core:** `DONE` — phases 7–11, atomic ResolutionPlan и group-aware outcome реализованы.
6. **Replay:** `DONE` — resolution semantic validator активен для `0.4.x`; historical interpretation не изменена.
7. **Tests/fixtures:** `DONE` — 128/128 IDs, пять новых `0.4.0` fixtures и immutable historical pins.
8. **Verification:** `LOCAL DONE` — restore, Debug/Release build/test, generated, TFM, coverage, historical и determinism gates green; CI pending.
9. **Closure:** `PENDING CI` — объявить WP-09 `COMPLETED` после green Windows/Linux matrix.

## 19. Readiness и Definition of Done

Текущее состояние: `IMPLEMENTED / LOCAL GATES GREEN`.

Все условия реализации выполнены локально:

- `BLOCK-WP09-BASE-01 — CLOSED`: ветка содержит WP-08-complete baseline;
- `OPEN-WP09-01..23 — CLOSED`;
- `BLOCK-WP09-DATA-01 — CLOSED` exact v0.1 policies §13.2 без schema migration;
- Combat Test Plan WP-09 принят как blocking matrix.
- Engine `battle.core/0.4.0` и все 128 acceptance IDs реализованы;
- Release/Debug, replay, target determinism, generated и coverage gates green;
- остаётся внешний completion gate: GitHub Actions Windows/Linux × Debug/Release.

WP-09 может стать `COMPLETED` только когда:

- все blocking acceptance IDs имеют автоматические тесты и green;
- Engine `0.4.0` replay детерминирован и semantic-verifiable;
- historical `0.1.0`/`0.2.0`/`0.3.0` bytes и digests сохранены;
- generated artifacts воспроизводимы, если DATA менялась;
- Release и Debug build/test проходят без warnings/errors;
- deterministic/coverage/target/historical gates проходят;
- GitHub Actions Windows/Linux matrix green;
- `UnityClient` не изменён.
