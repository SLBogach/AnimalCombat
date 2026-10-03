using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AnimalCombat.ReplayViewer.Contracts
{
    public sealed class ArenaDefinition
    {
        public ArenaDefinition(string arenaId, long minPosition, long maxPosition)
        {
            ArenaId = arenaId;
            MinPosition = minPosition;
            MaxPosition = maxPosition;
        }

        public string ArenaId { get; }
        public long MinPosition { get; }
        public long MaxPosition { get; }
    }

    public sealed class FighterFrame
    {
        public FighterFrame(
            string fighterId,
            long health,
            long maxHealth,
            long position,
            string facing,
            string state,
            long energy,
            long maxEnergy,
            long stagger,
            long staggerThreshold)
        {
            FighterId = fighterId;
            Health = health;
            MaxHealth = maxHealth;
            Position = position;
            Facing = facing;
            State = state;
            Energy = energy;
            MaxEnergy = maxEnergy;
            Stagger = stagger;
            StaggerThreshold = staggerThreshold;
        }

        public string FighterId { get; }
        public long Health { get; }
        public long MaxHealth { get; }
        public long Position { get; }
        public string Facing { get; }
        public string State { get; }
        public long Energy { get; }
        public long MaxEnergy { get; }
        public long Stagger { get; }
        public long StaggerThreshold { get; }

        public FighterFrame Copy()
        {
            return new FighterFrame(
                FighterId,
                Health,
                MaxHealth,
                Position,
                Facing,
                State,
                Energy,
                MaxEnergy,
                Stagger,
                StaggerThreshold);
        }
    }

    public sealed class FighterDefinition
    {
        public FighterDefinition(string fighterId, string animalId, string side, FighterFrame initialFrame)
        {
            FighterId = fighterId;
            AnimalId = animalId;
            Side = side;
            InitialFrame = initialFrame;
        }

        public string FighterId { get; }
        public string AnimalId { get; }
        public string Side { get; }
        public FighterFrame InitialFrame { get; }
    }

    public sealed class ReplayEvent
    {
        public ReplayEvent(
            long sequence,
            long tick,
            string eventId,
            string eventType,
            string actorId,
            string targetId,
            string actionId,
            FighterFrame afterActor,
            FighterFrame afterTarget,
            IReadOnlyList<FighterFrame> finalFrames,
            JObject payload)
        {
            Sequence = sequence;
            Tick = tick;
            EventId = eventId;
            EventType = eventType;
            ActorId = actorId;
            TargetId = targetId;
            ActionId = actionId;
            AfterActor = afterActor;
            AfterTarget = afterTarget;
            FinalFrames = finalFrames;
            Payload = payload;
        }

        public long Sequence { get; }
        public long Tick { get; }
        public string EventId { get; }
        public string EventType { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ActionId { get; }
        public FighterFrame AfterActor { get; }
        public FighterFrame AfterTarget { get; }
        public IReadOnlyList<FighterFrame> FinalFrames { get; }
        public JObject Payload { get; }
    }

    public sealed class ReplayDocument
    {
        public ReplayDocument(
            string schemaVersion,
            string replayId,
            string battleId,
            string engineVersion,
            ArenaDefinition arena,
            IReadOnlyList<FighterDefinition> fighters,
            IReadOnlyList<FighterFrame> initialFrames,
            IReadOnlyList<ReplayEvent> events)
        {
            SchemaVersion = schemaVersion;
            ReplayId = replayId;
            BattleId = battleId;
            EngineVersion = engineVersion;
            Arena = arena;
            Fighters = fighters;
            InitialFrames = initialFrames;
            Events = events;
        }

        public string SchemaVersion { get; }
        public string ReplayId { get; }
        public string BattleId { get; }
        public string EngineVersion { get; }
        public ArenaDefinition Arena { get; }
        public IReadOnlyList<FighterDefinition> Fighters { get; }
        public IReadOnlyList<FighterFrame> InitialFrames { get; }
        public IReadOnlyList<ReplayEvent> Events { get; }
    }
}
