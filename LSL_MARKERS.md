# LSL_MARKERS.md — LSL マーカー一覧

> Unity アプリ（Quest 実機）から LSL ストリーム `Markers` に送出される全マーカーの仕様。
> EMG とのオフライン同期解析時の参照リスト。**新しいマーカーを追加・変更したら必ずこのファイルを更新する。**

**最終更新：2026-05-25（Phase D / Phase E2 反映）**
**ストリーム名:** `Markers` / **タイプ:** `Markers` / **channel format:** `string` / **チャンネル数:** 1

---

## 1. 実験全体の境界

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `ExpStart` | `StartExperiment()` 呼び出し時（実験本体開始） | ExperimentManager |
| `ExpEnd` | Finished ステート到達時 | ExperimentManager |

---

## 2. ステート遷移マーカー

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `PracticeStart` | Practice ステート進入時 | ExperimentManager |
| `PracticeEnd` | Practice ステートから離脱時 | ExperimentManager |
| `RestStart` | BlockRest ステート進入時 | ExperimentManager |
| `RestEnd` | BlockRest ステートから離脱時 | ExperimentManager |
| `PhaseSkipped_{state}` | 各パネルの「次へ」ボタン押下時 | ExperimentManager |
| `PhaseBack_{state}` | 各パネルの「戻る」ボタン押下時 | ExperimentManager |

---

## 3. VHI 誘導フロー

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `InductionStart_A_{condition}` | Task A 誘導開始（筆なぞり開始） | VHIInductionController |
| `InductionEnd_A_{condition}` | Task A 誘導 Phase 1 終了 | VHIInductionController |
| `InductionStart_B` | Task B 誘導開始（筆なぞり開始） | VHIInductionController |
| `ActiveMovementStart_B` | Task B 慣らし運動開始（Phase 2） | VHIInductionController |
| `ActiveMovementEnd_B` | Task B 慣らし運動終了 | VHIInductionController |
| `InductionEnd_B` | Task B 誘導終了 | VHIInductionController |
| `BaselineStart_A_{condition}` | Task A 安静ベースライン計測開始（30秒） | VHIInductionController |
| `BaselineEnd_A_{condition}` | Task A 安静ベースライン計測終了 | VHIInductionController |
| `BaselineStart_B` | Task B 安静ベースライン計測開始（30秒） | VHIInductionController |
| `BaselineEnd_B` | Task B 安静ベースライン計測終了 | VHIInductionController |

`{condition}` には `sync` または `async` が入る（Phase D 実装後）。

---

## 4. Task B（SoA 計測）

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `TaskB_Start` | Task B 全体の計測開始（最初のブロック開始時） | TaskBController |
| `TaskB_End` | Task B 全体の計測終了（最終ブロック完了時） | TaskBController |
| `BlockStart_B_{condition}` | Task B の async/sync 各ブロック開始（55 試行単位） | TaskBController |
| `BlockEnd_B_{condition}` | Task B の async/sync 各ブロック終了 | TaskBController |
| `TrialStart_B_{n}_Delta{ms}ms` | 各試行の開始（n=試行番号、ms=Δt） | TaskBController |
| `FlexionDetected_B_{n}_count{i}` | ペース化屈曲の各回検出（i=1〜flexionCountPerTrial） | TaskBController |
| `ResponseWindowStart_B_{n}` | 回答フェーズ開始（合図表示） | TaskBController |
| `SoA_Yes_Trial{n}_Dt{ms}ms` | 左手ピンチ（親指+人差し指タッチ）検出による Yes 応答 | TaskBController |
| `SoA_No_Trial{n}_Dt{ms}ms` | 無反応 No 応答（時間切れ） | TaskBController |
| `TrialEnd_B_{n}` | 各試行の終了 | TaskBController |

### Phase E2 で廃止されたマーカー
- `MotionOnset_B_Delta{ms}ms`：Onset 検出・Δt 待機方式の廃止に伴い廃止
- `SoAResponse_{0 or 1}`：`SoA_Yes_Trial{n}_Dt{ms}ms` / `SoA_No_Trial{n}_Dt{ms}ms` に置換
- `SoAResponse_Missed`：無反応は `SoA_No_*` に統合

---

## 5. Task A（SoO 計測）

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `TaskA_Start` | Task A 全体の計測開始（最初のブロック開始時） | TaskAController |
| `TaskA_End` | Task A 全体の計測終了（最終ブロック完了時） | TaskAController |
| `BlockStart_A_{condition}` | Task A の async/sync 各ブロック開始（20 試行単位） | TaskAController |
| `BlockEnd_A_{condition}` | Task A の async/sync 各ブロック終了 | TaskAController |
| `TrialStart_A_{condition}_{n}` | 各試行の開始 | TaskAController |
| `AutoMotionStart_A_{condition}_{n}` | 試行内の自動屈曲開始時刻（motor overflow 解析の t=0 基準） | TaskAController |
| `TrialEnd_A_{condition}_{n}` | 各試行の終了 | TaskAController |

加えて `HandVisualizer.OnMarkerRequested` 経由でモーション関連の追加マーカーが送出される場合がある（実装時に追記）。

---

## 6. テストモード時の挙動

`MarkerSenderRouter.isTestMode = true` のとき、`DebugMarkerSender` がアクティブになり、
LSL Outlet には送出されず **Unity Console に `[DebugMarkerSender - TEST MODE] Marker: {marker}` 形式でログ出力のみ**となる。

本番計測時は必ず `isTestMode = false` であることを Inspector で確認する。

---

## 7. リアルタイム監視

`tools/marker_monitor.py`（EMG PC で実行）で全マーカーをリアルタイム表示可能。
詳細は [tools/README.md](tools/README.md) 参照。

---

## 8. オフライン解析時の使い方（典型例）

| 解析目的 | 必要なマーカー対 |
|---------|----------------|
| EMG ベースライン抽出 | `BaselineStart_*` 〜 `BaselineEnd_*` |
| Task A motor overflow（t=0〜100ms） | `AutoMotionStart_A_{condition}_{n}` 基準 |
| Task B 運動準備 EMG（t=-500〜0ms） | `FlexionDetected_B_{n}_count{i}` 基準（Phase E 後）／現状は `MotionOnset_B_*` |
| sync − async 差分 | `BlockStart_*_sync` / `BlockStart_*_async` でセグメント切り出し（Phase D 後） |
| 練習試行の除外 | `PracticeStart` 〜 `PracticeEnd` 区間を除外 |
| 休憩区間の除外 | `RestStart` 〜 `RestEnd` 区間を除外 |
| 誤操作の検出 | `PhaseSkipped_*` / `PhaseBack_*` が含まれる試行を要確認 |
