# -*- coding: utf-8 -*-
"""
emg_envelope_overview.py

複数の .xdf（分割記録）を時間軸で連結し、各 EMG チャンネルに
「4フィルタのリニアエンベロープ処理」を適用して、Unity マーカーと重ねた
1枚の overview 図（mion_plot 風）を出力する。

処理チェーン（emg_linear_envelope.compute_stages を再利用・すべて filtfilt ゼロ位相）:
    生EMG -> bandpass(20-450) -> notch(50Hz,Q30) -> 全波整流 -> lowpass(5Hz) = envelope

連結:
    入力 xdf を引数順に並べ、各ファイルの相対時刻を前ファイル終端の後ろへ
    オフセットして「1つながり」にする（既定 gap=0）。境界に破線と注記を描く。

依存: pyxdf, numpy, scipy, matplotlib
使い方:
    python emg_envelope_overview.py file1.xdf file2.xdf --outdir <dir> --prefix kawamoto \
        --channels 0 1 2 3
"""
import argparse
import os
import re
import sys

import numpy as np
import pyxdf

# 同じ tools/ 配下の既存スクリプトから関数を再利用（重複実装を避ける）
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from emg_linear_envelope import compute_stages, find_streams  # noqa: E402
from plot_emg_markers import marker_color  # noqa: E402


# テキストラベルを描く「構造マーカー」（trial 単位は線のみで可読性を確保）
STRUCT_RE = re.compile(
    r"^(BlockStart|BlockEnd|Task[AB]_(Start|End)|TaskB_End|Baseline|Induction|"
    r"Practice|PhaseSkipped|PhaseBack|Rest|ExpStart|ExpEnd|PreMainNotice)")


def process_file(path, chans, bp_low, bp_high, notch_freq, notch_q, lp_cut,
                 normalize=False, base_pct=10.0):
    """1ファイルを読み、選択chの envelope と相対時刻・マーカーを返す。
    normalize=True のとき、各ch をそのファイルの安静レベル（envelope の
    base_pct パーセンタイル＝動いていない時間の値）で割り「×安静」単位にする。
    別記録間のゲイン差・ch間のゲイン差を吸収し、比較可能にする。"""
    streams, _ = pyxdf.load_xdf(path)
    emg, markers = find_streams(streams)
    if emg is None:
        raise SystemExit(f"[error] EMG ストリームが見つかりません: {path}")

    emg_data = np.asarray(emg["time_series"], dtype=np.float64)
    emg_ts = np.asarray(emg["time_stamps"])
    fs = float(emg["info"]["nominal_srate"][0])
    if fs <= 0:
        fs = (len(emg_ts) - 1) / (emg_ts[-1] - emg_ts[0])

    # 共通原点（EMG とマーカーの早い方）— plot_emg_markers と同じ流儀
    t0 = emg_ts[0]
    m_ts = np.array([])
    m_labels = []
    if markers is not None and len(markers["time_stamps"]) > 0:
        m_ts = np.asarray(markers["time_stamps"])
        t0 = min(t0, m_ts[0])
        m_labels = [s[0] if isinstance(s, (list, tuple)) else str(s)
                    for s in markers["time_series"]]

    emg_t_rel = emg_ts - t0
    m_t_rel = (m_ts - t0) if len(m_ts) else np.array([])

    sub = emg_data[:, chans]
    env = compute_stages(sub, fs, bp_low, bp_high, notch_freq, notch_q, lp_cut)["envelope"]

    if normalize:
        base = np.percentile(env, base_pct, axis=0)        # (nch,) ファイル内安静レベル
        base = np.where(base <= 0, np.nan, base)
        env = env / base                                   # 「×安静」単位
        print(f"    baseline({base_pct}pct) per ch:", np.round(base, 5).tolist())

    return dict(fs=fs, env=env.astype(np.float32), t=emg_t_rel,
                m_t=m_t_rel, m_labels=m_labels,
                dur=float(emg_t_rel[-1]), name=os.path.basename(path))


def concat_files(results, gap):
    """相対時刻を連結。env/t/markers を 1 本につなぎ、境界情報を返す。"""
    env_parts, t_parts = [], []
    m_t_all, m_lab_all = [], []
    boundaries = []   # (offset, file_name) 各ファイルの開始位置
    offset = 0.0
    for r in results:
        boundaries.append((offset, r["name"]))
        env_parts.append(r["env"])
        t_parts.append(r["t"] + offset)
        if len(r["m_t"]):
            m_t_all.append(r["m_t"] + offset)
            m_lab_all.extend(r["m_labels"])
        offset += r["dur"] + gap
    env_cat = np.vstack(env_parts)
    t_cat = np.concatenate(t_parts)
    m_t_cat = np.concatenate(m_t_all) if m_t_all else np.array([])
    return env_cat, t_cat, m_t_cat, m_lab_all, boundaries


