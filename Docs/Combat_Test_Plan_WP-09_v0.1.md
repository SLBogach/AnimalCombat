# Combat Test Plan WP-09 v0.1 — Resolution

> Статус: `EXECUTED / PASSED`; WP-09 — `COMPLETED`.
>
> Все `128` acceptance cases реализованы и проходят локально и в CI. `OPEN-WP09-01..23` закрыты. Документ не разрешает изменение `UnityClient` и не меняет canonical DATA artifacts. Windows/Linux × Debug/Release matrix подтверждена green.

## 1. Назначение и gate

Этот Test Plan задаёт проверяемую границу реализации WP-09 Resolution:

- typed hit schedule и immutable resolution profiles;
- phases 7–11, intent groups и canonical ordering;
- impact geometry, Counter, Dodge и Block;
- damage, chip, stagger и hard-control threshold;
- combat MoveSelf, force, wall, grab и throw;
- group-aware defeat, victory и Double KO;
- canonical resolution events, RNG provenance и replay semantics;
- event-cap/watchdog atomicity, historical compatibility и determinism.

Правило gate:

1. Каждый ID из §8 обязан соответствовать минимум одному автоматически обнаруживаемому тесту.
2. Тест содержит traits `WorkPackage=WP09` и `AcceptanceId=<ID>`.
3. Inventory test проверяет отсутствие пропусков, дубликатов и лишних `WP09-*` IDs.
4. Все cases blocking; `Skip`, quarantine и conditional pass запрещены.
5. Snapshot/golden без semantic assertions не закрывает case.
6. WP-09 не получает статус `COMPLETED` до green локальных gates и GitHub Actions Windows/Linux × Debug/Release.

## 2. Нормативные источники

Source order и прочитанный scope заданы в [WP-09 Brief](./WP-09_Brief.md). Exact pass/fail этого документа уточняет Brief, но не может менять CDS formulas, Replay wire contract или TDD dependency rules.

Связанные документы:

- [WP-09 Brief](./WP-09_Brief.md);
- [Implementation Status](./Implementation_Status.md);
- [Decisions](./Decisions.md);
- [Index](./Index.md);
- [WP-08 Brief](./WP-08_Brief.md);
- [Combat Test Plan WP-08](./Combat_Test_Plan_WP-08_v0.1.md);
- canonical [combat.balance.v0.1.json](../CombatLab/config/generated/combat.balance.v0.1.json).

## 3. Approved decision baseline

Matrix предполагает явное принятие `OPEN-WP09-01..23`:

- Engine становится `battle.core/0.4.0`;
- `combat.event/0.1`, `combat.replay/0.1`, `combat.balance/0.1`, `pcg32/1`, `tick-pipeline/1` сохраняются;
- workbook/generated artifacts не меняются;
- matching Counter deterministic и не consume RNG;
- Dodge/Block используют Resolution `ChanceCheck` только после geometry/eligibility;
- stagger reset-to-zero; advanced effect/Armored/Unstoppable/KnockedDown mechanics отложены до WP-10;
- один schedule entry образует одну group; equal strike trade использует общий snapshot/group;
- full group plan атомарен для state/RNG/events;
- historical Engine `0.1.0`/`0.2.0`/`0.3.0` replay bytes immutable.

Решения имеют статус `CLOSED`, а этот Test Plan — `APPROVED / BLOCKING`.

## 4. Test harness и общие правила

### 4.1 Проекты

| Уровень | Проект | Ответственность |
|---|---|---|
| Unit | `Battle.Core.UnitTests` | formulas, profiles, intents, groups, mutations, safety и runtime transitions |
| Conformance | `Battle.ConformanceTests` | contracts, JSON, event/replay semantics, architecture, versions и immutable artifacts |
| Integration | `CombatLab.IntegrationTests` | полный `CombatEngine.Simulate`, canonical journals, fixtures и profile parity |

`CombatLab.PerformanceTests` не является blocking для WP-09, пока в нём нет тестов.

### 4.2 Numeric oracle `resolution_unit_v1`

Если case не задаёт иное, используется controlled synthetic profile:

