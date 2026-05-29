# tools/

実験運用・デバッグ用の補助スクリプト群。Unity プロジェクトの実行とは独立して、
EMG PC 等の外部 PC で動かす Python スクリプトを置く。

---

## marker_monitor.py

**目的:** Quest 実機の Unity アプリから送出される LSL マーカーをリアルタイムで EMG PC に表示する。

LabRecorder では「マーカーが今届いたか」をリアルタイムに見るのが難しいため、
このスクリプトを別ターミナルで走らせておくと実験中の動作確認が楽になる。

### 事前準備
```
pip install pylsl
```

### 使い方
```
python tools/marker_monitor.py
```

EMG PC のコンソールに以下のような出力が流れる：
```
LSL 'Markers' ストリームを探索中...
接続成功: name='Markers' type='Markers' host='Quest'
======================================================================
wall_clock      lsl_timestamp  marker
----------------------------------------------------------------------
14:23:01.123      12345.678   ExpStart
14:23:02.456      12347.012   PracticeStart
14:23:15.789      12360.345   PracticeEnd
14:23:15.811      12360.367   TaskB_Start
14:23:17.022      12361.578   TrialStart_B_1_Delta300ms
...
```

`Ctrl+C` で終了。

### 前提条件
- Quest 実機で Unity アプリが起動中（`#if UNITY_EDITOR` で Editor 実行時は LSL Outlet が出ない仕様）
- Quest と EMG PC が**同じ WiFi ネットワーク**に接続されている
- Windows ファイアウォール等で LSL 通信がブロックされていない
- LabRecorder と並行実行可能（LSL は購読型のため複数のクライアントが同じストリームを購読できる）

> **実行時の注意:** `.py` をパス直打ち（例 `D:\...\marker_monitor.py`）すると、ファイル関連付けでエディタが開くだけで実行されない。必ず `python <path>` の形で起動する。

### トラブルシューティング
| 症状 | 対処 |
|------|------|
| `Markers ストリームが見つかりません` | Quest 上で Unity アプリが起動しているか確認。同じ WiFi か確認。ファイアウォール確認 |
| `pylsl がインストールされていません` | `pip install pylsl` を実行 |
| 接続できるが何も流れてこない | Unity 側でステート遷移が発生していないか、`MarkerSenderRouter` が `DebugMarkerSender` 側に切り替わっている可能性。Inspector で `isTestMode = false` を確認 |

---

## plot_emg_markers.py

**目的:** LabRecorder で記録した `.xdf` から EMG 波形と Unity マーカーを共通の LSL 時刻軸で重ねてプロットし、解析前の目視確認を行う。

EMG ストリーム（数値・`nominal_srate>0`、type に "EMG" を含む or name="Delsys"）と
マーカーストリーム（type="Markers"・文字列イベント）を自動検出する。

### 事前準備
```
pip install pyxdf matplotlib
```

### 使い方
```
# ch1 をプロット（既定）
python tools/plot_emg_markers.py untitled.xdf

# ch を引数で指定（複数可、0始まり）
python tools/plot_emg_markers.py untitled.xdf --ch 1 9

# 全chを並べて構成を確認
python tools/plot_emg_markers.py untitled.xdf --ch all

# 特定マーカーだけ表示し、時間範囲をズーム、画像保存
python tools/plot_emg_markers.py untitled.xdf --ch 1 --marker-regex "SoA" --xmin 120 --xmax 180 --save out.png
```

### 引数
| 引数 | 内容 |
|------|------|
| `xdf_path`（必須） | 入力 `.xdf` ファイルのパス |
| `--ch` | プロットするEMGチャンネル（0始まり、複数可、`all`で全ch）。既定: 1 |
| `--marker-regex` | 表示マーカー名の正規表現フィルタ（例 `"SoA\|TrialStart"`） |
| `--no-marker-labels` | マーカーのテキストラベルを描かず縦線のみにする |
| `--xmin` / `--xmax` | 表示時間範囲（秒、記録開始からの相対時刻） |
| `--ylim LO HI` | y軸範囲を手動指定（例 `--ylim -0.2 0.2`）。未指定なら表示範囲内の信号に自動フィット |
| `--save` | 画像保存パス（指定時は画面表示せず保存。画面なし環境でも可） |

