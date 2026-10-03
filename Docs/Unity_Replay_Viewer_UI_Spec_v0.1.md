# Unity Replay Viewer Game-First UI Spec v0.1 — WP-UI-01

**Status:** `APPROVED / BLOCKING`  
**Owner approval:** `2026-09-08`  
**Applies to:** `UnityClient/AnimalCombat`, scene `Assets/ReplayViewer/Scenes/ReplayViewer.unity`  
**Visual direction:** arcade fighting game + sports broadcast overlay  
**Runtime contract:** read-only presentation of `combat.replay/0.1`

## 1. Normative role

This document is the visual and interaction contract for the WP-UI-01 Viewer. It refines the UX section of [WP-UI-01 Brief](./WP-UI-01_Brief.md) without changing replay semantics, event ordering, fixture bytes, or the authority boundary.

The current dashboard-style vertical slice is implementation evidence for loading and playback only. It is not the accepted final visual target. The approved target is the game-first layout in this document and the two reference mockups:

- [Desktop 16:9 reference](./Assets/WP-UI-01/replay-viewer-desktop-16x9.svg);
- [Mobile landscape reference](./Assets/WP-UI-01/replay-viewer-mobile-landscape.svg).

If prose, a mockup, and implementation differ, the priority is:

1. replay authority and safety rules from WP-UI-01 Brief/Test Plan;
2. measurable acceptance conditions in this document;
3. component/layout rules in this document;
4. reference mockup pixels.

## 2. Product intent

The default screen must read as a fight being watched, not as an administrative replay inspector.

Within one glance, a viewer must be able to answer:

- who is fighting;
- where each fighter is and which way they face;
- how much HP each fighter has;
- what combat event is happening now;
- whether playback is paused, playing, or completed;
- who won or whether the result is a draw.

Technical replay information remains available, but it is subordinate to the arena. Raw payload details and the complete event stream belong in an opt-in `Debug / Telemetry` drawer.

## 3. Non-negotiable authority boundary

- Unity never simulates combat, chooses an action, tests hit/miss, or calculates damage, movement, state, or outcome.
- Fighter state changes only by copying recorded `initial_frames`, event `after` frames, and `final_frames`.
- Visual effects may interpolate between recorded values, but must finish at the recorded value and must not feed data back into the session.
- Damage numbers use recorded event payload values; HP bars use recorded fighter frames.
- Facing uses the recorded current fighter frame, never an inferred attack direction.
- Unknown events produce a bounded generic cue and telemetry row; playback continues.
- Opening, closing, or scrolling telemetry cannot modify playback state or replay data.
- Historical fixtures and CombatLab production code remain unchanged.

## 4. Visual direction

### 4.1 Tone

The target is a readable arcade fight presented like a compact sports broadcast:

- high-contrast fighter colors;
- large arena and silhouettes;
- short, emphatic combat callouts;
- restrained technical chrome;
- fast but deterministic presentation effects;
- no sci-fi admin dashboard grids as the dominant visual language.

The v0.1 implementation may use original vector/USS placeholder silhouettes instead of production character art. A placeholder must still have a recognizable body mass, facing, grounded position, fighter color, and state response. Lettered circles alone are not acceptable as the final game-first fighter presentation.

### 4.2 Color tokens

| Token | Reference value | Use |
|---|---|---|
| `arena-sky` | `#102B34` | upper arena atmosphere |
| `arena-floor` | `#172128` | playable ground |
| `ink` | `#071018` | deep background and overlays |
| `surface` | `#101C26E8` | translucent broadcast surfaces |
| `fighter-a` | `#29D7E5` | Fighter A HUD, outline, cues |
| `fighter-b` | `#FF8B3D` | Fighter B HUD, outline, cues |
| `health-high` | `#66E38F` | healthy HP fill |
| `danger` | `#FF4D5E` | low HP, damage, defeat |
| `warning` | `#FFD166` | prepared/wall/unknown warning |
| `text-primary` | `#F4F7FA` | primary readable text |
| `text-muted` | `#A9B7C3` | secondary metadata |

Values may be tuned for contrast during implementation, but Fighter A cyan and Fighter B orange remain stable identifiers.

### 4.3 Typography and motion

