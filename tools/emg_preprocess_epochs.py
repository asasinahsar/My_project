# -*- coding: utf-8 -*-
"""
emg_preprocess_epochs.py

LabRecorder で記録した .xdf から EMG を前処理し、Unity マーカーを基準に
Task A / Task B のエポックを切り出して .npz に保存する解析パイプライン（前段）。

処理の流れ（Experiment.md §8.2 準拠）:
    生EMG → バンドパス(20-450Hz, filtfilt) → 全波整流 → RMS包絡線(50ms窓)
    → マーカー照合 → エポック切り出し → .npz 保存

エポック基準:
    Task A: MotionOnset_A_{finger} の時刻を t=0（直前の AutoMotionStart から cond/trial/finger を取得）
            既定窓 t = -0.5 〜 +1.0s（不随意窓 0〜100ms を含む）
    Task B: FlexionDetected_B_{trial}_count{k} の時刻を t=0
            既定窓 t = -1.0 〜 +0.5s（運動準備窓 -500〜0ms を含む）
            Δt は TrialStart_B_{trial}_Delta{X}ms、SoA は SoA_{Yes/No}_Trial{n} から付与

依存: pyxdf, numpy, scipy
    pip install pyxdf scipy

使い方:
    python emg_preprocess_epochs.py untitled.xdf
    python emg_preprocess_epochs.py untitled.xdf --out epochs.npz --channels 1
    python emg_preprocess_epochs.py untitled.xdf --plot check.png
"""
import argparse
import os
import re

import numpy as np
import pyxdf
from scipy.signal import butter, filtfilt


# ----------------------------------------------------------------------
# ストリーム検出（plot_emg_markers.py と同じロジック）
# ----------------------------------------------------------------------
def find_streams(streams):
    emg_stream = None
    marker_stream = None
    for st in streams:
        info = st["info"]
        stype = (info["type"][0] if info["type"] else "").lower()
        name = (info["name"][0] if info["name"] else "").lower()
        srate = float(info["nominal_srate"][0]) if info["nominal_srate"] else 0.0
        is_string = isinstance(st["time_series"], list)
        if is_string or stype == "markers":
            if marker_stream is None:
                marker_stream = st
        elif srate > 0 or "emg" in stype or name == "delsys":
            if emg_stream is None:
                emg_stream = st
    return emg_stream, marker_stream


# ----------------------------------------------------------------------
# 前処理
# ----------------------------------------------------------------------
def bandpass_filter(data, fs, low, high):
    """ゼロ位相バンドパス（filtfilt）。data shape: (samples, channels)"""
    nyq = fs / 2.0
    high = min(high, nyq * 0.99)  # Nyquist 超え防止
    b, a = butter(4, [low / nyq, high / nyq], btype="band")
    return filtfilt(b, a, data, axis=0)


def rms_envelope(data, fs, window_sec):
    """移動 RMS 包絡線。window_sec 幅の矩形窓で sqrt(moving_mean(x^2))。"""
    win = max(1, int(round(window_sec * fs)))
    kernel = np.ones(win) / win
    sq = data ** 2
    env = np.empty_like(sq)
    for ch in range(sq.shape[1]):
        # 'same' 畳み込みで中心揃え
        env[:, ch] = np.sqrt(np.convolve(sq[:, ch], kernel, mode="same"))
    return env


# ----------------------------------------------------------------------
# マーカーパース
# ----------------------------------------------------------------------
RE_BLOCK_A = re.compile(r"^BlockStart_A_(async|sync)")
RE_BLOCK_B = re.compile(r"^BlockStart_B_(async|sync)")
RE_AUTOMOTION = re.compile(r"^AutoMotionStart_A_(async|sync)_(\d+)_(\w+)")
RE_MOTIONONSET = re.compile(r"^MotionOnset_A_(\w+)")
RE_FLEXION = re.compile(r"^FlexionDetected_B_(\d+)_count(\d+)")
RE_TRIALSTART_B = re.compile(r"^TrialStart_B_(\d+)_Delta(\d+)ms")
RE_SOA = re.compile(r"^SoA_(Yes|No)_Trial(\d+)")


