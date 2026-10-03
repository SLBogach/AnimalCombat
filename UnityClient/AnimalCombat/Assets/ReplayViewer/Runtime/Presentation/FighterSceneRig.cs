using UnityEngine;

namespace AnimalCombat.ReplayViewer.Presentation
{
    public sealed class FighterSceneRig : MonoBehaviour
    {
        [SerializeField] Transform artRoot;
        [SerializeField] Transform head;
        [SerializeField] Transform arm;
        [SerializeField] Transform frontLeg;
        [SerializeField] Transform rearLeg;
        [SerializeField] Transform tail;
        [SerializeField] Transform weaponGrip;
        [SerializeField] Transform torsoArmor;
        [SerializeField] Transform headArmor;
        [SerializeField] Transform armArmor;

        Vector3 restArtScale;
        Animator animator;

        public Transform ArtRoot => artRoot;
        public Transform Head => head;
        public Transform Arm => arm;
        public Transform FrontLeg => frontLeg;
        public Transform RearLeg => rearLeg;
        public Transform Tail => tail;
        public Transform WeaponGrip => weaponGrip;
        public Transform TorsoArmor => torsoArmor;
        public Transform HeadArmor => headArmor;
        public Transform ArmArmor => armArmor;
        public Animator Animator => animator != null ? animator : animator = GetComponent<Animator>();
        public string CurrentPose { get; private set; } = "Idle";

        void Awake()
        {
            if (artRoot != null)
            {
                restArtScale = artRoot.localScale;
            }
        }

        public void Configure(Transform art, Transform headPivot, Transform armPivot,
            Transform frontLegPivot, Transform rearLegPivot, Transform tailPivot,
            Transform weaponSocket, Transform torsoSocket, Transform headSocket, Transform armSocket)
        {
            artRoot = art;
            head = headPivot;
            arm = armPivot;
            frontLeg = frontLegPivot;
            rearLeg = rearLegPivot;
            tail = tailPivot;
            weaponGrip = weaponSocket;
            torsoArmor = torsoSocket;
            headArmor = headSocket;
            armArmor = armSocket;
            restArtScale = artRoot.localScale;
        }

        public void SetFacing(bool right)
        {
            if (artRoot == null) return;
            float sign = right ? 1f : -1f;
            artRoot.localScale = new Vector3(Mathf.Abs(restArtScale.x) * sign, restArtScale.y, restArtScale.z);
        }

        public void PlayPose(string pose)
        {
            Animator player = Animator;
            if (player == null || player.runtimeAnimatorController == null)
                throw new System.InvalidOperationException("Fighter Animator is not configured: " + name);
            CurrentPose = pose;
            player.Play("Base Layer." + pose, 0, 0f);
            // Apply the first key immediately when the replay is paused.
            player.Update(0f);
        }
    }
}
