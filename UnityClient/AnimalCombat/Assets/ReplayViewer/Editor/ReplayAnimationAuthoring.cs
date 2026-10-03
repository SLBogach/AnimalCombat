using System;
using System.Collections.Generic;
using AnimalCombat.ReplayViewer.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Editor
{
    // Creates first-pass editable clips. Existing .anim/.controller assets are never overwritten.
    public static class ReplayAnimationAuthoring
    {
        const string Root = "Assets/ReplayViewer/Animations";
        const string ArtPath = "ArtRoot - move to adjust visual placement";
        const string MotionPath = ArtPath + "/MotionRoot - authored animation";

        struct Pose
        {
            public float X, Y, Head, Arm, FrontLeg, RearLeg, Tail;

            public Pose(float x, float y, float head, float arm, float frontLeg, float rearLeg, float tail)
            {
                X = x; Y = y; Head = head; Arm = arm;
                FrontLeg = frontLeg; RearLeg = rearLeg; Tail = tail;
            }
        }

        sealed class ClipSpec
        {
            public string Name;
            public float Duration;
            public bool Loop;
            public Pose[] Keys;

            public ClipSpec(string name, float duration, bool loop, params Pose[] keys)
            {
                Name = name; Duration = duration; Loop = loop; Keys = keys;
            }
        }

        [MenuItem("Animal Combat/Replay Viewer/Create missing animation clips")]
        public static void Build()
        {
            EnsureFolder("Assets/ReplayViewer", "Animations");
            BuildFighter("Bear", true);
            BuildFighter("Kangaroo", false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Replay Viewer: editable fighter Animation Clips and Animator Controllers are ready.");
        }

        static void BuildFighter(string animal, bool bear)
        {
            string folder = Root + "/" + animal;
            EnsureFolder(Root, animal);
            string controllerPath = folder + "/" + animal + ".controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            if (controller == null)
                throw new InvalidOperationException("Could not create Animator Controller: " + controllerPath);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ClipSpec spec in Specs(bear))
            {
                string path = folder + "/" + spec.Name + ".anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = CreateClip(spec, bear);
                    AssetDatabase.CreateAsset(clip, path);
                }

                AnimatorState state = FindState(machine, spec.Name);
                if (state == null)
                    state = machine.AddState(spec.Name);
                if (state.motion == null)
                    state.motion = clip;
                if (spec.Name == "Idle")
                    machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);

            string prefabPath = "Assets/ReplayViewer/Prefabs/" + animal + "Fighter.prefab";
            GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                FighterSceneRig rig = prefab.GetComponent<FighterSceneRig>();
                if (rig == null || rig.ArtRoot == null)
                    throw new InvalidOperationException("Fighter prefab has no scene rig: " + prefabPath);
                Transform motion = rig.ArtRoot.Find("MotionRoot - authored animation");
                if (motion == null)
                {
                    var motionObject = new GameObject("MotionRoot - authored animation");
                    motion = motionObject.transform;
                    motion.SetParent(rig.ArtRoot, false);
                    var parts = new List<Transform>();
                    foreach (Transform child in rig.ArtRoot)
                        if (child != motion) parts.Add(child);
                    foreach (Transform part in parts)
                        part.SetParent(motion, false);
                }

                Animator animator = prefab.GetComponent<Animator>();
                if (animator == null) animator = prefab.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        static AnimationClip CreateClip(ClipSpec spec, bool bear)
        {
            var clip = new AnimationClip { name = spec.Name, frameRate = 60f };
            SetCurve(clip, MotionPath, "m_LocalPosition.x", spec, pose => pose.X);
            SetCurve(clip, MotionPath, "m_LocalPosition.y", spec, pose => pose.Y);
            SetCurve(clip, MotionPath + "/HeadPivot", "localEulerAnglesRaw.z", spec, pose => -pose.Head);
            SetCurve(clip, MotionPath + "/ArmPivot", "localEulerAnglesRaw.z", spec, pose => -pose.Arm);
            SetCurve(clip, MotionPath + "/FrontLegPivot", "localEulerAnglesRaw.z", spec, pose => -pose.FrontLeg);
            SetCurve(clip, MotionPath + "/RearLegPivot", "localEulerAnglesRaw.z", spec, pose => -pose.RearLeg);
            if (!bear)
                SetCurve(clip, MotionPath + "/TailPivot", "localEulerAnglesRaw.z", spec, pose => -pose.Tail);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = spec.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static void SetCurve(AnimationClip clip, string path, string property,
            ClipSpec spec, Func<Pose, float> select)
        {
            var keys = new Keyframe[spec.Keys.Length];
            for (int index = 0; index < keys.Length; index++)
                keys[index] = new Keyframe(spec.Duration * index / (keys.Length - 1), select(spec.Keys[index]));
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), property), new AnimationCurve(keys));
        }

        static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states)
                if (child.state.name == name) return child.state;
            return null;
        }

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        static Pose P(float x = 0f, float y = 0f, float head = 0f, float arm = 0f,
            float front = 0f, float rear = 0f, float tail = 0f)
        {
            const float pixel = 0.012f;
            return new Pose(x * pixel, -y * pixel, head, arm, front, rear, tail);
        }

        static IEnumerable<ClipSpec> Specs(bool bear)
        {
            Pose idle = P();
            Pose windup = bear ? P(-10, -5, -10, -68, -9, 8, 12)
                : P(-10, -6, -12, -38, -24, 12, 18);
            Pose kickWindup = P(-10, -5, -10, -20, -42, 8, 12);
            Pose reach = bear ? P(26, -7, 14, 91, 12, -7, -10)
                : P(26, -9, 12, 82, 10, -8, -22);
            Pose kickReach = P(30, -18, 12, 42, 76, -15, -28);
            Pose hurt = P(-17, 4, -22, 29, -7, 7, 16);
            Pose damage = P(-13, 5, -24, 34, -8, 8, 18);
            Pose grabbing = P(8, -3, 7, 70, -8, 8, -14);
            Pose grabbed = P(-8, 5, -20, 50, -12, 8, 20);
            Pose fallen = P(-11, 28, -54, 65, -50, 31, 38);

            yield return new ClipSpec("Idle", 0.8f, true, idle, P(0, 2, 2, 3, 0, 0, -5), idle);
            yield return new ClipSpec("Decision", 0.28f, false, idle, P(0, -4, -9, -16, -4, 4, 9), idle);
            yield return new ClipSpec("Committed", 0.28f, false, idle, P(4, -5, -7, -25, -5, 5, 10), idle);
            yield return new ClipSpec("Windup", 0.3f, false, idle, windup, windup);
            yield return new ClipSpec("WindupKick", 0.3f, false, idle, kickWindup, kickWindup);
            yield return new ClipSpec("Strike", 0.38f, false, windup, reach, idle);
            yield return new ClipSpec("StrikeKick", 0.38f, false, kickWindup, kickReach, idle);
            yield return new ClipSpec("Miss", 0.38f, false, windup, P(33, -7, 14, 91, 12, -7, -10), idle);
            yield return new ClipSpec("MissKick", 0.38f, false, kickWindup, P(33, -18, 12, 42, 76, -15, -28), idle);
            yield return new ClipSpec("Hurt", 0.3f, false, idle, hurt, idle);
            yield return new ClipSpec("Damage", 0.3f, false, idle, damage, idle);
            yield return new ClipSpec("Move", 0.36f, false, idle, P(5, -8, 5, -17, 24, -24, -16), idle);
            yield return new ClipSpec("Knockback", 0.38f, false, idle, P(-24, -5, -23, 36, -23, 14, 25), idle);
            yield return new ClipSpec("Grabbing", 0.28f, false, idle, grabbing, grabbing);
            yield return new ClipSpec("Grabbed", 0.28f, false, idle, grabbed, grabbed);
            yield return new ClipSpec("Throw", 0.42f, false, grabbing, P(24, -12, 15, 95, -12, 12, -28), idle);
            yield return new ClipSpec("Thrown", 0.42f, false, grabbed, P(-25, -22, -42, 66, -47, 34, 38), idle);
            yield return new ClipSpec("WallImpact", 0.38f, false, idle, P(-18, 9, -35, 48, -24, 20, 31), idle);
            yield return new ClipSpec("Stagger", 0.3f, false, idle, P(-7, 4, -17, 22, -11, 8, 16), P(-4, 4, -13, 22, -11, 8, 16));
            yield return new ClipSpec("Knockdown", 0.3f, false, idle, fallen, fallen);
            yield return new ClipSpec("Recovery", 0.3f, false, fallen, P(-8, 17, -32, 32, -18, 9, 12), idle);
            yield return new ClipSpec("Victory", 0.4f, false, idle, P(0, -5, 10, -105, -6, 4, -20), P(0, -5, 10, -105, -6, 4, -20));
            yield return new ClipSpec("Defeated", 0.45f, false, idle, P(-10, 30, -58, 68, -44, 32, 43), P(-10, 30, -58, 68, -44, 32, 43));
        }
    }
}
