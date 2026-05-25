# LOG — 履歴

> このファイルは**追記オンリー**。完了したPhaseや解決した問題を、新しいものを上に積んでいく。
> 過去の記述は書き換えない（間違っていたこともそのまま記録として残す）。

---

## 完了済みPhase

| Phase | 内容 |
|-------|------|
| v5.3 Phase F（2026-05-25） | Task A 時間ベース化。`TaskAController.cs`：`stillnessSpeedThreshold`/`stillnessDuration`/`preMotionWait` SerializeField 削除、`WaitForStillness()` コルーチン削除。新規 SerializeField 追加：`autoMotionStartDelay=5.0f`/`autoMotionDuration=0.5f`/`autoMotionInterval=10.0f`。`TaskAMainRoutine` をブロック先頭5秒待機→試行ループ（屈曲0.5秒→10秒待機）の時間ベース構造に書き換え。`HandVisualizer.cs`：`StartAutoMotion(AutoMotionType, float duration=2.0f)` にシグネチャ拡張。`AutoMotionRoutine` が duration を引数で受け取るよう変更（デフォルト2.0fで TestModeController 互換維持） |
| VRデバッグ対応（2026-05-25） | ①ParticipantHUD / SoAResponseUI に `OnStateChanged` 購読を追加し `TaskB_Main` 中のみ `gameObject.SetActive(true)`（練習・誘導・BlockRest 中の残留表示を修正）。②`ParticipantHUD.HandleFlexionDetected` でローカル屈曲カウンタ（`flexionDetectedCount`）を+1して `屈曲 N/total` 表示更新に変更（旧: ペース合図タイミングで `current-1` 表示していたため最後の屈曲が反映されず 4/5 のまま回答フェーズに入るバグを修正）。③`HandVisualizer.HandleStateChanged` で `TaskB_Main` 以外のステートでは `delayMs=0` に強制リセット（Practice ステート中に前回 delayMs が残るバグを修正）。④`HandSignDetector.cs` を全面書き換え：握りこぶし検出（指先-手首距離平均、閾値0.05m）→ ピンチ（親指+人差し指タッチ、閾値0.025m）に変更。イベント `OnFistDetected` → `OnHandSignDetected`、プロパティ `CurrentAverageDistance` → `CurrentPinchDistance`、メソッド `IsFistDetectedNow()` → `IsHandSignDetectedNow()`。SerializeField `wrist`/`fingerTips[]` → `thumbTip`/`indexTip`。⑤`responseWindowSeconds` を 3 秒 → **5 秒**に変更（TaskBController / SoAResponseUI 両方）。⑥TMP フォント動的化は Unity Editor 側の設定（Atlas Population Mode = Dynamic）で対応予定 |
| v5.3 Phase E3（2026-05-25） | `VASInputUI.cs` → `SoAResponseUI.cs` にリネーム（GUID 保持）。SoA Yes/No ボタンを廃止し、`promptText`/`countdownText`/`fistFeedbackText` の TextMeshPro 表示に変更。`HandSignDetector.OnFistDetected` を購読し「Yes 受付！」フィードバック。3 秒カウントダウンコルーチン。`ParticipantHUD.cs` を新規作成：試行/屈曲進捗表示、円形拍動アニメーション、AudioClip.Create による実行時ビープ音生成（高音=ペース合図、低音=試行開始/回答開始）。TaskBController のイベント（OnTrialStartCue/OnPacingCue/OnFlexionDetected/OnResponseWindowOpened/OnSoAWindowClosed）を購読 |
| v5.3 Phase E2（2026-05-25） | `TaskBController.cs` の `TaskBMainRoutine` を全面書き換え。Onset 待機ループ＋Δt 待機を廃止し、計測フェーズ（ペース化屈曲を `flexionCountPerTrial` 回検出、`pacingInterval` 静止区間で分離）と回答フェーズ（HandSignDetector で握りこぶし検出 = Yes、無反応 = No、`responseWindowSeconds` で時間切れ）に分離。新規 SerializeField（`pacingInterval`/`flexionCountPerTrial`/`responseWindowSeconds`/`handSignDetector`）と新規イベント（`OnTrialStartCue`/`OnPacingCue`/`OnResponseWindowOpened`/`OnFlexionDetected`）追加。新規 LSL マーカー（`FlexionDetected_B`/`ResponseWindowStart_B`/`SoA_Yes_Trial`/`SoA_No_Trial`）。廃止マーカー（`MotionOnset_B`/`SoAResponse_{0,1}`/`SoAResponse_Missed`）。CSV ログに `condition`/`flexion_count`/`response_time` カラム追加、`motion_onset_time` カラム削除。`HandVisualizer.cs` に `FlexionDetectionMode`（VelocityBased / AngleVelocityBased）切替機構を追加 |
| v5.3 Phase E1（2026-05-25） | `Assets/Scripts/Common/HandSignDetector.cs` を新規作成。指先 Transform 配列と手首 Transform の距離平均ベースで握りこぶし（Fist）を検出する独立コンポーネント。`EnableDetection` フラグで回答フェーズのみ有効化、`detectionCooldown` で連続発火防止、`OnFistDetected` イベントを発火、`CurrentAverageDistance` でデバッグ可視化 |
| v5.3 Phase D（2026-05-25） | async/sync を「VHI 誘導の有無」で再定義。`VHIInductionController.cs:64` の `delayMs=500` 設定を `delayMs=0` 固定に変更（旧 v5.2 の async=Δt=500ms 廃止）。`TaskBController.cs` に async/sync の 2 ブロック構造を追加（`currentBlockIndex`/`CurrentCondition`/`HasRemainingBlocks`/`CompleteCurrentBlock`）、QUEST をブロック毎再初期化。`TaskAController.cs` の `CurrentCondition` を `"async"→"sync"` に順序反転。`BlockStart_B_{condition}` / `BlockEnd_B_{condition}` / `BlockStart_A_{condition}` / `BlockEnd_A_{condition}` マーカー追加。`ExperimentManager.AdvanceState` の Practice → `TaskB_Main` 直行、BlockRest 分岐を condition 判定に再編。`GetNextState`/`GetPreviousState` も同期更新。`LSL_MARKERS.md` 更新 |
| LSL マーカー補完 + 監視スクリプト（2026-05-25） | `ExpStart`/`PracticeEnd`/`RestEnd`/`TaskB_Start`/`TaskB_End`/`TaskA_Start`/`TaskA_End`/`AutoMotionStart_A_{condition}_{n}` の 8 種マーカーを追加。`tools/marker_monitor.py`（pylsl 監視スクリプト）と `tools/README.md` を新規作成。`LSL_MARKERS.md` を新規作成し全マーカーをドキュメント化 |
| Phase A.5 補正（2026-05-25） | `ExperimentManager.cs` に `lastStateBeforeBlockRest` を追加し、`ChangeState` で TaskA_Main/TaskB_Main からの BlockRest 流入時のみ記録。`GetPreviousState` の BlockRest 分岐を `lastStateBeforeBlockRest` 参照に変更し、GoBackPhase 経由（TaskA_Induction → BlockRest 等）での経路破壊を防止。`GetNextState` の TaskA_Main → `BlockRest` を `Finished` に変更（Next ボタンで無条件 Finished へ）。Start/SwitchState でメニュー復帰時に lastStateBeforeBlockRest をリセット |
| LSL ファイル整理（2026-05-25） | `Scripts/LSL/` の未使用 3 ファイル（`LslClockSynchronizer.cs` / `LslHealthMonitor.cs` / `EmgLslInletReceiver.cs`）を削除。理由：どこからも参照されておらず、EMG は EMG PC で記録する設計のため Unity 側 Inlet 不要。残置：`IMarkerSender.cs` / `LslMarkerSender.cs` / `DebugMarkerSender.cs` / `MarkerSenderRouter.cs` の 4 ファイル |
| v5.3 Phase B（2026-05-22） | VAS の全廃。`Data/VASRecorder.cs` 削除、`UI/VASInputUI.cs` から VAS 関連 SerializeField/メソッド削除（SoA 部分は Phase E まで残置）、`ExperimentManager.cs` から `EvaluateTaskAVAS` / `EvaluateTaskBVAS` と `taskARetryCount` / `taskBRetryCount` 削除。`TaskAController.MarkCurrentBlockExcluded` は Phase D 再利用候補としてコメント付き残置 |
| v5.3 Phase A.5（2026-05-22） | Consent/TaskA_VASCheck/TaskB_VASCheck の3ステートを削除。ExperimentManager に SkipCurrentPhase()/GoBackPhase()/GetNextState()/GetPreviousState() を追加。VHIInductionController の Induction 完了後遷移を VASCheck → Baseline に短絡。VASInputUI の HandleStateChanged から VASCheck 分岐削除（VAS 本体は Phase B で削除予定）。consentPanel SerializeField 削除 |
| v5.3 Phase A（2026-05-22） | タスク順序を Task B → Task A に反転。ExperimentManager に taskBCompletedFlag / NotifyTaskBCompleted() を追加し、AdvanceState の Practice/BlockRest 分岐と EvaluateTaskBVAS 除外時遷移を反転。TaskBController:176 の完了時 ChangeState(Finished) → NotifyTaskBCompleted() に変更 |
| P3 Task Aフロー | 静止確認（<5cm/s・1秒継続）・10秒待機→屈曲サイクル・onset検出のTask B専用化 |
| P2 人差し指屈曲 | AutoMotionType→IndexFingerFlexion・MCP/PIP/DIP 30°屈曲（LateUpdate差分加算）・Inspectorスロット追加 |
| P1 構造修正 | VirtualLeftHand分離・ApplyDelayedPose座標系修正（world統一）・_skeletonDriverインフラ全削除 |

## 解決済みの問題（〜2026-05-18）

| # | 問題 | 解決 |
|---|------|------|
| 1 | virtualHandWristのworld-position直接代入 | P1-3で解決 |
| 2 | 親指付け根のボーン変形（world position書き込み） | P1-4で解決 |
| 3 | XRHandSkeletonDriverとのUpdate競合 | P1-5（分離）で解決 |
