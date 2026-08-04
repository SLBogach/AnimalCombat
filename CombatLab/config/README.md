# Combat Lab balance configuration v0.1

WP-04 fixes the balance JSON envelope that was intentionally left open by the
technical design. The workbook `JSON Map` remains the source of leaf names,
types, required flags and source traceability.

The v0.1 runtime document has exactly seven root members:

- `actions`, `effects`, `fighters`, `gear`, `passives`, and `tactics` are arrays
  sorted by their canonical Stable ID;
- `settings` is an object whose keys are the `global` namespace property paths
  from `JSON Map` (for example `global.sim.fp_scale`).

Object keys use ordinal ASCII order. Gameplay numbers are integer JSON tokens;
`fp` values are scaled integers. The canonical document is compact UTF-8 without
a BOM. Empty optional workbook values are omitted. The SHA-256 of those exact
bytes is `config_hash`; the hash is not stored inside the runtime JSON.

Runtime code reads only the generated JSON and manifest. The XLSX reader is a
build-time adapter in `CombatLab.Runner`; it reads source cells directly and
never rewrites the source workbook. Cached `JSON Map` formula values are not
trusted. Formula targets are audited and source validation policies are
recalculated from source cells; stale formula caches are rejected. Schema and
semantic checks are repeated by `Battle.Config`.

The v0.1 schema exposes only one content edge: `passive.effect_id` points from a
passive to an effect. Effects have no outbound content edge, so a trigger cycle
cannot be represented by a schema-valid document. Effect conflict consistency is
defined by `stack_group` plus its single `stack_policy` and is checked before a
snapshot is compiled.

`source_workbook_sha256` is the SHA-256 of the raw XLSX bytes and is audit-only.
The loader hashes exact canonical config bytes and rejects noncanonical runtime
JSON. CI compares generated artifacts while ignoring only `generated_utc`.

Commands:

```text
dotnet run --project src/CombatLab.Cli -- export-config
dotnet run --project src/CombatLab.Cli -- validate-config
```

The JSON Schema is emitted to
`schemas/balance/v0.1/combat.balance.schema.json`; runtime artifacts are emitted
to `config/generated/`.