```text
FP_SCALE = 1000
armor_k = 200
damage_floor = 1
damage_cap = 600
block_slope = 3; block_min = 100; block_max = 900
dodge_slope = 3; dodge_min = 50; dodge_max = 850
control_k = 150; force_k = 120
stun_min = 3; stun_max = 20

Attacker:
  Power=100, Precision=120, GuardBreak=80,
  ControlPower=100, Mass=80, Initiative=100

Defender:
  HP=1000, Armor=100, Evasion=120, Guard=100,
  ControlResistance=100, Mass=120, StaggerThreshold=120,
  Initiative=90

Strike:
  BaseDamage=100, PowerRatio=500, MinDamage=10,
  BaseStagger=60, BaseStunTicks=6,
  BaseKnockback=600, KnockbackMin=0, KnockbackMax=1000,
  ChipMin=10, hit range=[0,1000],
  wall damage per unit=100, wall min=10, wall max=100

Defense:
  BlockBase=500, BlockReduction=500, DodgeBase=500
```

Обязательные результаты:

```text
PowerTerm = 150
RawDamage = 150
ArmorRatio = 333
AfterArmor = 100
AppliedDamage = 100

BlockChance = 560
BlockedDamage = 50
DodgeChance = 500

ControlRatio = 1000
StaggerGain = 60
StunTicks = 6

ForceRatio = 3000
RequestedMove = 1000
```

Если у стены `ActualMove=400`, то `BlockedByWall=600`, `WallDamage=60`, дополнительный `WallStagger=60`.

### 4.3 Range и RNG boundaries

- ranges inclusive: `gap == min` и `gap == max` являются valid;
- Resolution chance draw range `[0, fp_scale)`;
- success iff `result < chance_fp`;
- exact `result == chance_fp` — failure;
- ineligible defense, geometry miss, Undodgeable и deterministic Counter не consume draw;
- exact exclusive tie использует один `Resolution/TieBreak` draw;
- один event содержит максимум один draw;
- stream indices начинаются с `0` и непрерывны.

### 4.4 Stable External IDs

Все числа форматируются invariant culture:

```text
intent:<decision_id>:<ordinal:D2>
impact:<decision_id>:<ordinal:D2>
hit-group:<decision_id>:<ordinal:D2>
resolution:<tick:D10>:<ordinal:D4>
damage:<resolution_id>:<ordinal:D2>
conflict:<resolution_id>:<ordinal:D2>
grab:<decision_id>:<ordinal:D2>
```

Counters per battle, checked, начинаются с `0`. Они не зависят от Side enumeration, collection insertion order, culture или diagnostic profile.

### 4.5 Schedule/tag consistency

| Profile | Exact required relation |
|---|---|
| Damage-capable entry | `Hit`, `Counter`, `Throw` и `Wall` входят в `hit_count`; plain `Grab` не входит. |
| Counter | Exact `counter` tag, ровно один `Counter` entry, opponent target и `hit_count=1`. |
| Grab | Exact `grab` tag и минимум один `Grab` entry; `Throw`/`Wall` разрешён только после более раннего `Grab` той же schedule. |
| Block | Exact `block` tag, positive `block_base_chance_fp` и `block_reduction_fp`, empty damaging schedule. |
| Dodge | Exact `dodge` tag, positive `dodge_base_chance_fp`, empty damaging schedule. |
| WallImpact | `wall_impact=true` iff exact `wall_impact` tag и valid nonzero wall damage profile; false требует нулевые wall fields. |
| System | Empty schedule, `hit_count=0`, damage/control/force fields zero. |

Любое нарушение даёт typed config error по `$.actions[<action_id>].<field>` до `journal.Begin`.

## 5. Exact gameplay oracles

### 5.1 Intent/group order

Canonical key:

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

Class order: `Counter=0`, `Defense=1`, `Strike=2`, `Grab=3`, `ForcedMove=4`. Conflict matrix имеет приоритет над механическим class order: equal legal strikes образуют Trade, а не выбирают winner.

Class mapping: `Counter` primitive → Counter; active `block`/`dodge` window → Defense; numeric `Hit` → Strike; `Grab`/`Throw`/`Wall` primitive → Grab; derived post-impact displacement → ForcedMove. Numeric Hit с `wall_impact` tag остаётся Strike, а wall consequence создаётся после него.

Priority mapping:

- canonical in-class: `action_priority`;
- Counter/exclusive: `resolution_priority`;
- strike clash: `clash_priority`;
- grab conflict: `grab_priority`.

### 5.2 Per-impact order

```text
Invalid/Defeated/consumed
→ geometry miss
→ deterministic matching Counter
→ eligible Dodge ChanceCheck
→ eligible Block ChanceCheck
→ normal hit
→ damage/stagger
→ forced movement/wall/grab consequence
→ group defeat/outcome
```

Counter matches any observable `strike` tag except grab and requires `counter.resolution_priority >= incoming.resolution_priority`. On match it emits `Countered`, cancels incoming intent and resolves its own action-level hit in the same group if live counter geometry is valid. No Counter RNG is consumed.

