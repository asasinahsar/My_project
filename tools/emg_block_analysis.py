# -*- coding: utf-8 -*-
"""
emg_block_analysis.py

汎用 EMG 解析（図は作らず数値とエポックのみ）。
- 1個以上の xdf を受け取り、各ファイルの BlockStart_{A,B}_{async,sync} /
  BlockEnd ラベルの時刻窓で TaskA(MotionOnset) / TaskB(FlexionDetected) を
  async/sync に振り分ける（単一ファイル・複数ファイルどちらも対応）。
- 4フィルタ・リニアエンベロープ（emg_linear_envelope.compute_stages 再利用）。
- TaskA overflow / TaskB prep・burst の窓平均（生＋baseline正規化, 4ch+agg）、
  TaskB 心理測定 PSE(τ_SoA) を算出して表示。
- エポック等を npz 保存（後でプロット可能）。

使い方:
    python emg_block_analysis.py sano.xdf --outdir <dir> --prefix sano
    python emg_block_analysis.py file1.xdf file2.xdf --outdir <dir> --prefix x   # 複数可
"""
import argparse
import os
import re
import sys

import numpy as np
import pyxdf
from scipy.stats import mannwhitneyu
from scipy.optimize import curve_fit

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from emg_linear_envelope import compute_stages, find_streams  # noqa: E402

RE_MO = re.compile(r"^MotionOnset_A_(\w+)")
RE_BS = re.compile(r"^BlockStart_([AB])_(async|sync)")
RE_BE = re.compile(r"^BlockEnd_([AB])_(async|sync)")
RE_FLEX = re.compile(r"^FlexionDetected_B_(\d+)_count(\d+)")
RE_TS = re.compile(r"^TrialStart_B_(\d+)_Delta(\d+)ms")
RE_SOA = re.compile(r"^SoA_(Yes|No)_Trial(\d+)")
CH_LABELS = ["ch0", "ch1", "ch2", "ch3"]


def load_envelope(path, chans, params):
    streams, _ = pyxdf.load_xdf(path)
    emg, markers = find_streams(streams)
    data = np.asarray(emg["time_series"], dtype=np.float64)
    ets = np.asarray(emg["time_stamps"])
    fs = float(emg["info"]["nominal_srate"][0])
    if fs <= 0:
        fs = (len(ets) - 1) / (ets[-1] - ets[0])
    env = compute_stages(data[:, chans], fs, *params)["envelope"]
    m_ts = np.asarray(markers["time_stamps"])
    m_lab = [s[0] if isinstance(s, (list, tuple)) else str(s) for s in markers["time_series"]]
    return env, ets, fs, m_ts, m_lab


def parse_blocks(m_ts, m_lab):
    """{(task,cond):(start_abs,end_abs)} を返す。複数回出現時は最早start・最遅end。"""
    starts, ends = {}, {}
    for t, l in zip(m_ts, m_lab):
        m = RE_BS.match(l)
        if m:
            k = (m.group(1), m.group(2))
            starts[k] = min(starts.get(k, float(t)), float(t))
        m = RE_BE.match(l)
        if m:
            k = (m.group(1), m.group(2))
            ends[k] = max(ends.get(k, float(t)), float(t))
    win = {}
    for k in starts:
        if k in ends:
            win[k] = (starts[k], ends[k])
    return win


def cond_of(t, task, win):
    for cond in ("async", "sync"):
        k = (task, cond)
        if k in win and win[k][0] <= t <= win[k][1]:
            return cond
    return None


def epoch(env, ets, onsets_abs, pre, post, fs):
    n_pre, n_post = int(round(pre * fs)), int(round(post * fs))
    et = np.arange(-n_pre, n_post) / fs
    out, kept = [], []
    for i, t in enumerate(onsets_abs):
        idx = int(np.searchsorted(ets, t))
        i0, i1 = idx - n_pre, idx + n_post
        if i0 < 0 or i1 > env.shape[0]:
            continue
        out.append(env[i0:i1, :].T)
        kept.append(i)
    if not out:
        return np.empty((0, env.shape[1], n_pre + n_post)), et, []
    return np.stack(out), et, kept


