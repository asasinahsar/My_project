# LSL_MARKERS.md — LSL マーカー一覧

> Unity アプリ（Quest 実機）から LSL ストリーム `Markers` に送出される全マーカーの仕様。
> EMG とのオフライン同期解析時の参照リスト。**新しいマーカーを追加・変更したら必ずこのファイルを更新する。**

**最終更新：2026-06-01（VHI誘導手固定・60秒筆なぞり統一・3指ランダム・Staircase 反映）**
**ストリーム名:** `Markers` / **タイプ:** `Markers` / **channel format:** `string` / **チャンネル数:** 1

`{condition}` には `sync` または `async` が入る。`{n}`=試行番号、`{ms}`=遅延 Δt（ms）、
`{i}`=屈曲カウント、`{motionType}`=`IndexFingerFlexion`/`MiddleFingerFlexion`/`RingFingerFlexion`。

---

## 1. 実験全体の境界

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `ExpStart` | 実験本体開始時（`StartExperimentFromTaskA/B`） | ExperimentManager |
| `ExpOrder_TaskAFirst` | 「TaskAから」開始時（`ExpStart` 直後）。順序 `TaskA-async → TaskB-async → TaskA-sync → TaskB-sync` | ExperimentManager |
| `ExpOrder_TaskBFirst` | 「TaskBから」開始時（`ExpStart` 直後）。順序 `TaskB-async → TaskA-async → TaskB-sync → TaskA-sync` | ExperimentManager |
| `ExpEnd` | Finished ステート到達時 | ExperimentManager |

---

## 2. ステート遷移・操作マーカー

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `PracticeStart` | Practice ステート進入時 | ExperimentManager |
| `PracticeEnd` | Practice ステートから離脱時 | ExperimentManager |
| `RestStart` | BlockRest ステート進入時 | ExperimentManager |
| `RestEnd` | BlockRest ステートから離脱時 | ExperimentManager |
| `PhaseSkipped_{state}` | 各パネルの「次へ」ボタン押下時（スキップ） | ExperimentManager |
| `PhaseBack_{state}` | 各パネルの「戻る」ボタン押下時 | ExperimentManager |

> `PhaseSkipped_*` / `PhaseBack_*` が含まれる区間は、想定外の手動操作が入った可能性があるため解析時に要確認。

---

## 3. VHI 誘導フロー（sync ブロックのみ実施）

両タスクの誘導は **60秒筆なぞり**に統一（2026-05-29）。誘導中は準備15秒の後に手を固定する（2026-06-01）。

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `InductionStart_A_{condition}` | Task A 誘導開始 | VHIInductionController |
| `InductionPrep_A_{condition}` | Task A 準備時間開始（被験者がテーブルに手を置く・15秒） | VHIInductionController |
| `HandFrozen_A_{condition}` | 準備後、手の表示を固定（筆なぞり開始時） | VHIInductionController |
| `HandUnfrozen_A_{condition}` | 筆なぞり終了、手の固定解除 | VHIInductionController |
| `InductionEnd_A_{condition}` | Task A 誘導終了 | VHIInductionController |
| `InductionStart_B` | Task B 誘導開始（※常に sync ブロック） | VHIInductionController |
| `InductionPrep_B` | Task B 準備時間開始（15秒） | VHIInductionController |
| `HandFrozen_B` | 準備後、手の表示を固定（筆なぞり開始時） | VHIInductionController |
| `HandUnfrozen_B` | 筆なぞり終了、手の固定解除 | VHIInductionController |
| `InductionEnd_B` | Task B 誘導終了 | VHIInductionController |
| `BaselineStart_A_{condition}` | Task A 安静ベースライン計測開始（30秒） | VHIInductionController |
| `BaselineEnd_A_{condition}` | Task A 安静ベースライン計測終了 | VHIInductionController |
| `BaselineStart_B` | Task B 安静ベースライン計測開始（30秒） | VHIInductionController |
| `BaselineEnd_B` | Task B 安静ベースライン計測終了 | VHIInductionController |

> async ブロックには誘導を行わない（誘導なし＝コントロール）。よって Induction/Baseline マーカーは **sync ブロックでのみ出現**する。

---

## 4. Task B（SoA 計測）

### 4.1 練習ブロック（解析対象外）

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `PracticeStart_B` | Task B 練習ブロック開始 | TaskBController |
| `TrialStart_B_Practice_{n}_Delta0ms` | 練習試行開始（Δt=0ms 固定） | TaskBController |
| `FlexionDetected_B_Practice_{n}_count{i}` | 練習時の屈曲各回検出 | TaskBController |
| `ResponseWindowStart_B_Practice_{n}` | 練習時の回答フェーズ開始 | TaskBController |
| `SoA_Yes_Practice_{n}_Dt0ms` | 練習時の Yes 応答（ピンチ） | TaskBController |
| `SoA_No_Practice_{n}_Dt0ms` | 練習時の No 応答（無反応） | TaskBController |
| `TrialEnd_B_Practice_{n}` | 練習試行終了 | TaskBController |
| `PracticeEnd_B` | Task B 練習ブロック終了 | TaskBController |

