# tools/

実験運用・デバッグ用の補助スクリプト群。Unity プロジェクトの実行とは独立して、
EMG PC 等の外部 PC で動かす Python スクリプトを置く。

---

## marker_monitor.py

**目的:** Quest 実機の Unity アプリから送出される LSL マーカーをリアルタイムで EMG PC に表示する。

LabRecorder では「マーカーが今届いたか」をリアルタイムに見るのが難しいため、
このスクリプトを別ターミナルで走らせておくと実験中の動作確認が楽になる。

### 事前準備
```
pip install pylsl
```

### 使い方
```
python tools/marker_monitor.py
```

EMG PC のコンソールに以下のような出力が流れる：
```
LSL 'Markers' ストリームを探索中...
接続成功: name='Markers' type='Markers' host='Quest'
======================================================================
wall_clock      lsl_timestamp  marker
----------------------------------------------------------------------
14:23:01.123      12345.678   ExpStart
14:23:02.456      12347.012   PracticeStart
14:23:15.789      12360.345   PracticeEnd
14:23:15.811      12360.367   TaskB_Start
14:23:17.022      12361.578   TrialStart_B_1_Delta300ms
...
```

`Ctrl+C` で終了。

### 前提条件
- Quest 実機で Unity アプリが起動中（`#if UNITY_EDITOR` で Editor 実行時は LSL Outlet が出ない仕様）
- Quest と EMG PC が**同じ WiFi ネットワーク**に接続されている
- Windows ファイアウォール等で LSL 通信がブロックされていない
- LabRecorder と並行実行可能（LSL は購読型のため複数のクライアントが同じストリームを購読できる）

### トラブルシューティング
| 症状 | 対処 |
|------|------|
| `Markers ストリームが見つかりません` | Quest 上で Unity アプリが起動しているか確認。同じ WiFi か確認。ファイアウォール確認 |
| `pylsl がインストールされていません` | `pip install pylsl` を実行 |
| 接続できるが何も流れてこない | Unity 側でステート遷移が発生していないか、`MarkerSenderRouter` が `DebugMarkerSender` 側に切り替わっている可能性。Inspector で `isTestMode = false` を確認 |
