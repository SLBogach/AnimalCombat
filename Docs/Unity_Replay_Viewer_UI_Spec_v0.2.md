# Unity Replay Viewer — Illustrated Fight Screen v0.2

**Status:** APPROVED / implementation in progress  
**Owner approval:** 2026-10-01  
**Scope:** visual redesign of the read-only Unity Replay Viewer  
**Unity scene:** `UnityClient/AnimalCombat/Assets/ReplayViewer/Scenes/ReplayViewer.unity`  
**Previous contract:** [Game-First UI Spec v0.1](./Unity_Replay_Viewer_UI_Spec_v0.1.md) is retained as decision history. This revision supersedes its visual treatment; replay authority and safety rules are unchanged.

## 1. Intent and references

The Viewer should look first like a lively, illustrated animal-fighting game, not a dark broadcast dashboard. This revision follows the owner's cartoon-fighter references and the [approved animal art direction](./Animal_Fighters_Art_Direction_v0.1.md): funny, slightly unnatural anatomy; an attractive layered arena; bare starting fighters; weapons and armor reserved for later, separate equipment art.

Review the rendered proposals:

- [Desktop 16:9 preview](./Assets/WP-UI-01/replay-viewer-game-v0.2-desktop-preview.png) and [editable SVG composition](./Assets/WP-UI-01/replay-viewer-game-v0.2-desktop.svg).
- [Phone landscape 20:9 preview](./Assets/WP-UI-01/replay-viewer-game-v0.2-mobile-landscape-preview.png) and [editable SVG composition](./Assets/WP-UI-01/replay-viewer-game-v0.2-mobile-landscape.svg).
- [Illustrated arena concept](./Assets/WP-UI-01/arena-savanna-v0.2.png).

The owner approved the arena first and then accepted the complete desktop/mobile-landscape composition on 2026-10-01.

The mockups show an illustrative `AttackPrepared` moment (`TICK 0 · SEQ 5 / 10`), not a hard-coded initial screen. The composited whole-character PNGs are layout references. Runtime animation should retain the existing separate-part bear and kangaroo rigs; the mockups do not authorize replacing those rigs with static sprites.

## 2. Proposed visual changes

| Surface | Current v0.1 | Proposed v0.2 |
|---|---|---|
| Arena | Dark geometric sky, ridges, and floor | Full-bleed warm, layered cartoon savanna with a clear level fight lane; important fight space stays uncluttered |
| Fighter HUD | Navy broadcast cards, green HP fills | Two large cream-framed red HP bars, exact HP text, bold names, central comic `VS` |
| Fighter identity | Cyan A / orange B are dominant HUD colors | Same cyan/orange identity survives as small persistent name accents; red bars are common game-health language |
| Characters | Existing unequipped cutout rigs | Keep the rigs and attack motion; no weapon or armor elements in the default scene |
| Event cue | Technical/sports-broadcast label | Short comic fight callout in the arena, with bounded physical-feeling effects |
| Controls | Dark dashboard transport strip | Compact warm-brown/ink control strip visually subordinate to the fight |
| Technical detail | Optional telemetry drawer | Still optional and closed by default; no raw schema, event list, or replay filename over the arena |

The arena concept is an art-direction asset, not yet a production-ready parallax set. It should be separated into background/ground/foreground layers and checked with the actual cutout sprites before shipping. Do not bake HP bars, fighters, effects, or collision markers into the arena texture.

## 3. Layout contract

### Desktop

- Reference canvas: `1600×900` (16:9); blocking minimum remains `1280×720`.
- The illustrated stage and two fighters occupy at least 60% of safe viewport height. The HUD is at the top and the control strip at the bottom; neither covers a fighter, the ground contact point, or a wall-impact location.
- Left bar belongs to Fighter A, right bar to Fighter B. Both show exact recorded `health / max_health` text and a fill fraction copied from the recorded fighter frame. Red is the bar's visual material, not a health-state calculation.
- A legible `VS` sits between the bars. Names and the cyan/orange identity accents remain readable against all arena backgrounds. Tick and sequence stay visible but smaller than HP and the event cue.
- Position and facing remain readable near each fighter through a compact ground label and orientation. The number comes from the recorded frame; no presentation estimate is shown as game state.
- The selected replay, Restart, Play/Pause, numeric speed, and Details/Telemetry remain accessible in the bottom strip. Only the active Play or Pause action is visually primary.

