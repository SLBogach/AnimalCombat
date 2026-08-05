# Combat Test Plan WP-07 v0.1 — Movement

> Статус: `APPROVED FOR IMPLEMENTATION`; WP-07 production-код ещё не реализован. `OPEN-WP07-13` закрыт утверждённым defer stat clamp до WP-10.
>
> Этот документ является exact pass/fail-матрицей WP-07 и закрывает `OPEN-WP07-01..13` только для 1D geometry, system approach/retreat и separation.

## 1. Назначение

Test Plan задаёт blocking acceptance для:

- body-aware closed-arena geometry;
- surface gap, wall headroom и facing;
- `sys_approach`/`sys_retreat`/`sys_wait` availability;
- frozen movement commit и action lifecycle;
- phase-6 atomic pair movement;
- target-band allocation, wall clamp и separation;
- movement events, frames, causality и replay verification;
- determinism, safety, regression и architecture gates.

Статус `APPROVED FOR IMPLEMENTATION` означает, что pass/fail и DATA policy достаточно точны для начала кода. Он не означает, что WP-07 уже завершён.

## 2. Источники и precedence

- Combat Design Specification v0.1: §§2, 3.1–3.3, 4.1–4.3, 5.1–5.3, 6.2, 8.1, 9.1–9.2, 14.1, 15, 17.1–17.2, 18.
- Combat Lab Technical Design v0.1: §§2.2, 5–7.1, 11–13.1, 14.1, 14.3, 15.2, 21.2–21.4, 23.1–23.2, приложения B/E.
- Combat Event & Replay Schema v0.1: §§3.2, 4, 6.1, 7.1, 8.1–8.3, 9.2–9.3, 10.2, 13.2, 19.3–19.4, 21.2–21.4, приложение D.
- Canonical balance config v0.1: arena, fighter and system-action values.
- [WP-07 Brief](./WP-07_Brief.md) — scope, точные movement decisions и implementation plan.
- [Decisions](./Decisions.md) — история разрешения source ambiguities.

Source order: CDS combat semantics → balance DATA/Stable IDs → Replay wire/integrity → TDD architecture → этот документ pass/fail. JSON Schema остаётся первым авторитетом для wire shape.

## 3. Решения OPEN-WP07

| OPEN | Exact acceptance decision |
|---|---|
| `OPEN-WP07-01` | Этот документ является blocking acceptance matrix; все строки §13 обязательны. |
| `OPEN-WP07-02` | Canonical key — `global.sim.fp_scale`; `math_scale` alias/default запрещён. Arena shorthand разрешается только в prose, runtime keys имеют prefix `global.arena.`. |
| `OPEN-WP07-03` | Position — center. Legal center interval учитывает radius; start order A-left/B-right и non-overlap обязательны. Separation distance выводится из radii, без нового DATA key. |
| `OPEN-WP07-04` | Inclusive neutral band: inner=`approach.max=1500`, outer=`retreat.min=1600`; Retreat below inner, Wait inside, Approach above outer. |
| `OPEN-WP07-05` | Derived MoveSpeed — position units per active tick, frozen at segment start; identity state multiplier равен runtime fp scale. По закрытому `OPEN-WP07-13` stat clamp отложен и не подменяется default. |
| `OPEN-WP07-06` | Pair planning читает один phase-start snapshot; target budget использует proportional largest remainder, tie — WP-06 InitiativeOrder, новый RNG draw запрещён. |
| `OPEN-WP07-07` | Separation rollback пропорционален inward displacement, создавшему penetration; stationary actor не сдвигается; crossing запрещён. |
| `OPEN-WP07-08` | Commit direction/target position frozen; TrackTarget обновляет только live gap stop check; facing обновляется после atomic pair result. |
| `OPEN-WP07-09` | Phase 6 emit MoveEnded + completion marker; только phase 4 следующего tick переводит Active→Recovery. Phase order/version не меняется. |
| `OPEN-WP07-10` | Все post-commit WP-07 lifecycle/movement events actor-only, с null target ID/frames и no RNG/group; exact stop codes: `WallReached`, `PreferredRangeReached`, `SegmentExpired`. Recovery expiry emit отдельный `ActionPhaseChanged(Recovery→null, 0)`. |
| `OPEN-WP07-11` | requested delta target-limited и signed; actual equals to−from; wall field содержит только wall clip. Only actual authoritative mutation counts as position progress. |
| `OPEN-WP07-12` | Voluntary movement only. Forced movement execution/events остаются WP-09; event/replay schemas и ordering version не меняются. Core behavior change требует `engine_version=battle.core/0.2.0`. Перед bump runtime-generated WP-06 wait обязан быть сохранён новым immutable historical fixture; current-engine fixtures создаются отдельно. |
| `OPEN-WP07-13` | **CLOSED — DEFER APPROVED:** отсутствующие CDS keys `stat.move_speed.min/max` не заменяются default. Общий stat clamp/DATA migration отложен до WP-10; WP-07 использует checked base+gear pipeline без clamp и rejects non-positive derived speed в Core pre-start, сохраняя `combat.balance/0.1`, config/hash и Workbook. |