### 5.3 Damage/control/force

Formulas и floor points — exact formulas §9 [WP-09 Brief](./WP-09_Brief.md). Дополнительно:

- normal `DamageBreakdown.after_block = after_armor`;
- blocked `after_block = BlockedDamage`;
- `final` — damage до HP clamp;
- `overkill = final - (hp_before - hp_after)`;
- lethal iff `hp_after == 0`;
- successful Block suppresses normal stagger/force;
- threshold control применяется после полного damage set группы, затем meter reset `0`;
- wall damage — отдельный direct `DamageApplied`, armor повторно не применяется.

### 5.4 MoveSelf и forced movement

- MoveSelf budget распределяется по active ticks quotient/remainder, первые remainder ticks получают `+1`;
- movement происходит в phase 6 до impact collection;
- Push — target по hit direction; Pull — target к actor без crossing; Swap — atomic legal center exchange;
- WallImpact только при `wall_impact=true` и `BlockedByWall>0`;
- forced movement не создаёт voluntary movement gains.

### 5.5 Grab boundaries

- start tick `s` входит в hold; auto max release происходит на boundary `s + max_hold_ticks`;
- terminal primitive, hard control или defeat закрывает grab в своей group;
- без terminal primitive release происходит при завершении Active;
- после release tick `r` grab запрещён для `r <= tick < r + grab_lockout_ticks`;
- `GrabEnded` reason: `Throw`, `Release`, `Interrupted`, `GrabberDefeated`, `TargetDefeated` или `MaxHoldReached`;
- full KnockedDown lifecycle и wakeup immunity не входят в WP-09.

## 6. Canonical event oracles

### 6.1 Local group order

| Outcome | Минимальный local order |
|---|---|
| Miss | `AttackMissed` |
| Counter | `Countered → AttackHit(counter) → DamageApplied(counter)` при valid counter geometry |
| Dodge | `Dodged` |
| Block | `Blocked → DamageApplied(chip)` |
| Normal | `AttackHit → DamageApplied → optional ResourceChanged(StaggerGain) → optional StateChanged(Stunned) → optional ResourceChanged(StaggerReset) → optional KnockbackApplied` |
| Wall | `... → KnockbackApplied → WallImpact → DamageApplied(wall) → optional ResourceChanged(WallStagger) → optional StateChanged/ResourceChanged reset` |
| Grab | `optional ConflictResolved → GrabStarted → optional spatial events → GrabEnded` |
| Lethal | все mutations группы → `StateChanged(...→Defeated) → FighterDefeated` |
| Double KO | оба lethal `DamageApplied` → оба defeated chains → `DrawDeclared → BattleEnded` |

`FinisherTriggered` разрешён только для гарантированного lethal current plan и может ссылаться вперёд только на lethal event той же group.

### 6.2 Causality

```text
FighterDefeated
  → StateChanged(HealthDepleted)
  → DamageApplied
  → AttackHit | Blocked | WallImpact
  → conflict/lifecycle
  → ActionCommitted
  → DecisionMade | system source
```

`source_event_id` — одна первичная более ранняя причина. `related_event_ids` — только дополнительные более ранние причины, sorted ordinal. Events группы contiguous; закрытая group не открывается повторно.

## 7. Planned versioned fixtures

Новые artifacts создаются только во время реализации, отдельными файлами:

```text
fixtures/replay/v0.1/wait-equal-l1.engine-0.4.0.json
fixtures/replay/v0.1/decision-weighted-l1.engine-0.4.0.json
fixtures/replay/v0.1/resolution-basic-l1.engine-0.4.0.json
fixtures/replay/v0.1/resolution-double-ko-l1.engine-0.4.0.json
fixtures/replay/v0.1/resolution-wall-grab-l1.engine-0.4.0.json
```

До Engine bump фиксируются SHA-256 всех existing `0.1.0`, `0.2.0`, `0.3.0` fixtures. Новые input/final/file digests записываются в этот документ только после review canonical event trace. Existing fixture не перезаписывается.

## 8. Blocking acceptance matrix

