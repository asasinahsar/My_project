using UnityEngine;
using UnityVirtual.LSL;

namespace UnityVirtual.Task3
{
    public class PpsMarkerEmitter : MonoBehaviour
    {
        [SerializeField] private LslEventMarkerPublisher publisher;

        public void EmitApproachStart(float distanceMeters)
        {
            if (publisher == null) return;
            publisher.Publish($"PPS_APPROACH_START:d={distanceMeters:F3}");
        }
    }
}