> **y軸の自動スケール:** `--ylim` 未指定時は、`--xmin/--xmax` で指定した**表示範囲内の信号**の 0.5〜99.5 パーセンタイルに合わせて y軸を自動設定する。全体表示では大振幅区間（例: TaskA 後半）に引っ張られて小信号区間が潰れるため、見たい区間を `--xmin/--xmax` でズームすると、その区間の信号に縦いっぱいフィットする。

### マーカーの色分け
イベント種別ごとに色を変えて表示する（TrialStart=青、FlexionDetected=緑、ResponseWindowStart=橙、SoA_Yes=赤、SoA_No=紫、AutoMotionStart/MotionOnset=シアン、Block/Rest=茶）。

### 注意
- マーカーが密集する全体表示ではラベルが重なるため、`--xmin/--xmax` でのズームか `--marker-regex` での絞り込みを推奨。
- EMG の実信号がどの ch に乗っているかは、`--ch all` で全ch表示するか、各chの標準偏差を見て判断する（未接続chはノイズフロアで std がごく小さい）。

---

## emg_preprocess_epochs.py

**目的:** `.xdf` の EMG を前処理し、Unity マーカーを基準に Task A / Task B のエポックを切り出して `.npz` に保存する（解析パイプライン前段、Experiment.md §8.2 準拠）。

```
生EMG → バンドパス(20-450Hz, filtfilt) → 全波整流 → RMS包絡線(50ms窓)
       → マーカー照合 → エポック切り出し → .npz 保存
```

### 事前準備
```
pip install pyxdf scipy
```

### 使い方
```
# 全chを処理して <入力>_epochs.npz に保存
python tools/emg_preprocess_epochs.py untitled.xdf

# ch1 のみ保存し、確認プロットも出力
python tools/emg_preprocess_epochs.py untitled.xdf --channels 1 --plot check.png
```

### エポック基準
| Task | t=0 のマーカー | 既定窓 | 主窓（解析対象） | 付与メタ |
|------|---------------|--------|-----------------|---------|
| Task A | `MotionOnset_A_{finger}` | -0.5〜+1.0s | 0〜100ms（motor overflow） | condition / trial / finger |
| Task B | `FlexionDetected_B_{trial}_count{k}` | -1.0〜+0.5s | -500〜0ms（運動準備） | condition / trial / count / Δt / SoA |

### 主な引数
| 引数 | 既定 | 内容 |
|------|------|------|
| `--out` | `<入力>_epochs.npz` | 出力パス |
| `--channels` | all | 保存ch（0始まり、複数可、`all`） |
| `--bp-low` / `--bp-high` | 20 / 450 | バンドパス帯域(Hz) |
| `--rms-window` | 0.05 | RMS窓(秒) |
| `--taska-pre` / `--taska-post` | 0.5 / 1.0 | TaskA エポック前後幅(秒) |
| `--taskb-pre` / `--taskb-post` | 1.0 / 0.5 | TaskB エポック前後幅(秒) |
| `--plot` | なし | 平均RMSの確認プロット保存パス |

### .npz の中身
- `fs`, `channels`, `bp_low`, `bp_high`, `rms_window_sec`（前処理パラメータ）
- **Task A**: `taska_bp`（バンドパス後波形 n_epochs×n_ch×n_samples）, `taska_rms`（RMS包絡線, 同形状）, `taska_time`（エポック内相対時刻）, `taska_condition` / `taska_trial` / `taska_finger` / `taska_onset_time`
- **Task B**: `taskb_bp`, `taskb_rms`, `taskb_time`, `taskb_condition` / `taskb_trial` / `taskb_count` / `taskb_delta_ms` / `taskb_soa` / `taskb_onset_time`

読み込み例（Python）:
```python
import numpy as np
d = np.load("untitled_epochs.npz")
print(d.files)
taskb_rms = d["taskb_rms"]      # (n_epochs, n_ch, n_samples)
soa = d["taskb_soa"]            # 1=Yes, 0=No, -1=不明
delta = d["taskb_delta_ms"]     # 各エポックの Δt
```

### 注意
- 確認プロット（`--plot`）は**保存配列の先頭ch**を表示する。特定chだけ見たいときは `--channels <番号>` でそのchを指定する。
- `condition='unknown'` になる場合、記録開始が `BlockStart_A/B_{cond}` マーカーより後（タスク途中から記録）の可能性。本計測は**ブロック先頭から記録**すること。
- バンドパス上限はサンプリングの Nyquist（fs/2）未満に自動クランプされる。