### 8.1 Config и materialization — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-CFG-001` | Core | Canonical `combat.balance/0.1` materializes all fighter resolution stats and 24 action profiles before `journal.Begin`. |
| `WP09-CFG-002` | Core | Every schedule token materializes typed `Hit/Counter/Grab/Throw/Wall`; no runtime string parsing remains. |
| `WP09-CFG-003` | Conformance | Unknown prefix, empty inner token, negative/duplicate/unsorted/out-of-active tick returns sorted typed config errors before journal Begin. |
| `WP09-CFG-004` | Conformance | `hit_count`, typed damaging entries, block/dodge/counter/grab/wall tags and flags must satisfy fixed consistency table; mismatch is rejected. |
| `WP09-CFG-005` | Core | All ranges, probabilities, reductions, min/max, denominators and wall/force relations are validated without defaults. |
| `WP09-CFG-006` | Core | Reachable multiply/add/tick/ID counter overflow is rejected pre-start; checked runtime guard cannot wrap. |
| `WP09-CFG-007` | Conformance | Current canonical config remains byte-identical with SHA `0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f`; validation stays `0 errors / 0 warnings`. |
| `WP09-CFG-008` | Conformance | Production resolution contains no branches on canonical AnimalId/ActionId and preserves `Battle.Core → Battle.Contracts + BCL` graph. |

### 8.2 Schedule и Stable IDs — 6 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-SCH-001` | Core | Numeric schedule `0\|3\|6` becomes three `Hit` entries with ordinals `00..02`. |
| `WP09-SCH-002` | Core | `counter:0`, `grab:0`, `throw:5`, `wall:3` preserve kind, relative tick and ordinal. |
| `WP09-SCH-003` | Core | Absolute impact tick is checked `commit + startup + relative`; overflow fails atomically. |
| `WP09-SCH-004` | Core | External IDs match §4.4 exactly and reset per battle, not per process. |
| `WP09-SCH-005` | Conformance | Existing `AttackPrepared.impact_ticks` remains sorted numeric projection; wire/schema version unchanged. |
| `WP09-SCH-006` | Core | A hit-group ID is consumed at most once per target; second use resolves as `HitGroupConsumed` without RNG/mutation. |

### 8.3 Groups и ordering — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-GRP-001` | Core | One due schedule entry creates exactly one resolution group. |
| `WP09-GRP-002` | Core | Multi-hit entries create distinct groups in schedule ordinal order. |
| `WP09-GRP-003` | Core | Equal legal strikes create one Trade group from one pre-impact snapshot, no TieBreak draw. |
| `WP09-GRP-004` | Core | Next group reads fully committed HP/stagger/position/state from previous group. |
| `WP09-GRP-005` | Core | Canonical key applies class, priority DESC, initiative DESC, tie, Stable IDs and local indices in exact order. |
| `WP09-GRP-006` | Core | Unequal priority resolves with method `Priority`; equal priority/unequal initiative resolves with `Initiative`; neither path consumes RNG. |
| `WP09-GRP-007` | Core | Exact exclusive tie consumes one bounded Resolution/TieBreak draw, reports method `SeededHash` and maps result to canonical contender order. |
| `WP09-GRP-008` | Conformance | Group events are contiguous, never reopen and carry identical `resolution_group_id`. |

### 8.4 Geometry — 6 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-GEO-001` | Core | `gap == hit_range_min` is a hit. |
| `WP09-GEO-002` | Core | `gap == hit_range_max` is a hit. |
| `WP09-GEO-003` | Core | `gap < min` emits only `AttackMissed(OutOfRange)` for outcome, no RNG/mutation. |
| `WP09-GEO-004` | Core | `gap > max` emits only `AttackMissed(OutOfRange)` for outcome, no RNG/mutation. |
| `WP09-GEO-005` | Core | Table-driven invalid target, defeated target and target behind frozen direction emit respectively `InvalidTarget`, `DefeatedTarget`, `WrongDirection`; none consumes RNG or retargets. |
| `WP09-GEO-006` | Core | TrackTarget uses live target position/gap but keeps commit direction and target identity. |

### 8.5 Counter, Dodge и Block — 12 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-DEF-001` | Core | Valid strike with no active defense hits automatically and consumes no Resolution draw. |
| `WP09-DEF-002` | Core | Numeric fixture computes `DodgeChance=500`. |
| `WP09-DEF-003` | Core | Eligible Dodge draw `499` succeeds, emits `Dodged`, cancels current intent and applies no damage/control. |
| `WP09-DEF-004` | Core | Eligible Dodge draw `500` fails; draw is carried by following `AttackHit` with `DodgeFailed`. |
| `WP09-DEF-005` | Core | Undodgeable skips Dodge without draw but still performs geometry and possible Block. |
| `WP09-DEF-006` | Core | Numeric fixture computes `BlockChance=560`. |
| `WP09-DEF-007` | Core | Eligible Block draw `559` succeeds; `Blocked.guard_break=false → DamageApplied`, damage `50`, no normal stagger/force. |
| `WP09-DEF-008` | Core | Eligible Block draw `560` fails; following `AttackHit` owns draw/reason `BlockFailed`; exact incoming `guard_break` tag adds `GuardBreak`, normal resolution applies. |
| `WP09-DEF-009` | Core | Inactive/ineligible defense never consumes RNG and cannot alter outcome. |
| `WP09-DEF-010` | Core | Matching Counter with priority equal to incoming succeeds deterministically, emits `Countered` and its geometry-valid counter hit/damage in the same group, no RNG. |
| `WP09-DEF-011` | Core | Counter below incoming priority does not cancel strike; pipeline proceeds to Dodge/Block/hit without Counter draw. |
| `WP09-DEF-012` | Integration | One controlled trace proves exact precedence Counter before Dodge before Block and no event has more than one draw. |

