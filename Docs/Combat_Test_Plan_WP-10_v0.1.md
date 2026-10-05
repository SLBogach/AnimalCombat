# Combat Test Plan WP-10 v0.1 — Effects

> Статус: `APPROVED / BLOCKING — EXECUTED / PASSED`; `132/132` acceptance IDs; implementation `COMPLETED`, `2026-10-05`. Windows/Linux × Debug/Release CI green подтверждён владельцем; последний code head `620ebed`.
>
> Подготовлено и утверждено `2026-10-03`. Обязательная blocking matrix: `132` unique acceptance IDs.
> Все `OPEN-WP10-01..28` приняты владельцем 2026-10-03 и имеют статус `CLOSED`. Утверждение не означает, что WP-10 реализован или DATA migration уже выполнена.

## 1. Gate и источники

Нормативный scope и точные proposed rules находятся в [WP-10 Brief](./WP-10_Brief.md). Связанные документы: [Implementation Status](./Implementation_Status.md), [Decisions](./Decisions.md), [Index](./Index.md). Source sections перечислены в Brief §2; повторное чтение остальных оригинальных разделов для implementation не требуется.

После owner approval каждый ID §6 обязателен. Тест имеет `WorkPackage=WP10` и `AcceptanceId=<ID>`. Inventory запрещает пропуски, дубли и неизвестные IDs. Один parameterized test может закрыть несколько IDs только через явно discoverable metadata и отдельные semantic assertions; не считать само наличие строки в source доказательством execution. Skip/quarantine/platform-conditional pass запрещены. Golden/SHA без semantic assertions недостаточен.

Approval проекта закрывает `BLOCK-WP10-APPROVAL-01`; физические DATA/artifact/history/test gates не закрываются автоматически. Первый implementation slice — history pins + DATA/tooling, runtime включается только после strict materialization v0.2.

## 2. Test harness

| Уровень | Проект | Что проверяется |
|---|---|---|
| U | `Battle.Core.UnitTests` | materialization, floor/clamp, store/queue/expiry, control filters, atomic plans |
| C | `Battle.ConformanceTests` | schema/contracts, versions/roles/lineage, tamper, inventory/architecture/artifact pins |
| I | `CombatLab.IntegrationTests` | полная Simulate/journal/fixtures, target/process/profile/culture parity и failures |

Synthetic profiles создаются в tests; они не становятся production defaults. Порядок входных catalog collections можно переставлять до compilation; canonical compiler обязан выдавать тот же snapshot. Runtime не читает XLSX, JSON tokens, wall clock или diagnostic sink.

Для cap/overflow tests разрешён internal controlled harness с corrupted state/preview; тест явно отделяет валидный external input (Rejected до Begin) от невозможного internal mutation (FailedInvariant после начала).

## 3. Numeric oracles

### 3.1 `effect_unit_v1`

```text
FP_SCALE=1000
Synthetic stat bounds: Guard 0..1000, Armor 0..2000, MoveSpeed 1..500
Base Guard=100; Gear Add=+10; Temporary Add=-30; Multiply=1500
  Add sum=-20; subtotal=80; product=1500; derived=120
  This is not sequential 100+10 → *1.5 → -30 =135.
Override=7 after product => Guard=7; Override=-5 => clamp 0.
Signed floor: mul_fp(-3,500)=-2; not -1.
Product order: priorities/source IDs choose 1001,1001,1501
  fold 1000→1001→1002→1504; reverse order gives1503.
Base=1000 and test bound max2000 => derived1504.

Base Armor=100; Gear Armor+18 =>118
Thick-hide Multiply1150 =>135; remove =>118; repeated recompute remains135.
MoveSpeed base135 + gear12 =>147; multiplier2000 =>294; multiplier4000 =>clamp500.
MaxHealth base1650 + gear120 =>1770 at setup; dynamic MaxHealth modifier rejected.
```

Multiplier4000 — допустимое значение только controlled unit profile с соответствующим validated domain; production global weight multiplier max3000 не подменяется тестовой величиной. Arithmetic cases используют checked Int64 intermediates и checked Int32 gameplay results.

### 3.2 Damage/chance/weight oracle

```text
BaseDamage100, Power100, PowerRatio500 =>PowerTerm150
DamageTaken1250 =>Raw187 (floor)
Armor100, ArmorK200 =>ArmorRatio333
AfterArmor=floor(187*667/1000)=124
MinDamage10, global floor1/cap600 =>124
BlockReduction500, ChipMin10 =>62
Without exposed: Raw150, AfterArmor100, blocked50.

BlockBase500, Guard100, GuardBreak80, slope3, offset+50 =>610
DodgeBase500, Evasion120, Precision120, slope3, offset-50 =>450
Chance bounds remain block100..900, dodge50..850.
Chance success iff draw<chance; equality is failure.

Synthetic BaseWeight100 and all ordinary stages identity1000:
BlockWeight650 on block action =>65
PunishWeight1400 on punish action =>140
WallActionWeight1250 on wall_impact action =>125
Nonmatching tags =>100; no multiplier applied twice.
```

DamageTaken modifier применяется до armor; direct wall damage сохраняет WP-09 formula. В equal trade group Armor effect от первого удара не меняет второй impact; следующий group использует updated Armor.

### 3.3 Expiration/stack oracle

```text
Apply tick5,D3 =>end_exclusive8
Before-effect: active impacts5,6,7; RemovedBefore phase2 tick8
After-effect: active impacts5,6,7; RemovedAfter phase12 tick7
Frame remaining at ticks5,6,7:3,2,1
After D1 at tick5: Added then RemovedAfter tick5
Refresh ResetDuration at tick6,D3 =>end9
KeepLonger oldend10,applytick6,D3 =>end10
AddStacks Precision+8, ActionSpeed+5, cap3:
  stacks1:(+8,+5); stacks2:(+16,+10); stacks3:(+24,+15)
  fourth application:count3; refresh only when expiry changes
Reject occupied group =>no event, no expiry extension
Replace =>Removed(Replaced) before Added
StrongestWins Value1=1150 versus1300 =>1300 wins
Exact equal comparison =>lowest stable identity wins; same identity retains incumbent
```

