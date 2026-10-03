using System;
using AnimalCombat.ReplayViewer.Contracts;

namespace AnimalCombat.ReplayViewer.Runtime.Playback
{
    public sealed class ReplaySession
    {
        const double BaseSecondsPerEvent = 0.4d;
        double accumulator;
        double speed = 1d;

        public ReplaySession(ReplayDocument document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            State = new ReplayStateStore();
            Restart();
        }

        public event Action Changed;
        public event Action<ReplayEvent> EventApplied;

        public ReplayDocument Document { get; }
        public ReplayStateStore State { get; }
        public int CurrentIndex { get; private set; }
        public bool IsPlaying { get; private set; }
        public double Speed => speed;
        public ReplayEvent CurrentEvent => Document.Events[CurrentIndex];
        public bool IsAtEnd => CurrentIndex >= Document.Events.Count - 1;

        public void SetSpeed(double value)
        {
            speed = value < 0.25d ? 0.25d : value > 4d ? 4d : value;
            Changed?.Invoke();
        }

        public void Play()
        {
            if (!IsAtEnd)
                IsPlaying = true;
            Changed?.Invoke();
        }

        public void Pause()
        {
            IsPlaying = false;
            Changed?.Invoke();
        }

        public void Restart()
        {
            accumulator = 0d;
            IsPlaying = false;
            CurrentIndex = 0;
            State.Reset(Document);
            State.Apply(Document.Events[0]);
            Changed?.Invoke();
        }

        public void Update(double unscaledDeltaTime)
        {
            if (!IsPlaying || unscaledDeltaTime <= 0d)
                return;

            accumulator += unscaledDeltaTime * speed;
            while (accumulator >= BaseSecondsPerEvent && IsPlaying)
            {
                accumulator -= BaseSecondsPerEvent;
                AdvanceOne();
            }
        }

        public bool AdvanceOne()
        {
            if (IsAtEnd)
            {
                IsPlaying = false;
                Changed?.Invoke();
                return false;
            }

            CurrentIndex++;
            ReplayEvent replayEvent = CurrentEvent;
            State.Apply(replayEvent);
            if (string.Equals(replayEvent.EventType, "BattleEnded", StringComparison.Ordinal) || IsAtEnd)
                IsPlaying = false;

            EventApplied?.Invoke(replayEvent);
            Changed?.Invoke();
            return true;
        }
    }
}
