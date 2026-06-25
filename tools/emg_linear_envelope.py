# -*- coding: utf-8 -*-
"""
emg_linear_envelope.py

mion.xdf 等の Delsys sEMG に「標準的な4フィルタのリニアエンベロープ処理」を適用し、
全区間エンベロープ・段階別波形プロット・sync/async エポック比較を出力する。

処理チェーン（全選択chに適用・すべて filtfilt ゼロ位相）:
    生EMG
      1) Bandpass : butter 4次, 20-450Hz
      2) Notch    : iirnotch 50Hz, Q=30（商用電源・東日本）
      3) Rectify  : 全波整流 np.abs
      4) Low-pass : butter 4次, 5Hz  → リニアエンベロープ
  ※ RMS 包絡線（emg_preprocess_epochs.py）とは別物。こちらは整流後ローパスの
     リニアエンベロープ。

condition 再ラベル:
    mion.xdf は記録冒頭のナビ暴走で BlockStart の condition が全 sync に化けている。
    そこで MotionOnset_A / FlexionDetected_B の時刻列を「最大ギャップ」で2ブロックに
    分割し、実験順序（各タスク 1番目=async / 2番目=sync）で condition を付与する。

依存: pyxdf, numpy, scipy, matplotlib
使い方:
    python emg_linear_envelope.py "C:\\Users\\koike\\Downloads\\mion\\mion.xdf" \
        --outdir "C:\\Users\\koike\\Downloads"
"""
import argparse
import os
import re

import numpy as np
import pyxdf
from scipy.signal import butter, filtfilt, iirnotch, welch


# ----------------------------------------------------------------------
# ストリーム検出
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
# 4フィルタ（各段を返す）
# ----------------------------------------------------------------------
def bandpass_filter(data, fs, low, high):
    """butter 4次バンドパス・ゼロ位相。data: (samples, ch)"""
    nyq = fs / 2.0
    high = min(high, nyq * 0.99)
    b, a = butter(4, [low / nyq, high / nyq], btype="band")
    return filtfilt(b, a, data, axis=0)


def notch_filter(data, fs, freq, q):
    """iirnotch ノッチ・ゼロ位相（商用電源除去）。"""
    b, a = iirnotch(freq, q, fs)
    return filtfilt(b, a, data, axis=0)


def lowpass_filter(data, fs, cutoff):
    """butter 4次ローパス・ゼロ位相（リニアエンベロープ生成用）。"""
    nyq = fs / 2.0
    b, a = butter(4, cutoff / nyq, btype="low")
    return filtfilt(b, a, data, axis=0)


def compute_stages(data, fs, bp_low, bp_high, notch_freq, notch_q, lp_cut):
    """4フィルタを順に適用し、各段の波形を dict で返す。"""
    bp = bandpass_filter(data, fs, bp_low, bp_high)
    nt = notch_filter(bp, fs, notch_freq, notch_q)
    rect = np.abs(nt)
    env = lowpass_filter(rect, fs, lp_cut)
    env = np.clip(env, 0.0, None)  # 数値的アンダーシュートを 0 で打ち切り
    return {"raw": data, "bandpass": bp, "notch": nt, "rectified": rect, "envelope": env}


# ----------------------------------------------------------------------
# マーカー抽出 & ギャップ分割によるブロック再ラベル
# ----------------------------------------------------------------------
RE_MOTIONONSET = re.compile(r"^MotionOnset_A_(\w+)")
RE_FLEXION = re.compile(r"^FlexionDetected_B_(\d+)_count(\d+)")
RE_TRIALSTART_B = re.compile(r"^TrialStart_B_(\d+)_Delta(\d+)ms")
RE_SOA = re.compile(r"^SoA_(Yes|No)_Trial(\d+)")


def collect_onsets(m_ts_abs, m_labels, pattern, extract):
    """pattern にマッチするマーカーの (abs_time, *extract(match)) を時刻順で返す。"""
    out = []
    for t, label in zip(m_ts_abs, m_labels):
        m = pattern.match(label)
        if m:
            out.append((float(t),) + tuple(extract(m)))
    out.sort(key=lambda r: r[0])
    return out


def split_blocks_by_gap(abs_times):
    """時刻列を最大ギャップで2分割し、各要素のブロック index(0/1) を返す。
    1ブロックしか無い場合は全て 0。"""
    abs_times = np.asarray(abs_times, dtype=float)
    if len(abs_times) < 2:
        return np.zeros(len(abs_times), dtype=int), None
    d = np.diff(abs_times)
    k = int(np.argmax(d))               # 最大ギャップの直前 index
    boundary = (abs_times[k] + abs_times[k + 1]) / 2.0
    blk = (abs_times >= boundary).astype(int)
    return blk, boundary


