# CLAUDE.md

## ブランチ
- 作業ブランチ：`copilot_space`
- 常にこのブランチを参照・編集すること


## Role and Language
- 回答はすべて日本語で行う
- 簡潔かつ正確な技術的アドバイスを優先する


## Communication Rules
- 実装開始前に要件が曖昧な場合・設計の選択肢が複数ある場合は**必ず確認をとること**、勝手に進めない
- 指示されたタスクと直接関係のないコード生成・変更は行わない


## Implementation Policy
- 専門用語は省略せず、ドキュメント・既存コードの記載通りに使用する
- コード生成後は「Unityエディタ側で必要な操作」をステップバイステップで提示する
  - 例：Inspectorでのアタッチ、コンポーネント設定、タグ・レイヤー指定、Assetの配置
- 既存コードの変更時は理由がない限り削除しない
- 削除した場合は必ずその理由を明記する
- コードを生成しようとするときは**事前に許可をとる**（何をしようとしているか説明してから）


## Coding Style
- 可読性・保守性を優先する
- 実装選択の理由・メリット・デメリットを簡潔に説明する


## 参照ドキュメント
- 実験全体の概要：`Experiment.md`（リポジトリ内）
- 本ファイル（CLAUDE.md）を常に参照する


## セッション管理
- コンテキスト50%で /compact を実行すること
- 確定した設計決定は都度このファイルに追記すること


## 読み込み禁止ファイル・フォルダ（トークン節約のため必ず除外）
以下は**絶対に読まない**こと。glob や Find でも対象外にする。

- `Assets/Realistic VR Hands/`（配下すべて）
- `Assets/VR Hands FP Arms/`（配下すべて）
- `Assets/XR/`（配下すべて）
- `Library/`、`Temp/`、`Logs/`、`obj/`（配下すべて）
- 拡張子が `.fbx`、`.png`、`.jpg`、`.wav`、`.mp3`、`.asset`、`.meta` のファイル
- `*.dll`、`*.pdb`

読んでよいのは `Assets/Scripts/` 配下の `.cs` ファイル、および `CLAUDE.md`・`Experiment.md`・`README.md` のみ。


## 起動時の読み込み順
新セッション開始時は必ず以下の順に読み込むこと：
1. `CLAUDE.md`（本ファイル）
2. `Experiment.md`
3. `README.md`
4. 作業対象の `.cs` ファイル


## スクリプト構成（Assets/Scripts/）
Common/
ExperimentManager.cs 実験ステート管理
HandVisualizer.cs 実手トラッキング・仮想手制御・自動モーション
TestModeController.cs テストモード制御
VHIInductionController.cs VHI誘導フロー制御
RingBuffer.cs リングバッファ（遅延用）
Data/
VASRecorder.cs VAS記録
LSL/
DebugMarkerSender.cs
EmgLslInletReceiver.cs
IMarkerSender.cs
LslClockSynchronizer.cs
LslHealthMonitor.cs
LslMarkerSender.cs
MarkerSenderRouter.cs
TaskA/
TaskAController.cs Task A フロー制御
TaskB/
TaskBController.cs Task B フロー制御
UI/
ExperimentUI.cs
TaskInstructionUI.cs
VASInputUI.cs



## 確定済み設計決定

### Task A 設計
- **方式：案A（トラッキング同期あり）**
  - XRHandSkeletonDriverによるリアルタイムトラッキングを維持したまま、LateUpdate差分加算方式で人差し指のみ自動屈曲
- **自動屈曲対象：** 左手人差し指 MCP・PIP・DIP（最大30°）
- **AutoMotionType：** `IndexFingerFlexion`
- **静止確認条件：** 速度 < 5 cm/s が **1秒継続** → 10秒待機 → 自動屈曲（2秒）
- **onset検出（速度ベース）：** Task B 専用。Task A 中は無効化

### async条件（コントロール）
- **時間的非同期 Δt = 500ms 固定遅延** を採用
- 空間的オフセット（SetAsyncOffset）は廃止
- 根拠：SoO・SoAをともに有意に崩壊（Shimada et al., 2016）、Task B のリングバッファ遅延機構を流用可能、hypothesis awareness リスクが低い

### Unity Hierarchy 構成
- **actualHandWrist：** 元の `LeftHandAndroidXRVisual`（メッシュ非表示・トラッキング駆動のまま）
- **virtualHandWrist：** 複製した `VirtualLeftHand`（XRHandSkeletonDriver 削除済み）
- 両者は**別オブジェクト**（同一オブジェクトへの閉ループを解消済み）
- `LeftHandQuestVisual` / `RightHandQuestVisual`：削除済み（Android OpenXR ビルドでは不要）

### sEMG
- **チャンネル数：2ch**

### 練習ブロック
- **廃止**（ExperimentState.Practice および関連UI・遷移ロジックは削除予定）

### ブロック間休憩
- **30秒手指運動教示UI＋タイマー**を実装予定（Phase 5）


## 完了済みPhase

| Phase | 内容 |
|-------|------|
| P1 構造修正 | VirtualLeftHand分離・ApplyDelayedPose座標系修正（world統一）・_skeletonDriverインフラ全削除 |
| P2 人差し指屈曲 | AutoMotionType→IndexFingerFlexion・MCP/PIP/DIP 30°屈曲（LateUpdate差分加算）・Inspectorスロット追加 |
| P3 Task Aフロー | 静止確認（<5cm/s・1秒継続）・10秒待機→屈曲サイクル・onset検出のTask B専用化 |


## 残りのTo-Do

### Phase 4：async条件の時間的非同期化
- P4-1：`SetAsyncOffset()` を `HandVisualizer.cs` から削除
- P4-2：`VHIInductionController` でasync時 `delayMs = 500f`、sync時 `0f` を設定
- P4-3：`isAutoMode` ガードの見直し（async時もApplyDelayedPose()が動作するよう調整）

### Phase 5：BlockRest実装
- P5-1：30秒手指運動教示UIの表示
- P5-2：30秒タイマー＋完了後自動遷移（`ExperimentManager.cs`）

### Phase 6：仕様クリーンアップ
- P6-1：Practice関連ステート・UIの削除（`ExperimentManager.cs`）
- P6-2：`Experiment.md` チャンネル数表記を2chに統一
- P6-3：`Experiment.md` 静止確認「2秒」→「1秒」に修正
- P6-4：`Experiment.md` async条件を時間的非同期Δt=500msに書き換え
- P6-5：`Experiment.md` 練習ブロック記述を削除
- P6-6：`README.md` 「既知の問題」セクションを更新（解決済み項目を削除）


## 既知の問題・未解決事項（2026-05-18時点）

| # | 問題 | 状態 |
|---|------|------|
| 1 | virtualHandWristのworld-position直接代入 | ✅ P1-3で解決済み |
| 2 | 親指付け根のボーン変形（world position書き込み） | ✅ P1-4で解決済み |
| 3 | XRHandSkeletonDriverとのUpdate競合 | ✅ P1-5（分離）で解決済み |
| 4 | SetAsyncOffsetのworld Z軸固定問題 | → P4-1で廃止予定 |
| 5 | isAutoModeガードによるApplyDelayedPose無効化 | → P4-3で対処予定 |