## 4. Fixed DATA и fixture rules

### 4.1 Canonical DATA

```text
arena = [0, 10000]
start = 2000 / 8000
fp_scale = 1000

bear      radius=520 base_speed=70
gorilla   radius=550 base_speed=75
kangaroo  radius=430 base_speed=135

sys_approach weight=650 range=0..1500 startup/active/recovery=1/5/1
sys_retreat  weight=450 range=1600..3000 startup/active/recovery=1/5/1
sys_wait     weight=150 range=0..10000 startup/active/recovery=0/3/0
```

`gear_utility_sprint_soles` добавляет `MoveSpeed +12`. Поэтому golden build Bear/Kangaroo имеет derived speeds `82/147`.

WP-07 implementation меняет `ContractVersions.Engine` с `battle.core/0.1.0` на `battle.core/0.2.0`. `combat.event/0.1`, `combat.replay/0.1`, `tick-pipeline/1`, `pcg32/1` и canonical config hash сохраняются по утверждённому defer `OPEN-WP07-13`.

Canonical DATA сейчас не содержит требуемых CDS `stat.move_speed.min/max`. Утверждённая WP-07 policy запрещает numeric default и Workbook/generated edits: derived `base + gear` проверяется на checked overflow и `> 0` в Core pre-start. Полная stat-clamp/DATA migration закреплена за WP-10.

### 4.2 Fixture config

Каждый integration/golden candidate строится из generated canonical JSON, изменяет только явно перечисленные test settings/starts, заново компилируется штатным compiler и получает собственный canonical config hash. Использовать hash базового config после overrides запрещено.

До изменения `ContractVersions.Engine` текущий generator обязан один раз создать новый `CombatLab/fixtures/replay/v0.1/wait-equal-l1.engine-0.1.0.json`. Сейчас такого checked-in файла нет. Gate требует известные input/final digests `sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2` / `sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6`, green ReplayVerifier и pinned file SHA-256. После pin файл read-only по policy и не перезаписывается.

Все новые engine-run fixtures затем используют `battle.core/0.2.0`. Current-engine wait создаётся отдельно как `CombatLab/fixtures/replay/v0.1/wait-equal-l1.engine-0.2.0.json`.

No test-only runtime default, bypass semantic validation или direct mutation authoritative state после `BattleStarted` не допускается.

## 5. Geometry oracle

```text
center_min_i = checked_int64(arena_min + radius_i)
center_max_i = checked_int64(arena_max - radius_i)
signed_center_distance = position_right - position_left
radius_sum = checked_int64(radius_left + radius_right)
gap = max(0, signed_center_distance - radius_sum)
```

Pass после setup и каждого movement batch:

- `center_min_i <= position_i <= center_max_i`;
- preserved left/right order;
- `signed_center_distance >= radius_sum`;
- `gap >= 0`;
- left fighter faces Right, right fighter faces Left;
- no Int32/Int64 overflow, float, decimal, Unity transform/physics или frame delta.

Проверка через `abs` без preserved order недостаточна: полное crossing обязано быть отклонено/исправлено.

## 6. Availability oracle

```text
inner = sys_approach.preferred_range_max = 1500
outer = sys_retreat.preferred_range_min = 1600

gap < inner and outward_headroom > 0 => only sys_retreat
gap < inner and outward_headroom = 0 => only sys_wait
inner <= gap <= outer => only sys_wait
gap > outer => only sys_approach
```

Pass:

- один actor получает ровно один candidate;
- `candidate_count=1`, chosen/weight sum равны config base weight;
- selection mode `OnlyLegalAction`;
- Decision/Resolution RNG index не меняется;
- fixed zero-weight fallback остаётся regression guard и не снимает movement predicates;
- mixed Approach/Retreat production pair невозможен при одном shared gap/profile; direct mixed test intent не становится production selection semantics.

## 7. Movement arithmetic oracle

### 7.1 Target budget

Для Approach:

```text
required_gap_change = gap - outer
```

Для Retreat:

```text
required_gap_change = inner - gap
```

Положительный common budget ограничивается суммой frozen speed capacities. Initial target request распределяется пропорционально speed:

