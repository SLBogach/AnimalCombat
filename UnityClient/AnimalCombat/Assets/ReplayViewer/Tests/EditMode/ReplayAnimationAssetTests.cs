using System.Linq;
using AnimalCombat.ReplayViewer.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Tests.EditMode
{
    public sealed class ReplayAnimationAssetTests
    {
        static readonly string[] RequiredClips =
        {
            "Idle", "Decision", "Committed", "Windup", "WindupKick",
            "Strike", "StrikeKick", "Miss", "MissKick", "Hurt", "Damage",
            "Move", "Knockback", "Grabbing", "Grabbed", "Throw", "Thrown",
            "WallImpact", "Stagger", "Knockdown", "Recovery", "Victory", "Defeated"
        };

        [TestCase("Bear")]
        [TestCase("Kangaroo")]
        public void FighterPrefab_HasEditableClipsWithoutAnimatingRecordedPosition(string animal)
        {
            const string root = "Assets/ReplayViewer";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                root + "/Prefabs/" + animal + "Fighter.prefab");
            Assert.That(prefab, Is.Not.Null);
            FighterSceneRig rig = prefab.GetComponent<FighterSceneRig>();
            Animator animator = prefab.GetComponent<Animator>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);
            Assert.That(rig.ArtRoot.Find("MotionRoot - authored animation"), Is.Not.Null);

            string folder = root + "/Animations/" + animal + "/";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                folder + animal + ".controller");
            Assert.That(controller, Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController, Is.EqualTo(controller));
            Assert.That(controller.layers[0].stateMachine.defaultState.name, Is.EqualTo("Idle"));

            foreach (string name in RequiredClips)
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + name + ".anim");
                Assert.That(clip, Is.Not.Null, animal + " is missing " + name);
                Assert.That(clip.length, Is.GreaterThan(0f));
                Assert.That(controller.layers[0].stateMachine.states.Any(state =>
                    state.state.name == name && state.state.motion == clip), Is.True,
                    animal + " controller is missing " + name);

                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.That(bindings.Length, Is.GreaterThan(0));
                Assert.That(bindings.All(binding => binding.path.StartsWith(
                    "ArtRoot - move to adjust visual placement/MotionRoot - authored animation")), Is.True,
                    "A clip must not animate ReplayAnchor, camera, HUD or equipment assignment.");
            }
        }
    }
}
