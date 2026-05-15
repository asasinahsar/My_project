using UnityEngine;
using LSL;

namespace UnityVirtual.LSL
{
    public class LslMarkerSender : MonoBehaviour, IMarkerSender
    {
        #pragma warning disable CS0414
        [SerializeField] private string streamName = "Markers";
        [SerializeField] private string streamType = "Markers";
        #pragma warning restore CS0414

        private StreamOutlet outlet;
        private string[] sample = new string[1];

        private void Start()
        {
#if UNITY_EDITOR
            Debug.Log("[LslMarkerSender] Editorモード: LSL初期化をスキップします。");
#else
            var streamInfo = new StreamInfo(
                streamName,
                streamType,
                1,
                0,
                channel_format_t.cf_string,
                UnityEngine.SystemInfo.deviceUniqueIdentifier  // ★ System → UnityEngine に修正
            );
            outlet = new StreamOutlet(streamInfo);
#endif
        }

        public void SendMarker(string marker)
        {
#if UNITY_EDITOR
            Debug.Log($"[LslMarkerSender] (Editor) Marker: {marker}");
#else
            if (outlet == null) return;
            sample[0] = marker;
            outlet.push_sample(sample);
#endif
        }

        private void OnDestroy()
        {
#if !UNITY_EDITOR
            outlet?.Dispose();
#endif
        }
    }
}