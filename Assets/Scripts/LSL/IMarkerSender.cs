namespace LSL
{
    // マーカー送信処理を抽象化するためのインターフェース
    public interface IMarkerSender
    {
        void SendMarker(string marker);
    }
}