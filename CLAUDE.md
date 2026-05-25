# CLAUDE.md

このファイルは **AI（Claude Code）向けのルールと指示** だけを記載する。
進捗・履歴・確定仕様はここに書かない（後述の役割分担を参照）。

---

## ファイルの役割分担（最重要）

新しい `.md` ファイルは作らない（既に作成済みの `LSL_MARKERS.md` / `tools/README.md` は例外）。情報は必ず以下のいずれかに入れる。

| ファイル | 役割 | 更新の仕方 |
|---------|------|-----------|
| `CLAUDE.md`（本ファイル） | AI向けのルール・読み込み指示 | ルールが変わったときだけ |
| `Experiment.md` | 実験計画・確定した仕様 | 仕様が確定・変更されたとき |
| `STATUS.md` | 現在地（残りTo-Do・未解決事項） | 作業の区切りで**上書き**して最新化 |
| `LOG.md` | 履歴（完了Phase・解決済み問題） | 完了するたびに**追記**（過去は消さない） |
| `README.md` | 人間向けの概要・Hierarchy構成等 | 構成が変わったとき |
| `LSL_MARKERS.md` | LSL マーカー一覧（解析時の参照） | マーカー追加・変更時に必ず更新 |
| `tools/README.md` | tools/ 配下スクリプトの使い方 | tools/ 配下に追加・変更があったとき |

迷ったときの判断基準：
- 不変の事実・仕様 → `Experiment.md`
- これからやること・今困っていること → `STATUS.md`
- 終わったこと → `LOG.md`

---

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

---

## セッション管理
- コンテキスト50%で /compact を実行すること
- **確定した設計決定** → `Experiment.md` に反映する
- **完了したPhase・解決した問題** → `LOG.md` に追記する
- **作業の区切り** → `STATUS.md` を最新状態に上書きする
- このファイル（CLAUDE.md）には進捗を書かない

## 起動時の読み込み順
新セッション開始時は必ず以下の順に読み込むこと：
1. `CLAUDE.md`（本ファイル＝ルール）
2. `STATUS.md`（現在地・今やること）
3. `Experiment.md`（実験計画・確定仕様）
4. 必要に応じて `LOG.md`（過去の経緯を確認したいとき）
5. 作業対象の `.cs` ファイル

## 読み込み禁止ファイル・フォルダ（トークン節約のため必ず除外）
以下は**絶対に読まない**こと。glob や Find でも対象外にする。

- `Assets/Realistic VR Hands/`（配下すべて）
- `Assets/VR Hands FP Arms/`（配下すべて）
- `Assets/XR/`（配下すべて）
- `Library/`、`Temp/`、`Logs/`、`obj/`（配下すべて）
- 拡張子が `.fbx`、`.png`、`.jpg`、`.wav`、`.mp3`、`.asset`、`.meta` のファイル
- `*.dll`、`*.pdb`

読んでよいのは `Assets/Scripts/` 配下の `.cs` ファイル、および
`CLAUDE.md`・`Experiment.md`・`STATUS.md`・`LOG.md`・`README.md` のみ。

---

## スクリプト構成（Assets/Scripts/）
```
Common/
  ExperimentManager.cs     実験ステート管理
  HandVisualizer.cs        実手トラッキング・仮想手制御・自動モーション
  HandSignDetector.cs       左手ハンドサイン（ピンチ：親指+人差し指タッチ）検出
  TestModeController.cs     テストモード制御
  VHIInductionController.cs VHI誘導フロー制御
  RingBuffer.cs            リングバッファ（遅延用）
LSL/
  IMarkerSender.cs         マーカー送信インターフェース
  LslMarkerSender.cs       本番用：LSL Outlet 経由でマーカー送信（実機ビルド時のみ）
  DebugMarkerSender.cs     テスト用：Console にログ出力
  MarkerSenderRouter.cs    本番/テストの切替ルーター
TaskA/
  TaskAController.cs       Task A フロー制御
TaskB/
  TaskBController.cs       Task B フロー制御
UI/
  ExperimentUI.cs          実験者向け UI（ステート表示・キーボード Y/N 入力）
  TaskInstructionUI.cs     タスク指示テキスト表示
  SoAResponseUI.cs         Task B 回答フェーズ UI（ハンドサイン応答 + 残り時間）
  ParticipantHUD.cs        被験者向け HUD（試行/屈曲進捗、ペース合図、ビープ音）
```