```text
numerator_i = checked_int64(total_budget * speed_i)
base_i = numerator_i / speed_sum
remainder_i = numerator_i % speed_sum
```

Оставшаяся единица идёт actor с большим remainder; exact tie — первый в immutable InitiativeOrder. Side, FighterId, dictionary/HashSet iteration, culture или новый RNG draw не являются tie-break.

### 7.2 Wall clamp

Target-limited request хранится до wall clamp. Для signed request:

```text
requested_position = checked(from_position + requested_delta)
to_position = clamp(requested_position, center_min, center_max)
actual_delta = to_position - from_position
blocked_by_wall = abs(requested_delta) - abs(actual_delta)
```

Если часть planned pair budget заблокирована, unused budget детерминированно перераспределяется другому active mover в пределах unused speed и wall headroom. В event каждого actor:

- `requested_delta` — его окончательный target-limited attempted delta с sign;
- `actual_delta = to_position - from_position`;
- `blocked_by_wall >= 0` содержит только его wall clip;
- `abs(actual_delta) <= abs(requested_delta) <= frozen speed`.

Range/band clamp и separation не записываются в `blocked_by_wall`.

### 7.3 Separation

После provisional voluntary moves:

```text
penetration = radius_sum - (provisional_right - provisional_left)
```

Если `penetration > 0`, rollback делится пропорционально positive inward displacement, который создал penetration. Actor с zero cause получает zero rollback. Remainder — InitiativeOrder. Сумма correction ровно равна penetration, final order сохраняется, final center distance не меньше radius sum.

Каждый nonzero rollback получает отдельный `PositionChanged`:

- `movement_kind=Separation`;
- `action_id=null`;
- `decision_id=null`;
- `from_position=provisional_position`, `to_position=final_corrected_position`;
- signed `requested_delta=actual_delta=to_position-from_position`;
- `blocked_by_wall=0`;
- before/after actor frames отражают только эту correction, target frames null;
- без `MoveStarted`, `MoveEnded`, hit/damage/tempo/resource events.

`penetration > sum(inward cause)` после valid setup является `FailedInvariant`.

## 8. Lifecycle и event ordering

### 8.1 Segment lifecycle

1. Tick N phase 5: DecisionMade/ActionCommitted; state `Approach|Retreat`, phase `Startup`, remaining `1`.
2. Tick N+1 phase 4: `ActionPhaseChanged(Startup→Active, 5)`.
3. Tick N+1 phase 6: first and only `MoveStarted`, затем first PositionChanged.
4. Следующие active phase-6 ticks: PositionChanged по request; MoveStarted не повторяется.
5. Range/wall/last-active stop: one `MoveEnded` и internal completion marker в phase 6.
6. Следующий tick phase 4: `ActionPhaseChanged(Active→Recovery, 1)`; phase 6 больше не двигает actor.
7. Следующий phase-4 expiry: emit `ActionPhaseChanged(Recovery→null, phase_ticks=0)`, после чего actor frame становится `DecisionReady` с null action/phase/timer. Event сохраняет завершившиеся action/decision IDs и source на Active→Recovery.

Phase 6 не выполняет action transition. Это blocking architecture assertion.
Если movement завершился на первом active tick, before frame deferred Active→Recovery сохраняет `Active` и remaining `5`: completion marker имеет приоритет над decrement. После transition frame равен `Recovery`, remaining `1`.

### 8.2 Emit order

После полного pair calculation drafts идут по event class, внутри class — InitiativeOrder:

1. MoveStarted;
2. PositionChanged Voluntary;
3. PositionChanged Separation;
4. MoveEnded.

Phase-4 ActionPhaseChanged появляется в своей более ранней phase или на следующем tick.
Одновременные movement-lifecycle transitions внутри phase 4 также идут по InitiativeOrder. Зафиксированный WP-06 compatibility order `DecisionMade A→B`, затем `ActionCommitted A→B` не меняется.

### 8.3 Role и causality

- movement actor = mover;
- target ID и target before/after frame = null;
- WP-07 `ActionPhaseChanged` также имеет performer actor и null target ID/frames;
- voluntary movement action ID = committed system action;
- separation action ID = null;
- RNG и resolution group = null;
- related IDs unique и ordinal по EventId;
- source всегда указывает назад.

Primary chain:

```text
DecisionMade
  → ActionCommitted
  → ActionPhaseChanged(Startup→Active)
  → MoveStarted
  → PositionChanged*
  → MoveEnded
  → ActionPhaseChanged(Active→Recovery)
  → ActionPhaseChanged(Recovery→null)
```

