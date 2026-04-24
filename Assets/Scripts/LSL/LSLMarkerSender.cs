using UnityEngine;
using LSL; // LSL4Unityの名前空間

public class LSLMarkerSender : MonoBehaviour
{
    private StreamOutlet outlet;
    private string[] sample = new string[1];

    private void Start()
    {
        // 要件定義に沿ったストリーム情報の初期化
        // name: "UnityMarkers", type: "Markers", channel_count: 1, sample_rate: 0 (不定期), format: cf_string
        StreamInfo streamInfo = new StreamInfo("UnityMarkers", "Markers", 1, 0, channel_format_t.cf_string, "UnityMarkerSenderID");
        outlet = new StreamOutlet(streamInfo);
        
        Debug.Log("[LSLMarkerSender] LSL Outlet created: UnityMarkers");
    }

    /// <summary>
    /// LSLマーカー（文字列）をネットワーク上に送出します
    /// </summary>
    /// <param name="markerString">送出する文字列（例："ExpStart", "TrialStart_A_sync_1"）</param>
    public void SendMarker(string markerString)
    {
        if (outlet != null)
        {
            sample[0] = markerString;
            outlet.push_sample(sample);
            Debug.Log($"[LSL Marker] {markerString} (Time: {Time.realtimeSinceStartup:F3})");
        }
        else
        {
            Debug.LogWarning($"[LSL Marker Failed] LSL Outlet is not initialized. Missed marker: {markerString}");
        }
    }
}