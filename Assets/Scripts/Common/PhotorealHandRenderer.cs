using UnityEngine;

namespace UnityVirtual.Common
{
    public class PhotorealHandRenderer : MonoBehaviour
    {
        [SerializeField] private QuestHandTrackingProvider trackingProvider;
        [SerializeField] private Transform leftHandRoot;
        [SerializeField] private Transform rightHandRoot;

        private void LateUpdate()
        {
            if (trackingProvider == null) return;

            if (leftHandRoot != null)
            {
                leftHandRoot.SetPositionAndRotation(
                    trackingProvider.LeftHandPose.position,
                    trackingProvider.LeftHandPose.rotation);
            }

            if (rightHandRoot != null)
            {
                rightHandRoot.SetPositionAndRotation(
                    trackingProvider.RightHandPose.position,
                    trackingProvider.RightHandPose.rotation);
            }
        }
    }
}
