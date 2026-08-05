# WP-07 Movement

> Статус: `IMPLEMENTED / LOCAL ACCEPTANCE PASS; CI PENDING`. Все blocking cases реализованы, а локально исполнимые проверки прошли на Windows; финальный `COMPLETED` ожидает фактический green прогон Release matrix на Windows/Linux. `OPEN-WP07-13` закрыт утверждённым defer stat clamp до WP-10.
>
> Exact pass/fail: [Combat Test Plan WP-07 v0.1](./Combat_Test_Plan_WP-07_v0.1.md).

## Цель

Реализовать детерминированную одномерную геометрию и фазу добровольного движения поверх завершённого WP-06 Engine Shell:

- authoritative position и facing для двух бойцов;
- геометрию closed arena, body radius, surface gap и wall headroom;
- availability, commit и исполнение `sys_approach`/`sys_retreat`;
- neutral-band fallback к `sys_wait` без weighted selector;
- одновременное движение из одного snapshot;
- deterministic separation без overlap/crossing;
- canonical `MoveStarted → PositionChanged → MoveEnded`;
- строгую pre-start/config validation и movement-specific replay verification.

WP-07 не добавляет атаки, урон, knockback, grabs, effects или weighted AI.

## Нормативные источники

Прочитаны только связанные с movement разделы оригинальных документов:

- Combat Design Specification v0.1: §§2, 3.1–3.3, 4.1–4.3, 5.1–5.3, 6.2, 8.1, 9.1–9.2, 14.1, 15, 17.1–17.2 и 18;
- Combat Lab Technical Design v0.1: §§2.2, 5–7.1, 11–13.1, 14.1, 14.3, 15.2, 21.2–21.4, 23.1–23.2 и приложения B, E;
- Combat Event & Replay Schema v0.1: §§3.2, 4, 6.1, 7.1, 8.1–8.3, 9.2–9.3, 10.2, 13.2, 19.3–19.4, 21.2–21.4 и приложение D;
- canonical [combat.balance.v0.1.json](../CombatLab/config/generated/combat.balance.v0.1.json) — значения и Stable IDs;
- [Implementation Status](./Implementation_Status.md), [Decisions](./Decisions.md), [Index](./Index.md), [WP-06 Brief](./WP-06_Brief.md) и исполненный [Combat Test Plan WP-06](./Combat_Test_Plan_v0.1.md) — текущая граница реализации.

Product Vision не задаёт movement-формул или wire-семантики и поэтому не использовался как источник требований WP-07.

При конфликте игровое правило берётся из CDS, число/Stable ID — из canonical config, wire/integrity — из Replay Schema, архитектура — из Technical Design, exact pass/fail — из Test Plan WP-07. Уже написанный код не является нормативным источником.

## Scope

### Входит

- pure 1D geometry без Unity Physics;
- body-aware arena bounds и initial-state validation;
- derived `MoveSpeed` и `CollisionRadius` в runtime fighter state;
- movement profiles `sys_approach` и `sys_retreat` из config;
- deterministic availability, direction, active segment и recovery lifecycle;
- фаза 6 `MovementSystem` в существующем 12-фазном loop;
- atomic pair planning, wall clamp, target-band clamp и separation;
- facing update после полного movement batch;
- typed movement events, frames, causality и semantic verification;
- unit/conformance/integration/determinism/coverage gates.

### Не входит

- Tactic/Situation/Synergy/Counter/Variety/Opportunity weights и общий weighted selector — WP-08;
- выбор/commit combat actions — WP-08; их attack/Dodge/`MoveSelf` execution и forced movement — WP-09; movement modifiers/effect triggers — WP-10, fighter-specific resource/passive reactions — WP-11;
- forced movement, knockback, pull, swap, throw, WallImpact damage/stagger — WP-09;
- tempo/grip/passive/effect triggers от движения — WP-10/WP-11;
- Unity playback/runtime изменения;
- batch, storage, deployment и production rollout.

## Current DATA snapshot