- Fighter names, result, and combat callouts use a bold display treatment.
- Metadata uses a compact sans-serif treatment and never competes with HP or the current cue.
- At `1x`, event presentation remains based on the approved `400 ms/event` logical slice.
- Flash, shake, scale, and floating-text effects are presentation-only and bounded to the active slice.
- Pause freezes active presentation effects; Restart clears them.
- Reduced-motion fallback removes shake and large transforms while preserving text/color cues.

## 5. Screen architecture

The screen has four visual layers, back to front:

1. **Arena layer** — environment, floor, walls, fighter presenters, grab link, impact effects.
2. **Broadcast HUD** — fighter names/portraits, HP bars, state badges, tick/sequence.
3. **Playback layer** — replay selector, Play/Pause/Restart, speed, Debug button.
4. **Modal/overlay layer** — loading/error banner, telemetry drawer, battle result.

### 5.1 Desktop composition

At `1920×1080` and `1280×720`:

- arena occupies at least `60%` of the safe viewport height;
- fighter HUD runs along the top and does not use separate dashboard cards;
- Fighter A anchors left and Fighter B anchors right;
- tick/sequence and current short cue occupy the center broadcast zone;
- playback controls form one compact bottom strip;
- telemetry is closed by default and opens as an overlay/drawer, not as a permanently reserved column;
- the arena remains visible while telemetry is open.

Reference: [replay-viewer-desktop-16x9.svg](./Assets/WP-UI-01/replay-viewer-desktop-16x9.svg).

### 5.2 Mobile landscape composition

The mobile mockup is an approved forward-compatible design reference for a subsequent Android delivery stage. Android/iOS builds remain non-blocking for WP-UI-01.

- layout is landscape-first;
- HUD and controls stay inside platform safe-area insets;
- interactive targets are at least `48×48 dp` equivalent;
- the long replay filename moves behind a compact replay/menu control;
- Play/Pause is the dominant centered touch control;
- telemetry opens full-height from an edge and is closed by default;
- arena and fighters remain visible at common wide phone aspect ratios.

Reference: [replay-viewer-mobile-landscape.svg](./Assets/WP-UI-01/replay-viewer-mobile-landscape.svg).

## 6. Component contract

### 6.1 Fighter HUD

Each fighter HUD contains:

- fighter display name and stable A/B identity;
- portrait or original placeholder emblem;
- HP bar and exact numeric `health/max_health` label;
- compact recorded state label;
- optional transient status/resource indicator only when replay data supplies it.

HP depletion animates toward the newly recorded value but never calculates a delta independently. When a label and bar disagree, the implementation fails acceptance.

### 6.2 Arena and fighter presenters

- source arena range maps linearly to the presentation lane as already specified by WP-UI-01;
- fighters are grounded on the same readable baseline;
- shape, outline, nameplate, and facing make A/B distinguishable without relying on position alone;
- facing is visible from silhouette orientation plus a subtle ground arrow/nameplate;
- arena walls are visually explicit so `WallImpact` has a readable location;
- off-lane decorative motion cannot be mistaken for authoritative movement.

### 6.3 Current-event cue

The default screen shows a short human-readable cue such as `PREPARE`, `HIT`, `MISS`, `GRAB`, `THROW`, `WALL IMPACT`, `KO`, or `DRAW`. Schema type, actor/target IDs, raw fields, and warnings belong in telemetry unless essential to the short cue.

Only one primary cue is active at a time. Secondary damage/resource text may coexist but must clear or settle within a bounded number of slices.

### 6.4 Playback controls

- `Play`, `Pause`, `Restart`, and speed remain accessible throughout Ready/Paused/Playing/Completed as allowed by the session state contract;
- the control bar does not cover either fighter or a wall-impact area;
- speed shows an exact numeric multiplier;
- replay selection pauses and atomically loads a candidate;
- loading failure displays a bounded error without replacing the last valid replay state.

### 6.5 Debug / Telemetry drawer

Closed by default. When open it contains:

- replay filename/ID, schema and engine version;
- integrity label and warnings;
- current tick and sequence;
- ordered event stream;
- current event details and bounded raw payload preview;
- external read-only path loading controls when implemented.

The drawer is an inspection surface only. Selecting or scrolling an event row cannot seek or mutate state in v0.1.

### 6.6 Battle result overlay

At `BattleEnded`, a centered game-style overlay displays:

- `FIGHTER A WINS`, `FIGHTER B WINS`, or `DRAW`;
- recorded winner when present;
- recorded reason;
- `Restart` action;
- optional telemetry action.

The overlay must not derive a winner from HP.

## 7. Playback UI states

| State | Arena | HUD | Controls | Overlay |
|---|---|---|---|---|
| `Loading` | dimmed last valid arena or neutral stage | retained/neutral | disabled except safe navigation | bounded loading indicator |
| `Ready/Paused` | exact current recorded projection | visible | Play enabled | pause indicator, no modal |
| `Playing` | exact projection plus bounded tween/cue | visible | Pause enabled | no modal |
| `Completed` | exact final frames | visible | Restart enabled; Play no-op | result overlay |
| `LoadError` | last valid projection retained | retained | responsive | bounded error banner |
| `UnknownEvent` | current parseable `after` projection | visible | playback continues | generic warning cue; details in telemetry |

## 8. Event-to-presentation mapping

The table defines presentation only. Authoritative state always follows the replay projection policy.

| Replay event | Primary game cue | Arena/HUD response |
|---|---|---|
| `BattleStarted` | `FIGHT` staging cue | place both fighters from recorded initial frames; initialize HUD |
| `DecisionMade` | brief intent badge near actor | show recorded action/reason in compact form; no movement |
| `ActionCommitted` | action lock-in pulse | emphasize actor and recorded direction as a cue only |
| `AttackPrepared` | `PREPARE` + telegraph | actor wind-up silhouette/ground arc for recorded impact timing |
| `AttackHit` | `HIT` impact burst | actor-to-target impact line/flash using recorded direction/gap |
| `AttackMissed` | `MISS` ghost trail | miss text and non-contact sweep; frames remain replay-authoritative |
| `DamageApplied` | recorded damage number | HP animates to recorded `after.target.health` |
| `StateChanged` | compact state ribbon | HUD/silhouette state treatment follows recorded frame |
| `FighterDefeated` | `KO` | defeated silhouette treatment and badge |
| `PositionChanged` | movement trail | tween exactly recorded `from_position→to_position` |
| `KnockbackApplied` | forced-move streak | tween to recorded position; display blocked/actual detail when relevant |
| `GrabStarted` | `GRAB` | persistent visible actor→target grab link keyed by recorded grab ID |
| `GrabEnded` | `THROW` or release cue | remove matching grab link; show recorded reason/action |
| `WallImpact` | `WALL IMPACT` | flash the recorded wall and show recorded wall damage/stagger |
| `ResourceChanged` | small resource pulse | show recorded before/after value without deriving it |
| `DrawDeclared` | `DOUBLE KO`/`DRAW` staging | emphasize both fighters; await authoritative `BattleEnded` |
| `BattleEnded` | result overlay | copy recorded final frames/outcome and complete playback |
| unknown type | `UNKNOWN EVENT` | bounded neutral cue, warning badge, optional `after` copy, continue |

## 9. Responsive and input rules

### 9.1 Desktop WP-UI-01 blocking target

- reference aspect: `16:9`;
- minimum blocking resolution: `1280×720`;
- mouse operation is supported;
- keyboard shortcuts are optional for v0.1;
- resizing cannot hide Play/Pause/Restart, HP, tick/sequence, or result.

### 9.2 Mobile forward target

- landscape reference: `20:9` and safe-area insets;
- touch-first controls;
- no hover-only information;
- no dependency on direct desktop filesystem paths;
- Android `StreamingAssets` and platform document-picker behavior require a separate implementation/build gate;
- iOS delivery requires its own build/signing environment and is not implied by this spec.

## 10. Blocking game-first acceptance — 14 cases

These IDs extend the WP-UI-01 acceptance matrix. They do not change existing IDs.