def baseline_normalize(ep, et, base_win):
    i0 = int(np.searchsorted(et, base_win[0]))
    i1 = int(np.searchsorted(et, base_win[1]))
    base = ep[:, :, i0:i1].mean(axis=2, keepdims=True)
    base = np.where(base <= 0, np.nan, base)
    return ep / base


def add_agg(ep_norm):
    agg = np.nanmean(ep_norm, axis=1, keepdims=True)
    return np.concatenate([ep_norm, agg], axis=1)


def window_mean(ep, et, win):
    i0 = int(np.searchsorted(et, win[0]))
    i1 = int(np.searchsorted(et, win[1]))
    return ep[:, :, i0:i1].mean(axis=2)


def mw(a, b):
    a, b = np.asarray(a), np.asarray(b)
    a, b = a[~np.isnan(a)], b[~np.isnan(b)]
    if len(a) > 1 and len(b) > 1:
        try:
            return mannwhitneyu(a, b, alternative="two-sided")[1]
        except Exception:
            return np.nan
    return np.nan


def logistic(x, pse, k):
    return 1.0 / (1.0 + np.exp(-(x - pse) / k))


def fit_psychometric(d, y):
    d, y = np.asarray(d, float), np.asarray(y, float)
    if len(d) < 4 or len(np.unique(d)) < 3:
        return None
    try:
        p0 = [np.median(d), max(10.0, np.std(d) / 2)]
        bounds = ([float(d.min()) - 50, 1.0], [float(d.max()) + 100, 1000.0])
        popt, _ = curve_fit(logistic, d, y, p0=p0, bounds=bounds, maxfev=10000)
        return popt
    except Exception:
        return None


