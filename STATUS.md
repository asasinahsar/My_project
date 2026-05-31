# STATUS — 現在地

> このファイルは「今どこにいるか」だけを書く。作業の区切りで**まるごと上書き**して最新に保つ。
> 終わったことは消して `LOG.md` に移す。確定した仕様は `Experiment.md` に書く。

**最終更新：2026-05-29（EMG 3ch確定・指示文改訂・筆なぞりアニメ実装 完了）**

---

## 直近の Unity 側必須作業（コード実装に伴うアタッチ）

| 作業 | 対象 | 内容 |
|------|------|------|
| TaskInstructionUI を新規 GameObject に作成しアタッチ | ExperimentCanvas配下 | `taskAInstructionText`=TaskA_Panel/Text(TMP)、`taskBInstructionText`=TaskB_Panel/Text(TMP) を配線。TaskA_Manager/TaskB_Manager の `taskInstructionUI` にも参照設定 |
| BrushStrokeAnimator を GameObject に作成しアタッチ | 任意（手の近く推奨） | `brush`=既存筆モデル、`strokeStart`/`strokeEnd`=バーチャル左手ボーン配下に配置（手の甲：手首→指先）。VHIInductionController の `brushStrokeAnimator` に参照設定 |
| EMG 3ch 装着（FDS/FCR/EDC） | 実機 | Experiment.md §4.2 の電極配置表に従う |
| SoAResponse_Panel の Image 無効化 | UI | 文字だけ表示・背景非表示 |
| 白い丸（Sphere Interaction Caster）の可視化無効化 | XR Rig | 該当 Interactor の caster 可視化/Reticle を無効化 |

---

---

## 解析パイプライン（tools/）

LSL記録の `.xdf` を解析する Python ツールが `tools/` に揃った（詳細は `tools/README.md`）。

| スクリプト | 役割 |
|-----------|------|
| `marker_monitor.py` | 実験中の LSL マーカーをリアルタイム表示（EMG PC で実行） |
| `plot_emg_markers.py` | xdf の EMG 波形 + マーカーを重ねてプロット（目視確認） |
| `emg_preprocess_epochs.py` | 前処理(bandpass→整流→RMS)→エポック切り出し→.npz 保存 |

### 次の解析段階（段階2・未着手）
- **指標算出**: TaskA の MOA（motor overflow amplitude = sync−async の t=0〜100ms RMS）、MOL、TaskB の PRI / τ_SoA（マハラノビス距離）
- **NMF 筋シナジー解析**: ただし**現状 EMG が実質 1ch のため 2ch 以上が前提の NMF は実施不可**。次回記録で 2ch 確保が必要

### 本計測時の運用上の注意（段階1で判明）
- **記録は必ずブロック先頭から開始**（途中記録だと `BlockStart_*` を取りこぼし condition=unknown になる）
- **Delsys センサーを 2ch 装着・有効化**（今回 ch1 のみ信号、2ch目欠落）
- **TaskA 中の安静維持を徹底**（今回 TaskA 後半に大きな EMG 活動＝被験者が動いた可能性）

---

## ⚠️ コントローラ重複アタッチ（記録上は解決済みと推測）

以前「`Trial 1/20` と `Trial 1/21` 並列発火」で TaskAController 重複が疑われたが、
2026-05-29 記録の xdf マーカーは **TaskA が 21 系列のみ**（`/20` 混在なし）だったため、
**重複は削除済みと判断**。新規にシーンを編集した際は念のため `t:TaskAController` /
`t:TaskBController` で 1 つずつか再確認すること。

---

## 新実験順序（D-1 で確定）

```
Practice (TaskB練習, 3試行, 解析対象外)
  ↓
TaskB(async) 23試行
  ↓ BlockRest (30秒)
TaskA(async) 21試行
  ↓ BlockRest (30秒)
TaskB_Induction → TaskB_Baseline → TaskB(sync) 23試行
  ↓ BlockRest (30秒)
TaskA_Induction → TaskA_Baseline → TaskA(sync) 21試行
  ↓
Finished
```

async群と sync群が混在しない構造。VHI誘導は sync 群のみで TaskB/TaskA それぞれ向けに実施。

---

---

## 全体方針

`Experiment.md` は v5.3 に更新済み（仕様の正）。Phase A〜G の実装と VRテスト後の修正（B-1〜B-4、A-1、A-2）が完了。

---

## 進行中・次にやること

### VHI誘導 Phase 1（筆なぞり）画面の作成【優先度: 中】
- 実験者が筆で被験者の左手をなぞる
- Unity 側でバーチャルハンドにも同期して「なぞられる」映像を提示
- 被験者は静止
- 1分間
- Experiment.md §5 (Task B 向け誘導) Phase 1 仕様に基づく

