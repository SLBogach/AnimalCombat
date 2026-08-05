# WP-05 Replay

> Статус: завершён. Этот brief сохранён как исторический контракт WP-05 и не переименовывается под следующий этап.
>
> Следующий этап: [WP-06 Engine shell](./WP-06_Brief.md).

## Цель

Реализовать независимый от `Battle.Core` слой replay: типизированный журнал событий, каноническую JSON-сериализацию, SHA-256 цепочку целостности и verifier для нормативного machine package.

> Уточнение дорожной карты: в таблице work packages Combat Lab Technical Design v0.1 этап WP-05 называется **Replay**. Подготовка начального состояния боя относится к **WP-06 Engine shell**.

## Нормативные источники

- Combat Lab Technical Design v0.1: разделы 15.1–15.3, 19, 21.1–21.4, 23.2–23.3 и приложения A, C, E.
- Combat Event & Replay Schema v0.1: разделы 2.2–2.3, 3–8, 13–15, 17, 19, 21 и приложения B–D.
- Machine package в `CombatLab/schemas/replay/v0.1` и `CombatLab/fixtures/replay/v0.1` — нормативные JSON Schema, fixtures и digest vector; они не генерируются из C# типов и не редактируются для подгонки под код.

## Архитектурные границы

- `Battle.Replay` зависит только от `Battle.Contracts` и BCL.
- Payload остаётся типизированным immutable union, выбранным по `event_type`; десериализация gameplay payload в `Dictionary<string, object>` запрещена.
- Replay и verifier не зависят от Unity, времени, физики, файлового порядка или нестабильного обхода коллекций.
- Canonical events авторитетны; summary и keyframes являются производными и проверяются относительно event log.
- `standard` и `diagnostic` одного replay имеют одинаковые input, summary, keyframes, events, `input_digest` и `final_digest`. Различаются только profile, diagnostics и metadata.

## Требования к canonical JSON

- Hash preimage кодируется UTF-8 без BOM и trailing whitespace, с compact separators.
- Object keys сортируются по ascending ASCII; arrays сохраняют нормативный порядок.
- Обязательные `null` поля присутствуют; применимые пустые коллекции сериализуются как `[]` или `{}`.
- Integer записывается минимально, `-0`, float, decimal fraction, scientific notation, `NaN` и `Infinity` запрещены.
- `master_seed`, `rng.index` и `rng.raw_u32` передаются как canonical decimal strings без знака `+` и ведущих нулей.
- Неизвестные canonical поля, schema versions, enums и `event_type` приводят к hard rejection, кроме явно разрешённых namespaced extensions.

## Требования к integrity

- `input_digest` равен SHA-256 канонического объекта ровно с ключами `schema_version`, `replay_id`, `battle_id`, `engine`, `config`, `input`.
- Первый event — `BattleStarted` с `sequence = 0`; его `integrity.prev_digest` равен `input_digest`.
- Для каждого event в порядке `sequence` поле `prev_digest` равно предыдущему digest. В hash projection поле `event_digest` обязательно присутствует и равно `null`; рассчитанный SHA-256 записывается в `event_digest`.
- `sequence` непрерывен от 0, `event_id` имеет вид `evt-{sequence:0000000000}`, а `tick` не убывает.
- Последний event — `BattleEnded`; после него canonical events запрещены.
- `replay.integrity.final_digest` равен digest последнего event, а `summary.event_count`, `integrity.event_count` и длина `events` совпадают.
- Start keyframe имеет `tick = 0` и `after_sequence = 0`; end keyframe указывает на последний `BattleEnded` и содержит final frames.
- Контрольный machine fixture должен дать точные значения:
  - `input_digest`: `sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b`;
  - `final_digest`: `sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e`.

## Требования к журналу