def plot_overview(path, env, t, m_t, m_labels, boundaries, chans, ch_labels,
                  file_notes, normalized=False):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    n_ch = len(chans)
    fig, axes = plt.subplots(n_ch, 1, figsize=(16, 2.6 * n_ch + 2.0),
                             sharex=True, squeeze=False)
    axes = axes[:, 0]

    struct = [(tt, lab) for tt, lab in zip(m_t, m_labels) if STRUCT_RE.match(lab)]

    # 正規化時は ch 間比較できるよう共通 Y軸（全ch 99.5 パーセンタイル）
    shared_hi = np.nanpercentile(env, 99.5) if normalized else None

    for ax, ci, lab in zip(axes, range(n_ch), ch_labels):
        ax.plot(t, env[:, ci], lw=0.5, color="tab:red")
        ax.set_ylabel((lab + "\n(×rest)") if normalized else lab)
        ax.grid(True, alpha=0.3)
        if normalized:
            ax.set_ylim(0, shared_hi * 1.1 if shared_hi > 0 else 1.0)
            ax.axhline(1.0, color="gray", lw=0.8, ls=":")  # 安静レベル
        else:
            hi = np.percentile(env[:, ci], 99.5)
            ax.set_ylim(0, hi * 1.1 if hi > 0 else 1e-3)
        # 全マーカーを薄い縦線で
        for tt, l in zip(m_t, m_labels):
            ax.axvline(tt, color=marker_color(l), lw=0.4, alpha=0.35)
        # ファイル境界（最初の境界 offset=0 は除く）
        for off, _ in boundaries[1:]:
            ax.axvline(off, color="k", lw=1.6, ls="--")

    # 上段に構造マーカーのテキスト
    top = axes[0]
    _, ymax = top.get_ylim()
    for tt, lab in struct:
        top.text(tt, ymax, " " + lab, rotation=90, fontsize=5,
                 va="bottom", ha="center", color=marker_color(lab), clip_on=False)

    # ファイル区間の注記（各ファイルの中央に条件メモ）
    for i, (off, name) in enumerate(boundaries):
        end = boundaries[i + 1][0] if i + 1 < len(boundaries) else float(t[-1])
        note = file_notes.get(name, name)
        axes[-1].annotate(f"{name}\n{note}", xy=((off + end) / 2, 0),
                          xytext=((off + end) / 2, -0.18),
                          textcoords=("data", "axes fraction"),
                          ha="center", va="top", fontsize=8, color="navy")

    axes[-1].set_xlabel("time [s] (concatenated, relative to file1 start)")
    norm_tag = "  [normalized: x rest per ch/file]" if normalized else "  [raw envelope]"
    fig.suptitle("EMG linear-envelope (4 filters) + Unity Markers  [concatenated]" + norm_tag,
                 y=0.995)
    fig.subplots_adjust(left=0.06, right=0.98, bottom=0.12, top=0.78, hspace=0.18)
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print("Saved:", path)


def main():
    ap = argparse.ArgumentParser(description="複数xdf連結 + 4フィルタ envelope overview")
    ap.add_argument("xdf_paths", nargs="+", help="入力xdf（連結順）")
    ap.add_argument("--outdir", default=None)
    ap.add_argument("--prefix", default="concat")
    ap.add_argument("--channels", nargs="+", default=["0", "1", "2", "3"])
    ap.add_argument("--ch-labels", nargs="+", default=None)
    ap.add_argument("--gap", type=float, default=0.0, help="連結時のファイル間ギャップ秒（既定0）")
    ap.add_argument("--bp-low", type=float, default=20.0)
    ap.add_argument("--bp-high", type=float, default=450.0)
    ap.add_argument("--notch-freq", type=float, default=50.0)
    ap.add_argument("--notch-q", type=float, default=30.0)
    ap.add_argument("--lp-cut", type=float, default=5.0)
    ap.add_argument("--normalize", action="store_true",
                    help="各ch をファイル内安静レベルで割り「×rest」単位に正規化（ch間・file間を比較可能に）")
    ap.add_argument("--base-pct", type=float, default=10.0,
                    help="安静レベル推定に使う envelope パーセンタイル（既定10）")
    args = ap.parse_args()

    chans = [int(c) for c in args.channels]
    ch_labels = args.ch_labels if args.ch_labels else [f"ch{c}" for c in chans]
    outdir = args.outdir or os.path.dirname(args.xdf_paths[0])

    results = []
    for p in args.xdf_paths:
        print("Processing:", p)
        r = process_file(p, chans, args.bp_low, args.bp_high,
                         args.notch_freq, args.notch_q, args.lp_cut,
                         normalize=args.normalize, base_pct=args.base_pct)
        print(f"  fs={r['fs']:.0f}  dur={r['dur']:.1f}s  markers={len(r['m_labels'])}")
        results.append(r)

    env, t, m_t, m_labels, boundaries = concat_files(results, args.gap)
    print(f"Concatenated: env={env.shape}  total_dur={t[-1]:.1f}s  markers={len(m_labels)}")
    print("Boundaries:", [(round(o, 1), n) for o, n in boundaries])

    # ファイルごとの条件メモ（kawamoto 用の既知ラベル。汎用には空）
    file_notes = {
        "kawamoto.xdf": "TaskB-async / TaskA-async / TaskB-sync",
        "kawamoto2.xdf": "TaskA-sync",
    }

    suffix = "_norm" if args.normalize else ""
    png = os.path.join(outdir, f"{args.prefix}_envelope_overview{suffix}.png")
    plot_overview(png, env, t, m_t, m_labels, boundaries, chans, ch_labels, file_notes,
                  normalized=args.normalize)

    npz = os.path.join(outdir, f"{args.prefix}_envelope_concat{suffix}.npz")
    np.savez_compressed(
        npz,
        fs=results[0]["fs"], channels=np.array(chans),
        ch_labels=np.array(ch_labels),
        bp_low=args.bp_low, bp_high=args.bp_high,
        notch_freq=args.notch_freq, notch_q=args.notch_q, lp_cut=args.lp_cut,
        envelope=env, t=t,
        marker_t=m_t, marker_labels=np.array(m_labels, dtype=object),
        boundaries_t=np.array([o for o, _ in boundaries]),
        boundaries_name=np.array([n for _, n in boundaries]),
    )
    print("Saved:", npz)


if __name__ == "__main__":
    main()
