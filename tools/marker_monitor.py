"""
marker_monitor.py — LSL Markers ストリームをリアルタイム監視する。

Quest 実機で動作中の Unity アプリから送信される LSL マーカーを
EMG PC のコンソールに表示する。実験中の動作確認・デバッグ用途。

事前準備:
    pip install pylsl

使い方:
    python tools/marker_monitor.py

終了:
    Ctrl+C

備考:
    LabRecorder と並行実行可能（LSL は購読型のため競合しない）。
"""
from datetime import datetime

try:
    from pylsl import StreamInlet, resolve_byprop
except ImportError:
    print("ERROR: pylsl がインストールされていません。`pip install pylsl` を実行してください。")
    raise SystemExit(1)


TIMEOUT_SECONDS = 5.0


def main() -> None:
    print("LSL 'Markers' ストリームを探索中...")
    streams = resolve_byprop("type", "Markers", timeout=TIMEOUT_SECONDS)
    if not streams:
        print(
            "Markers ストリームが見つかりません。\n"
            " - Quest 上で Unity アプリが起動しているか\n"
            " - Quest と EMG PC が同じ WiFi に接続されているか\n"
            " - ファイアウォールで LSL の通信がブロックされていないか\n"
            "を確認してください。"
        )
        return

    inlet = StreamInlet(streams[0])
    info = streams[0]
    print(f"接続成功: name='{info.name()}' type='{info.type()}' host='{info.hostname()}'")
    print("=" * 70)
    print(f"{'wall_clock':<14} {'lsl_timestamp':>14}  marker")
    print("-" * 70)

    try:
        while True:
            sample, lsl_ts = inlet.pull_sample()
            now = datetime.now().strftime("%H:%M:%S.%f")[:-3]
            marker = sample[0] if sample else ""
            print(f"{now:<14} {lsl_ts:>14.3f}  {marker}")
    except KeyboardInterrupt:
        print("\n終了しました。")


if __name__ == "__main__":
    main()
