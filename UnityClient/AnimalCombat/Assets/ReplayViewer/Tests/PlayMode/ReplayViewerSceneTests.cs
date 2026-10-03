using System.Collections;
using AnimalCombat.ReplayViewer.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimalCombat.ReplayViewer.Tests.PlayMode
{
    public sealed class ReplayViewerSceneTests
    {
        VisualElement root;
        ReplayViewerController controller;
        ReplaySceneStage stage;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("ReplayViewer", LoadSceneMode.Single);
            yield return null;
            yield return null;
            UIDocument document = Object.FindAnyObjectByType<UIDocument>();
            Assert.That(document, Is.Not.Null);
            root = document.rootVisualElement;
            controller = Object.FindAnyObjectByType<ReplayViewerController>();
            stage = Object.FindAnyObjectByType<ReplaySceneStage>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(stage, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ArenaAndFighters_AreSceneObjectsWithEditablePartsAndEquipmentSockets()
        {
            yield return null;
            Camera camera = Object.FindAnyObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.cullingMask & 1, Is.Not.Zero);
            SpriteRenderer background = stage.transform.Find("Arena Background").GetComponent<SpriteRenderer>();
            Assert.That(background.sprite, Is.Not.Null);
            Assert.That(background.sortingOrder, Is.LessThan(0));
            Assert.That(stage.LeftBoundary, Is.Not.Null);
            Assert.That(stage.RightBoundary, Is.Not.Null);
            AssertRig(stage.FighterA);
            AssertRig(stage.FighterB);
            Assert.That(stage.FighterB.Tail, Is.Not.Null);
            Assert.That(stage.FighterA.ArtRoot.Find("MotionRoot - authored animation"), Is.Not.Null);
            Assert.That(stage.FighterB.ArtRoot.Find("MotionRoot - authored animation"), Is.Not.Null);
            Assert.That(root.Q<VisualElement>("fighter-a-silhouette"), Is.Null);
            Assert.That(root.Q<VisualElement>("fighter-b-silhouette"), Is.Null);
        }

        [UnityTest]
        public IEnumerator HudAndControls_StayInUIToolkit_AndTelemetryDoesNotAdvanceReplay()
        {
            yield return null;
            Assert.That(root.Q<Label>("fighter-a-health").text, Is.EqualTo("100 / 100 HP"));
            Assert.That(root.Q<Label>("fighter-b-health").text, Is.EqualTo("100 / 100 HP"));
            Assert.That(root.Q<Label>("sequence-label").text, Is.EqualTo("SEQ 0 / 10"));
            Color hp = root.Q<VisualElement>("fighter-a-health-fill").resolvedStyle.backgroundColor;
            Assert.That(hp.r, Is.GreaterThan(hp.g));
            string before = root.Q<Label>("sequence-label").text;
            Submit(root.Q<Button>("debug-button"));
            yield return null;
            Assert.That(root.Q<VisualElement>("telemetry-drawer").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Label>("sequence-label").text, Is.EqualTo(before));
            Submit(root.Q<Button>("telemetry-close-button"));
            yield return null;
            Assert.That(root.Q<VisualElement>("telemetry-drawer").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator BasicReplay_AnimatesSceneParts_AndDoesNotInventDamage()
        {
            Transform arm = stage.FighterA.Arm;
            float restAngle = arm.localEulerAngles.z;
            controller.Play();
            yield return WaitForSequence("SEQ 5 / 10");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Windup"));
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(arm.localEulerAngles.z, Is.Not.EqualTo(restAngle));
            Assert.That(root.Q<Label>("fighter-b-health").text, Is.EqualTo("100 / 100 HP"));
            controller.Pause();
            float heldAngle = arm.localEulerAngles.z;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(arm.localEulerAngles.z, Is.EqualTo(heldAngle));
            Assert.That(root.Q<Label>("sequence-label").text, Is.EqualTo("SEQ 5 / 10"));
            controller.Play();
            yield return WaitForSequence("SEQ 6 / 10");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Strike"));
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Hurt"));
            Assert.That(root.Q<Label>("fighter-b-health").text, Is.EqualTo("100 / 100 HP"));
            yield return WaitForSequence("SEQ 7 / 10");
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Damage"));
            Assert.That(root.Q<Label>("fighter-b-health").text, Is.EqualTo("0 / 100 HP"));
            yield return WaitForSequence("SEQ 10 / 10");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Victory"));
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Defeated"));
            Assert.That(root.Q<Label>("result-label").text, Is.EqualTo("BEAR WINS"));
            controller.Restart();
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Idle"));
            Assert.That(root.Q<Label>("fighter-b-health").text, Is.EqualTo("100 / 100 HP"));
        }

        [UnityTest]
        public IEnumerator WallGrab_UsesRecordedPositionsAndVisualReactions()
        {
            root.Q<DropdownField>("replay-picker").value = "resolution-wall-grab-l1.engine-0.4.0.json";
            yield return null;
            controller.Play();
            yield return WaitForSequence("SEQ 6 / 17");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Grabbing"));
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Grabbed"));
            Assert.That(root.Q<Label>("fighter-b-position").text, Is.EqualTo("9500"));
            yield return WaitForSequence("SEQ 10 / 17");
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Knockback"));
            Assert.That(root.Q<Label>("fighter-b-position").text, Is.EqualTo("9570"));
            yield return WaitForSequence("SEQ 11 / 17");
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("WallImpact"));
            yield return WaitForSequence("SEQ 14 / 17");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Throw"));
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Thrown"));
            yield return WaitForSequence("SEQ 16 / 17");
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Defeated"));
        }

        [UnityTest]
        public IEnumerator DoubleKo_LeavesBothFightersDefeatedWithoutWinner()
        {
            root.Q<DropdownField>("replay-picker").value = "resolution-double-ko-l1.engine-0.4.0.json";
            yield return null;
            controller.Play();
            yield return WaitForSequence("SEQ 12 / 16");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Defeated"));
            Assert.That(stage.FighterB.CurrentPose, Is.Not.EqualTo("Defeated"));
            yield return WaitForSequence("SEQ 14 / 16");
            Assert.That(stage.FighterA.CurrentPose, Is.EqualTo("Defeated"));
            Assert.That(stage.FighterB.CurrentPose, Is.EqualTo("Defeated"));
            yield return WaitForSequence("SEQ 16 / 16");
            Assert.That(root.Q<Label>("result-label").text, Is.EqualTo("DRAW"));
        }

        static void AssertRig(FighterSceneRig rig)
        {
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.ArtRoot, Is.Not.Null);
            Assert.That(rig.Animator, Is.Not.Null);
            Assert.That(rig.Animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(rig.Animator.applyRootMotion, Is.False);
            Assert.That(rig.Animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            Assert.That(rig.Head.Find("Head Sprite - replaceable").GetComponent<SpriteRenderer>().sprite, Is.Not.Null);
            Assert.That(rig.Arm.Find("Arm Sprite - replaceable").GetComponent<SpriteRenderer>().sprite, Is.Not.Null);
            Assert.That(rig.FrontLeg, Is.Not.Null);
            Assert.That(rig.RearLeg, Is.Not.Null);
            Assert.That(rig.WeaponGrip, Is.Not.Null);
            Assert.That(rig.TorsoArmor, Is.Not.Null);
            Assert.That(rig.HeadArmor, Is.Not.Null);
            Assert.That(rig.ArmArmor, Is.Not.Null);
            SpriteRenderer weapon = rig.WeaponGrip.GetChild(0).GetComponent<SpriteRenderer>();
            SpriteRenderer torso = rig.TorsoArmor.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.That(weapon, Is.Not.Null);
            Assert.That(torso, Is.Not.Null);
            Assert.That(weapon.sprite, Is.Null,
                "Equipment slots must not pretend that replay JSON contains a selected weapon.");
        }

        IEnumerator WaitForSequence(string expected)
        {
            float deadline = Time.realtimeSinceStartup + 9f;
            Label sequence = root.Q<Label>("sequence-label");
            while (sequence.text != expected && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(sequence.text, Is.EqualTo(expected));
        }

        static void Submit(Button button)
        {
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