### 4.2 本計測

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `TaskB_Start` | Task B 全体の計測開始（最初のブロック開始時） | TaskBController |
| `TaskB_End` | Task B 全体の計測終了（最終ブロック完了時） | TaskBController |
| `BlockStart_B_{condition}` | Task B の async/sync 各ブロック開始（23 試行単位） | TaskBController |
| `BlockEnd_B_{condition}` | Task B の async/sync 各ブロック終了 | TaskBController |
| `PreMainNotice_B_{condition}` | 「これから本番です」表示開始（試行開始前の待機） | TaskBController |
| `TrialStart_B_{n}_Delta{ms}ms` | 各試行の開始（ms=Δt） | TaskBController |
| `FlexionDetected_B_{n}_count{i}` | ペース化屈曲の各回検出（i=1〜flexionCountPerTrial=3） | TaskBController |
| `ResponseWindowStart_B_{n}` | 回答フェーズ開始（合図表示） | TaskBController |
| `SoA_Yes_Trial{n}_Dt{ms}ms` | 左手ピンチ（親指+人差し指タッチ）検出による Yes 応答 | TaskBController |
| `SoA_No_Trial{n}_Dt{ms}ms` | 無反応 No 応答（時間切れ） | TaskBController |
| `TrialEnd_B_{n}` | 各試行の終了 | TaskBController |

> Δt は Staircase 法で更新（初期0ms、No→+50ms、Yes→-30ms、範囲 0〜600ms）。本計測 23 試行 = Staircase 15 + 固定Δt 8（{0,150,300,500}ms×2）。

---

## 5. Task A（SoO 計測）

| マーカー | 送信タイミング | 送信元 |
|---------|--------------|-------|
| `TaskA_Start` | Task A 全体の計測開始（最初のブロック開始時） | TaskAController |
| `TaskA_End` | Task A 全体の計測終了（最終ブロック完了時） | TaskAController |
| `BlockStart_A_{condition}` | Task A の async/sync 各ブロック開始（21 試行単位） | TaskAController |
| `BlockEnd_A_{condition}` | Task A の async/sync 各ブロック終了 | TaskAController |
| `TrialStart_A_{condition}_{n}_{motionType}` | 各試行の開始（屈曲する指を含む） | TaskAController |
| `AutoMotionStart_A_{condition}_{n}_{motionType}` | 試行内の自動屈曲コマンド発行 | TaskAController |
| `MotionOnset_A_{motionType}` | 自動屈曲の実際の開始（**motor overflow 解析の t=0 基準**） | HandVisualizer 経由 |
| `TrialEnd_A_{condition}_{n}` | 各試行の終了 | TaskAController |
| `ProgressMilestone_A_{condition}_Remaining{N}pct` | ブロック進捗の節目（N=75/50/25/10） | TaskAController |

> 各指（人差し指/中指/薬指）は均等回数（21試行で各7回）、順序は Fisher-Yates でランダム化。
> 表面EMGでは指ごとの分離は不可のため、`{motionType}` は「視覚刺激としてどの指が動いたか」の記録に用いる（解析は刺激条件として扱う）。

---

## 6. テストモード時の挙動

`MarkerSenderRouter.isTestMode = true` のとき、`DebugMarkerSender` がアクティブになり、
LSL Outlet には送出されず **Unity Console に `[DebugMarkerSender - TEST MODE] Marker: {marker}` 形式でログ出力のみ**となる。
本番計測時は必ず `isTestMode = false` であることを Inspector で確認する。

---

## 7. リアルタイム監視

`tools/marker_monitor.py`（EMG PC で実行）で全マーカーをリアルタイム表示可能。詳細は [tools/README.md](tools/README.md) 参照。

---

## 8. オフライン解析時の使い方（典型例）

| 解析目的 | 必要なマーカー対 |
|---------|----------------|
| EMG ベースライン抽出 | `BaselineStart_*` 〜 `BaselineEnd_*` |
| Task A motor overflow（t=0〜100ms） | `MotionOnset_A_{motionType}` 基準 |
| Task B 運動準備 EMG（t=-500〜0ms） | `FlexionDetected_B_{n}_count{i}` 基準 |
| sync − async 差分 | `BlockStart_*_sync` / `BlockStart_*_async` でセグメント切り出し |
| 手固定区間の扱い | `HandFrozen_*` 〜 `HandUnfrozen_*`（誘導中・解析対象外） |
| 練習試行の除外 | `PracticeStart_B` 〜 `PracticeEnd_B`（および `*_Practice_*` マーカー）を除外 |
| 休憩区間の除外 | `RestStart` 〜 `RestEnd` を除外 |
| 誤操作の検出 | `PhaseSkipped_*` / `PhaseBack_*` が含まれる区間を要確認 |