- Journal получает typed event draft, проверяет соответствие payload/event role и обязательных null-полей.
- Core детерминированно назначает следующий `sequence` и `event_id`; journal проверяет их непрерывность/соответствие, связывает `prev_digest`, вычисляет `event_digest` и после append не позволяет изменить event.
- `Complete` сверяет наличие последнего `BattleEnded`, summary, количество событий и `final_digest`; keyframes проверяются verifier-ом на уровне готового replay envelope.
- Подключение diagnostic overlay не меняет canonical event bytes или digest chain.
- `StandardReplay` хранит canonical events, keyframes и summary; `DiagnosticReplay` использует ту же event chain и добавляет overlay вне digest.
- `SummaryOnly` получает те же drafts/RNG, обновляет deterministic counters и summary, но освобождает event bodies и не публикует replay.
- `FailureCapture` хранит bounded ring buffer последних drafts для диагностики invariant/crash и никогда не выдаёт его за валидный replay.
- Невалидный input/config до `BattleStarted` создаёт отдельный `combat.rejection/0.1`; replay при этом не создаётся. Ошибка после старта завершается `BattleEnded` с invalid outcome, а не `BattleRejected`.

## Требования к verifier

Verifier выполняет доступные на этом этапе уровни проверки в фиксированном порядке:

1. JSON Schema: types, required fields, enums, patterns и `additionalProperties`.
2. Semantic: версии и identity, первый/последний event, sequence/event ID/tick, роли и ссылки, causal lineage, согласованность summary/keyframes и изоляция diagnostic profile.
3. Integrity: `input_digest`, вся event chain, `event_count` и `final_digest`.

`source_event_id` и `related_event_ids` ссылаются только на более ранние события; циклы запрещены. Единственная разрешённая forward reference — `FinisherTriggered.payload.predicted_lethal_event_id` в той же resolution group, которая обязана разрешиться в последующий lethal event.

Полная determinism-проверка через повторную симуляцию и signature trust policy не входят в WP-05, потому что engine shell и server trust policy ещё не реализованы.

## Зафиксированное расхождение v0.1

Текст раздела 13.2 требует считать `keyframe.state_digest` по projection остальных полей keyframe, что формально включает `scope`. Нормативный machine fixture воспроизводит digest только для projection из `tick`, `after_sequence`, `fighters`, `active_grab_id`, исключая и `state_digest`, и `scope`. В WP-05 verifier следует machine fixture; `scope` всё равно зафиксирован JSON Schema как `public_playback`. Расхождение нельзя молча переносить в следующую версию: перед release его нужно устранить одновременно в тексте и machine package.

## Автоматические тесты

- Exact `input_digest` и `final_digest` нормативного fixture.
- `standard` и `diagnostic`: идентичные canonical events и final digest.
- Round trip deserialize → canonical serialize → verify с теми же canonical event bytes/digest.
- Tamper: удаление, перестановка и изменение event; неверные `prev_digest` и `final_digest` обязаны отклоняться.
- Negative cases: неизвестный `event_type`, duplicate/missing sequence, убывающий tick, неверный `event_id`, causal forward/cycle, отсутствующий required null, float вместо integer и diagnostics в `standard`.
- Verifier возвращает структурированный deterministic report; replay validation соответствует процессному коду `ReplayInvalid = 40` на границе будущего CLI.

## Критерии готовности

- В `Battle.Replay` реализованы typed journal, canonical JSON, integrity chain и verifier без обратных зависимостей.
- Нормативные schema/fixtures остаются byte-for-byte неизменными.
- Machine digest vector воспроизводится точно, а tamper cases дают hard rejection.
- `dotnet build CombatLab.sln --configuration Release` и `dotnet test CombatLab.sln --configuration Release` проходят.
- `UnityClient` не изменён.

## Не входит в этап

- Battle Setup, инициализация runtime state, tick coordinator и полноценная симуляция — WP-06+.
- Изменение `UnityClient`, presentation timeline player или Unity bindings.
- Re-simulation determinism, server signature/Ed25519 trust, storage/transport API и production deployment.
- Генерация или ручное исправление нормативных JSON Schema и fixtures из C# моделей.