| ID | Level | Exact pass condition |
|---|---|---|
| `WPUI01-UX-001` | Manual | At first load on `1920×1080`, the arena plus fighters occupies at least `60%` of safe viewport height and is the visually dominant region. |
| `WPUI01-UX-002` | Manual | Fighter A/B are identifiable without opening telemetry by name, stable cyan/orange treatment, silhouette/emblem, position, and visible facing. Lettered circles alone fail. |
| `WPUI01-UX-003` | PlayMode | Both exact numeric HP values and both HP fills remain visible for every applied sequence of all three required fixtures. |
| `WPUI01-UX-004` | PlayMode | The default loaded screen has no permanent event-stream column or raw payload panel; `Debug / Telemetry` is closed. |
| `WPUI01-UX-005` | PlayMode | Opening, scrolling, and closing telemetry does not change playback state, cursor, fighter projection, selected speed, or replay bytes. |
| `WPUI01-UX-006` | Manual | `AttackPrepared`, `AttackHit`, and `AttackMissed` have three visually distinguishable arena cues; distinction does not depend on reading a schema event type. |
| `WPUI01-UX-007` | Manual | `DamageApplied` shows a bounded recorded damage number and HP settles at the exact recorded target frame without a second/invented decrement. |
| `WPUI01-UX-008` | Manual | Forced movement, grab, throw, and wall impact each have a distinct visible arena treatment; active grab persists until its matching recorded end. |
| `WPUI01-UX-009` | Manual | `StateChanged` and `FighterDefeated` are readable from the fighter/HUD treatment; defeat produces a distinct `KO` cue. |
| `WPUI01-UX-010` | PlayMode | `BattleEnded` opens a game-style result overlay using recorded outcome/winner/reason; Restart closes it and returns to exact initial projection. |
| `WPUI01-UX-011` | PlayMode | An unknown event produces one bounded generic arena cue and warning, keeps raw detail accessible in telemetry, and does not prevent the next known event. |
| `WPUI01-UX-012` | Manual | At `1280×720`, both fighters, both HP bars, tick/sequence, current primary cue, Play/Pause/Restart/speed, and result remain readable without overlap. |
| `WPUI01-UX-013` | Manual | Default game view contains no JSON field dump, long engine/schema line, or always-visible administrative card grid; those details are available through telemetry. |
| `WPUI01-UX-014` | Static | Presentation code for flashes, tweens, callouts, and overlays has no API/reference that can calculate damage, hit, movement, state, or winner. |

The WP-UI-01 blocking inventory becomes `96` unique cases: the existing `82` plus `14` game-first UX cases.

## 11. Non-blocking mobile design checks

These are design-readiness checks for the later mobile stage and are not Android/iOS delivery gates for WP-UI-01.

| ID | Pass condition |
|---|---|
| `WPUI01-MOBILE-DESIGN-001` | A versioned landscape mockup exists and marks safe-area boundaries. |
| `WPUI01-MOBILE-DESIGN-002` | Primary touch targets are specified at `48×48 dp` equivalent or larger. |
| `WPUI01-MOBILE-DESIGN-003` | No required gameplay/replay information is hover-only. |
| `WPUI01-MOBILE-DESIGN-004` | Mobile file selection is explicitly deferred to platform document-picker work; no desktop-path UX is claimed portable. |

## 12. Implementation order after approval

1. Convert the current always-visible dashboard into layered arena/HUD/playback/overlay roots.
2. Implement original placeholder fighter silhouettes and explicit walls without adding production art dependencies.
3. Move event stream, raw details, integrity, schema/engine, and external path controls into the closed telemetry drawer.
4. Implement primary event cues and bounded presentation effects from §8.
5. Implement the result overlay and Restart transition.
6. Add PlayMode coverage for telemetry isolation, default visibility, result flow, and unknown-event continuation.
7. Verify `1920×1080` and blocking `1280×720`; capture evidence screenshots.
8. Run the full WP-UI-01 acceptance, Windows Standalone smoke, fixture immutability, and CombatLab regression gates.
9. Treat the mobile landscape reference as input to a later Android implementation stage, not as evidence of a shipped phone build.

## 13. Approval and change control

| Date | Decision | Status |
|---|---|---|
| `2026-09-08` | Replace the dashboard-dominant target with a game-first arcade-fighting/sports-broadcast layout; keep telemetry opt-in; approve desktop and mobile-landscape reference mockups. | `APPROVED / BLOCKING` |

Any material change to the four-layer hierarchy, default telemetry visibility, Fighter A/B identity colors, mobile landscape direction, or blocking acceptance conditions requires a new revision of this document and owner review. Minor spacing, easing, contrast, and placeholder-shape refinements may be made during implementation when they preserve all acceptance conditions.

No approval in this document authorizes replay mutation, CombatLab production changes, historical fixture changes, git commit/push, or gameplay calculations in Unity.