### 8.6 Damage — 10 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-DMG-001` | Core | Numeric fixture produces breakdown `150/150/100/100/100`, HP `1000→900`. |
| `WP09-DMG-002` | Core | Floor occurs after every specified fixed-point operation; algebraic regrouping fixture produces the prescribed lower integer. |
| `WP09-DMG-003` | Core | Damage below minimum returns action `MinDamage`. |
| `WP09-DMG-004` | Core | Damage above cap returns global `DamageCap`. |
| `WP09-DMG-005` | Core | Armor ratio denominator uses `max(armor+armor_k,1)` and valid negative/overflow-risk derived input is rejected before execution. |
| `WP09-DMG-006` | Core | Block breakdown uses `after_block=50`, `final=50`; chip floor wins when reduction result is lower. |
| `WP09-DMG-007` | Core | HP `70`, final `100` gives `after=0`, actual loss `70`, overkill `30`, lethal true. |
| `WP09-DMG-008` | Core | Explicit non-damaging control profile may apply `0`; damaging strike cannot silently produce zero. |
| `WP09-DMG-009` | Conformance | Only `DamageApplied` may change health frames; AttackHit/Blocked/WallImpact marker alone preserves HP. |
| `WP09-DMG-010` | Core | Arithmetic failure leaves HP, stagger, RNG and journal unchanged before reserved invalid termination. |

### 8.7 Stagger/control — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-CTL-001` | Core | Numeric fixture computes `ControlRatio=1000`, `StaggerGain=60`. |
| `WP09-CTL-002` | Core | Meter `0→60` below threshold emits stagger resource mutation but no Stunned state. |
| `WP09-CTL-003` | Core | Meter `60→120` reaches inclusive threshold; order is StaggerGain resource event → Stunned `6` ticks → StaggerReset resource event to `0`. |
| `WP09-CTL-004` | Core | Stun duration clamps independently at configured min/max; state lasts `[start,start+duration)` and exits on the exact end boundary. |
| `WP09-CTL-005` | Core | Stagger/stun never consume RNG. |
| `WP09-CTL-006` | Core | Hard control cancels current action only after all valid impacts of its group commit. |
| `WP09-CTL-007` | Core | `UninterruptibleImpact` preserves already-collected current-group impact, but future multi-hit groups cancel after control/defeat. |
| `WP09-CTL-008` | Integration | Ordinary non-threshold hit does not interrupt action; no Armored/Unstoppable strength behavior is invented in WP-09. |

### 8.8 Combat MoveSelf — 6 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-MOV-001` | Core | `move_distance=10, active_ticks=3` yields tick budgets `4,3,3` and total at most `10`. |
| `WP09-MOV-002` | Core | Damaging Approach/Follow stops exactly at live `hit_range_max`; no overshoot/crossing. |
| `WP09-MOV-003` | Core | Non-impact Approach stops at `preferred_range_max`. |
| `WP09-MOV-004` | Core | Retreat stops at `preferred_range_min`; Adaptive uses commit-frozen direction and matching min/max boundary. |
| `WP09-MOV-005` | Core | TrackTarget updates gap stop only; target side change does not reverse frozen direction. |
| `WP09-MOV-006` | Integration | Simultaneous MoveSelf reuses WP-07 atomic separation/wall behavior; unused budget is lost and only nonzero position delta resets watchdog. |

