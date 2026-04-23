using UnityEngine;

namespace UnityVirtual.LSL
{
    public class LslHealthMonitor : MonoBehaviour
    {
        [SerializeField] private bool isInletConnected;
        [SerializeField] private bool isOutletConnected;

        public bool IsHealthy => isInletConnected && isOutletConnected;

        public void SetInletStatus(bool connected) => isInletConnected = connected;
        public void SetOutletStatus(bool connected) => isOutletConnected = connected;
    }
}