def summarize(name, ep_raw, ep_norm, et, cond, wins):
    print(f"\n===== {name} window summary (norm | raw) =====")
    ep_norm_a = add_agg(ep_norm)
    labels = CH_LABELS + ["agg"]
    cond = np.asarray(cond)
    for wname, win in wins.items():
        wr = window_mean(ep_raw, et, win)
        wn = window_mean(ep_norm_a, et, win)
        print(f"-- {wname} {win}s --")
        for ci, lab in enumerate(labels):
            an = wn[cond == "async", ci]; sn = wn[cond == "sync", ci]
            line = f"   {lab:5s} norm async {np.nanmean(an):.3f} / sync {np.nanmean(sn):.3f}  p={mw(an,sn):.3g}"
            if ci < ep_raw.shape[1]:
                ar = wr[cond == "async", ci]; sr = wr[cond == "sync", ci]
                line += f"  | raw async {np.nanmean(ar):.2e} / sync {np.nanmean(sr):.2e}"
            print(line)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("xdf_paths", nargs="+")
    ap.add_argument("--outdir", default=None)
    ap.add_argument("--prefix", default="subject")
    ap.add_argument("--channels", nargs="+", default=["0", "1", "2", "3"])
    ap.add_argument("--bp-low", type=float, default=20.0)
    ap.add_argument("--bp-high", type=float, default=450.0)
    ap.add_argument("--notch-freq", type=float, default=50.0)
    ap.add_argument("--notch-q", type=float, default=30.0)
    ap.add_argument("--lp-cut", type=float, default=5.0)
    args = ap.parse_args()

    chans = [int(c) for c in args.channels]
    params = (args.bp_low, args.bp_high, args.notch_freq, args.notch_q, args.lp_cut)
    outdir = args.outdir or os.path.dirname(args.xdf_paths[0])

    A_PRE, A_POST = 0.5, 1.0
    B_PRE, B_POST = 1.0, 0.5
    epA_list, condA_list, fingerA_list = [], [], []
    epB_list, condB_list, deltaB_list, soaB_list = [], [], [], []
    trials = {}  # (file_idx, cond, trial) -> dict(delta, soa)
    etA = etB = None

    for fi, path in enumerate(args.xdf_paths):
        print(f"\nLoading [{fi}] {os.path.basename(path)} ...")
        env, ets, fs, m_ts, m_lab = load_envelope(path, chans, params)
        win = parse_blocks(m_ts, m_lab)
        print("  blocks:", {f"{a}_{c}": (round(s - ets[0], 1), round(e - ets[0], 1))
                             for (a, c), (s, e) in win.items()})

        # TaskA: MotionOnset -> cond by ('A',*) window
        a_t, a_fg = [], []
        for t, l in zip(m_ts, m_lab):
            m = RE_MO.match(l)
            if m:
                c = cond_of(float(t), "A", win)
                if c:
                    a_t.append(float(t)); a_fg.append((c, m.group(1)))
        ep, etA, kept = epoch(env, ets, a_t, A_PRE, A_POST, fs)
        epA_list.append(ep)
        condA_list += [a_fg[i][0] for i in kept]
        fingerA_list += [a_fg[i][1] for i in kept]

        # per-trial Δt/SoA (cond by window, key (fi,cond,trial))
        for t, l in zip(m_ts, m_lab):
            m = RE_TS.match(l)
            if m:
                c = cond_of(float(t), "B", win)
                if c:
                    trials[(fi, c, int(m.group(1)))] = dict(delta=int(m.group(2)), cond=c)
        for t, l in zip(m_ts, m_lab):
            m = RE_SOA.match(l)
            if m:
                c = cond_of(float(t), "B", win)
                key = (fi, c, int(m.group(2)))
                if key in trials:
                    trials[key]["soa"] = 1 if m.group(1) == "Yes" else 0

        # TaskB: FlexionDetected main (exclude Practice) -> cond by ('B',*)
        b_t, b_meta = [], []
        for t, l in zip(m_ts, m_lab):
            m = RE_FLEX.match(l)
            if m:
                c = cond_of(float(t), "B", win)
                if c is None:
                    continue
                tr = int(m.group(1))
                d = trials.get((fi, c, tr), {})
                b_t.append(float(t)); b_meta.append((c, d.get("delta", -1), d.get("soa", -1)))
        ep, etB, kept = epoch(env, ets, b_t, B_PRE, B_POST, fs)
        epB_list.append(ep)
        condB_list += [b_meta[i][0] for i in kept]
        deltaB_list += [b_meta[i][1] for i in kept]
        soaB_list += [b_meta[i][2] for i in kept]

    epA = np.concatenate(epA_list, axis=0); condA = np.array(condA_list)
    epB = np.concatenate(epB_list, axis=0); condB = np.array(condB_list)
    epA_norm = baseline_normalize(epA, etA, (-0.5, -0.3))
    epB_norm = baseline_normalize(epB, etB, (-1.0, -0.8))

    print(f"\n[{args.prefix}] TaskA epochs:", dict(zip(*np.unique(condA, return_counts=True))),
          " TaskB epochs:", dict(zip(*np.unique(condB, return_counts=True))))

    summarize(f"{args.prefix} TaskA", epA, epA_norm, etA, condA,
              {"overflow(0-100ms)": (0.0, 0.1), "overflow(0-300ms)": (0.0, 0.3),
               "baseline(-200-0ms)": (-0.2, 0.0)})
    summarize(f"{args.prefix} TaskB", epB, epB_norm, etB, condB,
              {"prep(-500-0ms)": (-0.5, 0.0), "burst(0-200ms)": (0.0, 0.2)})

    # 心理測定（図なし・PSE のみ）
    print(f"\n===== {args.prefix} TaskB psychometric (PSE=tau_SoA) =====")
    for c in ("async", "sync"):
        rows = [v for v in trials.values() if v.get("cond") == c and "soa" in v]
        if not rows:
            continue
        d = np.array([r["delta"] for r in rows]); y = np.array([r["soa"] for r in rows])
        popt = fit_psychometric(d, y)
        pse = f"{popt[0]:.0f}ms (k={popt[1]:.1f})" if popt is not None else "fit fail"
        print(f"  {c}: n={len(d)}  Yes_rate={y.mean():.2f}  PSE={pse}")

    npz = os.path.join(outdir, f"{args.prefix}_analysis.npz")
    np.savez_compressed(
        npz, fs=params and 1000, channels=np.array(chans), ch_labels=np.array(CH_LABELS),
        taskA_raw=epA.astype(np.float32), taskA_norm=epA_norm.astype(np.float32),
        taskA_time=etA, taskA_cond=condA, taskA_finger=np.array(fingerA_list),
        taskB_raw=epB.astype(np.float32), taskB_norm=epB_norm.astype(np.float32),
        taskB_time=etB, taskB_cond=condB,
        taskB_delta=np.array(deltaB_list), taskB_soa=np.array(soaB_list),
    )
    print("Saved:", npz)


if __name__ == "__main__":
    main()