def parse_markers(m_ts, m_labels):
    """
    マーカー時系列から TaskA / TaskB のエポック基準イベントを抽出する。
    戻り値:
        taska_events: list of dict(onset_time, condition, trial, finger)
        taskb_events: list of dict(onset_time, condition, trial, count, delta_ms, soa)
    """
    # 先に Δt と SoA の trial→値マップを作る
    delta_map = {}   # trial -> delta_ms
    soa_map = {}     # trial -> 1(Yes)/0(No)
    for label in m_labels:
        m = RE_TRIALSTART_B.match(label)
        if m:
            delta_map[int(m.group(1))] = int(m.group(2))
        m = RE_SOA.match(label)
        if m:
            soa_map[int(m.group(2))] = 1 if m.group(1) == "Yes" else 0

    taska_events = []
    taskb_events = []
    cond_a = None
    cond_b = None
    pending_a = None  # 直近の AutoMotionStart_A から (condition, trial, finger)

    for t, label in zip(m_ts, m_labels):
        mb_a = RE_BLOCK_A.match(label)
        if mb_a:
            cond_a = mb_a.group(1)
            continue
        mb_b = RE_BLOCK_B.match(label)
        if mb_b:
            cond_b = mb_b.group(1)
            continue

        m = RE_AUTOMOTION.match(label)
        if m:
            pending_a = (m.group(1), int(m.group(2)), m.group(3))
            continue

        m = RE_MOTIONONSET.match(label)
        if m:
            finger = m.group(1)
            if pending_a is not None:
                cond, trial, fpending = pending_a
            else:
                cond, trial, fpending = (cond_a, -1, finger)
            taska_events.append(dict(
                onset_time=float(t), condition=cond if cond else "unknown",
                trial=trial, finger=finger))
            pending_a = None
            continue

        m = RE_FLEXION.match(label)
        if m:
            trial = int(m.group(1))
            count = int(m.group(2))
            taskb_events.append(dict(
                onset_time=float(t),
                condition=cond_b if cond_b else "unknown",
                trial=trial, count=count,
                delta_ms=delta_map.get(trial, -1),
                soa=soa_map.get(trial, -1)))
            continue

    return taska_events, taskb_events


# ----------------------------------------------------------------------
# エポック切り出し
# ----------------------------------------------------------------------
def cut_epochs(emg_bp, emg_rms, emg_t, events, pre_sec, post_sec, fs):
    """
    events の onset_time を t=0 として [-pre, +post] のエポックを切り出す。
    範囲外（記録端）のイベントはスキップ（warn）。
    戻り値: epochs_bp, epochs_rms (n_epochs, n_ch, n_samples), epoch_time, kept_events
    """
    n_pre = int(round(pre_sec * fs))
    n_post = int(round(post_sec * fs))
    n_samples = n_pre + n_post
    epoch_time = (np.arange(-n_pre, n_post)) / fs

    bp_list, rms_list, kept = [], [], []
    for ev in events:
        # onset に最も近いサンプルインデックス
        idx = int(np.searchsorted(emg_t, ev["onset_time"]))
        i0, i1 = idx - n_pre, idx + n_post
        if i0 < 0 or i1 > emg_bp.shape[0]:
            print(f"[skip] onset={ev['onset_time']:.3f} はエポック範囲が記録端を超えるためスキップ")
            continue
        bp_list.append(emg_bp[i0:i1, :].T)    # (n_ch, n_samples)
        rms_list.append(emg_rms[i0:i1, :].T)
        kept.append(ev)

    if not kept:
        return (np.empty((0, emg_bp.shape[1], n_samples)),
                np.empty((0, emg_bp.shape[1], n_samples)),
                epoch_time, [])
    return (np.stack(bp_list), np.stack(rms_list), epoch_time, kept)


def meta_arrays(events, keys):
    """events(list of dict) を key ごとの numpy 配列に変換。"""
    out = {}
    for k in keys:
        vals = [ev[k] for ev in events]
        out[k] = np.array(vals)
    return out


