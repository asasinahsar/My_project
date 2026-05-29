# -*- coding: utf-8 -*-
"""
plot_emg_markers.py

LabRecorder で記録した .xdf ファイルから EMG 波形と Unity マーカーを
共通の LSL 時刻軸で重ねてプロットする汎用スクリプト。

EMG ストリーム（数値・nominal_srate>0、type に "EMG" を含む or name="Delsys"）と
マーカーストリーム（type="Markers" / 文字列イベント）を自動検出する。

依存: pyxdf, numpy, matplotlib
    pip install pyxdf matplotlib

使い方の例:
    # ch1 をプロット（デフォルト）
    python plot_emg_markers.py untitled.xdf

    # ch1 と ch9 を並べてプロット
    python plot_emg_markers.py untitled.xdf --ch 1 9

    # 全16chを並べて構成を確認
    python plot_emg_markers.py untitled.xdf --ch all

    # SoA 関連マーカーだけ表示、120〜180秒にズーム、画像保存
    python plot_emg_markers.py untitled.xdf --ch 1 --marker-regex "SoA" --xmin 120 --xmax 180 --save out.png
"""
import argparse
import re
import sys

import numpy as np
import pyxdf


# マーカー名のプレフィックスごとの色（見やすさのためカテゴリ分け）
MARKER_COLOR_RULES = [
    (re.compile(r"^TrialStart"), "tab:blue"),
    (re.compile(r"^TrialEnd"), "tab:gray"),
    (re.compile(r"^FlexionDetected"), "tab:green"),
    (re.compile(r"^ResponseWindowStart"), "tab:orange"),
    (re.compile(r"^SoA_Yes"), "tab:red"),
    (re.compile(r"^SoA_No"), "tab:purple"),
    (re.compile(r"^AutoMotionStart|^MotionOnset"), "tab:cyan"),
    (re.compile(r"^BlockStart|^BlockEnd|^Rest"), "tab:brown"),
]
DEFAULT_MARKER_COLOR = "black"


def marker_color(name):
    for pattern, color in MARKER_COLOR_RULES:
        if pattern.search(name):
            return color
    return DEFAULT_MARKER_COLOR


def find_streams(streams):
    """EMG（数値）ストリームとマーカー（文字列）ストリームを自動検出する。"""
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
            # 最も信号の大きい数値ストリームを EMG とみなす（複数あれば最初）
            if emg_stream is None:
                emg_stream = st
    return emg_stream, marker_stream


def parse_channels(ch_arg, n_channels):
    """--ch 引数を解釈してチャンネルインデックスのリストを返す。"""
    if ch_arg is None:
        return [1] if n_channels > 1 else [0]
    if len(ch_arg) == 1 and str(ch_arg[0]).lower() == "all":
        return list(range(n_channels))
    result = []
    for c in ch_arg:
        idx = int(c)
        if idx < 0 or idx >= n_channels:
            print(f"[warn] ch{idx} は範囲外（0〜{n_channels-1}）のためスキップ")
            continue
        result.append(idx)
    if not result:
        print("[warn] 有効なチャンネルがありません。ch0 を使用します。")
        result = [0]
    return result


