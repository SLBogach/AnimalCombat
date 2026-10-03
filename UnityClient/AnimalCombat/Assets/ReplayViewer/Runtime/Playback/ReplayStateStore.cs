using System;
using System.Collections.Generic;
using AnimalCombat.ReplayViewer.Contracts;

namespace AnimalCombat.ReplayViewer.Runtime.Playback
{
    public sealed class ReplayStateStore
    {
        readonly Dictionary<string, FighterFrame> fighters = new Dictionary<string, FighterFrame>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, FighterFrame> Fighters => fighters;

        public void Reset(ReplayDocument document)
        {
            fighters.Clear();
            foreach (FighterFrame frame in document.InitialFrames)
                fighters[frame.FighterId] = frame.Copy();
        }

        public void Apply(ReplayEvent replayEvent)
        {
            ApplyFrame(replayEvent.AfterActor);
            ApplyFrame(replayEvent.AfterTarget);

            if (string.Equals(replayEvent.EventType, "BattleEnded", StringComparison.Ordinal))
            {
                foreach (FighterFrame frame in replayEvent.FinalFrames)
                    ApplyFrame(frame);
            }
        }

        void ApplyFrame(FighterFrame frame)
        {
            if (frame != null)
                fighters[frame.FighterId] = frame.Copy();
        }
    }
}
