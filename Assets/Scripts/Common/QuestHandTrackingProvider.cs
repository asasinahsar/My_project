using UnityEngine;

namespace UnityVirtual.Common
{
    public class QuestHandTrackingProvider : MonoBehaviour
    {
        [SerializeField] private Transform leftHandSource;
        [SerializeField] private Transform rightHandSource;

        public Pose LeftHandPose { get; private set; }
        public Pose RightHandPose { get; private set; }

        private void Update()
        {
            if (leftHandSource != null)
            {
                LeftHandPose = new Pose(leftHandSource.position, leftHandSource.rotation);
            }

            if (rightHandSource != null)
            {
                RightHandPose = new Pose(rightHandSource.position, rightHandSource.rotation);
            }
        }
    }
}