Lifecycle reason codes exact: `StartupCompleted`, `MovementCompleted`, `RecoveryCompleted`. MoveStarted/voluntary/separation reason codes exact: `MovementStarted`, `VoluntaryMovement`, `SeparationCorrection`; MoveEnded использует свой stop reason как единственный reason code. Recovery→null source/related ID — предыдущее Active→Recovery event.

### 8.4 Stop decision

Для каждого actor:

1. `WallReached`, если его request был wall-clipped и actor дошёл до center wall bound;
2. иначе `PreferredRangeReached`, если final pair gap достиг neutral band;
3. иначе `SegmentExpired` после последнего active movement tick.

`MoveStarted.stop_conditions` содержит эти три unique codes в указанном порядке. `MoveEnded.stop_reason` обязан входить в declared conditions.

Terminal outcome не добавляет synthetic MoveEnded/phase cleanup после `BattleEnded`.

## 9. Golden `approach_band_l3`

### 9.1 Input

- `engine_version=battle.core/0.2.0`;
- generated balance + test overrides:
  - `global.arena.start_position_a=4000`;
  - `global.arena.start_position_b=6555`;
  - `battle.time_limit_ticks=3`;
- fighter A: Bear + Sprint Soles, radius `520`, frozen speed `82`;
- fighter B: Kangaroo + Sprint Soles, radius `430`, frozen speed `147`;
- mode rules allow exactly selected builds plus all three system actions;
- initial gap: `6555 - 4000 - 520 - 430 = 1605`;
- closure budget: `5`;
- largest remainder at speeds `82:147`: A `+2`, B `-3`;
- final centers: `4002/6552`, gap `1600`.

### 9.2 Exact event trace

| Seq | Tick | Event | Exact oracle |
|---:|---:|---|---|
| 0 | 0 | `BattleStarted` | Initial centers `4000/6555`, facing Right/Left, InitiativeOrder `B/A`, RNG indices `0/0`. |
| 1 | 0 | `DecisionMade` A | `sys_approach`, legal `[sys_approach]`, `650/650`, `OnlyLegalAction`, RNG null. |
| 2 | 0 | `DecisionMade` B | Те же candidate/weight semantics. |
| 3 | 0 | `ActionCommitted` A | Target B, direction Right, target position `6555`, timings `1/5/1`, costs/cooldown `0`. |
| 4 | 0 | `ActionCommitted` B | Target A, direction Left, target position `4000`, те же timings/costs. |
| 5 | 1 | `ActionPhaseChanged` B | Startup→Active, phase ticks `5`; B первый в fixture `InitiativeOrder`. |
| 6 | 1 | `ActionPhaseChanged` A | Startup→Active, phase ticks `5`. |
| 7 | 1 | `MoveStarted` B | From `6555`, Left, speed `147`, kind Approach, three stop conditions. |
| 8 | 1 | `MoveStarted` A | From `4000`, Right, speed `82`, kind Approach, three stop conditions. |
| 9 | 1 | `PositionChanged` B | `6555→6552`, requested/actual `-3/-3`, wall `0`, Voluntary. |
| 10 | 1 | `PositionChanged` A | `4000→4002`, requested/actual `+2/+2`, wall `0`, Voluntary. |
| 11 | 1 | `MoveEnded` B | Segment start/final `6555→6552`, `PreferredRangeReached`. |
| 12 | 1 | `MoveEnded` A | Segment start/final `4000→4002`, `PreferredRangeReached`. |
| 13 | 2 | `ActionPhaseChanged` B | Active→Recovery, phase ticks `1`, source event 11. |
| 14 | 2 | `ActionPhaseChanged` A | Active→Recovery, phase ticks `1`, source event 12. |
| 15 | 3 | `TimeoutReached` | Full HP; cross-products both `1897500`. |
| 16 | 3 | `DrawDeclared` | `TimeoutEqualHealthFraction`. |
| 17 | 3 | `BattleEnded` | Draw, end/duration tick `3`, event count `18`. |

### 9.3 Exact envelope, causality и frames

Для seq `0..17`:

```text
event_id = evt-{sequence:D10}
source_sequence = [null,0,0,1,2,4,3,5,6,7,8,9,10,11,12,null,15,16]
related_sequence_lists = [[],[0],[0],[1],[2],[4],[3],[5],[6],[7],[8],[9],[10],[11],[12],[],[15],[16]]
actor = [null,A,B,A,B,B,A,B,A,B,A,B,A,B,A,null,null,null]
target = [null,B,A,B,A,null,null,null,null,null,null,null,null,null,null,null,null,null]
reason = [Initialization,OnlyLegalAction,OnlyLegalAction,ActionSelected,ActionSelected,
          StartupCompleted,StartupCompleted,MovementStarted,MovementStarted,
          VoluntaryMovement,VoluntaryMovement,PreferredRangeReached,PreferredRangeReached,
          MovementCompleted,MovementCompleted,TimeLimitReached,
          TimeoutEqualHealthFraction,TimeoutEqualHealthFraction]
```

