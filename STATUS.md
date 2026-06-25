# STATUS — 現在地

> このファイルは「今どこにいるか」だけを書く。作業の区切りで**まるごと上書き**して最新に保つ。
> 終わったことは消して `LOG.md` に移す。確定した仕様は `Experiment.md` に書く。

**最終更新：2026-06-21（テストモード廃止＋開始タスク分岐を実装。EMG 4フィルタ適用は引き続き最優先で未着手）**

---

## 🔴 最優先：次セッションで着手するタスク

### mion.xdf に 4 フィルタを適用（未着手・要パラメータ確認）
ユーザー依頼：「mion.xdf に bandpass / Notch / full wave rectification / low pass の 4 フィルタをかける」。

- **対象データ**：`C:\Users\koike\Downloads\mion\mion.xdf`（EMG ストリーム。ch0=手首屈筋 FCR / ch1=要確認。有効 2ch、3ch目 EDC は欠落）
- **適用順序（標準的な EMG リニアエンベロープ処理）**：
  1. **Bandpass filter**（butter 4次・filtfilt ゼロ位相、20–450Hz）← 既存 `tools/emg_preprocess_epochs.py` に実装済
  2. **Notch filter**（`scipy.signal.iirnotch`、**日本＝50Hz** 商用電源、Q=30 目安）← **未実装・新規追加**
  3. **Full wave rectification**（`np.abs`）← 既存に実装済
  4. **Low pass filter**（butter 低域、**~5–10Hz** でリニアエンベロープ）← **未実装・新規追加（RMS とは別物）**
- **既存資産**：`tools/emg_preprocess_epochs.py` が bandpass + 整流 + RMS を実装済。これに Notch と専用 low-pass を足す形で拡張するか、別スクリプトを新規作成する。
- **着手前に確認すること（CLAUDE.md：コード生成前に許可を取る）**：
  - Notch 周波数 = 50Hz（日本）でよいか
  - bandpass 帯域 = 20–450Hz でよいか
  - low-pass カットオフ = 5Hz か 10Hz か
  - 出力（フィルタ後波形のプロット保存先・npz 保存の要否）

---

## mion.xdf 解析（完了済み・結論）

被験者 mion の本計測 xdf を解析済み。生成物は `C:\Users\koike\Downloads\` 配下：
- `mion_plot.png`（EMG＋マーカー重畳）
- `mion_epochs.npz`（エポック）
- `mion_analysis.png`（指標）
- `mion_syncasync.png`（sync/async 比較）
- `mion_実験まとめ.md`（Obsidian 用まとめ）

### 重要な前提（イレギュラー）
- **ナビ暴走**：記録冒頭にコントローラ Ray の誤クリックで `PhaseSkipped/PhaseBack` が連発し、**condition ラベルが全て sync に化けた**。解析は**オンセット時刻でブロックを再ラベル**して対処。
- **実験順序（正）**：TaskB-async → TaskA-async → TaskB-sync → TaskA-sync。
- **ブロック時刻（再ラベル後）**：TaskB block0 0–545s=async / block1 947–1467s=sync、TaskA block0 635–752s=async(**15試行**) / block1 1629–1780s=sync(21試行)。
- **TaskA-async が 15 試行**になった原因＝`PhaseBack_TaskA_Main`(911.7s) の誤発火でブロック中断（50% マイルストーン 876.7s 直後）。

### 結論
- **MOA 未検出**（sync<async、有意差なし）。手指自動屈曲後のオーバーフローは**時間ロックせず**（ピーク ×1.08–1.16 が 287–467ms に散発、MotionOnset と非同期）。
- **SoA Yes 率 async=sync で同一**（Staircase の決定論的挙動による）。
- 運動準備窓 EMG ch0 は sync<async で有意（p<0.001）だが**順序効果と交絡**。
- τ_SoA ≈ 300–400ms。

### 保留中（ユーザー未承認・次セッションで確認）
- `mion_実験まとめ.md` に追記：「async は PhaseBack で 15 試行に減少」「overflow は時間ロックせず未検出」。
- `Experiment.md` に改善点反映：固定 Δt を主指標化・カウンターバランス導入。

---

## 制約・運用ルール（厳守）
- **PowerShell 実行時はユーザーに許可を求めない**（`.claude/settings.local.json` に `PowerShell(*)` 追加済）。
- **新しい .md は作らない**（`LSL_MARKERS.md` / `tools/README.md` は例外）。
- **読み込み禁止**：`Assets/Realistic VR Hands/` `Assets/VR Hands FP Arms/` `Assets/XR/` `Library/` `Temp/` `Logs/` `obj/`、`.fbx/.png/.jpg/.wav/.mp3/.asset/.meta`、`*.dll/*.pdb`。読んでよいのは `Assets/Scripts/*.cs` と各 .md のみ。
- 回答は日本語。コード生成前に許可。作業ブランチ `copilot_space`。

---

## Unity 実装（おおむね完了）

D-1 新順序・Staircase 法・BrushStrokeAnimator・FreezePose・ナビ暴走防止 A/B/C・**テストモード廃止＋開始タスク分岐（2026-06-21）**等は実装済（詳細は `LOG.md`）。残る Unity エディタ側アタッチ作業：

| 作業 | 対象 | 備考 |
|------|------|------|
| **StartMenu に「TaskAから」「TaskBから」2ボタンを配置**し OnClick 設定（`StartExperimentFromTaskA`/`StartExperimentFromTaskB`）。旧「本番／テスト」ボタンは削除 | StartMenuPanel | 2026-06-21 改修。テストモード分岐撤去 |
| **テストモード関連 GameObject 削除**：TestModeController（Missing Script 化）/ testMenuPanel / experimentMenuPanel / testRunningPanel | Hierarchy | TestModeController.cs はスクリプトごと削除済 |
| ParticipantHUD/SoAResponseUI が常時 active な親階層下にあるか確認 | Hierarchy | Practice/TaskB_Main で自身を SetActive 制御するため |
| EMG **最低 2ch（できれば 3ch：FDS/FCR/EDC）装着**／Delsys を確実に有効化 | 実機 | 今回 ch1 中心・3ch目(EDC)欠落。次回は最低 2ch 確保（NMF 筋シナジーに必須） |
| 指ボーン（index/middle/ring の MCP/PIP/DIP）を `virtualLefthand` 配下にアタッチ | HandVisualizer | 実手側 `LeftHandAndroidXR` だと XRHandSkeletonDriver が上書きし屈曲が見えない |
| `TaskAHUD` をアタッチ（`milestoneText` / `audioSource`） | TaskAHUD | マイルストーン通知用 |
| `BrushStrokeAnimator` の `brush`/`strokeStart`/`strokeEnd` 配線 | VHIInductionController | strokeStart/End 名は "Stroke" 始まりで CollectRecursive 除外済 |
| `TaskInstructionUI` の TMP 配線 | ExperimentCanvas | taskA/B InstructionText |
| 白い丸（pinch visual）非表示 | XR Rig | PinchPointFollow を無効化 |

---

## 本計測・運用上の注意
- **記録は必ずブロック先頭から開始**（途中記録だと `BlockStart_*` を取りこぼし condition=unknown）。
- **ナビ暴走防止**：計測中はコントローラ Ray の誤クリックに注意（`lockNavigationDuringMeasurement=true` 実装済）。
- **TaskA 中の安静維持**を徹底（被験者が動くと EMG に混入）。