def build_taska_events(m_ts_abs, m_labels):
    rows = collect_onsets(m_ts_abs, m_labels, RE_MOTIONONSET, lambda m: (m.group(1),))
    times = [r[0] for r in rows]
    blk, boundary = split_blocks_by_gap(times)
    # 1番目ブロック=async, 2番目=sync（実験順序）
    cond_map = {0: "async", 1: "sync"}
    events = []
    for (t, finger), b in zip(rows, blk):
        events.append(dict(onset_abs=t, condition=cond_map[b], finger=finger, block=int(b)))
    return events, boundary


def build_taskb_events(m_ts_abs, m_labels):
    # Δt / SoA の trial→値マップ
    delta_map, soa_map = {}, {}
    for label in m_labels:
        m = RE_TRIALSTART_B.match(label)
        if m:
            delta_map[int(m.group(1))] = int(m.group(2))
        m = RE_SOA.match(label)
        if m:
            soa_map[int(m.group(2))] = 1 if m.group(1) == "Yes" else 0

    rows = collect_onsets(m_ts_abs, m_labels, RE_FLEXION,
                          lambda m: (int(m.group(1)), int(m.group(2))))
    times = [r[0] for r in rows]
    blk, boundary = split_blocks_by_gap(times)
    cond_map = {0: "async", 1: "sync"}
    events = []
    for (t, trial, count), b in zip(rows, blk):
        events.append(dict(onset_abs=t, condition=cond_map[b], trial=trial, count=count,
                           delta_ms=delta_map.get(trial, -1), soa=soa_map.get(trial, -1),
                           block=int(b)))
    return events, boundary


# ----------------------------------------------------------------------
# エポック切り出し（リニアエンベロープ）
# ----------------------------------------------------------------------
def cut_epochs(env, emg_t, events, pre_sec, post_sec, fs):
    n_pre = int(round(pre_sec * fs))
    n_post = int(round(post_sec * fs))
    epoch_time = np.arange(-n_pre, n_post) / fs
    ep_list, kept = [], []
    for ev in events:
        idx = int(np.searchsorted(emg_t, ev["onset_abs"]))
        i0, i1 = idx - n_pre, idx + n_post
        if i0 < 0 or i1 > env.shape[0]:
            print(f"[skip] onset={ev['onset_abs']:.3f} はエポック範囲が記録端を超過")
            continue
        ep_list.append(env[i0:i1, :].T)   # (n_ch, n_samples)
        kept.append(ev)
    if not kept:
        return np.empty((0, env.shape[1], n_pre + n_post)), epoch_time, []
    return np.stack(ep_list), epoch_time, kept


