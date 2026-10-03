using System;
using System.IO;
using AnimalCombat.ReplayViewer.Contracts;
using AnimalCombat.ReplayViewer.Presentation;
using AnimalCombat.ReplayViewer.Runtime.Loading;
using NUnit.Framework;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Tests.EditMode
{
    public sealed class EventPresentationMapperTests
    {
        const string BasicFixture = "resolution-basic-l1.engine-0.4.0.json";
        const string WallGrabFixture = "resolution-wall-grab-l1.engine-0.4.0.json";

        [Test]
        public void DamageApplied_UsesRecordedBreakdownValue()
        {
            ReplayDocument document = Load(WallGrabFixture);
            ReplayEvent replayEvent = Find(document, "DamageApplied", 12);

            EventPresentation presentation = EventPresentationMapper.Map(replayEvent);

            Assert.That(presentation.Cue, Is.EqualTo("DAMAGE"));
            Assert.That(presentation.Value, Is.EqualTo("−93"));
            Assert.That(presentation.EmphasizedFighterId, Is.EqualTo("fighter_b"));
        }

        [Test]
        public void WallImpact_UsesRecordedWallAndDamage()
        {
            ReplayDocument document = Load(WallGrabFixture);
            ReplayEvent replayEvent = Find(document, "WallImpact", 11);

            EventPresentation presentation = EventPresentationMapper.Map(replayEvent);

            Assert.That(presentation.CueClass, Is.EqualTo("cue-wall"));
            Assert.That(presentation.WallSide, Is.EqualTo("Right"));
            Assert.That(presentation.Value, Is.EqualTo("−93"));
        }

        [Test]
        public void GrabDirectives_PreserveRecordedGrabId()
        {
            ReplayDocument document = Load(WallGrabFixture);
            EventPresentation start = EventPresentationMapper.Map(Find(document, "GrabStarted", 6));
            EventPresentation end = EventPresentationMapper.Map(Find(document, "GrabEnded", 14));

            Assert.That(start.StartsGrab, Is.True);
            Assert.That(start.GrabId, Is.EqualTo("grab:dec-fighter_a-000001:00"));
            Assert.That(end.EndsGrab, Is.True);
            Assert.That(end.GrabId, Is.EqualTo(start.GrabId));
            Assert.That(end.Cue, Is.EqualTo("THROW"));
        }

        [Test]
        public void UnknownEvent_ProducesBoundedGenericDirective()
        {
            string json = File.ReadAllText(BundledPath(BasicFixture));
            string withUnknown = json.Replace(
                "\"event_type\":\"DecisionMade\"",
                "\"event_type\":\"FutureCombatEvent\"");
            ReplayLoadResult loadResult = new ReplayLoader().Load(withUnknown);

            Assert.That(loadResult.Success, Is.True, loadResult.Error);
            EventPresentation presentation = EventPresentationMapper.Map(loadResult.Document.Events[1]);
            Assert.That(presentation.IsUnknown, Is.True);
            Assert.That(presentation.Cue, Is.EqualTo("UNKNOWN EVENT"));
            Assert.That(presentation.Value, Is.EqualTo("FutureCombatEvent"));
        }

        static ReplayDocument Load(string fixtureName)
        {
            ReplayLoadResult result = new ReplayLoader().Load(File.ReadAllText(BundledPath(fixtureName)));
            Assert.That(result.Success, Is.True, result.Error);
            return result.Document;
        }

        static ReplayEvent Find(ReplayDocument document, string eventType, long sequence)
        {
            foreach (ReplayEvent replayEvent in document.Events)
            {
                if (replayEvent.Sequence == sequence &&
                    string.Equals(replayEvent.EventType, eventType, StringComparison.Ordinal))
                    return replayEvent;
            }
            throw new AssertionException($"Event {sequence}:{eventType} was not found.");
        }

        static string BundledPath(string fixtureName)
        {
            return Path.Combine(Application.dataPath, "StreamingAssets", "Replays", "v0.1", fixtureName);
        }
    }
}