### B-4 検証：ピンチ → No Response問題の原因究明
- VR テストで以下のログを確認：
  - `[HandSignDetector] ピンチ検出！ (距離: X.Xcm)` が出るか
  - `[TaskBController] OnHandSignDetectedHandler called` が出るか
  - `[TaskBController] SubmitSoAResponse(1) called. Previous currentSoAResponse=...` が出るか
- `SubmitSoAResponse(0)` が予期せず呼ばれていないか確認

### Phase C：操作系の左手移行・右手廃止【優先度: 低／実験完成に近づいてから】
- `ExperimentUI.cs`：Aボタン/Bボタン応答の置換は Phase E で実施。キーボード Y/N はテスト用に残置
- `bool testModeUIControl` フラグでテスト用操作の有効/無効を切替可能に
- Unity エディタ側で右手モデル非表示が必要（手順を別途提示）

---

## 未完了の Unity 側作業

| 作業 | 対象 | 備考 |
|------|------|------|
| 左手 Interactor（Poke / Near-Far / Direct）を無効化 | XR Rig の左手 GameObject | |
| TMP フォント: `NotoSansJP-VariableFont_wght SDF.asset` の Atlas Population Mode を Dynamic に | Font Asset | |
| **`HandVisualizer` の Inspector で中指/薬指の MCP/PIP/DIP ボーンをアタッチ** | HandVisualizer | A-2 追加分。`middleMCP/PIP/DIP`, `ringMCP/PIP/DIP` |
| **`TaskAHUD` GameObject を Hierarchy に追加（ExperimentCanvas 直下）** | TaskAHUD | A-4 リライト後。`milestoneText`（TextMeshProUGUI）と `audioSource`（AudioSource）をアタッチ。普段空白でマイルストーン時のみ表示。テキストは中央寄せ・大きめフォント推奨 |
| `TaskAController` Inspector で `autoMotionIntervalMin=5f` / `autoMotionIntervalMax=10f` を確認 | TaskAController | A-2 追加分（旧 `autoMotionInterval` フィールドは廃止） |
| `TaskBController` Inspector で `postFlexionDelaySeconds=3f` / `postPracticeDelaySeconds=3f` / `flexionCountPerTrial=3` を確認 | TaskBController | B-1, B-3, B-5 追加分 |
| **`TaskBController.taskBPanelMessageText` に TaskB_Panel 内の `Text (TMP)` をアタッチ** | TaskBController | B-7 追加分。「これから本番です」を5秒表示するための参照 |
| **SoAResponsePanel / ParticipantHUD の TextMeshProUGUI の Color を黒（#000000）に設定** | UI | 文字色変更（Editor 作業のみ） |
| **HandVisualizer の指ボーンを `virtualLefthand` 配下に再アタッチ** | HandVisualizer | **最優先**：旧アタッチが `LeftHandAndroidXR`（実手側）になっていた可能性。実手側は XRHandSkeletonDriver が毎フレーム上書きするため屈曲が見えない。9個全部（index/middle/ring の MCP/PIP/DIP）を仮想手側に変更 |

---

## 確定済みの設計決定

| 項目 | 確定値 |
|------|--------|
| Yes ハンドサインの具体形 | **ピンチ（親指+人差し指タッチ）** ※握りこぶしから変更（2026-05-25） |
| 回答フェーズの制限時間 | **初期値 5 秒**（`responseWindowSeconds`、SerializeField で公開） |
| 固定遅延の実装方式 | **RingBuffer 流用**（Δt 過去のポーズを描画） |
| 屈曲検出方式 | **両方実装・Inspector 切替**（VelocityBased / AngleVelocityBased） |
| 音素材 | **AudioClip 実行時生成**（サイン波ビープ、外部素材依存ゼロ） |
| QUEST 推定 | **ブロックごとに再初期化**（async/sync 独立） |
| BlockRest 戻り先 | `lastStateBeforeBlockRest` 記録ベース |
| TaskA_Main → Next | **無条件 Finished** |
| Practice 中の遅延 | **delayMs = 0 強制**（HandleStateChanged で TaskB_Main 以外はリセット） |

## 残る未確定事項（パイロット実験で決定）

| 項目 | タイミング |
|------|-----------|
| 屈曲検出の閾値（`velocityThreshold` / `angularVelocityThreshold`） | パイロット実験で調整 |
| 所要時間（試行数縮小検討） | パイロット実験で決定 |
| Phase F の `autoMotionInterval` 最終値 | パイロット |

---

## Phase H（将来）

- ハンドサインによる Task リセット機能。**今回は未実装**。設計メモのみ残置。
