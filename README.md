# VHI Experiment — Unity Project

**バーチャルハンドイリュージョンを用いた自己感覚の計算論的定量化**  
EMG motor overflow・筋シナジーによる SoO/SoA の客観的測定

**所属：** 東京科学大学 小池研究室  
**期間：** 2026年5月〜6月（6週間）  
**ブランチ：** `copilot_space`

---

## 研究概要

統合失調症の中核症状である SoA（主体感）・SoO（身体所有感）障害を、従来の主観的アンケートに依らず **sEMG の不随意反応（motor overflow）** で客観的に定量化する。Meta Quest スタンドアロン環境で VHI（Virtual Hand Illusion）を誘導し、2ch sEMG × LSL 同期によりバイオマーカーを計測する。

---

## システム構成

Meta Quest（スタンドアロン、PC不要）
├─ 左手ハンドトラッキング → Task A 自動運動 / Task B 遅延ミラーリング
├─ 右手コントローラー → UI操作（ボタンのみ、レイキャスト不使用）
└─ LSL受信（Wi-Fi） → sEMG同期マーカー記録

sEMG アンプ（左前腕 2ch, 1000Hz+）
└─ LSL送信（Wi-Fi）→ Meta Quest


---

## 動作モード

| モード | LSL | 用途 |
|-------|-----|------|
| **本番モード** | あり | 完全実験フロー・全データ記録 |
| **テストモード** | なし（デバッグログ） | 動作確認専用・短縮フロー |

アプリ起動時の選択画面で切り替える。

---

## スクリプト構成

Assets/Scripts/
├─ Core/
│ ├─ ExperimentManager.cs # ステート管理（Idle→TaskA→TaskB→Finished）
│ └─ HandVisualizer.cs # 実手トラッキング・仮想手制御・自動モーション
├─ Tasks/
│ ├─ TaskAController.cs # Task A（受動運動：人差し指自動屈曲）
│ ├─ TaskBController.cs # Task B（能動運動：QUEST法＋Δt遅延）
│ └─ VHIInductionController.cs # VHI誘導（筆なぞり、sync/async切り替え）
├─ LSL/
│ ├─ IMarkerSender.cs # マーカー送出インターフェース
│ ├─ LslMarkerSender.cs # LSL送出（本番モード）
│ ├─ DebugMarkerSender.cs # デバッグログ出力（テストモード）
│ ├─ MarkerSenderRouter.cs # 本番/テスト切り替えルーター
│ └─ EmgLslInletReceiver.cs # sEMG LSLストリーム受信
├─ UI/
│ ├─ ExperimentUI.cs # 実験UI管理
│ ├─ VASInputUI.cs # VAS入力（スティック操作）
│ └─ TaskInstructionUI.cs # タスク教示画面
└─ Utilities/
└─ RingBuffer.cs # 手のポーズ記録バッファ（Task B遅延用）


---

## 実験タスク

### Task A｜SoO定量化（motor overflow）

- **内容：** VHI成立後、左手人差し指 MCP・PIP・DIP を最大30°自動屈曲
- **試行フロー：** 手の静止確認（速度 < 5cm/s、2秒継続）→ 10秒待機 → 自動屈曲（1秒）
- **ブロック：** sync 20試行 / async 20試行（計40試行）
- **主要指標：** MOA（motor overflow amplitude）・MOL（latency）・NSS（筋シナジー類似度）

### Task B｜SoA定量化（遅延ミラーリング）

- **内容：** 被験者の随意左手首伸展にΔt遅延を挿入してバーチャルハンドを描画
- **応答：** Aボタン（SoA崩壊 = Yes）/ Bボタン（SoA維持 = No）でQUEST法を駆動
- **試行数：** QUEST 35試行 + 固定Δt確認 20試行（計55試行）
- **主要指標：** τ_SoA（崩壊閾値）・PRI（運動準備一貫性）・SCR（シナジー変容率）

---

## 操作系

| ボタン | 機能 |
|--------|------|
| 右スティック上下 | 選択移動・VAS値変更 |
| Aボタン / 右トリガー | 決定・次へ / Task B「SoA崩壊」 |
| Bボタン | 戻る / Task B「SoA維持」 |

- 左手 Interactor（Poke / Near-Far）は**実験全体を通じて無効化**
- 右手はコントローラーモデルのみ表示（ハンドトラッキングモデル非表示）
- LineVisual（ビーム）は全て非表示

---

## Unity セットアップ

### 必要パッケージ

- XR Interaction Toolkit 3.x
- XR Hands
- Meta XR SDK（OpenXR）
- LSL4Unity（Lab Streaming Layer）

### ビルド設定

- **Platform：** Android
- **Target Device：** Meta Quest 3 / 3S
- **Scripting Backend：** IL2CPP
- **Target Architecture：** ARM64

### Hierarchy 構成（関連部分）

XR
Complete XR Origin Set Up Hands Variant
Camera Offset
Left Hand ← Interactor無効化済み
Right Hand ← UI操作用Interactor
Right Controller ← コントローラーモデル
LeftHandAndroidXRVisual ← 左手メッシュ（使用中）
LeftHand ← XRHandSkeletonDriver 配置場所
LeftHandQuestVisual ← 非アクティブ（Quest専用）
RightHandAndroidXRVisual ← 非アクティブ（コントローラー表示のため）
Hand Visualizer ← 非アクティブ（重複メッシュのため）
VHI_Manager
HandVisualizer（スクリプト） ← actualHandWrist / virtualHandWrist アサイン


---

## LSL マーカー仕様

| マーカー文字列 | タイミング |
|-------------|----------|
| `MotionOnset_A_CompoundMotion` | Task A 自動運動開始直前（`_skeletonDriver.enabled=false` 直後） |
| `SoA_Yes_Trial{n}_Dt{delta_t}ms` | Task B「SoA崩壊」申告時 |
| `SoA_No_Trial{n}_Dt{delta_t}ms` | Task B「SoA維持」申告時 |
| `Baseline_Start` | ベースラインEMG記録開始 |
| `Block_Start_{condition}` | ブロック開始（sync / async） |

---

## 既知の問題・未解決事項

| 問題 | 状況 | 推定原因 |
|------|------|---------|
| `virtualHandWrist` の正しいアサイン先が未確定 | 調査中 | `ApplyDelayedPose()` の world/local 座標系ミスマッチ |
| 人差し指ボーン一覧未確認 | `LeftHand` 配下を要確認 | Task A 実装の前提条件 |

---

## 開発ルール

- コード生成前に**必ず実装内容を説明し許可を取る**
- 既存コードの削除は理由を明記する
- コード変更後は **Unity エディタ側の操作手順**をステップで提示する
- `Debug.Log` は `[ClassName]` プレフィックス必須
- 参照ドキュメント：`Experiment.md`（実験計画書）、`CLAUDE.md`（開発規約）

---

## ライセンス

東京科学大学 小池研究室 内部プロジェクト。外部公開不可。