# ----------------------------------------------------------------------
# メイン
# ----------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description="EMG 前処理 + エポック切り出し（.npz 出力）")
    ap.add_argument("xdf_path", help="入力 .xdf ファイルのパス")
    ap.add_argument("--out", default=None, help="出力 .npz パス（既定: <入力>_epochs.npz）")
    ap.add_argument("--channels", nargs="+", default=None,
                    help="保存するEMGチャンネル（0始まり、複数可、'all'）。既定: all")
    ap.add_argument("--bp-low", type=float, default=20.0, help="バンドパス下限Hz（既定20）")
    ap.add_argument("--bp-high", type=float, default=450.0, help="バンドパス上限Hz（既定450）")
    ap.add_argument("--rms-window", type=float, default=0.05, help="RMS窓秒（既定0.05）")
    ap.add_argument("--taska-pre", type=float, default=0.5, help="TaskA エポック pre 秒（既定0.5）")
    ap.add_argument("--taska-post", type=float, default=1.0, help="TaskA エポック post 秒（既定1.0）")
    ap.add_argument("--taskb-pre", type=float, default=1.0, help="TaskB エポック pre 秒（既定1.0）")
    ap.add_argument("--taskb-post", type=float, default=0.5, help="TaskB エポック post 秒（既定0.5）")
    ap.add_argument("--plot", default=None, help="平均エポックの確認プロット保存パス（任意）")
    args = ap.parse_args()

    print("Loading:", args.xdf_path)
    streams, _ = pyxdf.load_xdf(args.xdf_path)
    emg, markers = find_streams(streams)
    if emg is None:
        raise SystemExit("[error] EMG ストリームが見つかりません")
    if markers is None:
        raise SystemExit("[error] マーカーストリームが見つかりません")

    emg_data = np.asarray(emg["time_series"], dtype=np.float64)
    emg_ts = np.asarray(emg["time_stamps"])
    fs = float(emg["info"]["nominal_srate"][0])
    if fs <= 0:  # nominal が 0 のときは実効レートを推定
        fs = (len(emg_ts) - 1) / (emg_ts[-1] - emg_ts[0])
    n_ch_total = emg_data.shape[1]
    print(f"EMG: {emg_data.shape[0]} samples x {n_ch_total} ch, fs={fs:.2f} Hz")

    # チャンネル選択
    if args.channels is None or (len(args.channels) == 1 and str(args.channels[0]).lower() == "all"):
        chans = list(range(n_ch_total))
    else:
        chans = [int(c) for c in args.channels if 0 <= int(c) < n_ch_total]
    print("Channels to save:", chans)

    # ---- 前処理 ----
    print("Bandpass + rectify + RMS ...")
    emg_bp_full = bandpass_filter(emg_data, fs, args.bp_low, args.bp_high)
    emg_rms_full = rms_envelope(np.abs(emg_bp_full), fs, args.rms_window)
    emg_bp = emg_bp_full[:, chans]
    emg_rms = emg_rms_full[:, chans]

    # ---- マーカーパース ----
    m_ts = np.asarray(markers["time_stamps"])
    m_labels = [s[0] if isinstance(s, (list, tuple)) else str(s)
                for s in markers["time_series"]]
    taska_events, taskb_events = parse_markers(m_ts, m_labels)
    print(f"Parsed events: TaskA={len(taska_events)}  TaskB={len(taskb_events)}")

    # ---- エポック切り出し ----
    a_bp, a_rms, a_time, a_kept = cut_epochs(
        emg_bp, emg_rms, emg_ts, taska_events, args.taska_pre, args.taska_post, fs)
    b_bp, b_rms, b_time, b_kept = cut_epochs(
        emg_bp, emg_rms, emg_ts, taskb_events, args.taskb_pre, args.taskb_post, fs)
    print(f"TaskA epochs: {a_bp.shape}   TaskB epochs: {b_bp.shape}")

    a_meta = meta_arrays(a_kept, ["condition", "trial", "finger", "onset_time"])
    b_meta = meta_arrays(b_kept, ["condition", "trial", "count", "delta_ms", "soa", "onset_time"])

    # condition 内訳の表示
    if len(a_kept):
        ua, ca = np.unique(a_meta["condition"], return_counts=True)
        print("  TaskA condition:", dict(zip(ua.tolist(), ca.tolist())))
        uf, cf = np.unique(a_meta["finger"], return_counts=True)
        print("  TaskA finger:", dict(zip(uf.tolist(), cf.tolist())))
    if len(b_kept):
        ub, cb = np.unique(b_meta["condition"], return_counts=True)
        print("  TaskB condition:", dict(zip(ub.tolist(), cb.tolist())))
        print("  TaskB SoA Yes/No:", int((b_meta['soa'] == 1).sum()), "/", int((b_meta['soa'] == 0).sum()))

    # ---- 保存 ----
    out_path = args.out
    if out_path is None:
        base = os.path.splitext(args.xdf_path)[0]
        out_path = base + "_epochs.npz"

    np.savez_compressed(
        out_path,
        fs=fs,
        channels=np.array(chans),
        bp_low=args.bp_low, bp_high=args.bp_high, rms_window_sec=args.rms_window,
        # Task A
        taska_bp=a_bp, taska_rms=a_rms, taska_time=a_time,
        taska_condition=a_meta.get("condition", np.array([])),
        taska_trial=a_meta.get("trial", np.array([])),
        taska_finger=a_meta.get("finger", np.array([])),
        taska_onset_time=a_meta.get("onset_time", np.array([])),
        # Task B
        taskb_bp=b_bp, taskb_rms=b_rms, taskb_time=b_time,
        taskb_condition=b_meta.get("condition", np.array([])),
        taskb_trial=b_meta.get("trial", np.array([])),
        taskb_count=b_meta.get("count", np.array([])),
        taskb_delta_ms=b_meta.get("delta_ms", np.array([])),
        taskb_soa=b_meta.get("soa", np.array([])),
        taskb_onset_time=b_meta.get("onset_time", np.array([])),
    )
    print("Saved:", out_path)

    # ---- 確認プロット（任意） ----
    if args.plot:
        plot_check(args.plot, a_rms, a_time, b_rms, b_time, chans)