### 廃止されたマーカー（過去ログ互換のため記載）
- `ActiveMovementStart_B` / `ActiveMovementEnd_B`：旧 Task B 慣らし運動。60秒筆なぞり統一で廃止（2026-05-29）
- `MotionOnset_B_Delta{ms}ms`：Onset 検出・Δt 待機方式の廃止に伴い廃止
- `SoAResponse_{0/1}` / `SoAResponse_Missed`：`SoA_Yes/No_*` に置換・統合

---

## 9. 実験本番で予想されるマーカー順序（新フロー D-1）

実験順序：**Practice → TaskB(async) → TaskA(async) → [VHI誘導B] TaskB(sync) → [VHI誘導A] TaskA(sync) → 終了**
（async 群＝誘導なし、sync 群＝誘導あり）

```
ExpStart

── Practice（TaskB 練習・解析対象外）──
PracticeStart                              （ExperimentManager: Practice 進入）
PracticeStart_B
  ［練習 ×3 試行、各：］
  TrialStart_B_Practice_1_Delta0ms
  FlexionDetected_B_Practice_1_count1
  FlexionDetected_B_Practice_1_count2
  FlexionDetected_B_Practice_1_count3
  ResponseWindowStart_B_Practice_1
  SoA_No_Practice_1_Dt0ms                  （または SoA_Yes_Practice_1_Dt0ms）
  TrialEnd_B_Practice_1
  …（試行2,3 同様）…
PracticeEnd_B
PracticeEnd                                （ExperimentManager: Practice 離脱）

── TaskB(async) 本計測（23試行）──
TaskB_Start
BlockStart_B_async
PreMainNotice_B_async
  ［23試行、各：］
  TrialStart_B_1_Delta0ms
  FlexionDetected_B_1_count1..3
  ResponseWindowStart_B_1
  SoA_No_Trial1_Dt0ms                      （または SoA_Yes_Trial1_Dt{ms}ms）
  TrialEnd_B_1
  …（試行2〜23）…
BlockEnd_B_async

── ブロック間休憩 ──
RestStart
RestEnd

── TaskA(async) 本計測（21試行）──
TaskA_Start
BlockStart_A_async
  ［21試行、各：］
  TrialStart_A_async_1_RingFingerFlexion   （指はランダム）
  AutoMotionStart_A_async_1_RingFingerFlexion
  MotionOnset_A_RingFingerFlexion          （← motor overflow t=0）
  TrialEnd_A_async_1
  …（途中で ProgressMilestone_A_async_Remaining75pct / 50pct / 25pct / 10pct）…
BlockEnd_A_async

── ブロック間休憩 ──
RestStart
RestEnd

── VHI誘導 B（sync・60秒筆なぞり）──
InductionStart_B
InductionPrep_B                            （準備15秒）
HandFrozen_B                               （手を固定→筆なぞり60秒）
HandUnfrozen_B                             （固定解除）
InductionEnd_B
BaselineStart_B                            （安静30秒）
BaselineEnd_B

── TaskB(sync) 本計測（23試行）──
BlockStart_B_sync
PreMainNotice_B_sync
  ［23試行、各：TrialStart_B_n … TrialEnd_B_n］
BlockEnd_B_sync
TaskB_End                                  （全 TaskB ブロック完了）

── ブロック間休憩 ──
RestStart
RestEnd

── VHI誘導 A（sync・60秒筆なぞり）──
InductionStart_A_sync
InductionPrep_A_sync                       （準備15秒）
HandFrozen_A_sync                          （手を固定→筆なぞり60秒）
HandUnfrozen_A_sync                        （固定解除）
InductionEnd_A_sync
BaselineStart_A_sync                       （安静30秒）
BaselineEnd_A_sync

── TaskA(sync) 本計測（21試行）──
BlockStart_A_sync
  ［21試行、各：TrialStart_A_sync_n_{finger} … MotionOnset … TrialEnd_A_sync_n］
  （途中で ProgressMilestone_A_sync_Remaining{N}pct）
BlockEnd_A_sync
TaskA_End                                  （全 TaskA ブロック完了）

ExpEnd
```

> 注：実験者がパネルで「次へ」「戻る」を押すと、上記の合間に `PhaseSkipped_{state}` / `PhaseBack_{state}` が挿入される。本番では通常は自動進行だが、手動操作した場合の記録として残る。
