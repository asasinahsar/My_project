# VHI実験システム (Task A: SoO計測 & Task B: SoA計測)

本プロジェクトは、Meta Quest 3を用いたハンドトラッキングVR環境において、バーチャルハンドイリュージョン（VHI）を誘発し、身体所有感（SoO）と主体感（SoA）を客観的・定量的に計測するための実験システムです。

## 1. 実験概要

### Task A：身体所有感 (Sense of Ownership) の計測
- **目的**: 視覚フィードバック（自動動作）に対する SoO を、EMG motor overflow 等を用いて客観的に測定する。
- **条件**: 
  - `sync`: 仮想手を実際の手の位置に表示。
  - `async`: 仮想手を実際の手から2cm遠位（Z方向）にオフセットして表示。
- **動作**: 4種類のプロシージャルアニメーション（手首伸展/屈曲、回内/回外）をランダムに再生。

### Task B：主体感 (Sense of Agency) の計測
- **目的**: 随意運動に対する映像遅延（Δt）を挿入し、QUEST法（適応的階段法）を用いて SoA の減衰閾値を推定する。
- **計測**: 35試行のQUEST推定 + 20試行の固定遅延確認試行（計55試行）。
- **トリガー**: ハンドトラッキングの速度ベースによる運動開始（Kinematic Onset）検知。

---

## 2. 動作環境

- **Unity**: Unity 6 (6000.4.3f1)
- **Render Pipeline**: Universal Render Pipeline (URP)
- **XR SDK**: Meta XR SDK (Oculus Integration)
- **Communication**: Lab Streaming Layer (LSL) / `LSL4Unity` 使用
- **Hardware**: Meta Quest 3 (PC接続 Link/AirLink 推奨)
- **Input**: ハンドトラッキング（コントローラー不要）

---

## 3. シーン構成とオブジェクト階層

`MainExperiment` シーンでは、以下の構造に従ってオブジェクトを配置します。

### [Systems]
- `ExperimentManager`: 実験全体のステートマシン管理。
- `VHIInductionController`: 各タスク前の誘導フェーズ（タイマー）管理。

### [XR]
- `XR Origin (XR Rig)`: Quest 3 のカメラおよびトラッキング基盤。
- `LeftVirtualHandRoot`: 被験者に見せる仮想手。`HandVisualizer.cs` をアタッチ。
  - **制約**: 左手は実験対象のため、UI操作コンポーネント（Ray/Poke）を無効化すること。

### [LSL]
- `LSLMarkerSender`: 実験イベント（TrialStart, Onset等）をLSL経由で外部へ送信。

### [TaskA_SoO] / [TaskB_SoA]
- 各タスクの試行シーケンスを制御する `TaskAController` および `TaskBController` を配置。

### [UI] (2系統分離)
- `ExperimentUI`: **実験者用 (PC画面)**。`Screen Space - Overlay`。進行管理とデバッグ用。
- `VASInputUI`: **被験者用 (VR内)**。`World Space Canvas`。右手（非実験手）でのみ操作。

### [Data]
- `VASRecorder`: VAS回答値に特化したCSV記録。

---

## 4. スクリプトの役割 (Obsidian Map)

| ファイル名 | フォルダ | 役割 |
|:---|:---|:---|
| `ExperimentManager.cs` | Common | 実験全体の進行（State）を一元管理 |
| `HandVisualizer.cs` | Common | 仮想手の描画、遅延適用、運動検知、自動動作 |
| `RingBuffer.cs` | Common | GCフリーの姿勢データバッファ（VRのカクつき防止） |
| `VHIInductionController.cs` | Common | VHI誘導（筆なぞり等）の時間制御 |
| `TaskAController.cs` | TaskA | Task A（SoO）のブロック・試行制御 |
| `TaskBController.cs` | TaskB | Task B（SoA）のQUEST法エンジンと試行制御 |
| `LSLMarkerSender.cs` | LSL | LSLストリームへのマーカー文字列送出 |
| `VASInputUI.cs` | UI | VR空間内のWorld Space Canvas制御 |
| `ExperimentUI.cs` | UI | PCミラー画面上の管理UI・キーボード入力受付 |
| `VASRecorder.cs` | Data | VAS値の独立CSVロギング |

---

## 5. セットアップと実行手順

1. **手の関節紐付け**: 
   `HandVisualizer` の `Actual Joints` と `Virtual Joints` 配列に、トラッキング対象と仮想手の関節を**同じ順番で**ドラッグ&ドロップします。
2. **UI操作制限**: 
   左手プレハブから `XR Ray Interactor` 等を削除し、被験者が左手でUIに触れられないようにします。右手にのみ操作権限を与えます。
3. **LSLの準備**: 
   外部の筋電図記録ソフト等で "UnityMarkers" ストリームが受信可能な状態にします。
4. **データの保存先**: 
   実験データ（CSV）は以下に保存されます：  
   `Application.persistentDataPath/SessionData/yyyyMMdd/`

---

## 6. 注意事項

- 実験中は **右手のみ** でUIを操作してください。
- Task B の Onset 検知感度は `HandVisualizer` の `Velocity Threshold` で調整可能です。
- VASの値が 3 未満の場合、システムは自動的に再誘導またはブロック除外の判定を行います。