def plot_check(path, a_rms, a_time, b_rms, b_time, chans):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, axes = plt.subplots(1, 2, figsize=(13, 4.5))
    ch0 = 0  # 保存配列の先頭ch（=chans[0]）

    # Task A: 平均 RMS（不随意窓 0〜100ms をハイライト）
    ax = axes[0]
    if a_rms.shape[0] > 0:
        mean_a = a_rms[:, ch0, :].mean(axis=0)
        sem_a = a_rms[:, ch0, :].std(axis=0) / np.sqrt(a_rms.shape[0])
        ax.plot(a_time, mean_a, color="tab:blue")
        ax.fill_between(a_time, mean_a - sem_a, mean_a + sem_a, alpha=0.3, color="tab:blue")
        ax.axvspan(0.0, 0.1, color="red", alpha=0.15, label="0-100ms (overflow)")
        ax.axvline(0, color="k", lw=0.8)
        ax.legend(fontsize=8)
    ax.set_title(f"Task A mean RMS (ch{chans[ch0]}, n={a_rms.shape[0]})")
    ax.set_xlabel("time from MotionOnset [s]")
    ax.set_ylabel("RMS")

    # Task B: 平均 RMS（運動準備窓 -500〜0ms をハイライト）
    ax = axes[1]
    if b_rms.shape[0] > 0:
        mean_b = b_rms[:, ch0, :].mean(axis=0)
        sem_b = b_rms[:, ch0, :].std(axis=0) / np.sqrt(b_rms.shape[0])
        ax.plot(b_time, mean_b, color="tab:green")
        ax.fill_between(b_time, mean_b - sem_b, mean_b + sem_b, alpha=0.3, color="tab:green")
        ax.axvspan(-0.5, 0.0, color="orange", alpha=0.15, label="-500-0ms (prep)")
        ax.axvline(0, color="k", lw=0.8)
        ax.legend(fontsize=8)
    ax.set_title(f"Task B mean RMS (ch{chans[ch0]}, n={b_rms.shape[0]})")
    ax.set_xlabel("time from FlexionDetected [s]")
    ax.set_ylabel("RMS")

    fig.tight_layout()
    fig.savefig(path, dpi=150, bbox_inches="tight")
    print("Saved plot:", path)


if __name__ == "__main__":
    main()