### 8.9 Force и wall — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-FRC-001` | Core | Numeric fixture produces `ForceRatio=3000`, requested move `1000`. |
| `WP09-FRC-002` | Core | Push moves target away along hit direction after damage, preserving legal body bounds. |
| `WP09-FRC-003` | Core | Pull moves target toward actor and clamps at surface gap `0`; crossing is forbidden. |
| `WP09-FRC-004` | Core | Legal Swap atomically exchanges centers and reports `PositionChangeKind.Swap`. |
| `WP09-FRC-005` | Core | Swap with either destination outside recipient body bounds is wholly rejected; no partial position mutation. |
| `WP09-FRC-006` | Core | Untagged wall clip, and tagged impact with `BlockedByWall=0`, emit no WallImpact/wall damage/wall stagger. |
| `WP09-FRC-007` | Core | Tagged numeric wall fixture emits blocked `600`, wall damage `60`, wall stagger `60`; direct wall damage skips armor. |
| `WP09-FRC-008` | Integration | Order is damage/stagger → forced movement → wall → wall damage/stagger → group outcome; no voluntary movement resource gain. |

### 8.10 Grab/throw — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-GRB-001` | Core | Uncontested legal `grab` sets one ActiveGrabId, states Grabbing/Grabbed and emits one `GrabStarted(Uncontested)`. |
| `WP09-GRB-002` | Core | Contested grab priority, then initiative, then one exact TieBreak draw determine result in that order. |
| `WP09-GRB-003` | Core | `throw` requires matching active grab, resolves damage/spatial consequences and emits exactly one `GrabEnded(Throw)`. |
| `WP09-GRB-004` | Core | Grab without terminal primitive emits `GrabEnded(Release)` when Active ends. |
| `WP09-GRB-005` | Core | Start `s`, max `12` permits hold through `s+11` and auto-ends at boundary `s+12` with `MaxHoldReached`. |
| `WP09-GRB-006` | Core | Release `r`, lockout `20` rejects ticks `r..r+19` and permits tick `r+20`, without RNG on rejected attempts. |
| `WP09-GRB-007` | Core | Hard control/grabber defeat/target defeat closes grab once with corresponding reason and clears both authoritative states. |
| `WP09-GRB-008` | Integration | Orphan throw/wall primitive cancels without RNG/mutation; action costs/cooldown remain spent. |

### 8.11 Outcome — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-OUT-001` | Core | Single lethal group changes victim to Defeated only after all group spatial/control mutations. |
| `WP09-OUT-002` | Core | Fighter A lethal result yields `FighterAWin/Defeat`; mirrored result yields `FighterBWin/Defeat`. |
| `WP09-OUT-003` | Core | Equal strike trade applies both DamageApplied events before any defeat check. |
| `WP09-OUT-004` | Core | Both lethal trade impacts yield two defeated chains and `Draw/DoubleKO`. |
| `WP09-OUT-005` | Core | Defeat after multi-hit group N cancels N+1..end with no RNG/damage. |
| `WP09-OUT-006` | Integration | Defeat/DoubleKO on final active tick wins over timeout; no TimeoutReached event. |
| `WP09-OUT-007` | Integration | Without defeat, existing timeout health-fraction/equal semantics remain byte-semantic compatible. |
| `WP09-OUT-008` | Conformance | No event/mutation after BattleEnded; summary and BattleEnded payload match exactly. |

### 8.12 Events и replay semantics — 10 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-EVT-001` | Conformance | All activated WP-09 payloads round-trip typed canonical JSON under existing schema. |
| `WP09-EVT-002` | Conformance | Miss/Dodge/Block/Counter/normal/wall/grab traces match local order §6.1. |
| `WP09-EVT-003` | Conformance | before/after frames match the single event mutation; markers preserve frames. |
| `WP09-EVT-004` | Conformance | source is one earlier primary cause; related IDs are unique, earlier and ordinal sorted. |
| `WP09-EVT-005` | Conformance | Finisher forward reference is same-group guaranteed lethal only; every other forward reference fails. |
| `WP09-EVT-006` | Conformance | Damage `final=(before-after)+overkill`, lethal and defeat lineage are enforced after integrity recomputation. |
| `WP09-EVT-007` | Conformance | Resolution RNG stream/operation/range/raw/result/normalized/index are validated; malformed package returns typed failure, not exception. |
| `WP09-EVT-008` | Conformance | Group contiguity, no reopen, HitGroup single-consumption and no premature trade defeat are enforced. |
| `WP09-EVT-009` | Conformance | Attack/damage tags and cancelled intent IDs must be strict ordinal canonical sets. |
| `WP09-EVT-010` | Conformance | Resolution validator applies only to `0.4.x`; WP-08 decision validator also applies to `0.4.x`. |