- `decision_id`: A-events seq `1,3,6,8,10,12,14` используют `dec-fighter_a-000001`; B-events seq `2,4,5,7,9,11,13` — `dec-fighter_b-000001`; остальные null.
- `action_id=sys_approach` на seq `1..14`, иначе null.
- `rng`, `resolution_group_id` и effect/passive/group IDs равны null на всех events.
- Seq `0` имеет empty envelope frames и initial frames в payload. Seq `1/2` используют один immutable decision snapshot: оба fighter `DecisionReady`, frames до/после неизменны. Seq `3` меняет только A `DecisionReady → Approach/Startup(1)`, B остаётся DecisionReady; seq `4` меняет только B `DecisionReady → Approach/Startup(1)`, а target A уже находится в Approach/Startup(1). Seq `1..4` имеют actor+opponent frames; seq `5..14` — только actor frame.
- Seq `5/6`: before `Approach/Startup(1)`, after `Approach/Active(5)`. Seq `7/8` frame не меняют. Seq `9/10` изменяют только actor position на exact value §9.2. Seq `11/12` frame не меняют: completion marker internal, public actor остаётся `Approach/Active(5)`. Seq `13/14`: before `Approach/Active(5)`, after `Recovery/Recovery(1)`.
- Seq `15..17` имеют empty envelope frames; final frames находятся в outcome/summary payload. `TimeoutReached` намеренно имеет `source_event_id=null` и empty related IDs по WP-06 boundary semantics.
- Каждый payload `related_event_ids` точно равен соответствующему списку выше; каждый event имеет ровно один reason code из vector. Любое другое schema-valid causal/frame projection является golden mismatch.

Final frames: centers `4002/6552`, facing Right/Left, state/action phase `Recovery`, action `sys_approach`, remaining `1`; HP/energy/resources unchanged. Decision/Resolution RNG indices остаются `0`.

Fixture config hash, input digest и final digest не задаются заранее. Первая conforming implementation обязана вычислить их штатным pipeline, доказать repeated/profile/target parity и pin literals в этом разделе отдельным approved update. После pin любой drift требует version/decision review.

## 10. Additional exact fixtures

### 10.1 Reference `retreat_band_l3`

- starts `4000/6445`, initial gap `1495`;
- same builds/radii/speeds;
- expansion budget `5`;
- A `-2`, B `+3`;
- final centers `3998/6448`, gap `1500`;
- `sys_retreat`, weight `450/450`, directions Left/Right;
- тот же 18-event shape и deferred Active→Recovery на tick 2;
- full-health timeout Draw на tick 3, RNG indices `0`.

Этот scenario может быть exact integration fixture без отдельного pinned digest, но event-by-event oracle обязателен.

### 10.2 Wall `retreat_wall_l3`

- engine/builds/time limit/InitiativeOrder как §9;
- starts `521/2966`, initial gap `2966-521-520-430=1495`;
- A left wall headroom `1`, поэтому Retreat legal на decision snapshot;
- initial proportional requests A `-2`, B `+3`;
- A wall result: `521→520`, requested `-2`, actual `-1`, `blocked_by_wall=1`;
- unused unit перераспределяется B: `2966→2970`, requested/actual `+4/+4`, wall `0`;
- final gap `2970-520-520-430=1500`;
- action `sys_retreat`, weight `450`, directions A Left/B Right;
- seq/type/tick/source/related/actor/target/decision vectors и 18-event count совпадают с §9.2–9.3;
- payload/state substitutions: `Approach→Retreat`, positions как выше; seq 11 B заканчивается `PreferredRangeReached`, seq 12 A — `WallReached`; те же значения являются их единственными reason codes.

Fixture обязан доказать, что wall clamp не emit `WallImpact`, damage, stagger или Resolution RNG. Start непосредственно на wall запрещён для этой проверки: availability тогда корректно выбрала бы Wait.

### 10.3 Separation resolver `inward_overlap_even`

Separation не достижим из production `Approach/Retreat/Wait` при исправной neutral-band clamp. Поэтому blocking test использует immutable input pure pair resolver и не подменяет production availability/state:

```text
arena = 0..10000
initiative_order = [B,A]
centers = A:4000, B:5100
radii = A:500, B:500
voluntary_requests = A:+100, B:-100
provisional = A:4100, B:5000
penetration = 100
rollback = A:-50, B:+50
final = A:4050, B:5050
descriptor_a = action:fixture_inward_a, decision:dec-fixture-a-000001, move_started:evt-0000000101
descriptor_b = action:fixture_inward_b, decision:dec-fixture-b-000001, move_started:evt-0000000100
```

Exact draft order/payload, где emitter назначает `vB=evt-0000000102`, `vA=evt-0000000103`:

| Order | Draft | Exact payload/source |
|---:|---|---|
| 0 | Voluntary B | `5100→5000`, requested/actual `-100/-100`, wall `0`, source/related `evt-0000000100/[evt-0000000100]`, reason `VoluntaryMovement`, action/decision `fixture_inward_b/dec-fixture-b-000001`. |
| 1 | Voluntary A | `4000→4100`, requested/actual `+100/+100`, wall `0`, source/related `evt-0000000101/[evt-0000000101]`, reason `VoluntaryMovement`, action/decision `fixture_inward_a/dec-fixture-a-000001`. |
| 2 | Separation B | `5000→5050`, requested/actual `+50/+50`, wall `0`, source `vB`, related `[vB,vA]` ordinal, reason `SeparationCorrection`, action/decision null. |
| 3 | Separation A | `4100→4050`, requested/actual `-50/-50`, wall `0`, source `vA`, related `[vB,vA]` ordinal, reason `SeparationCorrection`, action/decision null. |

Все четыре drafts actor-only, target ID/frames null, RNG/group null; before/after actor frames совпадают с одной строкой. Resolver не создаёт `MoveStarted`/`MoveEnded`: это обязанность surrounding MovementSystem. Full production integration seam активируется WP-09 action/forced movement.

## 11. Negative и safety semantics

До journal Begin возвращается deterministic sorted `Rejected` для:

- missing/wrong type/raw range arena values, fp scale, move speed, radius;
- `min >= max` и arithmetic overflow;
- non-positive base MoveSpeed/CollisionRadius и non-positive/overflow derived value после modifier pipeline;
- body, не помещающегося в center bounds;
- start center вне body-aware bounds;
- equal/crossed/overlapping initial bodies;
- отсутствующего/неправильного `sys_approach` или `sys_retreat`: owner `all`, slot `System`, category `Movement`, matching mode `Approach`/`Retreat`, `track_target=true`, `move_distance=0`, positive weight/active duration, startup/recovery `min=base=max>=0`;
- nonzero energy/resource cost/cooldown, combat hit/damage/stagger/knockback/wall fields, non-empty hit schedule или `wall_impact=true` у system movement action;
- `approach.max > retreat.min`;
- invalid timings/cost/range и unsupported system movement shape.

Request с engine version, отличной от `battle.core/0.2.0`, отклоняется существующим `EngineVersionMismatch`. Отсутствующие `stat.move_speed.min/max` не проверяются WP-07 только после явного owner approval рекомендованного defer; без approval programming gate запрещает запуск реализации, а не создаёт runtime default.

После Begin:

- impossible separation/overflow/state mismatch → `FailedInvariant` с bounded failure journal;
- event cap по-прежнему резервирует terminal slot;
- position mutation сбрасывает zero-progress stamp;
- zero-delta event сам по себе position progress не создаёт;
- no-capacity movement не коммитится: availability выбирает Wait;
- voluntary wall clamp никогда не emit `WallImpact`, damage, stagger или Resolution RNG;
- `BattleEnded` остаётся последним canonical event.

## 12. Test families

### 12.1 Unit: geometry, allocation, lifecycle

