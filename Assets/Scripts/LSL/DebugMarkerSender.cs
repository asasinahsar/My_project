using UnityEngine;

namespace LSL
{
    public class DebugMarkerSender : MonoBehaviour, IMarkerSender
    {
        public void SendMarker(string marker)
        {
            // LSL通信を行わず、UnityのConsoleにマーカーを出力する
            Debug.Log($"<color=cyan>[DebugMarkerSender - TEST MODE]</color> Marker: {marker}");
        }
    }
}