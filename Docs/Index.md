# Combat Lab — индекс документации

## Источники истины

1. [Combat Design Specification v0.1](./Combat_Design_Specification_v0.1.docx) — игровая семантика.
2. [Combat Balance Workbook v0.1](../CombatLab/config/source/Combat_Balance_Workbook_v0.1.xlsx) и скомпилированный config — числа и Stable ID.
3. [Combat Event & Replay Schema v0.1](./Combat_Event_Replay_Schema_v0.1.docx) и machine package — wire-контракт, события и integrity.
4. [Combat Lab Technical Design v0.1](./Combat_Lab_Technical_Design_v0.1.docx) — архитектура и этапы WP.
5. [Combat Test Plan v0.1](./Combat_Test_Plan_v0.1.md) — exact pass/fail-матрица WP-06; закрывает `OPEN-05`.
6. [Combat Test Plan WP-07 v0.1](./Combat_Test_Plan_WP-07_v0.1.md) — исполненная локально exact pass/fail-матрица Movement; закрывает `OPEN-WP07-01..13`, внешний Windows/Linux CI pending.

## Рабочие этапы

| Этап | Результат | Статус | Brief |
|---|---|---|---|
| WP-00 | Bootstrap | завершён | — |
| WP-01 | Contracts | завершён | — |
| WP-02 | FixedMath | завершён | — |
| WP-03 | Deterministic RNG | завершён | — |
| WP-04 | Configuration pipeline | завершён | — |
| WP-05 | Replay | завершён | [WP-05 Brief](./WP-05_Brief.md) |
| WP-06 | Engine shell: initialization, tick coordinator, outcome/watchdog | завершён | [WP-06 Brief](./WP-06_Brief.md) |
| WP-07 | Movement | реализован; local acceptance pass, CI pending | [WP-07 Brief](./WP-07_Brief.md) |
| WP-08 | Decisions | запланирован | — |
| WP-09 | Resolution | запланирован | — |
| WP-10 | Effects | запланирован | — |
| WP-11 | Fighters | запланирован | — |
| WP-12 | Batch | запланирован | — |
| WP-13 | Acceptance | запланирован | — |

## Навигация

- [Текущий статус реализации](./Implementation_Status.md)
- [Принятые и закрытые решения](./Decisions.md)
- [Завершённый WP-05 Replay](./WP-05_Brief.md)
- [Combat Test Plan v0.1 — WP-06](./Combat_Test_Plan_v0.1.md)
- [Завершённый WP-06 Engine shell](./WP-06_Brief.md)
- [Реализованный WP-07 Movement — CI pending](./WP-07_Brief.md)
- [Combat Test Plan WP-07 v0.1](./Combat_Test_Plan_WP-07_v0.1.md)

## Правила

- При конфликте действует порядок источников истины выше; уже написанный код не становится нормативным источником.
- Конфликт требования нельзя разрешать молча: решение фиксируется в [Decisions.md](./Decisions.md), а блокирующий конфликт останавливает реализацию этапа.
- `UnityClient` не изменять до отдельного этапа.
- Оригинальные документы читать только по разделам, перечисленным в brief текущего WP.