### Phone landscape

- Design reference: `1600×720` (approximately 20:9). Actual implementation must use platform safe-area insets, not a fixed mockup inset.
- HUD and controls stay within the safe area. No essential text or button depends on hover or sits behind a cutout/rounded corner.
- Play/Pause is the largest central touch action; Restart, compact replay menu, speed menu, and Details each have at least `48×48 dp` equivalent hit targets. The long fixture filename stays inside the replay menu.
- The background may crop to fill. Fighters, HP bars, `VS`, tick/sequence, cue, and controls must not be cropped. If the usable area is too narrow, scale or simplify decorative art before shrinking required text.
- This is design readiness only. Android/iOS runtime loading, input, packaging, and build verification remain separate delivery work.

## 4. Replay and presentation contract

The authority boundary from [WP-UI-01 Brief](./WP-UI-01_Brief.md) and the [test plan](./Unity_Replay_Viewer_Test_Plan_v0.1.md) is unchanged. Unity reads `combat.replay/0.1`; Battle.Core remains the sole source of combat logic. No UI or animation code may choose actions, test hit/miss, calculate damage or movement, or infer a winner.

- Fighter positions, facing, HP, states, tick, sequence, and result are projected only from recorded `initial_frames`, event `after` frames, and `final_frames`.
- `AttackPrepared` drives a wind-up and short `PREPARE` cue. `AttackHit` and `AttackMissed` have visibly different contact/miss treatments. `DamageApplied` uses its recorded amount; HP settles exactly on the recorded target frame.
- Forced movement, grab, throw, wall impact, state change, defeat, and battle end retain distinct v0.1 presentation responses. Arena art must not obscure those responses.
- Pause freezes presentation motion; speed scales its timeline; Restart clears transient effects and restores recorded initial state. Reduced-motion mode preserves cues and values without shake.
- An unknown event gets a bounded generic cue and telemetry entry; it cannot crash or halt playback.
- The result overlay uses the recorded outcome/winner/reason, not HP inference. Telemetry remains read-only and closed by default.

## 5. Migration boundary

This draft changes visual composition, not the replay contract. The later implementation should preserve the existing controller-facing UXML names where possible:

| Existing bindings | v0.2 presentation |
|---|---|
| `fighter-a-marker`, `fighter-b-marker`, `*-silhouette`, rig parts | Place and animate the unequipped fighters over the illustrated stage |
| `fighter-a-health`, `fighter-b-health`, `*-health-fill` | Exact text and replay-driven width inside the new red bars |
| `fighter-a-facing`, `fighter-b-facing`, `*-position` | Compact ground/orientation labels |
| `tick-label`, `sequence-label`, `event-type-label`, `event-value-label` | Small center status and one primary comic cue |
| `play-button`, `pause-button`, `restart-button`, `speed-slider`, `replay-picker` | Reskinned desktop transport; touch-sized mobile equivalents with unchanged actions |
| `telemetry-drawer`, `result-overlay` | Closed-on-load Details drawer and game-style recorded result |

No CombatLab production code or historical replay fixture may be changed for this redesign. No git commit is part of this draft stage.

## 6. Proposed acceptance additions

These are active visual acceptance additions. All replay semantics and safety gates in v0.1 remain in force.