### 3.4 Control/knockdown oracle

```text
Fatigue lookup[0..3]=[1000,750,500,250], threshold3, cap3,D100
BaseStun8, ControlRatio1000, bounds3..20:
  fatigue0=>8; fatigue1=>6; fatigue2=>4; fatigue3=>3
ControlEnded:count0→1→2→3; third crossing creates immunityD25
Refresh fatigue while count3 does not renew immunity
Normal immunity blocks new Stun/Knockdown/Grab; damage still applies
Threshold response when prevented resets Stagger to0 once

GrabEnd tick10,D20 after-boundary:blocked through tick29; allowed tick30
Knockdown tick10, base phases2/6/3, ratio1000,fatigue1000:
  Fall[10,12),Grounded[12,18),GetUp[18,21),Ready phase2 tick21
Same phases at fatigue750 =>1/4/2,total7,Ready tick17
Wakeup tick21,D25 =>after-boundary expiry phase12 tick45
No new decision before Ready; no MaxHold extension; no cost refund
```

## 4. Event and failure assertions

Каждый mutation case проверяет typed payload, before/after, affected actor/source target, action/effect IDs, source/related/group lineage, RNG nullability, exact effect delta и resulting derived values (последнее — producer tests, не угадывание из frames).

Mutation groups строятся preview→validate→exact preflight→commit. Failure tests требуют отсутствия partial state/effects/cache/timers/budgets/visited sets/RNG/events из отказавшего batch. Earlier committed batches сохраняются. FailedInvariant не превращается в draw/loss и не создаёт второй Begin/BattleEnded/Complete.

Standalone verifier проверяет observable semantic contradictions. Negative fixture после изменения пересчитывает integrity chain, когда цель — semantic rejection; иначе hash mismatch мог бы скрыть отсутствующий semantic validator.

## 5. Golden fixtures

Новые versioned files в `CombatLab/fixtures/replay/v0.1`:

- `effects-stack-expiry-l1.engine-0.5.0.json`: AddStacks/Refresh, remaining ticks, exact before/after expiration и modifier restoration;
- `effects-impact-snapshot-l1.engine-0.5.0.json`: synthetic Global rule `DamageTaken` с recipient Self, Armor buff, equal-group snapshot и subsequent-group dynamic damage;
- `effects-control-chain-l1.engine-0.5.0.json`: разрешённый control, ControlEnded, fatigue threshold/immunity и suppressed re-control;
- `effects-knockdown-l1.engine-0.5.0.json`: throw/knockdown stages, illegal regular hit/legal GroundHit, wakeup/lockout/immunity.

Source builds/config patches/seeds и metadata фиксируются в fixture harness. Controlled effects synthetic, без подключения production bear/kangaroo/gorilla passive logic. Для determinism сравниваются canonical events/input/final digest; byte-for-byte whole artifact comparison использует fixed metadata. Actual SHA pins добавляются только после verified production execution.

Дополнительно создать current `.0.5` wait/decision/basic/double-KO/wall-grab; `.0.4` копии остаются historical. Existing Unity bundled `.0.4` artifacts и their parity tests не изменяются.

## 6. Blocking acceptance matrix

Все `132` строки blocking. U/C/I обозначают минимум required test level; дополнительное покрытие разрешено.

### 6.1 Baseline/contracts — 6

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-BASE-001` | C | Baseline has completed WP-09 and typed effect vocabulary; new version is `battle.core/0.5.0`, with no unapproved dependency on Unity/Replay/Config from Core. |
| `WP10-BASE-002` | C | v0.2 compiled catalog copies input immutably, rejects duplicate RuleId/unknown owners, preserves source-compatible legacy catalog construction. |
| `WP10-BASE-003` | C | Event/replay/RNG/ordering/mode versions remain specified Brief values; only engine/balance bump. |
| `WP10-BASE-004` | C | EffectFrame unique ordinal IDs, stacks1..255, remaining>=0, two expiry enum values; >128 entries invalid. |
| `WP10-BASE-005` | C | No runtime XLSX/JSON parsing, float/double/decimal gameplay, time/random/global mutable state in Effects path. |
| `WP10-BASE-006` | C | Inventory enumerates exactly this matrix; missing/extra/duplicate AcceptanceId or skipped discovery fails. |

### 6.2 DATA/materialization — 11

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-DATA-001` | C | v0.2 requires all15 bound pairs, three queue limits and three knockdown stages; delete each required key: typed validation, no default. |
| `WP10-DATA-002` | U | min>max, nonpositive divisor/speed domain, stackcap outside1..255, duration0, instance limit>128 rejected before Begin. |
| `WP10-DATA-003` | C | Each new Workbook Value has correct JSON Map namespace/ref/Required/IncludeRuntime; source→export→loader→compiled agrees. |
| `WP10-DATA-004` | U | Unknown target/operation/ref/trigger/condition/recipient/phase token rejected with stable code/path, sorted errors, draws0. |
| `WP10-DATA-005` | U | Missing CompareKey/lookup entry, incompatible group policy/target domain, duplicate semantic role rejected. |
| `WP10-DATA-006` | U | Fatigue threshold3<=cap3, lookup4 entries and global/effect immunity/decay/lockout durations agree; mismatch rejects. |
| `WP10-DATA-007` | U | Reachable sum/product/timer/counter overflow risk rejects before journal.Begin; no final clamp conceals overflow. |
| `WP10-DATA-008` | C | Legacy v0.1 loader/compiler still accepts pinned config; engine0.5+v0.1 returns version rejection, not implicit migration. |
| `WP10-DATA-009` | C | v0.2 export0errors/0warnings; JSON/schema/map/validation bytes reproducible; manifest differs only permitted generated_utc. |
| `WP10-DATA-010` | U | Dormant WP-11 channels allowed in catalog; reachable rule using GripGain/resource-only channel rejected; unselected Action-owned rule never executes. |
| `WP10-DATA-011` | U | Flag Override accepts only0/1; Multiply on allow flag and Add on multiplicative weight channel reject before Begin, with no generic numeric coercion. |