Новые geometry/separation keys для WP-07 не требуются: bounds выводятся из arena/radius/range, а minimum separation равен сумме collision radii. Однако CDS §6.2 требует `stat.move_speed.min/max`, которых нет в Workbook/generated config; это отдельный `OPEN-WP07-13` ниже.

| DATA | v0.1 |
|---|---:|
| `global.arena.min_position` | `0` |
| `global.arena.max_position` | `10000` |
| `global.arena.start_position_a` | `2000` |
| `global.arena.start_position_b` | `8000` |
| `global.arena.wall_zone_size` | `1200` |
| `global.sim.fp_scale` | `1000` |

| Fighter | `MoveSpeed` | `CollisionRadius` |
|---|---:|---:|
| bear | `70` | `520` |
| gorilla | `75` | `550` |
| kangaroo | `135` | `430` |

| System action | Weight | Preferred range | Startup / Active / Recovery | Movement |
|---|---:|---:|---:|---|
| `sys_approach` | `650` | `0..1500` | `1 / 5 / 1` | `Approach`, TrackTarget, `move_distance=0` |
| `sys_retreat` | `450` | `1600..3000` | `1 / 5 / 1` | `Retreat`, TrackTarget, `move_distance=0` |
| `sys_wait` | `150` | `0..10000` | `0 / 3 / 0` | `None` |

`move_distance=0` у двух system movement actions означает непрерывный segment со скоростью derived `MoveSpeed`, а не нулевое движение. Максимальная длительность segment определяется `active_ticks`.

## Решения OPEN-WP07

| OPEN | Решение |
|---|---|
| `OPEN-WP07-01` | Этот Brief и Test Plan WP-07 являются scope/pass-fail gate. Matrix реализована и локально green; этап остаётся не `COMPLETED` только до фактического Windows/Linux CI pass. |
| `OPEN-WP07-02` | Canonical scale key — `global.sim.fp_scale`. `global.sim.math_scale` в TDD §7.1 считается терминологической ошибкой; alias/default запрещены. CDS shorthand `arena.*` отображается на `global.arena.*`. |
| `OPEN-WP07-03` | Legal center bounds: `[arena.min + radius, arena.max - radius]`. Initial bodies обязаны помещаться, сохранять A-left/B-right order и не пересекаться. Minimum center distance — сумма radii; отдельного separation key нет. |
| `OPEN-WP07-04` | Neutral surface-gap band — `[sys_approach.preferred_range_max, sys_retreat.preferred_range_min]`, сейчас `[1500,1600]`. Retreat legal при `gap < 1500`, Approach — при `gap > 1600`, Wait — на inclusive band или когда нужное движение не имеет headroom. Это исключает positive-weight overlap и oscillation до WP-08. |
| `OPEN-WP07-05` | `MoveSpeed` — distance units per active tick. Magnitude считается положительно через checked integer/`FixedMath`, затем применяется sign; system state multiplier в WP-07 равен FP identity. По закрытому `OPEN-WP07-13` stat clamp отложен и не заменяется default. Target-limited pair budget распределяется пропорционально speed capacity методом largest remainder. |
| `OPEN-WP07-06` | Оба request строятся из одного phase-start snapshot. Planning, wall clamp, band clamp и separation завершаются до единой atomic mutation. Remainder tie использует уже вычисленный WP-06 InitiativeOrder, а не Side, iteration order или новый RNG draw. |
| `OPEN-WP07-07` | Overlap устраняется minimum rollback, пропорциональным inward displacement, который создал overlap. Stationary actor получает нулевую долю; equal remainder — InitiativeOrder. Crossing запрещён. Неразрешимый deficit является invariant failure. |
| `OPEN-WP07-08` | Direction определяется и фиксируется на commit. `ActionCommitted.target_position` остаётся commit snapshot. TrackTarget разрешает проверять live surface gap для stop predicate, но не меняет frozen direction/target field. Facing пересчитывается только после полного movement/separation batch. |
| `OPEN-WP07-09` | Movement segment начинается в первый Active tick. Phase 6 может emit `MoveEnded` и поставить internal completion marker, но Action transition выполняет только phase 4 следующего тика. Порядок 12 фаз и `ordering_version` не меняются. |
| `OPEN-WP07-10` | Event chain: `ActionCommitted → ActionPhaseChanged(Startup→Active) → MoveStarted → PositionChanged* → MoveEnded → ActionPhaseChanged(Active→Recovery) → ActionPhaseChanged(Recovery→null)`. Все post-commit WP-07 events actor-only: `target_id` и target frames null; committed target доступен через source chain. RNG и `resolution_group_id` null. Stop reason определяется для каждого actor: `WallReached`, если request обрезан стеной; иначе `PreferredRangeReached`, если pair достиг band; иначе `SegmentExpired` на последнем active tick. |
| `OPEN-WP07-11` | `PositionChanged.requested_delta` — signed target-limited attempted delta; `actual_delta = to_position - from_position`; `blocked_by_wall` содержит только wall-clipped magnitude. Range allocation и separation не маскируются как wall block. Actual nonzero movement является progress; marker/zero-delta event сам по себе position progress не создаёт. |
| `OPEN-WP07-12` | WP-07 подключает только voluntary movement и pure separation. Existing payloads, event/replay schema и `ordering_version` достаточны и не меняются. Engine повышен с `battle.core/0.1.0` до `battle.core/0.2.0`. До bump сохранён immutable `wait-equal-l1.engine-0.1.0.json` (file SHA-256 `4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292`); current-engine `wait-equal-l1.engine-0.2.0.json` создан отдельно (file SHA-256 `ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409`). Forced-movement combat integration остаётся WP-09. |
| `OPEN-WP07-13` | **CLOSED — DEFER APPROVED.** CDS требует clamp `MoveSpeed` по отсутствующим `stat.move_speed.min/max`. Для WP-07 общий stat-clamp и DATA/schema migration утверждённо отложены до WP-10: используется checked `base + gear` pipeline без clamp, а до `journal.Begin` требуется positive derived speed. Checks остаются Core applicability validation; `combat.balance/0.1`, config version/hash и Workbook не меняются. Runtime default запрещён. WP-10 обязан закрыть полный stat-clamp до подключения movement effects/modifiers. |