| ID | Verification | Exact pass condition |
|---|---|---|
| `WPUI01-VIS-201` | Manual screenshot | At 1600×900 and 1280×720, the warm illustrated arena and both complete fighters are the dominant image; arena/fighter region is at least 60% of safe height. |
| `WPUI01-VIS-202` | PlayMode + visual | Both red comic HP bars show exact recorded numeric HP and matching fill fraction at every sequence in basic, double-KO, and wall-grab fixtures. |
| `WPUI01-VIS-203` | Manual | `VS`, both names, cyan/orange identity accents, current tick/sequence, primary cue, position, and facing are readable without opening Details. |
| `WPUI01-VIS-204` | Hierarchy + visual | Neither fighter has a weapon or armor element in the default scene; existing separate-part motion still plays and freezes on Pause. |
| `WPUI01-VIS-205` | Manual + PlayMode | Default screen has no permanent telemetry/card grid; Details opens over the stage and closing it does not alter replay state. |
| `WPUI01-VIS-206` | Manual | At basic replay's recorded prepare/hit/damage beats, wind-up, contact cue, and damage/HP change are visually distinct; no HP change occurs before `DamageApplied`. |
| `WPUI01-VIS-207` | Manual | At 1280×720, Play/Pause, Restart, replay choice, speed, Details, both HP bars, fighters, and battle result have no overlap or clipped required text. |
| `WPUI01-VIS-208` | Design review | At 1600×720 phone landscape, required content respects safe-area guides, every primary touch target is at least 48×48 dp equivalent, and no required action depends on hover. |
| `WPUI01-VIS-209` | PlayMode | Three required fixtures, unknown-event fallback, restart, and recorded result still pass after the visual migration. |
| `WPUI01-VIS-210` | Static | New arena/HUD/effect code has no combat-calculation path or new dependency on Battle.Core. |

## 7. Implementation sequence

1. Adapt the arena concept into runtime background layers and verify import, crop, visual contrast, and desktop memory cost. Keep it in documentation only until approved.
2. Rebuild the HUD and arena UXML/USS to match the desktop composition while retaining binding names and separate-part fighter animation. Remove superseded dark broadcast styling; do not alter replay parsing or projection.
3. Reskin event cues, result overlay, and transport. Keep Details closed by default. Add a responsive landscape layout with safe-area and touch-target handling; do not claim phone build support from layout alone.
4. Capture actual Unity screenshots at 1600×900, 1280×720, and 1600×720; compare against these mockups and adjust spacing/art without changing recorded values.
5. Run PlayMode/fixture regression and the existing WP-UI-01 safety gates. Run Windows Standalone smoke separately; schedule Android build/input/loading verification as its own stage.

## 8. Art provenance and approval points

The arena bitmap was created with the built-in image-generation tool as a new background, using this final prompt:

~~~text
Use case: stylized-concept
Asset type: illustrated 2D fighting-game arena background for a desktop and landscape-phone replay viewer mockup; background art only, no UI
Primary request: a polished whimsical outdoor animal-combat arena, inspired by warm hand-painted cartoon brawler environments. Use a dusty golden-sand arena with a readable flat fight lane across the middle and foreground; layered ochre hills and soft blue mountains in the distance; a few angular crooked trees, wooden boundary posts, and small fluttering cloth pennants at the far sides. Slightly absurd and energetic but visually coherent with expressive cartoon animal fighters. Richer graphical quality than a simple Flash-era game screenshot: confident hand-inked outlines, layered shapes, nuanced texture, controlled cel-shaded lighting.
Composition/framing: wide landscape 16:9 game screen, eye-level side-view, level playable ground. Keep central 60 percent of the image unobstructed for two fighters and effects. Preserve low-contrast clear space across top 17 percent for later HP-bar overlay. Depth and detail concentrated in side framing and distant background. Bright warm daylight, balanced contrast so brown furry fighters remain legible.
Color palette: warm sand gold, terracotta and honey browns, dusty sage foliage, muted cool blue distant hills; natural vivid but not neon.
Constraints: no characters, no animals, no weapons, no armor, no interface, no health bars, no VS icon, no lettering, no logos, no watermark; single continuous finished environment, not a collage or panels.
~~~

The new artwork does not replace the approved bare/equipped fighter concepts or the existing cutout atlases. The old v0.1 UI spec and mockups remain preserved as decision history.

**Approval decision:** warm savanna arena, red comic HP bars with small cyan/orange A/B accents, central `VS`, lighter bottom controls, and compact phone-landscape adaptation are the current visual target. Approval does not waive actual Unity screenshot review, fixture regression, or device/build gates.