### 8.13 Safety — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-SAFE-001` | Core | Event cap one below complete group rejects before first group state/RNG/event commit. |
| `WP09-SAFE-002` | Core | Event cap exactly equal to group+terminal requirement succeeds without over-reservation. |
| `WP09-SAFE-003` | Core | Previewed ChanceCheck/TieBreak state commits only with successful whole plan. |
| `WP09-SAFE-004` | Core | Any invariant/arithmetic failure rolls back HP/stagger/position/state/grab/RNG/event batch. |
| `WP09-SAFE-005` | Core | HP mutation resets zero-progress counter. |
| `WP09-SAFE-006` | Core | Nonzero stagger/position/control/grab mutation resets zero-progress counter. |
| `WP09-SAFE-007` | Core | Miss, failed conflict, marker-only event and RNG draw without mutation do not reset counter. |
| `WP09-SAFE-008` | Integration | Invalid resolution config/request is Rejected before journal Begin with sorted typed errors and zero draws. |

### 8.14 Integration fixtures — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-INT-001` | Integration | `resolution-basic-l1` produces reviewed hit→damage→defeat→BattleEnded canonical fixture and replay verifies. |
| `WP09-INT-002` | Integration | `resolution-double-ko-l1` produces one trade group, both damages before defeats and Draw/DoubleKO. |
| `WP09-INT-003` | Integration | `resolution-wall-grab-l1` covers GrabStarted, throw/wall spatial chain, GrabEnded and wall direct damage. |
| `WP09-INT-004` | Integration | New `wait-equal-l1.engine-0.4.0` preserves wait semantics with Engine-only version drift. |
| `WP09-INT-005` | Integration | New `decision-weighted-l1.engine-0.4.0` preserves WP-08 decision trace; a due-current-tick valid enemy impact sets Emergency and suppresses HardOpportunity without RNG/future-state reads. |
| `WP09-INT-006` | Integration | Standard/Diagnostic simulations have identical canonical events, RNG counters, summary and final digest. |
| `WP09-INT-007` | Integration | Fixture reader imports every activated resolution event into typed contracts and round-trips canonical bytes. |
| `WP09-INT-008` | Integration | Full canonical config battle reaches a normal terminal outcome without unsupported-action fallback or exception. |

### 8.15 Determinism — 8 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-DET-001` | Integration | Each WP-09 golden run repeated 100 times yields identical events, RNG indices, summary and digest. |
| `WP09-DET-002` | Core | `en-US`, `ru-RU`, `tr-TR` cultures produce identical ordering, IDs and arithmetic. |
| `WP09-DET-003` | Core | Reversed config/internal collection insertion order produces identical result. |
| `WP09-DET-004` | Integration | Mirrored fighters preserve symmetric numeric outcomes; only explicit actor/side identities mirror. |
| `WP09-DET-005` | Conformance | `netstandard2.1` and `net10.0` target probes produce identical WP-09 vectors. |
| `WP09-DET-006` | Conformance | Debug and Release generated fixtures/digests are identical. |
| `WP09-DET-007` | Conformance | Windows and Linux CI target-determinism outputs are byte-identical. |
| `WP09-DET-008` | Core | Diagnostic/profile/observer presence never changes Decision/Resolution draw count, index or result. |

### 8.16 Regression и quality — 6 cases

| ID | Проект | Exact pass condition |
|---|---|---|
| `WP09-REG-001` | Conformance | Production project graph still matches TDD; no Core dependency on Config/Replay/Runner/Unity. |
| `WP09-REG-002` | Conformance | Contract versions equal the plan: only Engine is `battle.core/0.4.0`. |
| `WP09-REG-003` | Conformance | All historical `0.1.0`/`0.2.0`/`0.3.0` fixture bytes and pinned SHA/digests remain unchanged. |
| `WP09-REG-004` | Conformance | WP-04 generated reproducibility passes and canonical config SHA remains pinned. |
| `WP09-REG-005` | Conformance | Inventory finds exactly `128` unique blocking WP09 IDs and every test is discoverable/not skipped. |
| `WP09-REG-006` | Conformance | Critical arithmetic/order/transition/safety branches are 100%; Battle.Core line coverage remains `>=85%`; UnityClient diff is empty. |

Итого: `128` unique blocking acceptance IDs.

## 9. Traceability OPEN → acceptance