def main():
    ap = argparse.ArgumentParser(description="EMG 波形と Unity マーカーを重ねてプロット")
    ap.add_argument("xdf_path", help="入力 .xdf ファイルのパス")
    ap.add_argument("--ch", nargs="+", default=None,
                    help="プロットするEMGチャンネル（0始まり、複数可、'all'で全ch）。既定: 1")
    ap.add_argument("--marker-regex", default=None,
                    help="表示するマーカー名の正規表現フィルタ（例: 'SoA|TrialStart'）")
    ap.add_argument("--no-marker-labels", action="store_true",
                    help="マーカーのテキストラベルを描かない（縦線のみ）")
    ap.add_argument("--xmin", type=float, default=None, help="表示開始時刻（秒、相対）")
    ap.add_argument("--xmax", type=float, default=None, help="表示終了時刻（秒、相対）")
    ap.add_argument("--ylim", nargs=2, type=float, default=None, metavar=("LO", "HI"),
                    help="y軸範囲を手動指定（例: --ylim -0.2 0.2）。未指定なら表示範囲の信号に自動フィット")
    ap.add_argument("--save", default=None, help="画像保存パス（指定時は画面表示せず保存）")
    args = ap.parse_args()

    print("Loading:", args.xdf_path)
    streams, _ = pyxdf.load_xdf(args.xdf_path)
    emg, markers = find_streams(streams)

    if emg is None:
        print("[error] EMG（数値）ストリームが見つかりません。")
        sys.exit(1)

    emg_data = np.asarray(emg["time_series"])  # shape: (samples, channels)
    emg_ts = np.asarray(emg["time_stamps"])
    n_ch = emg_data.shape[1] if emg_data.ndim == 2 else 1
    emg_name = emg["info"]["name"][0] if emg["info"]["name"] else "EMG"
    print(f"EMG stream: '{emg_name}' samples={emg_data.shape[0]} channels={n_ch}")

    # 共通の時刻原点（EMG とマーカーの最も早い時刻を 0 とする相対秒）
    t0 = emg_ts[0]
    if markers is not None and len(markers["time_stamps"]) > 0:
        t0 = min(t0, markers["time_stamps"][0])
    emg_t = emg_ts - t0

    # マーカーの抽出とフィルタ
    marker_events = []  # (relative_time, label)
    if markers is not None:
        m_ts = np.asarray(markers["time_stamps"]) - t0
        m_series = markers["time_series"]
        regex = re.compile(args.marker_regex) if args.marker_regex else None
        for t, s in zip(m_ts, m_series):
            label = s[0] if isinstance(s, (list, tuple)) else str(s)
            if regex is not None and not regex.search(label):
                continue
            marker_events.append((t, label))
        print(f"Marker stream: {len(marker_events)} events"
              + (f" (filtered by /{args.marker_regex}/)" if regex else ""))
    else:
        print("[warn] マーカーストリームが見つかりません。EMG のみ描画します。")

    channels = parse_channels(args.ch, n_ch)
    print("Plotting channels:", channels)

    # ---- 描画 ----
    import matplotlib
    if args.save:
        matplotlib.use("Agg")  # 画面なし環境でも保存できるように
    import matplotlib.pyplot as plt

    # 描画する時間範囲を決定（指定がなければ EMG 全体）
    plot_xmin = args.xmin if args.xmin is not None else float(emg_t[0])
    plot_xmax = args.xmax if args.xmax is not None else float(emg_t[-1])

    # 範囲外のマーカーは描画しない（図が横に広がるのを防ぐ）
    visible_markers = [(t, lab) for (t, lab) in marker_events
                       if plot_xmin <= t <= plot_xmax]

    # 表示範囲内のサンプルだけを抽出（y軸オートスケール用）
    view_mask = (emg_t >= plot_xmin) & (emg_t <= plot_xmax)

    # マーカーラベルを描くかどうか（描く場合は figure 上部に余白を固定確保する）
    draw_labels = bool(visible_markers) and not args.no_marker_labels

    n_plots = len(channels)
    # 波形領域を十分確保するため、1ch あたりの高さを大きめにする
    fig_height = 2.8 * n_plots + 1.5
    fig, axes = plt.subplots(n_plots, 1, figsize=(14, fig_height),
                             sharex=True, squeeze=False)
    axes = axes[:, 0]

    for ax, ch in zip(axes, channels):
        y = emg_data[:, ch] if emg_data.ndim == 2 else emg_data
        ax.plot(emg_t, y, lw=0.4, color="steelblue")
        ax.set_ylabel(f"ch{ch}")
        ax.grid(True, alpha=0.3)
        ax.set_xlim(plot_xmin, plot_xmax)

        # y軸スケール: --ylim 指定があれば優先、なければ表示範囲内の信号に合わせて自動
        if args.ylim is not None:
            ax.set_ylim(args.ylim[0], args.ylim[1])
        else:
            yv = y[view_mask] if view_mask.any() else y
            if yv.size:
                lo, hi = np.percentile(yv, [0.5, 99.5])
                if hi <= lo:  # 信号がほぼ平坦な場合のフォールバック
                    c = float(np.median(yv))
                    lo, hi = c - 1e-3, c + 1e-3
                margin = (hi - lo) * 0.15
                ax.set_ylim(lo - margin, hi + margin)

        # マーカーを縦線で重畳（範囲内のみ）
        for t, label in visible_markers:
            ax.axvline(t, color=marker_color(label), lw=0.6, alpha=0.6)

    # マーカーラベルは最上段の軸の上に、軸の外へはみ出して描く（波形領域を圧迫しない）
    if draw_labels:
        top = axes[0]
        _, ymax = top.get_ylim()
        for t, label in visible_markers:
            top.text(t, ymax, " " + label, rotation=90, fontsize=6,
                     va="bottom", ha="center", color=marker_color(label),
                     clip_on=False)

    axes[-1].set_xlabel("time [s] (relative to recording start)")
    fig.suptitle(f"EMG ({emg_name}) + Unity Markers", y=0.995)

    # tight_layout はラベルを収めようと波形を潰すため使わず、
    # 上部にラベル用の固定余白（top）を確保する subplots_adjust を使う。
    top_margin = 0.62 if draw_labels else 0.90
    fig.subplots_adjust(left=0.07, right=0.98, bottom=0.10, top=top_margin,
                        hspace=0.25)

    if args.save:
        fig.savefig(args.save, dpi=150, bbox_inches="tight")
        print("Saved:", args.save)
    else:
        plt.show()


if __name__ == "__main__":
    main()