| ID | Проверка | Pass |
|---|---|---|
| `WP07-GEO-001` | Centers/radii gap | `(100,500,r=50/75) → 275`; touching/overlap → `0`; symmetry. |
| `WP07-GEO-002` | Radius-adjusted wall clamp | Arena `0..1000`, radius `100`, from `150`, request `-75` → to `100`, actual `-50`, wall `25`; mirrored right case. |
| `WP07-GEO-003` | Center bounds/overflow | Exact inclusive edges pass; impossible body/checked overflow reject. |
| `WP07-GEO-004` | Preserved order/facing | Crossing never passes through abs-gap loophole; facing derives from final batch. |
| `WP07-AVL-001` | Neutral-band truth table | `1499→Retreat`, `1500/1550/1600→Wait`, `1601→Approach`; wall-pinned Retreat→Wait. |
| `WP07-AVL-002` | OnlyLegalAction/no RNG | Exact weight and zero RNG index change for each system action. |
| `WP07-MOV-001` | Approach outer clamp | Stops at gap `1600`, no overshoot. |
| `WP07-MOV-002` | Retreat inner clamp | Stops at gap `1500`, no overshoot. |
| `WP07-MOV-003` | Proportional allocation | Budget `5`, speeds `82:147` → `2:3`; remainder tie follows InitiativeOrder. |
| `WP07-MOV-004` | Shared snapshot/atomic apply | A mutation cannot alter B request; final result independent of processing order. |
| `WP07-MOV-005` | Wall redistribution | Target request, wall-only block и redistributed unused budget exact. |
| `WP07-MOV-006` | Wait regression | No movement events/position/RNG change. |
| `WP07-SEP-001` | Equal inward overlap | Centers `4000/5100`, radii `500/500`, requests `+100/-100` → provisional `4100/5000`, rollback `50/50`, final `4050/5050`. |
| `WP07-SEP-002` | One mover | Same start, requests `+200/0` → rollback only A `-100`, final `4100/5100`. |
| `WP07-SEP-003` | Odd penetration | Penetration `101`: `51/50` according to InitiativeOrder, not A/B order. |
| `WP07-SEP-004` | Crossing/invariant | Preserved left/right order restored; impossible cause deficit fails invariant. |
| `WP07-LIFE-001` | Startup boundary | Commit tick does not move; first movement only after Startup→Active. |
| `WP07-LIFE-002` | Completion marker ownership | MoveEnded phase 6; Active→Recovery only next tick phase 4. |
| `WP07-LIFE-003` | Segment expiry | Exactly five movement ticks and one `MoveEnded(SegmentExpired)`. |
| `WP07-LIFE-004` | Terminal priority | No movement cleanup after terminal event. |
| `WP07-LIFE-005` | Recovery expiry | Time-limit-4 fixture: Recovery→null event has phase ticks `0`, source/action/decision/reason/frames exact; only after it actor is DecisionReady and may decide in phase 5. |

### 12.2 Config/pre-start validation

| ID | Проверка | Pass |
|---|---|---|
| `WP07-VAL-001` | Movement stats | Missing/wrong/non-positive/overflow base radius/speed and non-positive/overflow derived speed rejected before Begin. |
| `WP07-VAL-002` | Arena/body geometry | Bounds, starts, fit, order and overlap failures sorted deterministically. |
| `WP07-VAL-003` | System actions | IDs, all-owner, System slot, category/mode, TrackTarget, zero move-distance/combat/cost/cooldown shape, timings and ranges exact. |
| `WP07-VAL-004` | Neutral band | `inner <= outer`; inclusive boundary semantics. |
| `WP07-VAL-005` | Canonical scale | `fp_scale` required; `math_scale` does not satisfy contract. |
| `WP07-VAL-006` | Engine version | Current request requires `battle.core/0.2.0`; old/unknown value rejected, archived replay remains verifiable. |

### 12.3 Replay conformance

| ID | Проверка | Pass |
|---|---|---|
| `WP07-CON-001` | Wire shape | Existing three payloads serialize exact snake_case fields/enums; no schema bump. |
| `WP07-CON-002` | Delta/frame continuity | `actual=to-from`; actor before/after positions and facing agree. |
| `WP07-CON-003` | Roles | Actor mover; target ID/frame null; action ID rules exact. |
| `WP07-CON-004` | Causal chain | All source/related IDs exist, point backward and match §8.3. |
| `WP07-CON-005` | Stop contract | Conditions unique/fixed order; final reason declared and semantically valid. |
| `WP07-CON-006` | Separation projection | Separate kind, no action/wall/RNG/combat payload. |
| `WP07-CON-007` | Full replay | Both reference scenarios pass schema, semantic, integrity and round-trip validators. |
| `WP07-CON-008` | Tamper | Wrong delta/frame/wall/source/reason/order is rejected. |

Replay alone не содержит derived collision radius, поэтому body/wall correctness доказывается Engine unit/integration tests, а verifier проверяет только доступную event/frame continuity. Нельзя добавлять ложную replay validation на отсутствующие данные.

### 12.4 Integration/regression

