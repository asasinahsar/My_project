# LOG — 履歴

> このファイルは**追記オンリー**。完了したPhaseや解決した問題を、新しいものを上に積んでいく。
> 過去の記述は書き換えない（間違っていたこともそのまま記録として残す）。

---

## 完了済みPhase

| Phase | 内容 |
|-------|------|
| P3 Task Aフロー | 静止確認（<5cm/s・1秒継続）・10秒待機→屈曲サイクル・onset検出のTask B専用化 |
| P2 人差し指屈曲 | AutoMotionType→IndexFingerFlexion・MCP/PIP/DIP 30°屈曲（LateUpdate差分加算）・Inspectorスロット追加 |
| P1 構造修正 | VirtualLeftHand分離・ApplyDelayedPose座標系修正（world統一）・_skeletonDriverインフラ全削除 |

## 解決済みの問題（〜2026-05-18）

| # | 問題 | 解決 |
|---|------|------|
| 1 | virtualHandWristのworld-position直接代入 | P1-3で解決 |
| 2 | 親指付け根のボーン変形（world position書き込み） | P1-4で解決 |
| 3 | XRHandSkeletonDriverとのUpdate競合 | P1-5（分離）で解決 |
