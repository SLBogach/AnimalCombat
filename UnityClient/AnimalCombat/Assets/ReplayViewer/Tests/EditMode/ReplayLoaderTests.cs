using System.IO;
using System.Security.Cryptography;
using AnimalCombat.ReplayViewer.Runtime.Loading;
using AnimalCombat.ReplayViewer.Runtime.Playback;
using NUnit.Framework;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Tests.EditMode
{
    public sealed class ReplayLoaderTests
    {
        static readonly string[] FixtureNames =
        {
            "resolution-basic-l1.engine-0.4.0.json",
            "resolution-double-ko-l1.engine-0.4.0.json",
            "resolution-wall-grab-l1.engine-0.4.0.json"
        };

        [TestCaseSource(nameof(FixtureNames))]
        public void RequiredFixture_LoadsWithoutSimulation(string fixtureName)
        {
            ReplayLoadResult result = LoadFixture(fixtureName);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Document.SchemaVersion, Is.EqualTo("combat.replay/0.1"));
            Assert.That(result.Document.Events[0].EventType, Is.EqualTo("BattleStarted"));
            Assert.That(result.Document.Events[result.Document.Events.Count - 1].EventType, Is.EqualTo("BattleEnded"));
            Assert.That(result.Document.InitialFrames, Has.Count.EqualTo(2));
        }

        [Test]
        public void BasicReplay_FinalStateComesFromRecordedFinalFrames()
        {
            ReplayLoadResult result = LoadFixture(FixtureNames[0]);
            var session = new ReplaySession(result.Document);

            while (session.AdvanceOne())
            {
            }

            Assert.That(session.State.Fighters["fighter_a"].Health, Is.EqualTo(100));
            Assert.That(session.State.Fighters["fighter_b"].Health, Is.EqualTo(0));
            Assert.That(session.State.Fighters["fighter_b"].State, Is.EqualTo("Defeated"));
        }

        [Test]
        public void UnknownEvent_IsAcceptedAndDoesNotCrashPlayback()
        {
            string json = ReadBundled(FixtureNames[0]);
            string withUnknownEvents = json.Replace(
                "\"event_type\":\"DecisionMade\"",
                "\"event_type\":\"FutureTelemetryEvent\"");

            ReplayLoadResult result = new ReplayLoader().Load(withUnknownEvents);
            Assert.That(result.Success, Is.True, result.Error);

            var session = new ReplaySession(result.Document);
            Assert.DoesNotThrow(() => session.AdvanceOne());
            Assert.That(session.CurrentEvent.EventType, Is.EqualTo("FutureTelemetryEvent"));
        }

        [Test]
        public void TransportControls_PlayPauseSpeedAndRestartAreDeterministic()
        {
            ReplayLoadResult result = LoadFixture(FixtureNames[0]);
            var session = new ReplaySession(result.Document);

            session.SetSpeed(4d);
            session.Play();
            session.Update(0.11d);
            Assert.That(session.CurrentIndex, Is.EqualTo(1));

            session.Pause();
            session.Update(10d);
            Assert.That(session.CurrentIndex, Is.EqualTo(1));

            session.Restart();
            Assert.That(session.CurrentIndex, Is.EqualTo(0));
            Assert.That(session.IsPlaying, Is.False);
            Assert.That(session.State.Fighters["fighter_b"].Health, Is.EqualTo(100));
        }

        [TestCaseSource(nameof(FixtureNames))]
        public void BundledFixture_IsByteForByteCanonicalCopy(string fixtureName)
        {
            byte[] canonical = File.ReadAllBytes(CanonicalPath(fixtureName));
            byte[] bundled = File.ReadAllBytes(BundledPath(fixtureName));

            Assert.That(Sha256(bundled), Is.EqualTo(Sha256(canonical)));
            Assert.That(bundled, Is.EqualTo(canonical));
        }

        static ReplayLoadResult LoadFixture(string fixtureName)
        {
            return new ReplayLoader().Load(ReadBundled(fixtureName));
        }

        static string ReadBundled(string fixtureName) => File.ReadAllText(BundledPath(fixtureName));

        static string BundledPath(string fixtureName)
        {
            return Path.Combine(Application.dataPath, "StreamingAssets", "Replays", "v0.1", fixtureName);
        }

        static string CanonicalPath(string fixtureName)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "..",
                "CombatLab",
                "fixtures",
                "replay",
                "v0.1",
                fixtureName));
        }

        static string Sha256(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return System.BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }
    }
}