### 6.3 Modifiers/consumers — 14

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-MOD-001` | U | §3.1 mixed Add/Multiply returns120, not135; source order deterministic under input permutation. |
| `WP10-MOD-002` | U | Signed floor(-3,500)=-2; Int64 multiplication and int32 result overflow never wrap. |
| `WP10-MOD-003` | U | Three sorted multipliers yield1504 for stated order; shuffled insertion does not yield1503. |
| `WP10-MOD-004` | U | Ordered Override last wins; Override7=>7, Override-5=>0 after final clamp. |
| `WP10-MOD-005` | U | MoveSpeed147→294, controlled cap clamp500, lower clamp1; min/max boundary equality legal. |
| `WP10-MOD-006` | U | Armor118→135 on add; 100 recomputations stay135; removal restores118, not a rounded inverse/division estimate. |
| `WP10-MOD-007` | U | Three AddStacks yield Precision+24/ActionSpeed+15; Multiply repeats per stack; Override applied once. |
| `WP10-MOD-008` | I | Existing committed startup/recovery/system segment speed unchanged after effect; next commit/segment uses new values. |
| `WP10-MOD-009` | U | Damage/chip oracle124/62; wall direct formula unchanged; final HP loss/overkill remain WP-09 semantics. |
| `WP10-MOD-010` | U | Chance offsets produce610/450 before clamps; draw=chance fails; inactive/ineligible defense has no draw. |
| `WP10-MOD-011` | U | Matching block/punish/wall weights65/140/125; nonmatching100; effective GrabPriority/Mass/ControlResistance read at specified boundary. |
| `WP10-MOD-012` | I | Equal trade sees pre-group armor for both impacts; subsequent group sees added effect; decisions share post-expiry snapshot and no retroactive re-sort. |
| `WP10-MOD-013` | U | Effect on A invalidates only A cache; B stats remain base+gear; reuse same compiled config in A/B/A simulations leaks no mutable effect/counter state. |
| `WP10-MOD-014` | U | Runtime modifiers on MaxHealth/MaxEnergy/CollisionRadius/StaggerThreshold rejected; static MaxHealth1770/currentHP1770 valid and no effect-induced HP mutation. |

### 6.4 Lifetime — 9

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-EXP-001` | U | tick5,D3 Before remains at5..7 and removed phase2tick8 with ExpiredBeforeTick. |
| `WP10-EXP-002` | U | tick5,D3 After remains through impacts7, removed phase12tick7 with ExpiredAfterTick. |
| `WP10-EXP-003` | I | D1 After added/removed same tick in correct order; D1 Before present until next phase2. |
| `WP10-EXP-004` | U | Remaining3/2/1 for ticks5/6/7, never negative; silent timer update does not append EffectAdded. |
| `WP10-EXP-005` | U | Reset at6,D3→end9; shorter reset allowed; KeepLonger oldend10 remains10. |
| `WP10-EXP-006` | I | Before expiry recomputes decision/impact stats before phase5; After expiry never weakens current tick impacts. |
| `WP10-EXP-007` | U | Multiple expiries use priority/ID/fighter canonical order; registry permutation produces identical plan. |
| `WP10-EXP-008` | I | Expiry removal triggers bounded closure once, restores base-derived stats, source references latest successful application. |
| `WP10-EXP-009` | I | After-expiry closure creates a due D1 After-effect: same boundary drains it once; bounded cycles/caps still apply and no expired effect leaks into next tick. |

### 6.5 Stacking — 13

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-STACK-001` | U | Empty group creates one profile/stack/expiry and exactly one EffectAdded. |
| `WP10-STACK-002` | U | Reject occupied group changes nothing, no event/extension/effect activation charge. |
| `WP10-STACK-003` | U | Refresh changes only approved lifetime/lineage, retains one profile; same expiry is no-op. |
| `WP10-STACK-004` | U | Replace emits Removed(Replaced) then Added, never duplicate simultaneous occupants. |
| `WP10-STACK-005` | U | Strongest Value1 comparison1150/1300 chooses1300; weaker incoming cannot extend lifetime. |
| `WP10-STACK-006` | U | Strongest exact tie chooses lowest stated stable identity, same identity retains incumbent, no RNG. |
| `WP10-STACK-007` | U | DurationTicks and StackCount CompareKey use declared domain; absent/incompatible key rejected. |
| `WP10-STACK-008` | U | AddStacks0→1→2→3; fourth caps3; real expiry refresh emits Added3→3. |
| `WP10-STACK-009` | U | At-cap samecount+sameexpiry no-op; admitted rule budget changes, effect budget/progress do not. |
| `WP10-STACK-010` | U | Explicit Remove removes once; absent profile removal no-op; surviving group modifiers remain correct. |
| `WP10-STACK-011` | U | Two profiles in compatible Replace/Strongest group never coexist; Refresh/AddStacks multi-ID group invalid. |
| `WP10-STACK-012` | I | Replacement at instance cap succeeds without transient129th/public duplicate entry; count/new-group failure handled atomically. |
| `WP10-STACK-013` | U | Two distinct real sources with rule/effect tick cap2 and cooldown0 create stacks1→2 in one group; repeated source does not create stack3; one public EffectId entry. |

### 6.6 Triggers — 14

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-TRG-001` | U | Global/Action/Effect owner resolution and Self/Opponent recipient follow typed subject; no AnimalId/ActionId hardcoded branch. |
| `WP10-TRG-002` | I | BattleStart closure runs after both initial frames and BattleStarted seq0; no partially initialized opponent access. |
| `WP10-TRG-003` | U | Eligibility/living/condition guards reject before enqueue; selected dormant passive does not activate itself. |
| `WP10-TRG-004` | U | Causal FIFO levels and sequence/priority/rule/owner/recipient key exact; shuffled registration produces same queue. |
| `WP10-TRG-005` | U | Same owner/rule/source/recipient once; distinct real source event permits new activation within budgets. |
| `WP10-TRG-006` | U | Applytick10,cooldown5: blocked ticks10..14 after admission, ready15; no wall clock. |
| `WP10-TRG-007` | U | Rule/effect per-tick/per-battle caps independently enforced; admitted no-op charges only rule budget. |
| `WP10-TRG-008` | U | A→B→A repeated root node suppressed with diagnostic reason; no extra canonical marker/RNG/progress. |
| `WP10-TRG-009` | U | Chain child depth8 legal; eligible depth9 causes EffectTriggerDepthExceeded and full current-closure rollback. |
| `WP10-TRG-010` | U | 128 admitted nodes legal;129th eligible causes EffectTriggerTickCapExceeded; suppressed candidate not counted; next tick resets. |
| `WP10-TRG-011` | I | EndOfTick hooks reference earlier event; closure then After expiry; normal hooks suppressed on terminal tick. |
| `WP10-TRG-012` | I | EffectAdded/Removed triggers inherit source/root causality; remove owner captured from pre-removal snapshot, no dangling/private side channel. |
| `WP10-TRG-013` | U | Global ControlEnded rule with cap1/tick has independent subject-A and subject-B budgets; simultaneous lifecycle exits charge each once, without side favoritism. |
| `WP10-TRG-014` | I | Two EndOfTick hooks on consecutive silent ticks share earlier SourceEventId but differ by occurrence tick; both run when budgets/cooldown permit, once each. |