`OPEN-WP07-01..13` закрыты и реализованы. Проектных/DATA blockers нет; exact acceptance закреплён в Test Plan WP-07. Локальные gates green, внешний Linux CI ещё не исполнялся для незакоммиченных изменений.

## Геометрия

Для fighter `i`:

```text
center_min_i = checked(arena_min + radius_i)
center_max_i = checked(arena_max - radius_i)
center_distance = abs(position_a - position_b)
minimum_center_distance = radius_a + radius_b
gap = max(0, center_distance - minimum_center_distance)
```

Инварианты после initialization и каждого movement batch:

- `center_min_i <= position_i <= center_max_i`;
- `position_a < position_b` до появления explicit side-swap semantics в WP-09;
- `position_b - position_a >= radius_a + radius_b`;
- `gap >= 0`;
- facing A/B направлен к фактической позиции opponent;
- все промежуточные суммы/произведения checked `Int64`, публичные coordinates — `Int32`.

Sprite width, Unity transform/Rigidbody и frame delta не участвуют в расчёте.

## Neutral band и availability

```text
inner = sys_approach.preferred_range_max   # 1500
outer = sys_retreat.preferred_range_min    # 1600
require 0 <= inner <= outer

gap < inner  => Retreat, если actor имеет outward headroom; иначе Wait
inner <= gap <= outer => Wait
gap > outer  => Approach
```

Для одного actor production availability возвращает ровно один positive-weight system candidate. Поэтому WP-07 использует `OnlyLegalAction`, не делает Decision RNG draw и не реализует weighted выбор. Existing fixed zero-weight priority `sys_approach > sys_retreat > sys_wait` сохраняется как guard, но не подменяет predicates.

Direction для actor слева: Approach `Right`, Retreat `Left`; для actor справа — наоборот. При commit target position равен opponent position из decision snapshot.

## Movement planning

Фаза 6 работает с immutable phase-start positions и active movement descriptors обоих fighters.

