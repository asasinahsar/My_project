using UnityEngine;
using LSL; // LSL4Unityの標準ネームスペース

public class LSLMarkerSender : MonoBehaviour
{
    private StreamOutlet outlet;
    private string[] sample = new string[1];

    [Header("LSL Stream Settings")]
    public string streamName = "UnityMarkers";
    public string streamType = "Markers";
    public string streamId = "UnityMarkerStream_1";

    void Start()
    {
        // LSLのStreamInfoを定義（1チャンネル、不定期送信、文字列型）
        StreamInfo streamInfo = new StreamInfo(streamName, streamType, 1, LSL.LSL.IRREGULAR_RATE, channel_format_t.cf_string, streamId);
        
        // ネットワーク上にストリームを公開
        outlet = new StreamOutlet(streamInfo);
        Debug.Log($"[LSL] Marker stream '{streamName}' created and ready.");
    }

    /// <summary>
    /// 他のコントローラーから呼ばれるマーカー送信用のメソッド
    /// </summary>
    /// <param name="marker">送信する文字列（例："InductionStart_A_async"）</param>
    public void SendMarker(string marker)
    {
        if (outlet != null)
        {
            sample[0] = marker;
            outlet.push_sample(sample);
            // デバッグログが多すぎる場合は以下の行をコメントアウトしてください
            // Debug.Log($"[LSL Marker Sent] {marker}");
        }
        else
        {
            Debug.LogWarning("[LSL] Failed to send marker. StreamOutlet is not initialized.");
        }
    }
}