| Решение | Основные acceptance prefixes |
|---|---|
| `OPEN-WP09-01` | `REG`, inventory и все matrix IDs |
| `OPEN-WP09-02` | `REG`, `INT` |
| `OPEN-WP09-03` | `CFG`, `CTL`, `REG` |
| `OPEN-WP09-04` | `INT`, `EVT`, `REG` |
| `OPEN-WP09-05` | `CFG` |
| `OPEN-WP09-06` | `SCH` |
| `OPEN-WP09-07` | `GRP`, `GEO`, `INT` |
| `OPEN-WP09-08` | `GRP`, `OUT` |
| `OPEN-WP09-09` | `GRP`, `DET` |
| `OPEN-WP09-10` | `GEO`, `MOV` |
| `OPEN-WP09-11` | `DEF` |
| `OPEN-WP09-12` | `DMG`, `EVT` |
| `OPEN-WP09-13` | `DEF`, `DMG` |
| `OPEN-WP09-14` | `CTL`, `OUT` |
| `OPEN-WP09-15` | `MOV`, `FRC` |
| `OPEN-WP09-16` | `FRC`, `INT` |
| `OPEN-WP09-17` | `GRB`, `INT` |
| `OPEN-WP09-18` | `DEF`, `CTL`, `GRB`, `REG` |
| `OPEN-WP09-19` | `SCH`, `DET` |
| `OPEN-WP09-20` | `SAFE`, `OUT` |
| `OPEN-WP09-21` | `EVT`, `INT` |
| `OPEN-WP09-22` | `DET`, `REG` |
| `OPEN-WP09-23` | `CFG`, `REG` |

## 10. Planned test layout

```text
tests/Battle.Core.UnitTests/Resolution/
  ResolutionProfileTests.cs
  HitScheduleTests.cs
  IntentOrderingTests.cs
  GeometryTests.cs
  DefenseResolverTests.cs
  DamageResolverTests.cs
  ControlResolverTests.cs
  CombatMoveSelfTests.cs
  ForcedMovementResolverTests.cs
  GrabResolverTests.cs
  GroupOutcomeTests.cs
  ResolutionSafetyTests.cs

tests/Battle.ConformanceTests/Replay/
  ResolutionPayloadContractTests.cs
  ResolutionReplaySemanticTests.cs
  Wp09HistoricalReplayTests.cs

tests/Battle.ConformanceTests/
  Wp09ArchitectureGuardsTests.cs
  Wp09BlockingCaseInventoryTests.cs
  Wp09CrossCuttingRegressionTests.cs

tests/CombatLab.IntegrationTests/Resolution/
  ResolutionBasicGoldenTests.cs
  ResolutionDoubleKOGoldenTests.cs
  ResolutionWallGrabGoldenTests.cs
  Wp09CrossCuttingDeterminismTests.cs
```

Фактические имена могут уточняться без изменения IDs и pass conditions.

## 11. Required verification after implementation

```powershell
dotnet restore --locked-mode
dotnet build CombatLab.sln --configuration Release --no-restore
dotnet test CombatLab.sln --configuration Release --no-build --no-restore
dotnet test CombatLab.sln --configuration Release --no-build --no-restore --filter WorkPackage=WP09

./scripts/verify-wp04-generated.ps1 -Configuration Release
./scripts/verify-wp06-target-determinism.ps1 -Configuration Release
./scripts/verify-wp07-target-determinism.ps1 -Configuration Release
./scripts/verify-wp08-target-determinism.ps1 -Configuration Release
./scripts/verify-wp09-target-determinism.ps1 -Configuration Release
./scripts/verify-wp09-coverage.ps1 `
  -CoreResultsDirectory ./TestResults/Coverage/Core `
  -IntegrationResultsDirectory ./TestResults/Coverage/WP09Integration `
  -ReplayResultsDirectory ./TestResults/Coverage/WP09Replay
```

Дополнительно:

- повторить build/test в Debug;
- выполнить target probes для `netstandard2.1` и `net10.0`;
- проверить immutable historical SHA/digests;
- проверить Standard/Diagnostic parity;
- дождаться green `windows-latest`/`ubuntu-latest` × Debug/Release.

## 12. Approval и переход к реализации

Owner approval получен `2026-09-07`:

- `OPEN-WP09-01..23 — CLOSED`;
- Test Plan имеет статус `APPROVED / BLOCKING`;
- WP-09 имеет статус `COMPLETED`;
- реализация закрывает все `128` IDs без изменения scope;
- fixtures, workbook и generated artifacts изменяются только в явно разрешённых этим планом границах; `UnityClient` остаётся неизменным.

Локальное исполнение `2026-09-07`: `128/128` IDs обнаружены inventory; Release/Debug build/test, replay/historical/generated/target-determinism gates green; selected critical branch coverage `100%`, combined Battle.Core line coverage `88.02%`.

GitHub Actions execution от `2026-09-08` для code head `9317c82`: `ubuntu-latest`/`windows-latest` × Debug/Release — все четыре jobs green. Completion gate закрыт; WP-09 — `COMPLETED`.