# ----------------------------------------------------------------------
# プロット
# ----------------------------------------------------------------------
def plot_stages(path, stages, emg_t, fs, win_center_abs, ch, ch_label,
                notch_freq, bp_low, bp_high):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    # 代表区間（±2s）
    i_c = int(np.searchsorted(emg_t, win_center_abs))
    half = int(2.0 * fs)
    i0, i1 = max(0, i_c - half), min(len(emg_t), i_c + half)
    tt = (emg_t[i0:i1] - emg_t[i_c])

    fig, axes = plt.subplots(5, 2, figsize=(14, 11),
                             gridspec_kw={"width_ratios": [2.2, 1]})

    order = [("raw", "1) raw", "tab:gray"),
             ("bandpass", "2) bandpass 20-450Hz", "tab:blue"),
             ("notch", f"3) notch {notch_freq:.0f}Hz", "tab:purple"),
             ("rectified", "4) full-wave rectified", "tab:orange"),
             ("envelope", "5) low-pass envelope", "tab:red")]

    # 左列：時間波形（段階別）
    for r, (key, title, col) in enumerate(order):
        ax = axes[r, 0]
        ax.plot(tt, stages[key][i0:i1, ch], color=col, lw=0.6)
        if key == "rectified":
            ax.plot(tt, stages["envelope"][i0:i1, ch], color="tab:red", lw=1.6,
                    label="envelope")
            ax.legend(fontsize=8, loc="upper right")
        ax.set_title(f"{title}   ({ch_label})", fontsize=9, loc="left")
        ax.set_ylabel("amp")
        if r < 4:
            ax.set_xticklabels([])
    axes[4, 0].set_xlabel("time from window center [s]")

    # 右列：PSD（raw / bandpass / notch）で 50Hz 除去と帯域制限を可視化
    def psd(sig):
        f, p = welch(sig, fs=fs, nperseg=min(4096, len(sig)))
        return f, p
    ax = axes[0, 1]
    for key, col, lab in [("raw", "tab:gray", "raw"),
                          ("bandpass", "tab:blue", "bandpass"),
                          ("notch", "tab:purple", "notch")]:
        f, p = psd(stages[key][i0:i1, ch])
        ax.semilogy(f, p, color=col, lw=1.0, label=lab)
    ax.axvline(notch_freq, color="r", ls="--", lw=0.8, label=f"{notch_freq:.0f}Hz")
    ax.axvline(bp_low, color="k", ls=":", lw=0.6)
    ax.axvline(bp_high, color="k", ls=":", lw=0.6)
    ax.set_xlim(0, 500)
    ax.set_title("PSD (raw vs bandpass vs notch)", fontsize=9, loc="left")
    ax.set_xlabel("Hz"); ax.set_ylabel("power")
    ax.legend(fontsize=8)

    # 右列：50Hz 近傍ズーム
    ax = axes[1, 1]
    for key, col in [("bandpass", "tab:blue"), ("notch", "tab:purple")]:
        f, p = psd(stages[key][i0:i1, ch])
        ax.semilogy(f, p, color=col, lw=1.2)
    ax.axvline(notch_freq, color="r", ls="--", lw=0.8)
    ax.set_xlim(notch_freq - 15, notch_freq + 15)
    ax.set_title(f"PSD zoom @ {notch_freq:.0f}Hz (blue=bp, purple=notch)", fontsize=9, loc="left")
    ax.set_xlabel("Hz")

    for r in range(2, 5):
        axes[r, 1].axis("off")

    fig.suptitle("EMG linear-envelope filter stages", fontsize=12)
    fig.tight_layout(rect=[0, 0, 1, 0.98])
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print("Saved plot:", path)


def _mean_sem(ep, ch):
    m = ep[:, ch, :].mean(axis=0)
    s = ep[:, ch, :].std(axis=0) / np.sqrt(max(1, ep.shape[0]))
    return m, s


def plot_syncasync(path, a_ep, a_time, a_meta, b_ep, b_time, b_meta,
                   chans, ch_labels):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    n_ch = len(chans)
    fig, axes = plt.subplots(2, n_ch, figsize=(6.2 * n_ch, 8), squeeze=False)
    cond_color = {"async": "tab:red", "sync": "tab:blue"}

    a_cond = np.asarray(a_meta["condition"])
    b_cond = np.asarray(b_meta["condition"])

    for ci in range(n_ch):
        # --- Task A（MotionOnset 基準, overflow 0-100ms）---
        ax = axes[0, ci]
        for cond in ("async", "sync"):
            sel = a_cond == cond
            if sel.sum() == 0:
                continue
            m, s = _mean_sem(a_ep[sel], ci)
            ax.plot(a_time, m, color=cond_color[cond], label=f"{cond} (n={int(sel.sum())})")
            ax.fill_between(a_time, m - s, m + s, color=cond_color[cond], alpha=0.25)
        ax.axvspan(0.0, 0.1, color="red", alpha=0.12)
        ax.axvline(0, color="k", lw=0.8)
        ax.set_title(f"Task A  {ch_labels[ci]}  (overflow 0-100ms)", fontsize=10)
        ax.set_xlabel("time from MotionOnset [s]"); ax.set_ylabel("envelope")
        ax.legend(fontsize=8)

        # --- Task B（FlexionDetected 基準, prep -500-0ms）---
        ax = axes[1, ci]
        for cond in ("async", "sync"):
            sel = b_cond == cond
            if sel.sum() == 0:
                continue
            m, s = _mean_sem(b_ep[sel], ci)
            ax.plot(b_time, m, color=cond_color[cond], label=f"{cond} (n={int(sel.sum())})")
            ax.fill_between(b_time, m - s, m + s, color=cond_color[cond], alpha=0.25)
        ax.axvspan(-0.5, 0.0, color="orange", alpha=0.12)
        ax.axvline(0, color="k", lw=0.8)
        ax.set_title(f"Task B  {ch_labels[ci]}  (prep -500-0ms)", fontsize=10)
        ax.set_xlabel("time from FlexionDetected [s]"); ax.set_ylabel("envelope")
        ax.legend(fontsize=8)

    fig.suptitle("Linear-envelope: sync vs async", fontsize=12)
    fig.tight_layout(rect=[0, 0, 1, 0.97])
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print("Saved plot:", path)


