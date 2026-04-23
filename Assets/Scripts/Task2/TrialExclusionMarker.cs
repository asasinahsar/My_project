using UnityEngine;

namespace UnityVirtual.Task2
{
    public class TrialExclusionMarker : MonoBehaviour
    {
        [SerializeField] private float exclusionThreshold = 3f;

        public bool IsExcluded(float vasScore)
        {
            return vasScore < exclusionThreshold;
        }
    }
}
