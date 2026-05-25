# STATUS — 現在地

> このファイルは「今どこにいるか」だけを書く。作業の区切りで**まるごと上書き**して最新に保つ。
> 終わったことは消して `LOG.md` に移す。確定した仕様は `Experiment.md` に書く。

**最終更新：2026-05-25（Phase F 完了）**

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

### Phase G：練習ブロックの整理【優先度: 中】
- Task A の練習を廃止
- Task B 本計測前に `practiceTrialCount`（3 試行）の練習。練習試行は解析対象外フラグ付き

### Phase C：操作系の左手移行・右手廃止【優先度: 低／実験完成に近づいてから】
- `ExperimentUI.cs`：Aボタン/Bボタン応答の置換は Phase E で実施。キーボード Y/N はテスト用に残置
- `bool testModeUIControl` フラグでテスト用操作の有効/無効を切替可能に
- Unity エディタ側で右手モデル非表示が必要（手順を別途提示）

---

## 未完了の Unity 側作業（VRデバッグ対応）

| 作業 | 対象 |
|------|------|
| `responseWindowSeconds` を Inspector で 5 秒に変更 | TaskBController / SoAResponseUI |
| HandSignDetector の Inspector を再設定（`thumbTip` / `indexTip` をアタッチ） | HandSignDetector |
| 左手 Interactor（Poke / Near-Far / Direct）を無効化 | XR Rig の左手 GameObject |
| SoAResponseUI / ParticipantHUD を taskBPanel 外の常時 active な親階層下に配置 | Hierarchy 確認 |
| TMP フォント: `NotoSansJP-VariableFont_wght SDF.asset` の Atlas Population Mode を Dynamic に | Font Asset |

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
