# -*- coding: utf-8 -*-
"""
emg_kawamoto_analysis.py

被験者 kawamoto（4ch sEMG, 2ファイル分割）の本解析。
- ファイル＝条件が既知（ナビ暴走なし）:
    file1(kawamoto.xdf) : TaskB-async / TaskA-async / TaskB-sync
    file2(kawamoto2.xdf): TaskA-sync
- 4フィルタ・リニアエンベロープ（emg_linear_envelope.compute_stages を再利用）
- TaskA(overflow/SoO) と TaskB(SoA) の両方をフル解析
- 生エンベロープ と baseline 正規化 の両方を出力
- 弱い指屈曲を横断検出するための 4ch アグリゲート(正規化平均)も併記

条件付与（推測なし・正規ラベル/ファイルから）:
    TaskA: file1 の MotionOnset = async / file2 の MotionOnset = sync
    TaskB: file1 のみ。BlockStart_B_async..BlockEnd_B_async = async /
           BlockStart_B_sync..BlockEnd_B_sync = sync（実マーカー時刻で窓決定）

依存: pyxdf, numpy, scipy, matplotlib
使い方:
    python emg_kawamoto_analysis.py file1.xdf file2.xdf --outdir <dir> --prefix kawamoto
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

RE_MOTIONONSET = re.compile(r"^MotionOnset_A_(\w+)")
RE_BLOCK_B_START = re.compile(r"^BlockStart_B_(async|sync)")
RE_BLOCK_B_END = re.compile(r"^BlockEnd_B_(async|sync)")
RE_FLEXION = re.compile(r"^FlexionDetected_B_(\d+)_count(\d+)")
RE_TRIALSTART_B = re.compile(r"^TrialStart_B_(\d+)_Delta(\d+)ms")
RE_SOA = re.compile(r"^SoA_(Yes|No)_Trial(\d+)")

CH_LABELS = ["ch0 radial-prox", "ch1 radial-dist", "ch2 ulnar-prox", "ch3 ulnar-dist"]
COND_COLOR = {"async": "tab:red", "sync": "tab:blue"}


# ----------------------------------------------------------------------
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


def epoch(env, ets, onsets_abs, pre, post, fs):
    n_pre, n_post = int(round(pre * fs)), int(round(post * fs))
    et = np.arange(-n_pre, n_post) / fs
    out, kept_idx = [], []
    for i, t in enumerate(onsets_abs):
        idx = int(np.searchsorted(ets, t))
        i0, i1 = idx - n_pre, idx + n_post
        if i0 < 0 or i1 > env.shape[0]:
            continue
        out.append(env[i0:i1, :].T)  # (nch, nsamp)
        kept_idx.append(i)
    if not out:
        return np.empty((0, env.shape[1], n_pre + n_post)), et, []
    return np.stack(out), et, kept_idx


def baseline_normalize(ep, et, base_win):
    """各エポック・各chを base_win 平均で割る（比）。agg 用に正規化版を返す。"""
    i0 = int(np.searchsorted(et, base_win[0]))
    i1 = int(np.searchsorted(et, base_win[1]))
    base = ep[:, :, i0:i1].mean(axis=2, keepdims=True)  # (n,nch,1)
    base = np.where(base <= 0, np.nan, base)
    return ep / base


# ----------------------------------------------------------------------
def parse_taskb(m_ts, m_lab):
    """file1 のマーカーから TaskB のブロック窓・per-trial 表・flexion イベントを得る。"""
    # ブロック窓
    starts, ends = {}, {}
    for t, l in zip(m_ts, m_lab):
        m = RE_BLOCK_B_START.match(l)
        if m:
            starts[m.group(1)] = float(t)
        m = RE_BLOCK_B_END.match(l)
        if m:
            ends[m.group(1)] = float(t)
    windows = {c: (starts[c], ends[c]) for c in starts if c in ends}

    def cond_of(t):
        for c, (a, b) in windows.items():
            if a <= t <= b:
                return c
        return None

    # per-trial: Δt, cond
    # 注意: trial番号はブロックごとに 1.. でリセットされるため (cond, trial) でキー化する
    trials = {}
    for t, l in zip(m_ts, m_lab):
        m = RE_TRIALSTART_B.match(l)
        if m:
            tr = int(m.group(1))
            c = cond_of(float(t))
            if c is None:
                continue
            trials[(c, tr)] = dict(delta=int(m.group(2)), cond=c)
    # SoA（ラベルに cond は無いので時刻で判定）
    for t, l in zip(m_ts, m_lab):
        m = RE_SOA.match(l)
        if m:
            tr = int(m.group(2))
            c = cond_of(float(t))
            if (c, tr) in trials:
                trials[(c, tr)]["soa"] = 1 if m.group(1) == "Yes" else 0

    # flexion イベント（本番のみ。Practice 除外）
    flex = []
    for t, l in zip(m_ts, m_lab):
        m = RE_FLEXION.match(l)
        if m:
            tr = int(m.group(1))
            c = cond_of(float(t))
            if c is None:
                continue
            d = trials.get((c, tr), {})
            flex.append(dict(t=float(t), trial=tr, cond=c,
                             delta=d.get("delta", -1), soa=d.get("soa", -1)))
    return windows, trials, flex


def parse_taska_onsets(m_ts, m_lab):
    return [float(t) for t, l in zip(m_ts, m_lab) if RE_MOTIONONSET.match(l)]


# ----------------------------------------------------------------------
def logistic(x, pse, k):
    """Yes率の増加ロジスティック（Δt大→「非同期検出 Yes」増）。
    pse = Yes率50%となる Δt（検出閾値）, k = 傾きスケール。"""
    return 1.0 / (1.0 + np.exp(-(x - pse) / k))


def fit_psychometric(deltas, soas):
    deltas, soas = np.asarray(deltas, float), np.asarray(soas, float)
    if len(deltas) < 4 or len(np.unique(deltas)) < 3:
        return None
    try:
        p0 = [np.median(deltas), max(10.0, np.std(deltas) / 2)]
        bounds = ([float(deltas.min()) - 50.0, 1.0],
                  [float(deltas.max()) + 100.0, 1000.0])
        popt, _ = curve_fit(logistic, deltas, soas, p0=p0, bounds=bounds, maxfev=10000)
        return popt  # (pse, k)
    except Exception:
        return None


# ----------------------------------------------------------------------
def mw(a, b):
    a, b = np.asarray(a), np.asarray(b)
    a, b = a[~np.isnan(a)], b[~np.isnan(b)]
    if len(a) > 1 and len(b) > 1:
        try:
            return mannwhitneyu(a, b, alternative="two-sided")[1]
        except Exception:
            return np.nan
    return np.nan


def window_mean(ep, et, win):
    i0 = int(np.searchsorted(et, win[0]))
    i1 = int(np.searchsorted(et, win[1]))
    return ep[:, :, i0:i1].mean(axis=2)  # (n, nch)


def add_agg(ep_norm):
    """正規化エポックの 4ch 平均を 1ch 追加した配列を返す（agg を最後のch扱い）。"""
    agg = np.nanmean(ep_norm, axis=1, keepdims=True)  # (n,1,nsamp)
    return np.concatenate([ep_norm, agg], axis=1)


# ----------------------------------------------------------------------
def plot_task(path, title, ep_raw, ep_norm, et, conds, hi_win, xlabel):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    labels = CH_LABELS + ["agg (norm mean 4ch)"]
    ep_norm_a = add_agg(ep_norm)
    conds = np.asarray(conds)
    nrow = len(labels)
    fig, axes = plt.subplots(nrow, 2, figsize=(13, 2.3 * nrow), squeeze=False)

    for r in range(nrow):
        for col, (data, tag) in enumerate([(ep_raw, "raw"), (ep_norm_a, "norm")]):
            ax = axes[r, col]
            if tag == "raw" and r == nrow - 1:
                ax.axis("off"); continue  # agg は raw 無し
            for cond in ("async", "sync"):
                sel = conds == cond
                if sel.sum() == 0:
                    continue
                m = np.nanmean(data[sel, r, :], axis=0)
                s = np.nanstd(data[sel, r, :], axis=0) / np.sqrt(max(1, sel.sum()))
                ax.plot(et, m, color=COND_COLOR[cond], lw=1.2,
                        label=f"{cond} (n={int(sel.sum())})")
                ax.fill_between(et, m - s, m + s, color=COND_COLOR[cond], alpha=0.2)
            ax.axvspan(hi_win[0], hi_win[1], color="orange", alpha=0.12)
            ax.axvline(0, color="k", lw=0.7)
            ax.set_ylabel(labels[r], fontsize=8)
            if r == 0:
                ax.set_title(tag, fontsize=10)
                ax.legend(fontsize=7)
    axes[-1, 1].set_xlabel(xlabel)
    axes[-2, 0].set_xlabel(xlabel)
    fig.suptitle(title, fontsize=12)
    fig.tight_layout(rect=[0, 0, 1, 0.985])
    fig.savefig(path, dpi=140, bbox_inches="tight")
    plt.close(fig)
    print("Saved:", path)


def plot_psychometric(path, trials):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, ax = plt.subplots(figsize=(7.5, 5.5))
    summary = {}
    for cond in ("async", "sync"):
        rows = [v for v in trials.values() if v.get("cond") == cond and "soa" in v]
        if not rows:
            continue
        d = np.array([r["delta"] for r in rows], float)
        y = np.array([r["soa"] for r in rows], float)
        # 生データ散布（jitter）
        ax.scatter(d + (3 if cond == "sync" else -3), y + np.random.uniform(-0.02, 0.02, len(y)),
                   color=COND_COLOR[cond], alpha=0.5, s=25,
                   label=f"{cond} trials (n={len(d)})")
        # Δt ビンの Yes 率
        bins = np.linspace(d.min(), d.max(), 6)
        idx = np.digitize(d, bins)
        bx, by = [], []
        for b in range(1, len(bins) + 1):
            sel = idx == b
            if sel.sum() >= 2:
                bx.append(d[sel].mean()); by.append(y[sel].mean())
        ax.plot(bx, by, "o-", color=COND_COLOR[cond], lw=1.0, alpha=0.7)
        # ロジスティック近似
        popt = fit_psychometric(d, y)
        if popt is not None:
            xx = np.linspace(d.min(), d.max(), 200)
            ax.plot(xx, logistic(xx, *popt), color=COND_COLOR[cond], lw=2.0)
            summary[cond] = dict(pse=popt[0], k=popt[1], n=len(d), yes_rate=float(y.mean()))
        else:
            summary[cond] = dict(pse=None, k=None, n=len(d), yes_rate=float(y.mean()))
    ax.axhline(0.5, color="gray", ls=":", lw=0.8)
    ax.set_xlabel("Delta t [ms]"); ax.set_ylabel("SoA Yes rate")
    ax.set_title("TaskB psychometric: SoA Yes-rate vs Delta t")
    ax.legend(fontsize=8)
    fig.tight_layout(); fig.savefig(path, dpi=140, bbox_inches="tight")
    plt.close(fig)
    print("Saved:", path)
    return summary


# ----------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("file1"); ap.add_argument("file2")
    ap.add_argument("--outdir", default=None)
    ap.add_argument("--prefix", default="kawamoto")
    ap.add_argument("--channels", nargs="+", default=["0", "1", "2", "3"])
    ap.add_argument("--bp-low", type=float, default=20.0)
    ap.add_argument("--bp-high", type=float, default=450.0)
    ap.add_argument("--notch-freq", type=float, default=50.0)
    ap.add_argument("--notch-q", type=float, default=30.0)
    ap.add_argument("--lp-cut", type=float, default=5.0)
    args = ap.parse_args()

    chans = [int(c) for c in args.channels]
    params = (args.bp_low, args.bp_high, args.notch_freq, args.notch_q, args.lp_cut)
    outdir = args.outdir or os.path.dirname(args.file1)

    print("Loading file1 ...")
    env1, ets1, fs1, m1_ts, m1_lab = load_envelope(args.file1, chans, params)
    print("Loading file2 ...")
    env2, ets2, fs2, m2_ts, m2_lab = load_envelope(args.file2, chans, params)

    # ===== TaskA =====
    a_async_on = parse_taska_onsets(m1_ts, m1_lab)  # file1 = async
    a_sync_on = parse_taska_onsets(m2_ts, m2_lab)   # file2 = sync
    A_PRE, A_POST = 0.5, 1.0
    epA_a, etA, _ = epoch(env1, ets1, a_async_on, A_PRE, A_POST, fs1)
    epA_s, _, _ = epoch(env2, ets2, a_sync_on, A_PRE, A_POST, fs2)
    epA_raw = np.concatenate([epA_a, epA_s], axis=0)
    condA = np.array(["async"] * len(epA_a) + ["sync"] * len(epA_s))
    epA_norm = baseline_normalize(epA_raw, etA, (-0.5, -0.3))
    print(f"TaskA epochs: async={len(epA_a)} sync={len(epA_s)}")

    # ===== TaskB（file1のみ）=====
    windows, trials, flex = parse_taskb(m1_ts, m1_lab)
    _rt = ets1[0]
    print("TaskB block windows (rel s):",
          {k: (round(a - _rt, 1), round(b - _rt, 1)) for k, (a, b) in windows.items()})
    B_PRE, B_POST = 1.0, 0.5
    flex_t = [f["t"] for f in flex]
    epB_raw, etB, keptB = epoch(env1, ets1, flex_t, B_PRE, B_POST, fs1)
    condB = np.array([flex[i]["cond"] for i in keptB])
    epB_norm = baseline_normalize(epB_raw, etB, (-1.0, -0.8))
    ub, cb = np.unique(condB, return_counts=True)
    print("TaskB flexion epochs:", dict(zip(ub.tolist(), cb.tolist())))

    # ===== プロット =====
    pngA = os.path.join(outdir, f"{args.prefix}_taskA_overflow.png")
    pngB = os.path.join(outdir, f"{args.prefix}_taskB_flexion.png")
    pngP = os.path.join(outdir, f"{args.prefix}_taskB_psychometric.png")
    plot_task(pngA, "TaskA: envelope around MotionOnset (overflow 0-100ms)",
              epA_raw, epA_norm, etA, condA, (0.0, 0.1), "time from MotionOnset [s]")
    plot_task(pngB, "TaskB: envelope around FlexionDetected (prep -500-0ms)",
              epB_raw, epB_norm, etB, condB, (-0.5, 0.0), "time from FlexionDetected [s]")
    psy = plot_psychometric(pngP, trials)

    # ===== 窓平均サマリ =====
    def summarize(ep_raw, ep_norm, et, cond, wins, name):
        print(f"\n===== {name} 窓平均サマリ =====")
        ep_norm_a = add_agg(ep_norm)
        labels = CH_LABELS + ["agg"]
        cond = np.asarray(cond)
        for wname, win in wins.items():
            wr = window_mean(ep_raw, et, win)            # (n,nch)
            wn = window_mean(ep_norm_a, et, win)         # (n,nch+1)
            print(f"-- {wname} {win}s --")
            for ci, lab in enumerate(labels):
                src = wr if ci < ep_raw.shape[1] else None
                a_n = wn[cond == "async", ci]; s_n = wn[cond == "sync", ci]
                line = f"   {lab:18s} norm: async {np.nanmean(a_n):.3f} / sync {np.nanmean(s_n):.3f}  p={mw(a_n,s_n):.3g}"
                if src is not None:
                    a_r = wr[cond == "async", ci]; s_r = wr[cond == "sync", ci]
                    line += f"  | raw: async {np.nanmean(a_r):.2e} / sync {np.nanmean(s_r):.2e}"
                print(line)

    summarize(epA_raw, epA_norm, etA, condA,
              {"overflow(0-100ms)": (0.0, 0.1), "overflow(0-300ms)": (0.0, 0.3),
               "baseline(-200-0ms)": (-0.2, 0.0)}, "TaskA")
    summarize(epB_raw, epB_norm, etB, condB,
              {"prep(-500-0ms)": (-0.5, 0.0), "burst(0-200ms)": (0.0, 0.2)}, "TaskB")

    print("\n===== TaskB psychometric =====")
    for c, v in psy.items():
        pse = f"{v['pse']:.0f}ms" if v["pse"] is not None else "fit失敗"
        print(f"  {c}: Yes率={v['yes_rate']:.2f} (n={v['n']})  PSE(τ_SoA)={pse}  k={v['k'] if v['k'] else '-'}")

    # ===== npz 保存 =====
    npz = os.path.join(outdir, f"{args.prefix}_analysis.npz")
    np.savez_compressed(
        npz, fs=fs1, channels=np.array(chans), ch_labels=np.array(CH_LABELS),
        taskA_raw=epA_raw.astype(np.float32), taskA_norm=epA_norm.astype(np.float32),
        taskA_time=etA, taskA_cond=condA,
        taskB_raw=epB_raw.astype(np.float32), taskB_norm=epB_norm.astype(np.float32),
        taskB_time=etB, taskB_cond=condB,
        taskB_delta=np.array([flex[i]["delta"] for i in keptB]),
        taskB_soa=np.array([flex[i]["soa"] for i in keptB]),
    )
    print("Saved:", npz)


if __name__ == "__main__":
    main()