1. Вычислить current surface gap и требуемое изменение до ближайшей neutral-band boundary.
2. Получить positive effective `MoveSpeed` каждого active mover; значение замораживается на старте segment и записывается в `MoveStarted.speed_per_tick`.
3. Распределить общий target-limited budget пропорционально speed capacity. Использовать integer quotient; оставшуюся единицу получает больший fractional remainder, exact tie — первый в InitiativeOrder.
4. Применить center wall headroom. Wall-clipped часть записать отдельно; доступный остаток budget детерминированно перераспределить другому active mover в пределах его unused speed/headroom.
5. Построить provisional positions, не изменяя `BattleState`.
6. Выполнить separation rollback при overlap/crossing.
7. Проверить invariants, одним batch применить final positions/facing и только затем сформировать drafts в стабильном event-class/InitiativeOrder.

No-op arithmetic и overflow не исправляются defaults. Невозможный batch после валидного старта даёт `FailedInvariant`, а не draw/loss.

## Separation

Separation не является hit, damage, knockback или wall impact.

Если provisional center distance меньше суммы radii:

```text
deficit = radius_sum - provisional_center_distance
cause_i = inward part of actor_i voluntary displacement
require cause_a + cause_b >= deficit
rollback_i = proportional_share(deficit, cause_i)
```

- actor с `cause=0` не сдвигается;
- сумма rollback точно равна deficit;
- integer remainder разрешается InitiativeOrder;
- rollback не выходит за pre-phase valid positions и потому не требует wall spill;
- final center distance ровно не меньше radius sum;
- crossing после correction запрещён;
- ненулевой rollback получает отдельный `PositionChanged(movement_kind=Separation)` с `action_id=null`, `decision_id=null`;
- для correction `from_position` равен provisional voluntary position, `to_position` — final corrected position, а signed `requested_delta = actual_delta = to_position - from_position`; `blocked_by_wall=0`;
- before/after actor frames отражают ровно эту submutation, target frames null.

## Action lifecycle

`sys_approach`/`sys_retreat` исполняют frozen config timings `1 / 5 / 1`:

1. Decision phase: `DecisionMade`, затем atomic `ActionCommitted`; state становится `Approach`/`Retreat`, phase `Startup`.
2. Phase 4: по истечении startup emit `ActionPhaseChanged(Startup→Active)`.
3. Первый Active phase 6: emit `MoveStarted`, затем movement batch.
4. Каждый обработанный active request создаёт `PositionChanged`; `actual_delta=0` допустим только при объяснимом target/wall/separation clamp.
5. При достижении band/wall либо после последнего active movement tick phase 6 emit `MoveEnded` и ставит completion marker.
6. Следующий phase 4 переводит action `Active→Recovery`, сохраняя ownership фаз из TDD; одновременные movement lifecycle transitions упорядочиваются по InitiativeOrder.
7. По окончании recovery phase 4 emit `ActionPhaseChanged(Recovery→null, phase_ticks=0)`, затем переводит fighter в `DecisionReady` и очищает action/phase/timer. Event сохраняет завершившиеся `action_id`/`decision_id`, имеет null target и source на `Active→Recovery`; after actor frame уже очищен.

Terminal outcome имеет приоритет: после `BattleEnded` movement/action events не дописываются, а финальный frame может содержать незавершённый segment.

## Canonical movement events

Внутри phase 6 drafts создаются только после расчёта всего pair batch:

1. `MoveStarted` active movers в InitiativeOrder;
2. voluntary `PositionChanged` в InitiativeOrder;
3. separation `PositionChanged` в InitiativeOrder;
4. `MoveEnded` в InitiativeOrder.

Для всех трёх event types:

- `actor_id` — mover, `target_id=null`;
- target before/after frames — null;
- `rng=null`, `resolution_group_id=null`;
- voluntary events несут committed `action_id`; separation correction имеет `action_id=null`;
- before/after actor frames соответствуют той же ordered submutation;
- related IDs сортируются ordinal по EventId.