### 6.7 Control/guard/grab — 12

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-CTRL-001` | U | Fatigue stack0/1/2/3 reads1000/750/500/250 exactly once; never multiply lookup and value1 twice. |
| `WP10-CTRL-002` | I | One living ControlEnded lifecycle creates one fatigue activation; GrabEnded and StateChanged do not double-charge same lifecycle. |
| `WP10-CTRL-003` | U | BaseStun8 gives8/6/4/3; resistance then fatigue then existing stun clamp; no ChanceCheck. |
| `WP10-CTRL-004` | I | Crossing2→3 applies immunityD25; at-cap fatigue refresh does not reapply or extend immunity. |
| `WP10-CTRL-005` | U | Immunity prevents Stun/Knockdown/Grab, leaves permitted damage, logs unchanged StateChanged Prevented and resets threshold once. |
| `WP10-CTRL-006` | U | New hard control inside immunity cannot renew it; at expiry normal eligible control available. |
| `WP10-CTRL-007` | U | Fatigue D100 expires entire stack group; next lookup returns1000; refresh moves one shared end only. |
| `WP10-CTRL-008` | I | GrabEnd tick10 enforces lockout through29, allows30; attacker/opponent cannot bypass via another grab ActionId. |
| `WP10-CTRL-009` | U | Same-source multi-hit control does not refresh lifecycle; ordinary fallback preserves immunity/lockout predicates. |
| `WP10-CTRL-010` | I | Failed eligible Block+guard_break emits GuardBreak hook→guard-broken; effective Guard-30/BlockWeight650 applied next group/decision. |
| `WP10-CTRL-011` | U | Successful Block, no active Block, miss, Dodge, counter do not activate guard-broken. |
| `WP10-CTRL-012` | I | Existing maxhold/release/throw/current-group trade preserved; effective control protections never extend maxhold or dead-target grab. |

### 6.8 Knockdown — 8

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-KDN-001` | U | Base2/6/3 at tick10 gives stage boundaries12/18/21, no early decision. |
| `WP10-KDN-002` | U | fatigue750 gives1/4/2,total7; stage minimum1 and overflow checks exact. |
| `WP10-KDN-003` | I | State KnockedDown through stages, null ActionId/ActionPhase; StateChanged same-state reasons expose Grounded/GetUp, total remaining decreases. |
| `WP10-KDN-004` | U | Regular hit against all KnockedDown stages misses InvalidTarget; no defense RNG/damage. |
| `WP10-KDN-005` | I | ground_hit can damage, lethal defeats; same-source knockdown does not refresh timer and cannot resurrect victim. |
| `WP10-KDN-006` | I | Wakeup phase2 tick21 applies fatigue/wakeup rules before decisions; immunity expires aftertick45. |
| `WP10-KDN-007` | U | ControlImmunity/WakeupImmunity/Unstoppable protection prevents new knockdown without blocking valid damage. |
| `WP10-KDN-008` | I | Throw/force/wall consequences complete before knockdown/defeat; no duplicate ActionCancelled, lockout, fatigue or stage timers. |

### 6.9 Interrupts — 8

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-INT-001` | U | DATA phase lists and strengths0..3 validate; threshold equality interrupts, lower strength does not. |
| `WP10-INT-002` | U | Armored threshold3 ignores Light1/Medium2 Startup hit interruption; Heavy3 cancels; damage still passes. |
| `WP10-INT-003` | U | Unstoppable ignores only configured Stun/Knockdown in protected phases; outside phases allowed. |
| `WP10-INT-004` | U | Grab/Defeat cannot be bypassed by generic Unstoppable; Counter keeps matching deterministic policy. |
| `WP10-INT-005` | I | Ordinary combat Active/Recovery hit does not cancel; interruptible movement Startup/Active respects explicit phases. |
| `WP10-INT-006` | I | Cancellation keeps exact committed costs/cooldown and removes future schedule entries, no refund. |
| `WP10-INT-007` | I | UninterruptibleImpact current-group intent survives cancellation; future multi-hit entries do not. |
| `WP10-INT-008` | U | Prevented immunity control does not cancel action; permitted hard control cancellation emits once. |

### 6.10 Events/verifier — 9

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-EVT-001` | C | EffectAdded/Removed require EffectId and correct affected actor/source target roles, self/global target null. |
| `WP10-EVT-002` | C | Source earlier; related unique ordinal; group/action/decision link inherited only where applicable; RNG null. |
| `WP10-EVT-003` | C | Added delta/remaining/stack policy matches before/after; duplicate member or inconsistent stacks rejected after integrity rehash. |
| `WP10-EVT-004` | C | Removed absent/wrong stacks/reason/boundary rejected; Replace Removed→Added lineage verified. |
| `WP10-EVT-005` | C | Premature Before/After expiry or stale post-expiry membership rejected with typed semantic failure. |
| `WP10-EVT-006` | C | Prevented control cannot change state/action or extend immunity; illegal knockdown/wakeup frame sequence rejected. |
| `WP10-EVT-007` | C | Snapshot/keyframe/reducer/final frames consistent with effect membership; advisory keyframe failure retains canonical replay path. |
| `WP10-EVT-008` | C | Engine0.5 invokes composed validators; historical0.1–0.4 retain old policy and hashes, no retroactive effect requirements. |
| `WP10-EVT-009` | C | Config-aware producer conformance recomputes stat/damage/queue rules for pinned input; standalone verifier checks public deltas without inventing private stats/cooldown history. |

