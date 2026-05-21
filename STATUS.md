# STATUS — 現在地

> このファイルは「今どこにいるか」だけを書く。作業の区切りで**まるごと上書き**して最新に保つ。
> 終わったことは消して `LOG.md` に移す。確定した仕様は `Experiment.md` に書く。

**最終更新：2026-05-18**

---

## 進行中・次にやること

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

---

## 未解決の課題

| # | 問題 | 対処予定 |
|---|------|---------|
| 4 | SetAsyncOffsetのworld Z軸固定問題 | P4-1で廃止予定 |
| 5 | isAutoModeガードによるApplyDelayedPose無効化 | P4-3で対処予定 |