Для WP-07 `ActionPhaseChanged` actor — performer, а `target_id` и target before/after frames равны null; committed target восстанавливается по source chain до `ActionCommitted`. Actor before/after frames присутствуют. Startup→Active, Active→Recovery и Recovery→null сохраняют committed `action_id`/`decision_id`, не несут RNG/group и используют reason codes `StartupCompleted`, `MovementCompleted`, `RecoveryCompleted` соответственно.

Stop conditions `MoveStarted` имеют фиксированный порядок:

1. `WallReached`;
2. `PreferredRangeReached`;
3. `SegmentExpired`.

Primary causality:

- Startup→Active phase event source — `ActionCommitted`;
- `MoveStarted` source — Startup→Active event;
- voluntary `PositionChanged` source — `MoveStarted`;
- separation event source — actor voluntary change, related IDs содержат все movement changes, создавшие overlap;
- `MoveEnded` source — последний actor movement event либо `MoveStarted` при отсутствии delta;
- Active→Recovery source — `MoveEnded`;
- Recovery→null source — предыдущее Active→Recovery event.

## Validation

До `journal.Begin` должны быть отклонены:

- missing/wrong-type arena bounds, starts, `global.sim.fp_scale`;
- `min >= max`, checked overflow center bounds;
- non-positive base `MoveSpeed`/`CollisionRadius` и non-positive/overflow derived значения после modifier pipeline;
- body, не помещающееся в arena;
- start center за body-aware bounds;
- initial overlap, equal/crossed A/B positions;
- missing/wrong system action shape: owner `all`, slot `System`, category `Movement`, matching movement mode `Approach`/`Retreat`, `track_target=true`, `move_distance=0`, positive weight/active duration; startup и recovery требуют `min=base=max>=0`;
- nonzero energy/resource cost или cooldown, combat hit/damage/stagger/knockback/wall fields, non-empty hit schedule либо `wall_impact=true` у `sys_approach`/`sys_retreat`;
- invalid neutral band (`approach.max > retreat.min`);
- non-positive movement active duration или invalid startup/recovery;
- unsupported negative cost/speed/range и movement arithmetic overflow.

Expected invalid data возвращает sorted `BattleResult.Rejected` без journal session. Programming misuse и невозможное состояние после старта сохраняют WP-06 `FailedInvariant` lifecycle.

## Reference fixtures

Главный pinned golden — `approach_band_l3`:

- fighter A: bear + `gear_utility_sprint_soles`, start center `4000`, radius `520`, derived speed `82`;
- fighter B: kangaroo + `gear_utility_sprint_soles`, start center `6555`, radius `430`, derived speed `147`;
- initial surface gap `1605`, outer neutral boundary `1600`;
- proportional budget `5` даёт A `+2`, B `-3`;
- final centers `4002` и `6552`, gap `1600`;
- `battle.time_limit_ticks=3`, full-health timeout — Draw;
- exact 18-event oracle задан в Test Plan;
- fixture config hash: `sha256:6abd6c81701abacdb394fe637e450ae357719e5caf49ef17ccb269573e2ee7b4`;
- input digest: `sha256:dae170bccf84b44e6c0c173692e6198c45ec0e0ae1484bf9c7dd989cad4a0b20`;
- final digest: `sha256:956b15fd915222f8b404823dfab070c6bc2f6e1852309d1ef12dc988954cfe93`;
- fixture file SHA-256: `7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873`.

Дополнительные fixtures покрывают retreat к inner boundary, one-sided movement у стены, separation even/odd deficit, mirror/InitiativeOrder, segment completion и no-progress request.

Exact wall fixture `retreat_wall_l3`: starts `521/2966`, initial gap `1495`; A имеет left headroom `1`. Initial allocation `A -2 / B +3`, wall clamp A даёт actual `-1`, wall block `1`, после redistribution B request/actual `+4`; final centers `520/2970`, gap `1500`. Stop reasons: A `WallReached`, B `PreferredRangeReached`.