### 6.11 Safety/terminal — 9

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-SAFE-001` | U | Effect closure event cap one below/at exact batch+cleanup+BattleEnded reserve: below rolls back, at commits fully. |
| `WP10-SAFE-002` | U | Depth/tick cap failure rolls back stats/effects/timers/RNG/budgets/visited sets and drafts in entire originating group. |
| `WP10-SAFE-003` | U | Instance32 legal;33rd new group fatal; replacement at32 legal; public128 still structural maximum. |
| `WP10-SAFE-004` | U | Corrupted runtime overflow/inconsistent mutation gives typed failure; no partial Append, cache drift or wraparound. |
| `WP10-SAFE-005` | U | Suppressed/rejected/no-op/queue-only/RNG-only/events-only activity is no progress; actual expiry/timer/stack/derived mutation is progress. |
| `WP10-SAFE-006` | I | Winning/lethal/timeout tick runs terminal cleanup once, no normal EndOfTick/reapplication triggers; final effects empty, BattleEnded last. |
| `WP10-SAFE-007` | I | Late ApplyEffect to Defeated/HP0 suppressed; lethal same-group closure cannot heal/resurrect or change chosen outcome. |
| `WP10-SAFE-008` | I | All failure paths preserve Begin→Append→Complete at most once and reserved invalid terminal; bounded failure causal capture, never Draw fallback. |
| `WP10-SAFE-009` | I | Failure with already-active effects has reserved capacity for all cleanup removals+BattleEnded; cannot emit partial cleanup or continue removal triggers on invalid terminal. |

### 6.12 Determinism — 8

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-DET-001` | I | Each four effect goldens repeated100 in one process: identical canonical events/RNG counters/input/final digest. |
| `WP10-DET-002` | I | Ten fresh-process executions with fixed metadata: byte-identical golden output. |
| `WP10-DET-003` | I | Standard/Diagnostic canonical chains equal; SummaryOnly outcome/summary/RNG equal; diagnostic candidate detail differs only outside chain. |
| `WP10-DET-004` | I | Actual netstandard2.1/net10.0 assemblies produce same nine current scenario artifacts; assert loaded TFM, not just same source. |
| `WP10-DET-005` | I | Debug/Release output equality; Windows/Linux CI compares committed golden bytes. |
| `WP10-DET-006` | I | en-US/ru-RU/tr-TR cultures and catalog/rule insertion permutations preserve output. |
| `WP10-DET-007` | I | Mirrored build/arena case preserves symmetric gameplay after identity mapping; no side priority replaces required resolution tie draw. |
| `WP10-DET-008` | U | Effect queue/stack/expiry/control consumes0 RNG; existing eligible defense/tie draws keep exact stream/index protocol. |