# ----------------------------------------------------------------------
# 窓平均の要約統計（考察用）
# ----------------------------------------------------------------------
def window_summary(ep, ep_time, meta, chans, ch_labels, win, win_name, fs):
    from scipy.stats import mannwhitneyu
    cond = np.asarray(meta["condition"])
    i0 = int(np.searchsorted(ep_time, win[0]))
    i1 = int(np.searchsorted(ep_time, win[1]))
    print(f"\n--- {win_name}  window {win[0]}..{win[1]}s ---")
    for ci, lab in enumerate(ch_labels):
        vals = ep[:, ci, i0:i1].mean(axis=1)  # 各エポックの窓平均
        a = vals[cond == "async"]; s = vals[cond == "sync"]
        line = f"  {lab}: async {a.mean():.4e}±{a.std()/np.sqrt(max(1,len(a))):.1e} (n={len(a)})  " \
               f"sync {s.mean():.4e}±{s.std()/np.sqrt(max(1,len(s))):.1e} (n={len(s)})"
        if len(a) > 1 and len(s) > 1:
            try:
                u, p = mannwhitneyu(a, s, alternative="two-sided")
                line += f"  Mann-Whitney p={p:.3g}"
            except Exception:
                pass
        print(line)


# ----------------------------------------------------------------------
# メイン
# ----------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description="EMG 4フィルタ・リニアエンベロープ + sync/async 解析")
    ap.add_argument("xdf_path")
    ap.add_argument("--outdir", default=None, help="出力先（既定: xdf と同じフォルダ）")
    ap.add_argument("--prefix", default=None, help="出力ファイル接頭辞（既定: xdf のベース名）")
    ap.add_argument("--channels", nargs="+", default=["0", "1"], help="解析chの0始まり index（既定 0 1）")
    ap.add_argument("--ch-labels", nargs="+", default=["FCR (ch0)", "ch1 (FDS?)"])
    ap.add_argument("--bp-low", type=float, default=20.0)
    ap.add_argument("--bp-high", type=float, default=450.0)
    ap.add_argument("--notch-freq", type=float, default=50.0)
    ap.add_argument("--notch-q", type=float, default=30.0)
    ap.add_argument("--lp-cut", type=float, default=5.0)
    ap.add_argument("--taska-pre", type=float, default=0.5)
    ap.add_argument("--taska-post", type=float, default=1.0)
    ap.add_argument("--taskb-pre", type=float, default=1.0)
    ap.add_argument("--taskb-post", type=float, default=0.5)
    args = ap.parse_args()

    print("Loading:", args.xdf_path)
    streams, _ = pyxdf.load_xdf(args.xdf_path)
    emg, markers = find_streams(streams)
    if emg is None or markers is None:
        raise SystemExit("[error] EMG / Marker ストリームが見つかりません")

    emg_data = np.asarray(emg["time_series"], dtype=np.float64)
    emg_t = np.asarray(emg["time_stamps"])
    fs = float(emg["info"]["nominal_srate"][0])
    if fs <= 0:
        fs = (len(emg_t) - 1) / (emg_t[-1] - emg_t[0])
    n_ch_total = emg_data.shape[1]
    print(f"EMG: {emg_data.shape[0]} samples x {n_ch_total} ch, fs={fs:.2f} Hz, dur={emg_t[-1]-emg_t[0]:.1f}s")

    chans = [int(c) for c in args.channels if 0 <= int(c) < n_ch_total]
    ch_labels = args.ch_labels[:len(chans)] if args.ch_labels else [f"ch{c}" for c in chans]
    print("Channels:", chans, "labels:", ch_labels)

    # ---- 4フィルタ（選択chのみ：メモリ節約）----
    print("Filtering: bandpass -> notch -> rectify -> lowpass envelope ...")
    sub = emg_data[:, chans]
    stages = compute_stages(sub, fs, args.bp_low, args.bp_high,
                            args.notch_freq, args.notch_q, args.lp_cut)
    env = stages["envelope"]  # (samples, n_ch)

    # ---- マーカー & ブロック再ラベル ----
    m_ts_abs = np.asarray(markers["time_stamps"])
    m_labels = [s[0] if isinstance(s, (list, tuple)) else str(s) for s in markers["time_series"]]
    a_events, a_boundary = build_taska_events(m_ts_abs, m_labels)
    b_events, b_boundary = build_taskb_events(m_ts_abs, m_labels)
    t0 = emg_t[0]
    print(f"TaskA events: {len(a_events)} (gap boundary @ {a_boundary - t0:.1f}s rel)")
    print(f"TaskB events: {len(b_events)} (gap boundary @ {b_boundary - t0:.1f}s rel)")
    for name, evs in [("TaskA", a_events), ("TaskB", b_events)]:
        u, c = np.unique([e["condition"] for e in evs], return_counts=True)
        print(f"  {name} condition:", dict(zip(u.tolist(), c.tolist())))

    # ---- エポック切り出し ----
    a_ep, a_time, a_kept = cut_epochs(env, emg_t, a_events, args.taska_pre, args.taska_post, fs)
    b_ep, b_time, b_kept = cut_epochs(env, emg_t, b_events, args.taskb_pre, args.taskb_post, fs)
    print(f"TaskA epochs: {a_ep.shape}   TaskB epochs: {b_ep.shape}")

    def meta_of(kept, keys):
        return {k: np.array([e[k] for e in kept]) for k in keys}
    a_meta = meta_of(a_kept, ["condition", "finger", "block", "onset_abs"])
    b_meta = meta_of(b_kept, ["condition", "trial", "count", "delta_ms", "soa", "block", "onset_abs"])

    # ---- 出力パス ----
    outdir = args.outdir or os.path.dirname(args.xdf_path)
    prefix = args.prefix or os.path.splitext(os.path.basename(args.xdf_path))[0]
    npz_path = os.path.join(outdir, f"{prefix}_linear_envelope.npz")
    stage_png = os.path.join(outdir, f"{prefix}_linear_envelope.png")
    sa_png = os.path.join(outdir, f"{prefix}_le_syncasync.png")

    # ---- 保存（envelope は float32 で容量半減）----
    np.savez_compressed(
        npz_path,
        fs=fs, channels=np.array(chans), ch_labels=np.array(ch_labels),
        bp_low=args.bp_low, bp_high=args.bp_high,
        notch_freq=args.notch_freq, notch_q=args.notch_q, lp_cut=args.lp_cut,
        envelope=env.astype(np.float32), emg_time=emg_t,
        taska_env=a_ep.astype(np.float32), taska_time=a_time,
        taska_condition=a_meta["condition"], taska_finger=a_meta["finger"],
        taska_onset_abs=a_meta["onset_abs"],
        taskb_env=b_ep.astype(np.float32), taskb_time=b_time,
        taskb_condition=b_meta["condition"], taskb_trial=b_meta["trial"],
        taskb_delta_ms=b_meta["delta_ms"], taskb_soa=b_meta["soa"],
        taskb_onset_abs=b_meta["onset_abs"],
    )
    print("Saved:", npz_path)

    # ---- 段階プロット（活動の強い区間：TaskB async の代表 onset を中心）----
    if len(b_kept):
        center = b_kept[min(5, len(b_kept) - 1)]["onset_abs"]
    else:
        center = emg_t[len(emg_t) // 2]
    plot_stages(stage_png, stages, emg_t, fs, center, ch=0, ch_label=ch_labels[0],
                notch_freq=args.notch_freq, bp_low=args.bp_low, bp_high=args.bp_high)

    # ---- sync/async 比較プロット ----
    plot_syncasync(sa_png, a_ep, a_time, a_meta, b_ep, b_time, b_meta, chans, ch_labels)

    # ---- 窓平均の要約統計（考察用）----
    print("\n================ 窓平均サマリ ================")
    if a_ep.shape[0]:
        window_summary(a_ep, a_time, a_meta, chans, ch_labels, (0.0, 0.1), "Task A overflow", fs)
        window_summary(a_ep, a_time, a_meta, chans, ch_labels, (-0.2, 0.0), "Task A baseline(-200-0ms)", fs)
    if b_ep.shape[0]:
        window_summary(b_ep, b_time, b_meta, chans, ch_labels, (-0.5, 0.0), "Task B prep", fs)
        window_summary(b_ep, b_time, b_meta, chans, ch_labels, (0.0, 0.2), "Task B post(0-200ms)", fs)
    print("\nDone.")


if __name__ == "__main__":
    main()
