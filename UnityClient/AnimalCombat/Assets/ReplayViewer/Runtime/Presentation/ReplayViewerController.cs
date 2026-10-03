using System;
using System.Collections.Generic;
using System.IO;
using AnimalCombat.ReplayViewer.Contracts;
using AnimalCombat.ReplayViewer.Runtime.Loading;
using AnimalCombat.ReplayViewer.Runtime.Playback;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalCombat.ReplayViewer.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class ReplayViewerController : MonoBehaviour
    {
        static readonly List<string> SupportedReplays = new List<string>
        {
            "resolution-basic-l1.engine-0.4.0.json",
            "resolution-double-ko-l1.engine-0.4.0.json",
            "resolution-wall-grab-l1.engine-0.4.0.json"
        };

        static readonly string[] CueClasses =
        {
            "cue-neutral",
            "cue-intent",
            "cue-prepared",
            "cue-hit",
            "cue-miss",
            "cue-damage",
            "cue-state",
            "cue-move",
            "cue-grab",
            "cue-throw",
            "cue-wall",
            "cue-defeat",
            "cue-result",
            "cue-unknown"
        };

        readonly ReplayLoader loader = new ReplayLoader();
        ReplaySession session;
        FighterDefinition fighterA;
        FighterDefinition fighterB;
        string activeGrabId;
        bool telemetryOpen;

        DropdownField replayPicker;
        Button playButton;
        Button pauseButton;
        Button restartButton;
        Button debugButton;
        Button telemetryCloseButton;
        Button resultRestartButton;
        Button resultDebugButton;
        Slider speedSlider;

        Label speedValue;
        Label statusLabel;
        Label tickLabel;
        Label sequenceLabel;
        Label playbackStateLabel;
        Label eventTypeLabel;
        Label eventValueLabel;
        Label eventDetailsLabel;
        Label resultLabel;
        Label resultDetailsLabel;
        Label telemetryReplayLabel;
        Label telemetryEventTypeLabel;
        Label telemetryEventDetailsLabel;
        ScrollView eventScroll;

        VisualElement eventCueLayer;
        VisualElement telemetryDrawer;
        VisualElement telemetryScrim;
        VisualElement resultOverlay;
        VisualElement grabLink;
        VisualElement wallFlashLeft;
        VisualElement wallFlashRight;
        VisualElement markerA;
        VisualElement markerB;
        ReplaySceneStage sceneStage;
        FighterClipPlayer animatorA;
        FighterClipPlayer animatorB;
        Label markerNameA;
        Label markerNameB;
        Label markerFacingA;
        Label markerFacingB;
        Label fighterNameA;
        Label fighterNameB;
        Label fighterStateA;
        Label fighterStateB;
        Label fighterPositionA;
        Label fighterPositionB;
        Label fighterHealthA;
        Label fighterHealthB;
        VisualElement fighterHealthFillA;
        VisualElement fighterHealthFillB;

        void OnEnable()
        {
            sceneStage = FindAnyObjectByType<ReplaySceneStage>();
            if (sceneStage == null || sceneStage.FighterA == null || sceneStage.FighterB == null)
                throw new InvalidOperationException("Replay Viewer scene stage and both fighter prefabs are required.");
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            BindElements(root);

            replayPicker.choices = SupportedReplays;
            replayPicker.formatSelectedValueCallback = FormatReplayChoice;
            replayPicker.formatListItemCallback = FormatReplayChoice;
            replayPicker.value = SupportedReplays[0];
            replayPicker.RegisterValueChangedCallback(OnReplayChanged);
            speedSlider.RegisterValueChangedCallback(OnSpeedChanged);
            telemetryScrim.RegisterCallback<ClickEvent>(OnTelemetryScrimClicked);
            playButton.clicked += OnPlay;
            pauseButton.clicked += OnPause;
            restartButton.clicked += OnRestart;
            debugButton.clicked += ToggleTelemetry;
            telemetryCloseButton.clicked += CloseTelemetry;
            resultRestartButton.clicked += OnRestart;
            resultDebugButton.clicked += OpenTelemetryFromResult;

            SetTelemetryOpen(false);
            ResetPresentationState();
            LoadReplay(replayPicker.value);
        }

        void OnDisable()
        {
            if (replayPicker != null)
                replayPicker.UnregisterValueChangedCallback(OnReplayChanged);
            if (speedSlider != null)
                speedSlider.UnregisterValueChangedCallback(OnSpeedChanged);
            if (telemetryScrim != null)
                telemetryScrim.UnregisterCallback<ClickEvent>(OnTelemetryScrimClicked);
            if (playButton != null)
                playButton.clicked -= OnPlay;
            if (pauseButton != null)
                pauseButton.clicked -= OnPause;
            if (restartButton != null)
                restartButton.clicked -= OnRestart;
            if (debugButton != null)
                debugButton.clicked -= ToggleTelemetry;
            if (telemetryCloseButton != null)
                telemetryCloseButton.clicked -= CloseTelemetry;
            if (resultRestartButton != null)
                resultRestartButton.clicked -= OnRestart;
            if (resultDebugButton != null)
                resultDebugButton.clicked -= OpenTelemetryFromResult;
            DetachSession();
        }

        void Update()
        {
            if (session == null)
                return;

            if (session.IsPlaying)
            {
                float replaySeconds = Time.unscaledDeltaTime * (float)session.Speed;
                animatorA.Update(replaySeconds);
                animatorB.Update(replaySeconds);
            }
            session.Update(Time.unscaledDeltaTime);
        }

        void BindElements(VisualElement root)
        {
            replayPicker = Required<DropdownField>(root, "replay-picker");
            playButton = Required<Button>(root, "play-button");
            pauseButton = Required<Button>(root, "pause-button");
            restartButton = Required<Button>(root, "restart-button");
            debugButton = Required<Button>(root, "debug-button");
            telemetryCloseButton = Required<Button>(root, "telemetry-close-button");
            resultRestartButton = Required<Button>(root, "result-restart-button");
            resultDebugButton = Required<Button>(root, "result-debug-button");
            speedSlider = Required<Slider>(root, "speed-slider");

            speedValue = Required<Label>(root, "speed-value");
            statusLabel = Required<Label>(root, "status-label");
            tickLabel = Required<Label>(root, "tick-label");
            sequenceLabel = Required<Label>(root, "sequence-label");
            playbackStateLabel = Required<Label>(root, "playback-state-label");
            eventTypeLabel = Required<Label>(root, "event-type-label");
            eventValueLabel = Required<Label>(root, "event-value-label");
            eventDetailsLabel = Required<Label>(root, "event-details-label");
            resultLabel = Required<Label>(root, "result-label");
            resultDetailsLabel = Required<Label>(root, "result-details-label");
            telemetryReplayLabel = Required<Label>(root, "telemetry-replay-label");
            telemetryEventTypeLabel = Required<Label>(root, "telemetry-event-type-label");
            telemetryEventDetailsLabel = Required<Label>(root, "telemetry-event-details-label");
            eventScroll = Required<ScrollView>(root, "event-scroll");

            eventCueLayer = Required<VisualElement>(root, "event-cue-layer");
            telemetryDrawer = Required<VisualElement>(root, "telemetry-drawer");
            telemetryScrim = Required<VisualElement>(root, "telemetry-scrim");
            resultOverlay = Required<VisualElement>(root, "result-overlay");
            grabLink = Required<VisualElement>(root, "grab-link");
            wallFlashLeft = Required<VisualElement>(root, "wall-flash-left");
            wallFlashRight = Required<VisualElement>(root, "wall-flash-right");

            markerA = Required<VisualElement>(root, "fighter-a-marker");
            markerB = Required<VisualElement>(root, "fighter-b-marker");
            animatorA = new FighterClipPlayer(sceneStage.FighterA);
            animatorB = new FighterClipPlayer(sceneStage.FighterB);
            markerNameA = Required<Label>(root, "fighter-a-marker-name");
            markerNameB = Required<Label>(root, "fighter-b-marker-name");
            markerFacingA = Required<Label>(root, "fighter-a-facing");
            markerFacingB = Required<Label>(root, "fighter-b-facing");

            fighterNameA = Required<Label>(root, "fighter-a-name");
            fighterNameB = Required<Label>(root, "fighter-b-name");
            fighterStateA = Required<Label>(root, "fighter-a-state");
            fighterStateB = Required<Label>(root, "fighter-b-state");
            fighterPositionA = Required<Label>(root, "fighter-a-position");
            fighterPositionB = Required<Label>(root, "fighter-b-position");
            fighterHealthA = Required<Label>(root, "fighter-a-health");
            fighterHealthB = Required<Label>(root, "fighter-b-health");
            fighterHealthFillA = Required<VisualElement>(root, "fighter-a-health-fill");
            fighterHealthFillB = Required<VisualElement>(root, "fighter-b-health-fill");
        }

        void LoadReplay(string replayName)
        {
            try
            {
                string directory = Path.Combine(Application.streamingAssetsPath, "Replays", "v0.1");
                IReplaySource source = new DirectoryReplaySource(directory);
                ReplayLoadResult loadResult = loader.Load(source.ReadAllText(replayName));
                if (!loadResult.Success)
                {
                    ShowLoadError(loadResult.Error);
                    return;
                }

                DetachSession();
                fighterA = FindFighter(loadResult.Document, "A", 0);
                fighterB = FindFighter(loadResult.Document, "B", 1);
                session = new ReplaySession(loadResult.Document);
                session.SetSpeed(speedSlider.value);
                session.Changed += Render;
                session.EventApplied += OnEventApplied;

                eventScroll.Clear();
                ResetPresentationState();
                AddEventLog(session.CurrentEvent);
                statusLabel.text = $"READY · {loadResult.Document.ReplayId}";
                statusLabel.RemoveFromClassList("status-error");
                telemetryReplayLabel.text =
                    $"{replayName}\n{loadResult.Document.ReplayId}\n{loadResult.Document.SchemaVersion} · {loadResult.Document.EngineVersion}\n{loadResult.Document.Events.Count} events";
                Render();
                PresentEvent(session.CurrentEvent);
            }
            catch (Exception exception)
            {
                ShowLoadError(exception.Message);
            }
        }

        void DetachSession()
        {
            if (session == null)
                return;
            session.Changed -= Render;
            session.EventApplied -= OnEventApplied;
            session = null;
        }

        void OnReplayChanged(ChangeEvent<string> changeEvent) => LoadReplay(changeEvent.newValue);

        void OnSpeedChanged(ChangeEvent<float> changeEvent)
        {
            session?.SetSpeed(changeEvent.newValue);
            speedValue.text = $"{changeEvent.newValue:0.##}×";
        }

        void OnPlay() => Play();
        void OnPause() => Pause();
        void OnRestart() => Restart();

        public void Play() => session?.Play();
        public void Pause() => session?.Pause();

        public void Restart()
        {
            if (session == null)
                return;

            session.Restart();
            eventScroll.Clear();
            ResetPresentationState();
            AddEventLog(session.CurrentEvent);
            Render();
            PresentEvent(session.CurrentEvent);
        }

        void OnEventApplied(ReplayEvent replayEvent)
        {
            AddEventLog(replayEvent);
            PresentEvent(replayEvent);
        }

        void Render()
        {
            if (session == null)
                return;

            ReplayEvent current = session.CurrentEvent;
            EventPresentation presentation = EventPresentationMapper.Map(current);
            tickLabel.text = $"TICK {current.Tick}";
            sequenceLabel.text = $"SEQ {current.Sequence} / {session.Document.Events.Count - 1}";
            playbackStateLabel.text = session.IsAtEnd ? "COMPLETED" : session.IsPlaying ? "PLAYING" : "PAUSED";
            eventTypeLabel.text = presentation.Cue;
            eventDetailsLabel.text = presentation.Detail;
            eventValueLabel.text = presentation.Value ?? string.Empty;
            eventValueLabel.EnableInClassList("event-value-visible", !string.IsNullOrEmpty(presentation.Value));
            eventValueLabel.style.display = string.IsNullOrEmpty(presentation.Value) ? DisplayStyle.None : DisplayStyle.Flex;
            telemetryEventTypeLabel.text = $"{current.Sequence:00} · tick {current.Tick} · {current.EventType}";
            telemetryEventDetailsLabel.text = presentation.Detail;

            playButton.style.display = session.IsPlaying ? DisplayStyle.None : DisplayStyle.Flex;
            pauseButton.style.display = session.IsPlaying ? DisplayStyle.Flex : DisplayStyle.None;
            playButton.SetEnabled(!session.IsAtEnd);
            pauseButton.SetEnabled(session.IsPlaying);
            restartButton.SetEnabled(session.CurrentIndex > 0 || session.IsAtEnd);

            RenderFighter(fighterA, session.State.Fighters, sceneStage.FighterA, markerA, markerNameA, markerFacingA, fighterNameA, fighterStateA, fighterPositionA, fighterHealthA, fighterHealthFillA);
            RenderFighter(fighterB, session.State.Fighters, sceneStage.FighterB, markerB, markerNameB, markerFacingB, fighterNameB, fighterStateB, fighterPositionB, fighterHealthB, fighterHealthFillB);
            animatorA.SetPlayback(session.IsPlaying, session.IsAtEnd, (float)session.Speed);
            animatorB.SetPlayback(session.IsPlaying, session.IsAtEnd, (float)session.Speed);
            UpdateGrabLink();

            bool ended = string.Equals(current.EventType, "BattleEnded", StringComparison.Ordinal);
            resultOverlay.EnableInClassList("result-visible", ended && !IsTelemetryOpen());
            resultOverlay.style.display = ended && !IsTelemetryOpen() ? DisplayStyle.Flex : DisplayStyle.None;
            if (ended)
                RenderResult(current);
        }

        void PresentEvent(ReplayEvent replayEvent)
        {
            EventPresentation presentation = EventPresentationMapper.Map(replayEvent);
            if (presentation.IsUnknown)
            {
                Debug.LogWarning(
                    $"Replay Viewer: unknown event '{replayEvent.EventType}' at sequence {replayEvent.Sequence}; continuing.");
            }

            foreach (string cueClass in CueClasses)
                eventCueLayer.RemoveFromClassList(cueClass);
            eventCueLayer.AddToClassList(presentation.CueClass);

            wallFlashLeft.RemoveFromClassList("wall-flash-active");
            wallFlashRight.RemoveFromClassList("wall-flash-active");
            wallFlashLeft.style.display = DisplayStyle.None;
            wallFlashRight.style.display = DisplayStyle.None;

            switch (replayEvent.EventType)
            {
                case "DecisionMade":
                    AnimatorFor(replayEvent.ActorId)?.Decide();
                    break;
                case "ActionCommitted":
                    AnimatorFor(replayEvent.ActorId)?.Commit();
                    break;
                case "AttackPrepared":
                    AnimatorFor(replayEvent.ActorId)?.Prepare(replayEvent.ActionId);
                    break;
                case "AttackHit":
                    AnimatorFor(replayEvent.ActorId)?.Strike();
                    AnimatorFor(replayEvent.TargetId)?.ReactHit();
                    break;
                case "AttackMissed":
                    AnimatorFor(replayEvent.ActorId)?.Miss();
                    break;
                case "DamageApplied":
                    AnimatorFor(replayEvent.TargetId)?.ReactDamage();
                    break;
                case "PositionChanged":
                    AnimatorFor(replayEvent.ActorId)?.Move();
                    break;
                case "KnockbackApplied":
                    AnimatorFor(replayEvent.TargetId)?.ReactKnockback();
                    break;
                case "GrabStarted":
                    AnimatorFor(replayEvent.ActorId)?.BeginGrab();
                    AnimatorFor(replayEvent.TargetId)?.BeGrabbed();
                    break;
                case "GrabEnded":
                    bool isThrow = string.Equals(
                        replayEvent.Payload["end_reason"]?.ToObject<string>(), "Throw",
                        StringComparison.OrdinalIgnoreCase);
                    if (isThrow)
                        AnimatorFor(replayEvent.ActorId)?.Throw();
                    else
                        AnimatorFor(replayEvent.ActorId)?.EndGrab(false);
                    AnimatorFor(replayEvent.TargetId)?.EndGrab(isThrow);
                    break;
                case "WallImpact":
                    AnimatorFor(replayEvent.TargetId)?.ReactWall();
                    break;
                case "ResourceChanged":
                    if (string.Equals(replayEvent.Payload["resource_kind"]?.ToObject<string>(),
                            "Stagger", StringComparison.OrdinalIgnoreCase))
                        AnimatorFor(replayEvent.ActorId)?.ReactStagger();
                    break;
                case "StateChanged":
                    AnimatorFor(replayEvent.ActorId)?.StateChanged(
                        replayEvent.Payload["new_state"]?.ToObject<string>());
                    break;
                case "FighterDefeated":
                    AnimatorFor(presentation.EmphasizedFighterId)?.SetDefeated(true);
                    break;
                case "BattleEnded":
                    AnimatorFor(replayEvent.Payload["winner_fighter_id"]?.ToObject<string>())?.Celebrate();
                    break;
            }

            if (presentation.StartsGrab && !string.IsNullOrEmpty(presentation.GrabId) && presentation.GrabId != "—")
            {
                activeGrabId = presentation.GrabId;
                grabLink.AddToClassList("grab-link-active");
                grabLink.style.display = DisplayStyle.Flex;
            }
            else if (presentation.EndsGrab &&
                     !string.IsNullOrEmpty(activeGrabId) &&
                     string.Equals(activeGrabId, presentation.GrabId, StringComparison.Ordinal))
            {
                activeGrabId = null;
                grabLink.RemoveFromClassList("grab-link-active");
                grabLink.style.display = DisplayStyle.None;
            }

            if (string.Equals(presentation.WallSide, "Left", StringComparison.OrdinalIgnoreCase))
            {
                wallFlashLeft.AddToClassList("wall-flash-active");
                wallFlashLeft.style.display = DisplayStyle.Flex;
            }
            else if (string.Equals(presentation.WallSide, "Right", StringComparison.OrdinalIgnoreCase))
            {
                wallFlashRight.AddToClassList("wall-flash-active");
                wallFlashRight.style.display = DisplayStyle.Flex;
            }

            Render();
        }

        void RenderFighter(
            FighterDefinition fighter,
            IReadOnlyDictionary<string, FighterFrame> frames,
            FighterSceneRig sceneFighter,
            VisualElement marker,
            Label markerName,
            Label markerFacing,
            Label name,
            Label state,
            Label position,
            Label health,
            VisualElement healthFill)
        {
            if (fighter == null || !frames.TryGetValue(fighter.FighterId, out FighterFrame frame))
                return;

            string animalName = fighter.AnimalId.Replace('_', ' ').ToUpperInvariant();
            name.text = animalName;
            markerName.text = animalName;
            markerFacing.text = string.Equals(frame.Facing, "Right", StringComparison.OrdinalIgnoreCase) ? "▶" : "◀";
            state.text = DisplayWords(frame.State);
            position.text = frame.Position.ToString();
            health.text = $"{frame.Health} / {frame.MaxHealth} HP";

            bool facesRight = string.Equals(frame.Facing, "Right", StringComparison.OrdinalIgnoreCase);
            AnimatorFor(fighter.FighterId)?.SetFacing(facesRight);
            sceneStage.PlaceFighter(sceneFighter, frame.Position,
                session.Document.Arena.MinPosition, session.Document.Arena.MaxPosition);
            AnimatorFor(fighter.FighterId)?.SetRecordedState(frame.State);

            float hpPercent = frame.MaxHealth > 0 ? Mathf.Clamp01((float)frame.Health / frame.MaxHealth) * 100f : 0f;
            healthFill.style.width = Length.Percent(hpPercent);
            marker.style.left = Length.Percent(ProjectPosition(frame.Position));
        }

        void RenderResult(ReplayEvent replayEvent)
        {
            string outcome = replayEvent.Payload["outcome"]?.ToObject<string>() ?? "Unknown";
            string reason = replayEvent.Payload["end_reason"]?.ToObject<string>() ?? "Unknown";
            string winnerId = replayEvent.Payload["winner_fighter_id"]?.ToObject<string>();

            if (string.Equals(outcome, "Draw", StringComparison.OrdinalIgnoreCase))
            {
                resultLabel.text = "DRAW";
            }
            else if (!string.IsNullOrEmpty(winnerId))
            {
                FighterDefinition winner = string.Equals(winnerId, fighterA?.FighterId, StringComparison.Ordinal) ? fighterA : fighterB;
                string winnerName = winner?.AnimalId?.Replace('_', ' ').ToUpperInvariant() ?? winnerId.ToUpperInvariant();
                resultLabel.text = $"{winnerName} WINS";
            }
            else
            {
                resultLabel.text = DisplayWords(outcome);
            }

            resultDetailsLabel.text = $"{DisplayWords(reason)} · TICK {replayEvent.Tick}";
        }

        void UpdateGrabLink()
        {
            if (session == null || string.IsNullOrEmpty(activeGrabId) ||
                fighterA == null || fighterB == null ||
                !session.State.Fighters.TryGetValue(fighterA.FighterId, out FighterFrame frameA) ||
                !session.State.Fighters.TryGetValue(fighterB.FighterId, out FighterFrame frameB))
                return;

            float a = ProjectPosition(frameA.Position);
            float b = ProjectPosition(frameB.Position);
            float left = Mathf.Min(a, b);
            float right = Mathf.Max(a, b);
            grabLink.style.left = Length.Percent(left);
            grabLink.style.right = Length.Percent(100f - right);
        }

        float ProjectPosition(long position)
        {
            long arenaSpan = session.Document.Arena.MaxPosition - session.Document.Arena.MinPosition;
            if (arenaSpan <= 0)
                return 50f;
            float percent = (float)(position - session.Document.Arena.MinPosition) / arenaSpan * 100f;
            return Mathf.Clamp(percent, 6f, 94f);
        }

        FighterClipPlayer AnimatorFor(string fighterId)
        {
            if (fighterA != null && string.Equals(fighterA.FighterId, fighterId, StringComparison.Ordinal))
                return animatorA;
            if (fighterB != null && string.Equals(fighterB.FighterId, fighterId, StringComparison.Ordinal))
                return animatorB;
            return null;
        }

        void ResetPresentationState()
        {
            activeGrabId = null;
            animatorA?.Reset();
            animatorB?.Reset();
            if (grabLink != null)
            {
                grabLink.RemoveFromClassList("grab-link-active");
                grabLink.style.display = DisplayStyle.None;
            }
            if (wallFlashLeft != null)
            {
                wallFlashLeft.RemoveFromClassList("wall-flash-active");
                wallFlashLeft.style.display = DisplayStyle.None;
            }
            if (wallFlashRight != null)
            {
                wallFlashRight.RemoveFromClassList("wall-flash-active");
                wallFlashRight.style.display = DisplayStyle.None;
            }
            if (resultOverlay != null)
            {
                resultOverlay.RemoveFromClassList("result-visible");
                resultOverlay.style.display = DisplayStyle.None;
            }
        }

        void AddEventLog(ReplayEvent replayEvent)
        {
            var row = new VisualElement();
            row.AddToClassList("event-row");
            var meta = new Label($"{replayEvent.Sequence:00} · tick {replayEvent.Tick} · {replayEvent.EventType}");
            meta.AddToClassList("event-row-meta");
            var detail = new Label(EventCueFormatter.Format(replayEvent));
            detail.AddToClassList("event-row-detail");
            row.Add(meta);
            row.Add(detail);
            eventScroll.contentContainer.Add(row);
        }

        void ShowLoadError(string message)
        {
            statusLabel.text = $"LOAD ERROR · {message}";
            statusLabel.AddToClassList("status-error");
            if (session == null)
            {
                eventTypeLabel.text = "LOAD ERROR";
                eventDetailsLabel.text = message;
            }
            Debug.LogError($"Replay Viewer: {message}");
        }

        void ToggleTelemetry() => SetTelemetryOpen(!IsTelemetryOpen());
        void CloseTelemetry() => SetTelemetryOpen(false);
        void OnTelemetryScrimClicked(ClickEvent _) => SetTelemetryOpen(false);

        void OpenTelemetryFromResult()
        {
            resultOverlay.RemoveFromClassList("result-visible");
            resultOverlay.style.display = DisplayStyle.None;
            SetTelemetryOpen(true);
        }

        void SetTelemetryOpen(bool open)
        {
            telemetryOpen = open;
            telemetryDrawer.EnableInClassList("telemetry-open", open);
            telemetryScrim.EnableInClassList("telemetry-open", open);
            telemetryDrawer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            telemetryScrim.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open && session != null &&
                string.Equals(session.CurrentEvent.EventType, "BattleEnded", StringComparison.Ordinal))
            {
                resultOverlay.AddToClassList("result-visible");
                resultOverlay.style.display = DisplayStyle.Flex;
            }
        }

        bool IsTelemetryOpen() => telemetryOpen;

        static string FormatReplayChoice(string replayName)
        {
            switch (replayName)
            {
                case "resolution-basic-l1.engine-0.4.0.json":
                    return "REPLAY 01 · BASIC";
                case "resolution-double-ko-l1.engine-0.4.0.json":
                    return "REPLAY 02 · DOUBLE KO";
                case "resolution-wall-grab-l1.engine-0.4.0.json":
                    return "REPLAY 03 · WALL GRAB";
                default:
                    return replayName;
            }
        }

        static string DisplayWords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            var result = new System.Text.StringBuilder(value.Length + 8);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '_')
                {
                    result.Append(' ');
                }
                else
                {
                    if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1]))
                        result.Append(' ');
                    result.Append(char.ToUpperInvariant(current));
                }
            }
            return result.ToString();
        }

        static FighterDefinition FindFighter(ReplayDocument document, string side, int fallbackIndex)
        {
            foreach (FighterDefinition fighter in document.Fighters)
            {
                if (string.Equals(fighter.Side, side, StringComparison.OrdinalIgnoreCase))
                    return fighter;
            }
            return document.Fighters[fallbackIndex];
        }

        static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            return element ?? throw new InvalidOperationException($"Replay Viewer UI element '{name}' is missing.");
        }
    }
}
