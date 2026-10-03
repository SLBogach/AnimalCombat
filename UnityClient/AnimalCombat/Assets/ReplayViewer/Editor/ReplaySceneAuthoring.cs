using System;
using System.Linq;
using AnimalCombat.ReplayViewer.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Editor
{
    // One-shot authoring tool. It serializes real GameObjects and prefab instances;
    // runtime does not regenerate the visual hierarchy.
    public static class ReplaySceneAuthoring
    {
        const string Root = "Assets/ReplayViewer";
        const string ScenePath = Root + "/Scenes/ReplayViewer.unity";
        const float Pixel = 0.012f;

        [MenuItem("Animal Combat/Replay Viewer/Build editable scene")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            string prefabFolder = Root + "/Prefabs";
            if (!AssetDatabase.IsValidFolder(prefabFolder))
                AssetDatabase.CreateFolder(Root, "Prefabs");

            GameObject bear = BuildFighter("Bear", "bear-rig-atlas-v0.1", true);
            GameObject kangaroo = BuildFighter("Kangaroo", "kangaroo-rig-atlas-v0.1", false);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject oldStage = GameObject.Find("Arena & Fighters");
            if (oldStage != null)
                UnityEngine.Object.DestroyImmediate(oldStage);

            var stageRoot = new GameObject("Arena & Fighters");
            var stage = stageRoot.AddComponent<ReplaySceneStage>();
            GameObject background = Child(stageRoot.transform, "Arena Background");
            SpriteRenderer backgroundRenderer = background.AddComponent<SpriteRenderer>();
            backgroundRenderer.sprite = LoadSprite("Assets/Resources/ReplayViewer/Environment/arena-savanna-v0.2.png", 0);
            backgroundRenderer.sortingOrder = -100;
            background.transform.localScale = new Vector3(1.08f, 1.08f, 1f);

            Transform left = Child(stageRoot.transform, "Replay Position 0 - move to adjust projection").transform;
            Transform right = Child(stageRoot.transform, "Replay Position Max - move to adjust projection").transform;
            left.localPosition = new Vector3(-7.25f, -3.3f, 0f);
            right.localPosition = new Vector3(7.25f, -3.3f, 0f);

            FighterSceneRig fighterA = PrefabUtility.InstantiatePrefab(bear, scene) is GameObject bearInstance
                ? bearInstance.GetComponent<FighterSceneRig>() : null;
            FighterSceneRig fighterB = PrefabUtility.InstantiatePrefab(kangaroo, scene) is GameObject kangarooInstance
                ? kangarooInstance.GetComponent<FighterSceneRig>() : null;
            if (fighterA == null || fighterB == null)
                throw new InvalidOperationException("Could not instantiate fighter prefabs.");
            fighterA.transform.SetParent(stageRoot.transform);
            fighterB.transform.SetParent(stageRoot.transform);
            fighterA.transform.localPosition = new Vector3(-1.45f, -3.3f, 0f);
            fighterB.transform.localPosition = new Vector3(0.29f, -3.3f, 0f);
            fighterB.ArtRoot.localScale = new Vector3(-1f, 1f, 1f);
            stage.Configure(left, right, fighterA, fighterB);

            Camera camera = GameObject.Find("Replay Viewer Camera").GetComponent<Camera>();
            camera.cullingMask = 1;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ReplayAnimationAuthoring.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("Replay Viewer: editable arena and fighter prefabs authored.");
        }

        static GameObject BuildFighter(string name, string atlasName, bool bear)
        {
            var root = new GameObject(name + " - ReplayAnchor");
            var rig = root.AddComponent<FighterSceneRig>();
            Transform art = Child(root.transform, "ArtRoot - move to adjust visual placement").transform;
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/Resources/ReplayViewer/Characters/" + atlasName + ".png")
                .OfType<Sprite>().ToArray();
            if (sprites.Length == 0)
                throw new InvalidOperationException("Missing atlas sprites: " + atlasName);
            Func<int, Sprite> sprite = index => sprites.First(s => s.name == atlasName + "_" + index);

            Transform tail = null;
            if (!bear)
                tail = Part(art, "Tail", sprite(4), 0, 0, 128, 119, 47, 0.92f, 0.42f);
            Transform rear = bear
                ? Part(art, "RearLeg", sprite(9), 1, 88, 151, 55, 77, 0.44f, 0.13f)
                : Part(art, "RearLeg", sprite(6), 1, 100, 138, 55, 75, 0.42f, 0.12f);
            Transform body = bear
                ? Part(art, "Body", sprite(0), 2, 60, 59, 100, 105, 0.5f, 0.5f)
                : Part(art, "Body", sprite(0), 2, 90, 62, 80, 87, 0.5f, 0.5f);
            Transform front = bear
                ? Part(art, "FrontLeg", sprite(8), 3, 68, 148, 52, 76, 0.48f, 0.12f)
                : Part(art, "FrontLeg", sprite(5), 3, 96, 137, 70, 85, 0.48f, 0.12f);
            Transform head = bear
                ? Part(art, "Head", sprite(5), 4, 108, 20, 84, 62, 0.23f, 0.87f)
                : Part(art, "Head", sprite(1), 4, 113, 20, 67, 71, 0.27f, 0.83f);
            Transform arm = bear
                ? Part(art, "Arm", sprite(3), 5, 132, 82, 70, 99, 0.28f, 0.16f)
                : Part(art, "Arm", sprite(2), 5, 148, 95, 45, 48, 0.23f, 0.14f);

            Transform weapon = Socket(arm, "WeaponGrip", bear ? new Vector3(0.53f, -0.84f, 0f) : new Vector3(0.36f, -0.36f, 0f));
            Transform torsoArmor = Socket(body, "TorsoArmor", Vector3.zero);
            Transform headArmor = Socket(head, "HeadArmor", new Vector3(0.18f, 0.28f, 0f));
            Transform armArmor = Socket(arm, "ArmArmor", new Vector3(0.13f, -0.24f, 0f));
            rig.Configure(art, head, arm, front, rear, tail, weapon, torsoArmor, headArmor, armArmor);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root,
                Root + "/Prefabs/" + name + "Fighter.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform Part(Transform parent, string name, Sprite sprite, int order,
            float left, float top, float width, float height, float pivotX, float pivotY)
        {
            Transform pivot = Child(parent, name + "Pivot").transform;
            float originX = left + width * pivotX;
            float originY = top + height * pivotY;
            pivot.localPosition = new Vector3((originX - 110f) * Pixel, (230f - originY) * Pixel, 0f);
            GameObject art = Child(pivot, name + " Sprite - replaceable");
            SpriteRenderer renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            art.transform.localScale = new Vector3(width * Pixel / sprite.bounds.size.x,
                height * Pixel / sprite.bounds.size.y, 1f);
            // Atlas sub-sprites have bottom-left pivots. Offset from the joint to the art's bottom-left.
            art.transform.localPosition = new Vector3((left - originX) * Pixel,
                (originY - top - height) * Pixel, 0f);
            return pivot;
        }

        static Transform Socket(Transform parent, string name, Vector3 offset)
        {
            Transform socket = Child(parent, name + " Socket - attach sprite here").transform;
            socket.localPosition = offset;
            GameObject visual = Child(socket, name + " Visual - assign art in Inspector");
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = name == "WeaponGrip" ? 8 : 7;
            return socket;
        }

        static Sprite LoadSprite(string path, int index)
        {
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .FirstOrDefault(s => s.name.EndsWith("_" + index, StringComparison.Ordinal));
            if (sprite == null) throw new InvalidOperationException("Missing sprite: " + path);
            return sprite;
        }

        static GameObject Child(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
