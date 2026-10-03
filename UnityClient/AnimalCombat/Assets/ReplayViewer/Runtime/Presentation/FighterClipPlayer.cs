using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Presentation
{
    // Maps recorded events to authored Animator states. No Transform pose curves or combat rules live here.
    internal sealed class FighterClipPlayer
    {
        readonly FighterSceneRig rig;
        readonly Dictionary<string, float> clipLengths = new Dictionary<string, float>(StringComparer.Ordinal);
        string recordedState;
        bool defeated;
        bool grabbing;
        bool grabbed;
        bool victorious;
        bool kick;
        float elapsed;
        float transientLength;

        public FighterClipPlayer(FighterSceneRig rig)
        {
            this.rig = rig ?? throw new ArgumentNullException(nameof(rig));
            Animator animator = rig.Animator;
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Fighter prefab is missing its Animator Controller: " + rig.name);
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                clipLengths[clip.name] = clip.length;
            Reset();
        }

        public void SetFacing(bool right) => rig.SetFacing(right);

        public void SetPlayback(bool playing, bool atEnd, float speed)
        {
            // Terminal celebration/defeat may finish after the last replay event.
            bool finishTerminal = atEnd && (rig.CurrentPose == "Victory" || rig.CurrentPose == "Defeated");
            rig.Animator.speed = playing ? speed : finishTerminal ? 1f : 0f;
        }

        public void SetRecordedState(string state)
        {
            if (string.Equals(recordedState, state, StringComparison.OrdinalIgnoreCase))
                return;
            recordedState = state;
            SetDefeated(string.Equals(state, "Defeated", StringComparison.OrdinalIgnoreCase));
        }

        public void SetDefeated(bool value)
        {
            if (defeated == value) return;
            defeated = value;
            if (value)
            {
                grabbing = false;
                grabbed = false;
                Play("Defeated");
            }
            else
            {
                ReturnToBase();
            }
        }

        public void Decide() => Play("Decision", true);
        public void Commit() => Play("Committed", true);

        public void Prepare(string actionId)
        {
            kick = string.Equals(actionId, "kangaroo_flying_kick", StringComparison.Ordinal);
            Play(kick ? "WindupKick" : "Windup");
        }

        public void Strike() => Play(kick ? "StrikeKick" : "Strike", true);
        public void Miss() => Play(kick ? "MissKick" : "Miss", true);
        public void ReactHit() => Play("Hurt", true);
        public void ReactDamage() => Play("Damage", true);
        public void Move() => Play("Move", true);
        public void ReactKnockback() => Play("Knockback", true);
        public void ReactWall() => Play("WallImpact", true);
        public void ReactStagger() => Play("Stagger", true);

        public void BeginGrab()
        {
            grabbing = true;
            Play("Grabbing");
        }

        public void BeGrabbed()
        {
            grabbed = true;
            Play("Grabbed");
        }

        public void EndGrab(bool thrown)
        {
            grabbing = false;
            grabbed = false;
            if (thrown) Play("Thrown", true);
            else ReturnToBase();
        }

        public void Throw()
        {
            grabbing = false;
            Play("Throw", true);
        }

        public void StateChanged(string newState)
        {
            if (string.Equals(newState, "Defeated", StringComparison.OrdinalIgnoreCase))
                SetDefeated(true);
            else if (string.Equals(newState, "Stunned", StringComparison.OrdinalIgnoreCase))
                Play("Stagger");
            else if (string.Equals(newState, "KnockedDown", StringComparison.OrdinalIgnoreCase))
                Play("Knockdown");
            else if (string.Equals(newState, "Recovery", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(newState, "Recovering", StringComparison.OrdinalIgnoreCase))
                Play("Recovery", true);
        }

        public void Celebrate()
        {
            victorious = true;
            Play("Victory");
        }

        public void Update(float replaySeconds)
        {
            if (replaySeconds <= 0f || transientLength <= 0f) return;
            elapsed += replaySeconds;
            if (elapsed >= transientLength)
                ReturnToBase();
        }

        public void Reset()
        {
            recordedState = null;
            defeated = false;
            grabbing = false;
            grabbed = false;
            victorious = false;
            kick = false;
            Play("Idle");
        }

        void ReturnToBase()
        {
            if (defeated) Play("Defeated");
            else if (victorious) Play("Victory");
            else if (grabbing) Play("Grabbing");
            else if (grabbed) Play("Grabbed");
            else if (string.Equals(recordedState, "KnockedDown", StringComparison.OrdinalIgnoreCase)) Play("Knockdown");
            else if (string.Equals(recordedState, "Stunned", StringComparison.OrdinalIgnoreCase)) Play("Stagger");
            else Play("Idle");
        }

        void Play(string pose, bool transient = false)
        {
            if (defeated && pose != "Defeated") return;
            if (!clipLengths.TryGetValue(pose, out float length))
                throw new InvalidOperationException("Fighter Animator is missing clip state: " + pose);
            rig.PlayPose(pose);
            elapsed = 0f;
            transientLength = transient ? length : 0f;
        }
    }
}