| ID | Проверка | Pass |
|---|---|---|
| `WP07-TRACE-001` | `approach_band_l3` | Exact 18 events §9, final state and no RNG. |
| `WP07-TRACE-002` | `retreat_band_l3` | Exact mirrored 18-event semantics §10. |
| `WP07-TRACE-003` | Wall stop | Exact `retreat_wall_l3` §10.2; no WallImpact/damage/stagger/Resolution RNG. |
| `WP07-TRACE-004` | Separation resolver | Exact four submutations §10.3 and final non-overlap. |
| `WP07-REG-001` | Pre-migration WP-06 archive | До Engine bump материализован новый `wait-equal-l1.engine-0.1.0.json`; оба известных digest совпадают, ReplayVerifier green, file SHA-256 pinned; после pin bytes immutable. |
| `WP07-REG-002` | WP-05 replay | Existing fixture/digests unchanged. |
| `WP07-REG-003` | Current-engine wait | Separate `wait-equal-l1.engine-0.2.0.json`: same eight event meanings/state/RNG as WP-06, new approved input/final digests and file SHA-256 pinned. |
| `WP07-SAFE-001` | Watchdog | Actual movement/lifecycle obey existing stamp; marker-only event is not position progress. |
| `WP07-SAFE-002` | Event cap | Movement-heavy trace never exceeds cap and preserves terminal slot. |

### 12.5 Determinism/architecture

| ID | Проверка | Pass |
|---|---|---|
| `WP07-DET-001` | Repeat ×100 | Exact events/summary/input/final digest. |
| `WP07-DET-002` | Profile parity | Standard/Diagnostic canonical chain equal; SummaryOnly result/digest equal. |
| `WP07-DET-003` | Target parity | `netstandard2.1` and `net10.0` replay bytes equal. |
| `WP07-DET-004` | OS/culture/mirror | Windows/Linux selected fixture equal; cultures do not alter output; transformed mirror has no hidden A/B advantage. |
| `WP07-ARCH-001` | Dependencies | Movement remains Battle.Core → Contracts+BCL only. |
| `WP07-ARCH-002` | Forbidden APIs | No float/decimal/System.Random/wall clock/I/O/Unity APIs. |
| `WP07-ARCH-003` | Phase/version | Existing 12 phases and ordering version unchanged. |
| `WP07-ARCH-004` | Scope | No weighted selector, forced combat movement, effects or UnityClient diff. |

## 13. Blocking acceptance matrix

`OPEN-WP07-13` закрыт: Test Plan фиксирует intentional CDS clamp defer до WP-10. Все строки ниже blocking:

| Family | Required cases |
|---|---|
| Geometry | `WP07-GEO-001..004` |
| Availability/movement | `WP07-AVL-001..002`, `WP07-MOV-001..006` |
| Separation/lifecycle | `WP07-SEP-001..004`, `WP07-LIFE-001..005` |
| Validation | `WP07-VAL-001..006` |
| Replay | `WP07-CON-001..008` |
| Integration/regression/safety | `WP07-TRACE-001..004`, `WP07-REG-001..003`, `WP07-SAFE-001..002` |
| Determinism/architecture | `WP07-DET-001..004`, `WP07-ARCH-001..004` |

Дополнительные gates:

- 100% branch coverage для geometry bounds/gap, target allocation, wall clamp, separation, availability и movement lifecycle guards;
- Battle.Core line coverage не ниже 85%;
- existing WP-02/WP-03/WP-06 critical coverage gates green;
- generated WP-04 artifact reproducible; config hash не меняется без реального DATA change;
- no skipped/flaky retry.

## 14. Execution gate

После реализации выполнить:

```powershell
dotnet restore --locked-mode
dotnet build CombatLab.sln --configuration Release --no-restore
dotnet test CombatLab.sln --configuration Release --no-build
./scripts/verify-wp04-generated.ps1 -Configuration Release
./scripts/verify-wp06-target-determinism.ps1 -Configuration Release
./scripts/verify-wp07-target-determinism.ps1 -Configuration Release
```

Coverage run обязан пройти WP-02, WP-03, WP-06 и новый WP-07 gate на одном fresh report.

WP-07 переводится в `COMPLETED` только когда:

- утверждённый defer `OPEN-WP07-13` реализован без runtime default и незаявленного DATA/schema drift;
- вся §13 green;
- Release build/test green;
- golden hashes/digests pinned после independent rerun;
- existing WP-05 fixtures не drift; pre-migration WP-06 wait archive создан/pinned до bump и не меняется; current-engine wait добавлен отдельно;
- config/schema/manifest остаются актуальными;
- `UnityClient` не изменён;
- `Implementation_Status.md`, Brief, Test Plan, Decisions и Index обновлены итоговыми фактами.

## 15. Не входит

- full action candidate catalog, weighted RNG, tactic/situation/opportunity/repeat — WP-08;
- defense, hit geometry, damage, stagger, knockback, pull, swap, throw, WallImpact, grabs — WP-09;
- effects/triggers/tempo/grip/resource movement reactions — WP-10/WP-11;
- Unity playback implementation, batch, storage, deployment и production rollout.
