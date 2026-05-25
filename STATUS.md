# STATUS — 現在地

> このファイルは「今どこにいるか」だけを書く。作業の区切りで**まるごと上書き**して最新に保つ。
> 終わったことは消して `LOG.md` に移す。確定した仕様は `Experiment.md` に書く。

**最終更新：2026-05-25（Phase A / A.5 / B / A.5 補正 完了、LSL ファイル整理完了）**

---

## 全体方針

`Experiment.md` は v5.3 に更新済み（仕様の正）。一方コードは v5.2 のままなので、
`MIGRATION_v5.3.md`（`C:\Users\koike\Downloads\MIGRATION_v5.3.md`）の Phase A〜G を
**1 Phase ＝ 1 コミット**で順に実装する。各 Phase 着手前に変更内容を提示し、許可を得てから着手する。

> v5.2 で計画していた Phase 4「async＝Δt=500ms 固定遅延」「Phase 5：BlockRest 30秒手指運動UI」
> 「Phase 6：仕様クリーンアップ」は v5.3 の設計変更により次の通り吸収・置換される：
> - Phase 4 → 廃止（async＝VHI誘導なしに再定義。MIGRATION Phase D で扱う）
> - Phase 5 → Experiment.md 4.5 節に記載済み（実装は別途、Phase G 後に検討）
> - Phase 6 → Experiment.md は v5.3 で書き換え済み。README/コード側は各 Phase で対応

---

## 進行中・次にやること

### Phase A.5 後の Unity 側残作業（手順は本セッションの応答末尾に提示）
- 旧 `Consent Panel` GameObject をシーンから削除（コード参照はゼロ済）
- 各実験パネル（Practice/TaskA/TaskB/BlockRest/Finished）に Btn_Next / Btn_Back を配置
- OnClick を `ExperimentManager.SkipCurrentPhase()` / `GoBackPhase()` に紐付け

### Phase C：操作系の左手移行・右手廃止【優先度: 低／実験完成に近づいてから】
- `ExperimentUI.cs`：Aボタン/Bボタン応答の置換は Phase E で実施。キーボード Y/N はテスト用に残置
- `bool testModeUIControl` フラグでテスト用操作の有効/無効を切替可能に
- Unity エディタ側で右手モデル非表示が必要（手順を別途提示）

### Phase D：async/sync を「VHI 誘導の有無」で定義
- `VHIInductionController.cs`、`ExperimentManager.cs`、`TaskAController.cs`、`TaskBController.cs`
- `SetAsyncOffset` 関連・async 時 `delayMs=500` 設定を廃止
- async（誘導なし）→ sync（誘導あり）の 2 ブロック構造を実装
- Task B 各ブロック 55 試行（QUEST 35 + 固定 Δt 20）、Task A 各ブロック 20 試行

### Phase E：Task B 試行構造の全面改修（最重要）
- 遅延機構：Onset 検出（`EnableOnsetDetection` / `OnMovementDetected` 駆動）を廃止し、
  **固定遅延を RingBuffer 流用で常時適用**（Δt 過去のポーズを描画）
- 計測フェーズ：`pacingInterval`（5秒）ごとに視覚＋音ペース合図、左手首屈曲を検出してカウント、
  `flexionCountPerTrial`（5回）で終了
- 回答フェーズ：合図後、`responseWindowSeconds`（**SerializeField・初期値 3 秒**）以内に
  **左手握りこぶし（Fist）**を検出 → Yes、無反応 → No
- LSL マーカー：`SoA_Yes_Trial{n}_Dt{delta_t}ms` / `SoA_No_Trial{n}_Dt{delta_t}ms`
- ハンドサイン検出は新規実装（XR Hands のジョイント姿勢から判定、回答フェーズでのみ有効化）

### Phase F：Task A 時間ベース化・自動屈曲 1 秒
- 対象：`TaskAController.cs`、`HandVisualizer.cs`
- 速度ベース静止確認を廃止し、`autoMotionStartDelay`（3秒）の待機に変更
- 自動屈曲 2秒 → `autoMotionDuration`（1秒）
- サイクル：`autoMotionDuration` + `autoMotionInterval`
- **テストモード（TestModeController）の「待機→自動屈曲→繰り返し」挙動を本番モードで流用**

### Phase G：練習ブロックの整理
- Task A の練習を廃止
- Task B 本計測前に `practiceTrialCount`（3 試行）の練習。練習試行は解析対象外フラグ付き

---

## 着手前に確定済みの未確定事項（MIGRATION 5 節より）

| 項目 | 確定値 |
|------|--------|
| Yes ハンドサインの具体形 | **握りこぶし（Fist）** |
| 回答フェーズの制限時間 | **SerializeField で公開・初期値 3 秒**（`responseWindowSeconds`） |
| 固定遅延の実装方式 | **RingBuffer 流用**（Δt 過去のポーズを描画） |

## 残る未確定事項（実装中に詰める）

| 項目 | タイミング |
|------|-----------|
| 屈曲検出の閾値（角度・速度） | Phase E 着手時に確認 |
| 所要時間（パイロットで縮小検討） | パイロット実験時（実装後） |

---

## Phase H（将来）

- ハンドサインによる Task リセット機能。**今回は未実装**。設計メモのみ残置。
