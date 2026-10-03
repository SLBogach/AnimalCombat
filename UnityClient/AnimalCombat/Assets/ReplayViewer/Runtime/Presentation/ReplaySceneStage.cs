using UnityEngine;

namespace AnimalCombat.ReplayViewer.Presentation
{
    // Scene-authored presentation surface. No combat calculations or state live here.
    public sealed class ReplaySceneStage : MonoBehaviour
    {
        [SerializeField] Transform leftBoundary;
        [SerializeField] Transform rightBoundary;
        [SerializeField] FighterSceneRig fighterA;
        [SerializeField] FighterSceneRig fighterB;

        public FighterSceneRig FighterA => fighterA;
        public FighterSceneRig FighterB => fighterB;
        public Transform LeftBoundary => leftBoundary;
        public Transform RightBoundary => rightBoundary;

        public void Configure(Transform left, Transform right, FighterSceneRig a, FighterSceneRig b)
        {
            leftBoundary = left;
            rightBoundary = right;
            fighterA = a;
            fighterB = b;
        }

        public void PlaceFighter(FighterSceneRig fighter, long recordedPosition, long arenaMin, long arenaMax)
        {
            if (fighter == null || leftBoundary == null || rightBoundary == null) return;
            float fraction = arenaMax > arenaMin
                ? Mathf.Clamp01((float)(recordedPosition - arenaMin) / (arenaMax - arenaMin))
                : 0.5f;
            Vector3 position = fighter.transform.position;
            position.x = Mathf.Lerp(leftBoundary.position.x, rightBoundary.position.x, fraction);
            fighter.transform.position = position;
        }
    }
}