Separation не достижим из production `Approach/Retreat/Wait` при исправной target-band clamp. Поэтому его exact acceptance использует immutable resolver input, а не test-only production selection: centers `4000/5100`, radii `500/500`, active inward requests `+100/-100`, provisional `4100/5000`, correction `-50/+50`, final `4050/5050`. Production integration этого seam начинается только вместе с action/forced movement в WP-09.

## Реализованные изменения

Реализация включает:

- `Battle.Core/Movement`: `ArenaGeometry`, proportional allocator, pair resolver и separation resolver;
- расширение runtime fighter/setup/snapshot: radius, effective move speed, movement segment и completion marker;
- generalization system action definitions и WP-07 availability;
- подключение phase 6 и movement-aware phase 4 в `TickCoordinator`;
- Core pre-start applicability validation без изменения `combat.balance/0.1` по утверждённому defer `OPEN-WP07-13`;
- movement-specific replay semantic checks;
- pre-migration materialization исторического `wait_equal_l1@battle.core/0.1.0` в новый immutable fixture без перезаписи существующих файлов и отдельный current-engine wait fixture;
- `ContractVersions.Engine` migration `battle.core/0.1.0 → battle.core/0.2.0` без изменения event/replay/ordering versions;
- unit, conformance, integration, golden, target-determinism и coverage tests;
- CI gates `verify-wp07-coverage.ps1`, pinned target probe и Windows/Linux Release matrix.

Public movement payloads/event enums и replay JSON Schema уже достаточны. Их shape/version не меняется без отдельно обнаруженного blocking conflict.

## Выполненный план реализации

1. **Approved DATA policy — выполнено.** Defer `OPEN-WP07-13` защищён без stat clamp/default и без Workbook/schema migration.
2. **Pre-migration archive — выполнено.** Historical wait `0.1.0` создан до Engine bump и SHA-pinned.
3. **Version/geometry foundation — выполнено.** Engine `0.2.0`, pure checked geometry и negative cases реализованы.
4. **Runtime movement model — выполнено.** Derived radius/speed, system actions, neutral-band availability и frozen descriptor реализованы.
5. **Phase 6 vertical slice — выполнено.** Atomic allocation, wall clamp, separation, facing и lifecycle реализованы.
6. **Replay projection — выполнено.** Canonical chain, strict semantic verifier, round-trip и tamper tests green.
7. **Acceptance/gates — локально выполнено.** 528 automated tests green, target parity совпадает с pinned fixture, critical coverage 100%, Battle.Core line gate ≥85%.
8. **Documentation closure — выполнено до статуса CI pending.** Golden digests закреплены; `COMPLETED` разрешён только после внешнего Windows/Linux CI pass.

## Definition of Done WP-07

- [x] Утверждённый defer `OPEN-WP07-13` реализован без runtime default и без незаявленного DATA/schema drift.
- [x] Все локально исполнимые blocking cases Test Plan WP-07 green без skip/flaky retry.
- [x] Body-aware bounds, gap, neutral band, simultaneous planning и separation реализованы без float/Unity Physics.
- [x] `sys_approach`/`sys_retreat` исполняют config timings/ranges/speed и не потребляют RNG.
- [x] Movement chain проходит schema/semantic/integrity verification и profile parity.
- [x] До Engine bump создан и SHA-pinned immutable historical wait fixture `battle.core/0.1.0`; existing WP-05 fixtures не изменены, current-engine wait закреплён отдельно.
- [x] Golden `approach_band_l3` закреплён exact events и digests для `battle.core/0.2.0`.
- [x] Critical movement/geometry/transition branches имеют 100% coverage; Battle.Core line gate не ниже 85%.
- [ ] Фактический GitHub Actions Windows/Linux Release matrix green.
- [x] `netstandard2.1`/`net10.0` локально byte-equal и совпадают с pinned fixture; Windows/Linux matrix настроена, но Linux job ещё не исполнялась.
- [x] `dotnet restore --locked-mode`, Release build/test и WP-04 generated verification green.
- [x] `UnityClient` не изменён.
- [x] `Implementation_Status.md` обновлён честным статусом `LOCAL PASS; CI PENDING`, без преждевременного `COMPLETED`.