### 6.13 Regressions — 7

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-REG-001` | C | Every0.1–0.4 historical replay file has pinned SHA and verifier success; .0.4 pins from Brief§8 acquired before bump. |
| `WP10-REG-002` | C | v0.1 workbook/config/schema/map/validation/manifest remain pinned; source replay package manifest still matches exact bytes. |
| `WP10-REG-003` | I | New0.5 no-effect wait retains exact timeout boundary and no-decision tick at limit; defeat/DoubleKO precedence unchanged. |
| `WP10-REG-004` | I | Current0.5 decision/movement/basic/wall/grab scenarios preserve body geometry, frozen commits, costs and required resolution RNG. |
| `WP10-REG-005` | C | Whole Unit/Conformance/Integration suite and legacy critical coverage gates remain enforced; no broad gate weakening for new branches. |
| `WP10-REG-006` | C | Unity tracked bytes/bundled0.4 copies untouched by WP-10; no gear/passive/resource-specific rules silently auto-enabled. |
| `WP10-REG-007` | C | Migrated v0.2 retains all old fighter/gear/action numerical fields and Stable IDs except explicit version/new metadata proposals; no incidental rebalance through exporter. |

### 6.14 Goldens — 4

| ID | Level | Setup / exact pass criterion |
|---|---|---|
| `WP10-GOLD-001` | I | stack-expiry fixture matches semantic §3.3 oracles, strict schema/verifier, pinned bytes/input/final digest. |
| `WP10-GOLD-002` | I | impact-snapshot fixture proves old same-group armor/new next-group armor, ordered causal additions, no extra RNG. |
| `WP10-GOLD-003` | I | control-chain fixture proves one fatigue/lifecycle, crossing-only immunity and bounded re-control with exact boundaries. |
| `WP10-GOLD-004` | I | knockdown fixture proves2/6/3 stages, GroundHit filter, wakeup/lockout/immunity and terminal cleanup. |

## 7. Commands и completion evidence

Из `CombatLab` после implementation:

```powershell
dotnet restore --locked-mode
dotnet build CombatLab.sln --configuration Release --no-restore
dotnet test CombatLab.sln --configuration Release --no-build
dotnet test CombatLab.sln --configuration Release --no-build --filter 'WorkPackage=WP10'
dotnet build CombatLab.sln --configuration Debug --no-restore
dotnet test CombatLab.sln --configuration Debug --no-build
./scripts/verify-wp04-generated.ps1 -Configuration Release
./scripts/verify-wp10-generated.ps1 -Configuration Release
./scripts/verify-wp10-target-determinism.ps1 -Configuration Release
./scripts/verify-wp10-target-determinism.ps1 -Configuration Debug
```

`verify-wp10-generated.ps1`, `verify-wp10-target-determinism.ps1` и `verify-wp10-coverage.ps1` реализованы и проверены локально. Generated gate выполняет две fresh-process native migration/export/loader проверки v0.2 без изменения рабочего дерева; target gate проверяет девять `.0.5` golden hashes на actual loaded netstandard2.1/net10.0 dependencies. Coverage gate требует100% critical branches и Core line>=85%. Reproducibility v0.1 сохраняется отдельным existing WP04 gate. Existing WP06/07/08/09 scripts после bump проверяют historical pins/current compatible probes, не сравнивают producer0.5 с immutable0.4 golden.

Coverage: fresh collected Core/Conformance/Integration coverage; selected effect arithmetic/ordering/stack/expiry/cycle/control/state-transition guards100% branch; Battle.Core combined line>=85%; WP-02/03/06/07/08/09 critical scopes сохраняются. Новые branches покрываются assertions, не исключаются blanket filters. В CI coverage/artifact mismatch публикует bounded diagnostics. Performance/batch targets не являются scope WP-10.

Перед COMPLETED нужны inventory132/132, отсутствие skips, actual local restore/build/test evidence, generated v0.1/v0.2 reproducibility, historical SHA+semantic verify, four effect goldens и five current baseline goldens, process/TFM/profile/culture gates, Windows/Linux × Debug/Release green. Totals/hashes не фиксировать как green до фактического run.

## 8. Decision coverage и статус

| OPEN group | Blocking families |
|---|---|
| 01–04,28: gate/baseline/scope/versions | BASE, DATA, REG |
| 05–07,16: DATA/math/snapshots/consumers | DATA, MOD, DET |
| 08–09,13–15: rules/barriers/queue/cycles | DATA, TRG, SAFE |
| 10–12: expiry/stack identity | EXP, STACK, EVT |
| 17–21: guard/control/grab/knockdown/interrupt | CTRL, KDN, INT, GOLD |
| 22–25: events/atomicity/progress/terminal | EVT, SAFE, REG |
| 26–27: historical/gates | REG, DET, GOLD, BASE |

Previous checkpoint `2026-10-04 — full-loop integration`: matrix утверждена, tooling/physical DATA/artifact gates CLOSED. Исполнены91/132 IDs:

- `WP10-BASE-002`, `WP10-DATA-001..007/009/010/011`;
- `WP10-MOD-001..014`;
- `WP10-EXP-001..009`;
- `WP10-STACK-001..006/008/009/010/013`;
- `WP10-TRG-001/002/004/005/006/008/009/010/011/012/014`;
- `WP10-CTRL-001..007/009/011`;
- `WP10-KDN-001..008`, `WP10-INT-001..008`;
- `WP10-SAFE-001..007/009`, `WP10-REG-001/002/007`.

Всего323 WP10 tests (239U/48C/36I), full Release/Debug suite1230 (776 Core/377 Conformance/77 Integration), no failures/skips. WorkPackage=WP10 запуск исполнил239U/48C/36I; source trait audit91 entries/91 unique/0 duplicate/0 unknown; full132 discovery inventory ещё не закрыт. Performance project пуст, это existing warning, не skip. Matrix/thresholds не изменены.

Versioned setup slice ранее добавил21 unit test:17 `EffectVersionedSetupTests` и4 consumer proof tests. `DATA-002/004/005/006/007/010/011`, `MOD-014` исполняются через strict versioned factory/full Simulate на real external compiled v0.2, без injected definition. Проверены все36 required keys, initial aggregate gear/maxima clamp, оба билда/geometry, domains/divisors/groups/roles/durations/lookups, unknown vocabulary со stable sorted paths, flag integer0/1 без coercion, reachable arithmetic overflow и guarded Int32.MaxValue activation caps. Dormant WP-11 catalog/unselected Action rules не активируются; reachable resource modifier reject. Consumer proof включает movement/geometry/cleanup-reserve arithmetic и fixed system timing.

Последний срез добавил27 integration tests в `EffectLifecycleIntegrationTests`, `EffectControlIntegrationTests`, `EffectBarrierAndTerminalIntegrationTests`, `EffectInterruptIntegrationTests`, shared `EffectEngineFixture`. Synthetic JSON проходит production compiler0.2, strict setup, весь Engine0.5 loop и canonical journal; observer read-only. Новые22 IDs: `MOD-008/012`, `EXP-003/006/008/009`, `TRG-002/011/012/014`, `CTRL-002/004`, `KDN-003/005/006/008`, `INT-005/006/007`, `SAFE-006/007/009`.

Exact assertions покрывают BattleStart после обоих empty initial frames; D1/D2 expiry/refresh/child closure; silent EndOfTick occurrences; old same-group/new next-group Armor; frozen startup/recovery/segment speed/intent ordering и post-expiry decision snapshot; один fatigue/lifecycle, threshold immunity без renewal; knockdown2/6/3, wakeup tick21/expiry45, GroundHit/lethal и throw/wall ordering; ordinary/explicit movement interrupt phases, paid costs/cooldown/future hits и current-group UninterruptibleImpact; terminal suppression/cleanup, HP0 recipient и invalid event-cap reserve. Producer journal0.5 теперь использует inherited WP08 event roles; standalone verifier0.5 не включён.

Owner разрешил native `.NET/OpenXML мигратор`; v0.2 source/schema/generated reproducible и historical bytes сохранены. Setup/Simulate0.5 доступны internal versioned путём; public Engine/CLI остаются0.4/balance0.1 до replay/golden gates. Остались41 blocking case:

- `BASE-001/003..006`, `DATA-008`;
- `STACK-007/011/012`, `TRG-003/007/013`, `CTRL-008/010/012`;
- `EVT-001..009`, `SAFE-008`;
- `DET-001..008`, `REG-003..006`, `GOLD-001..004`.

Требуются replay0.5 verifier/current+effect goldens, full132 inventory/critical coverage/process/TFM/profile/culture/OS/remote CI. Locked restore/Release+Debug build green,0 warnings/errors. WP04 generated Release, WP10 generated Release/Debug, WP09 actual-target/historical Release/Debug и legacy critical gates green; fresh combined Core line92.36%, consumer proof line100%/branch97.36%. Evidence: `WP10IntegrationCore`, `WP10IntegrationFinalIntegration`, sequential `WP10IntegrationWP08Replay`/`WP10IntegrationWP09Replay`. New full WP10 critical coverage ещё открыт. WP-10 не COMPLETED; UnityClient, existing fixtures/hashes и посторонние changes сохранены; commit/push не выполнялись.

## 9. Previous execution checkpoint — remaining cases + replay0.5, 2026-10-04

Исполнены108/132 blocking IDs. Source audit108 traits/108 unique/0 duplicate/0 unknown; матрица по-прежнему содержит132 строки. Это не full inventory/discovery closure.

- `WP10-BASE-002`, `WP10-DATA-001..007/009/010/011`;
- `WP10-MOD-001..014`, `WP10-EXP-001..009`;
- `WP10-STACK-001..013`, `WP10-TRG-001..014`, `WP10-CTRL-001..012`;
- `WP10-KDN-001..008`, `WP10-INT-001..008`;
- `WP10-EVT-001..008`;
- `WP10-SAFE-001..007/009`, `WP10-REG-001/002/007`.

Новые17: `STACK-007/011/012`, `TRG-003/007/013`, `CTRL-008/010/012`, `EVT-001..008`.6 unit remaining-acceptance cases +1 timer-projection regression,9 full-loop stack/control cases,60 conformance replay cases и9 positive producer replay roundtrips добавлены в этот срез. Compare/group/guards/budgets проверяются отдельно от I capacity/lockout/guard-break/max-hold. Negative replay tests используют integrity rehash и требуют semantic failures без integrity errors; historical fixtures не перезаписаны.

Verifier0.5 проверяет explicit balance0.2 metadata и public authoritative effect membership, affected frame roles, earlier causes/lineage/no RNG, stack deltas, exact Before/After expiry/latest application, replacement adjacency/cause/policy, prevented control, actionless knockdown/total ready boundary, final cleanup и advisory sparse keyframes. Повреждённый advisory keyframe даёт warning и не делает canonical event path invalid. Decision/resolution validators вызываются composed для0.5; старые0.1–0.4 policies остаются. Public deltas не доказывают private stats/cooldowns/queue rules: `EVT-009` остаётся blocking.

WP10 filtered408 tests =246U/108C/54I; полный Release/Debug1315 =783 Core/437 Conformance/95 Integration.0 failures/skips; locked restore/build0 warnings/errors. Performance project пуст, сообщение no tests не скрывает skipped acceptance. WP04 generated Release, WP10 fresh-process generated Release/Debug, WP09 actual-target five0.4 scenarios/historical Release/Debug green. Source/schema/generated/manifest/fixtures/hashes unchanged. Это historical target evidence, не WP10 DET-004/005.

Fresh coverage `WP10ReplayCore`, `WP10ReplayIntegration`, `WP10ReplayFinalConformance`: legacy WP02/03/06/07/08/09 selected critical gates100%, combined Core line92.45%; новый effect replay witness line98%/branch91.44%. New WP10 full critical threshold ещё не достигнут; matrix/thresholds неизменны.

Остались24 IDs:

- `BASE-001/003..006`, `DATA-008`, `EVT-009`, `SAFE-008`;
- `DET-001..008`, `REG-003..006`, `GOLD-001..004`.

Следующий срез: config-aware producer conformance, current+four effect goldens/public Engine0.5 CLI, version/contract/architecture/inventory/failure gates и full WP10 coverage/process/TFM/profile/culture/mirror/OS/remote CI. Internal producer0.5 + public verifier0.5 проверены; public Engine/CLI ещё0.4. WP10 не COMPLETED; Unity/посторонние changes сохранены, commit/push не выполнялись.

## 10. Previous execution evidence — local132/132 passed, 2026-10-04

Все строки §6 исполнены локально. Inventory проверяет actual built Unit/Conformance/Integration/Performance assemblies, тестовые attributes и ровно132 документированные IDs; дубликатов, неизвестных IDs, missing IDs и skips нет.

| Family | Local blocking cases passed |
|---|---|
| BASE / DATA / MOD / EXP | 6/6 · 11/11 · 14/14 · 9/9 |
| STACK / TRG / CTRL / KDN | 13/13 · 14/14 · 12/12 · 8/8 |
| INT / EVT / SAFE | 8/8 · 9/9 · 9/9 |
| DET / REG / GOLD | 8/8 · 7/7 · 4/4 |

Evidence: locked restore, Release/Debug builds0 warnings/errors; full suites1443 (864 Core/469 Conformance/110 Integration),0 failures/skips. WP10 total536 (327U/140C/69I). Empty Performance project не представляет skipped acceptance. Новые test classes: `Wp10GoldenAndDeterminismTests`, `Wp10ReleaseSafetyAndTargetTests`, `Wp10ReleaseConformanceTests`, `Wp10BlockingCaseInventoryTests`, `Wp10GoldenManifestTests`, `Wp10EffectReplayGuardTests` и Core guard classes. DET008 также закреплён на existing exact eligible defense RNG test.

Nine replay/config pairs и SHA/input/final/event-count pins находятся в `CombatLab/fixtures/replay/v0.1/wp10.engine-0.5.0.manifest.json`; metadata UTC фиксирована2026-10-04T00:00:00+00:00. Production compiler/engine/verifier roundtrip и exact canonical sidecar bytes подтверждены. Historical hashes/source/generated0.1 неизменны. EVT009 доказывает DamageTaken Self per-owner cap1 и damage66/66→50/50 независимой arithmetic + pinned config-aware re-simulation. Expiry EffectRemoved сохраняет original causal resolution group; actual impact reopen/cross-tick остаётся semantic error.

Effect goldens: stacks1/2/3/3 applications ticks0/1/2/3, before-expiry6 и after-expiry2; impact66/66/50/50; crossing-only fatigue/immunity; grab9/throw10, regular miss11/GroundHit12, knockdown stages12/18/ready21, grab-lockout expiry29 и wakeup-immunity expiry45. Числовые unit oracles §3 сохраняются, fixture ticks не являются их заменой.

DET001:4×100 in-process. DET002:4×10 fresh CLI processes. DET003: Standard/Diagnostic chains и SummaryOnly summary/RNG equal. DET004: actual loaded netstandard2.1/net10.0 four dependencies ×9 artifacts, в обеих конфигурациях. DET005: Debug и Release совпадают с одними pinned bytes; CI сравнивает те же bytes на Windows/Linux. DET006:3 cultures и reverse catalogs/rules. DET007: mirrored symmetric outcome/positions и required grab tie Resolution draw0. SAFE008: cap/depth/watchdog single Begin/Complete, bounded capture, reserved invalid terminal без Draw fallback.

`verify-wp10-coverage.ps1`:100% branch для целых selected arithmetic/stat/store/queue/control/runtime/atomic classes и всех их generated closures/iterators, а также effect replay policy/witnesses; Core line92.74%. DATA reader/export adapter scaffolding не добавлены в этот arithmetic/state critical scope, но не исключены из coverage collection. Latest reports: `WP10ReleaseTerminalCore`, `WP10ReleaseIntegration`, `WP10ReleaseFilteredReplay`; полный Replay regression report `WP10ReleaseGuardReplay`. WP02/03/06/07/08/09 critical gates остаются100%. Generated WP04 Release/WP10 Release+Debug и actual-target/historical gates green.

CI содержит WP10 filtered inventory, nine target goldens и Release critical coverage. Единственный оставшийся completion gate — remote Windows/Linux × Debug/Release jobs. До этого статус CI PENDING, не COMPLETED; local Windows/TFM runs не считаются Linux proof. UnityClient unchanged; git commit/push не выполнялись.

## 11. Previous execution evidence — resumed final run, 2026-10-05

Матрица §6 и решения OPEN-WP10-01..28 не изменены. После возобновления заново прошли locked restore, Release/Debug build0 warnings/errors, полный1443-test suite в каждой конфигурации (864U/469C/110I),0 failures/skips. Отдельный Release `WorkPackage=WP10` выполнил536 tests (327U/140C/69I); actual built-assembly inventory132/132 green. Integration suites повторно исполнили DET/GOLD/process/actual-target проверки; historical regression tests также green.

Generated gates: WP04 Release, WP10 native Release/Debug — green. Все coverage gates WP02/03/06/07/08/09/10 повторно прошли на final reports из §10: critical branches100%, combined Core line92.74%; это повторная проверка сохранённого coverage evidence, не новая collection. Production-код между collection и этим запуском не менялся. CLI smoke `effects-knockdown` дал byte-identical replay/config и FighterAWin на tick47, без перезаписи существующих outputs/fixtures.

Таким образом все132 IDs имеют локальное execution evidence, а не только source attributes. Remote OS gate по-прежнему pending: для COMPLETED нужны четыре green Windows/Linux × Debug/Release CI jobs. UnityClient не изменялся; commit/push не выполнялись.

## 12. Previous execution evidence — CI schema portability repair, 2026-10-05

Windows generated schema mismatch и Linux WP10-REG-002/DATA-009 SHA failures объяснены OS-dependent CRLF exporter/local pins versus существующие LF Git blobs. `BalanceSchemaJson` теперь явно LF; обе balance versions имеют LF checkout policy. Pins указывают на persisted LF bytes, а не host-specific checkout. Schema contents/Git blobs, workbook/config/replay hashes и132-row blocking matrix unchanged; никакой нормализации внутри hash assertions или ослабления checks не добавлено.

Добавлены2 дополнительные regression executions `BalanceSchemasAreCanonicalLfArtifactsOnEveryPlatform` для v0.1/v0.2: LF, no CR/BOM/EOF newline, exact schema file equality и JSON parse. После fix locked restore и Release/Debug build0 warnings/errors; полный suite по1445 passed (864U/471C/110I),0 failed/skipped. Affected historical/data classes7 passed в каждой конфигурации; Release WP10 Conformance142 passed, inventory132/132. Все WP10 tests в full suite:538 (327U/142C/69I).

Generated WP04/WP10 в Release/Debug green; Git checkout filters при `core.autocrlf=true/false` сохраняют оба pinned schema SHA. Full suites также повторно исполнили process/actual-target/profile/culture/historical/golden gates. Saved coverage для unchanged Core/Replay снова green: critical100%, combined Core line92.74%; новая collection не требовалась для schema writer fix вне этих scopes. Remote confirmation для нового fix commit pending, поэтому статус не COMPLETED. UnityClient unchanged; commit/push не выполнялись.

## 13. Previous execution evidence — target gate without Get-FileHash, 2026-10-05

Windows Debug remote run после740fd91 прошёл schema/conformance, затем DET004 упал на отсутствующем Get-FileHash в child Windows PowerShell. Gate заменяет оба cmdlet calls на .NET stream/SHA256, без изменения алгоритма, manifest pins или требований к actual loaded TFMs. DET004 теперь2-row theory: normal host и throwing global Get-FileHash guard. Каждый row исполняет полный9×2 target gate; success marker и exit0 обязательны. Skip/platform-conditional pass не добавлены; acceptance ID остаётся одним, inventory132/132 unchanged.

Фактические проверки: locked restore; Release/Debug build0 warnings/errors; по1446 full-suite passed (864U/471C/111I),0 failed/skipped. Targeted DET0042/2 green в каждой конфигурации, оба rows green и в full suite. WP10 total539 (327U/142C/70I); process/profile/culture/golden/historical/inventory gates в полных suites green. WP04/WP10 generated Release/Debug green. Saved coverage unchanged critical Core/Replay scopes100%, combined line92.74%; новая collection не выполнялась. Fixtures/hashes/DATA unchanged, UnityClient unchanged, commit/push не выполнялись. COMPLETED только после нового four-job remote green run.

## 14. Completion evidence — 2026-10-05

Все132 acceptance IDs §6 исполнены, discovery inventory clean без missing/unknown/duplicate IDs и skips. Локальное evidence сохранено в §13: Release/Debug по1446 passed, WP10539 executions, critical coverage100%, Core line92.74%, required generated/historical/golden/determinism gates green. Матрица/пороги не менялись.

Remote gate закрыт по сообщению владельца: `ubuntu-latest / Debug`, `ubuntu-latest / Release`, `windows-latest / Debug`, `windows-latest / Release` — все green после последнего fix. Последняя локальная code revision620ebed. Run URL/ID не предоставлены; agent не выполнял отдельную GitHub проверку в этой status-update сессии. WP10=COMPLETED / matrix EXECUTED-PASSED. Previous checkpoints — история прежних состояний; незавершённых WP10 blocking условий нет. Этот patch только документационный, без нового build/test run, изменения code/DATA/fixtures или UnityClient; inventory после правок проверяется отдельно.
