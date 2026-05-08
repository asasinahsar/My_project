using UnityEngine;
using LSL; // LSL4Unityの標準ライブラリを参照

namespace UnityVirtual.LSL
{
    public class LslMarkerSender : MonoBehaviour, IMarkerSender
    {
        private StreamOutlet outlet;
        private string[] sample = new string[1];

        [Header("LSL Stream Settings")]
        public string streamName = "UnityMarkers";
        public string streamType = "Markers";
        public string streamId = "UnityMarkerStream";

        void Start()
        {
            // LSL.LSL.IRREGULAR_RATE が正しくライブラリ側を参照できるようになります
            StreamInfo streamInfo = new StreamInfo(streamName, streamType, 1, global::LSL.LSL.IRREGULAR_RATE, channel_format_t.cf_string, streamId);
            outlet = new StreamOutlet(streamInfo);
            Debug.Log($"[LslMarkerSender] LSL Stream Outlet created: {streamName}");
        }

        public void SendMarker(string marker)
        {
            if (outlet != null)
            {
                sample[0] = marker;
                outlet.push_sample(sample);
            }
        }
    }
}
