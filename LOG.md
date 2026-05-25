# LOG — 履歴

> このファイルは**追記オンリー**。完了したPhaseや解決した問題を、新しいものを上に積んでいく。
> 過去の記述は書き換えない（間違っていたこともそのまま記録として残す）。

---

## 完了済みPhase

| Phase | 内容 |
|-------|------|
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
