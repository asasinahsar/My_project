# -*- coding: utf-8 -*-
"""
emg_taskb_multivariate.py

TaskB の多変量 EMG 指標を算出（図なし・数値のみ）。保存済み <prefix>_analysis.npz
（taskB_raw, taskB_time, taskB_cond, taskB_delta）から計算する。

指標（条件 async/sync 別）:
  1) τ_SoA-EMG : 運動準備窓(-500..0ms)の 4ch パターンについて、同期参照(Δt≤50ms)で
     作った分布からのマハラノビス距離が、Δt 増で有意に増える閾値。
     - 参照分布の共分散は Ledoit-Wolf 縮小推定（小標本に頑健）。
     - Δt をビン化して各ビンの平均距離を出し、参照の95%ile を超える最小 Δt を τ_SoA とする。
     - 併せて 距離 vs Δt の Spearman 相関（連続評価）。
  2) PRI : 準備窓 4ch パターン（L2正規化）の試行間 平均コサイン類似度（一貫性）。
  3) SCR : 屈曲バースト窓(0..200ms)の 4ch を NMF で筋シナジー分解（k は VAF≥90% 自動）。
     同期参照(Δt≤50ms)から参照シナジー W_ref を抽出し、各 Δt ビンを W_ref で
     NNLS 再構成した VAF_ref の低下（1-VAF）の Δt 傾き＝SCR（手首屈曲協調の崩れ率）。

依存: numpy, scipy, scikit-learn
使い方:
    python emg_taskb_multivariate.py sub1_analysis.npz sub2_analysis.npz ...
"""
import argparse
import os

import numpy as np
from scipy.optimize import nnls
from scipy.stats import spearmanr, mannwhitneyu
from sklearn.covariance import LedoitWolf
from sklearn.decomposition import NMF

PREP_WIN = (-0.5, 0.0)
BURST_WIN = (0.0, 0.2)
REF_DELTA_MAX = 50.0          # 同期参照とみなす Δt 上限(ms)
EVAL_BINS = [50, 150, 250, 350, 1e9]  # 評価 Δt ビン境界(ms)


def win_mean(raw, t, win):
    i0, i1 = np.searchsorted(t, win[0]), np.searchsorted(t, win[1])
    return raw[:, :, i0:i1].mean(axis=2)   # (n, nch)


def bin_centers_and_idx(delta):
    """評価ビン(>REF)へ割当。戻り: list of (center, mask)。"""
    out = []
    for a, b in zip(EVAL_BINS[:-1], EVAL_BINS[1:]):
        m = (delta > a) & (delta <= b)
        if m.sum() > 0:
            out.append((float(delta[m].mean()), m))
    return out


def tau_soa_emg(prep, delta, cond, c):
    sel = (cond == c) & (delta >= 0)
    P, D = prep[sel], delta[sel]
    ref = D <= REF_DELTA_MAX
    if ref.sum() < 5:
        return None
    lw = LedoitWolf().fit(P[ref])
    mu, VI = lw.location_, lw.precision_
    def maha(X):
        d = X - mu
        return np.sqrt(np.einsum("ij,jk,ik->i", d, VI, d))
    dref = maha(P[ref])
    thr = np.percentile(dref, 95)
    rows, tau = [], None
    for center, m in bin_centers_and_idx(D):
        dm = maha(P[m]).mean()
        rows.append((center, dm, int(m.sum())))
        if tau is None and dm > thr:
            tau = center
    dall = maha(P)
    rho, p = spearmanr(D, dall)
    return dict(ref_n=int(ref.sum()), ref_mean=float(dref.mean()), thr95=float(thr),
                bins=rows, tau=tau, spearman=(float(rho), float(p)))


def pri(prep, cond, c):
    V = prep[cond == c]
    if len(V) < 3:
        return None
    n = V / (np.linalg.norm(V, axis=1, keepdims=True) + 1e-12)
    G = n @ n.T
    iu = np.triu_indices(len(V), k=1)
    return float(G[iu].mean())


def pick_k_vaf(X, thresh=0.90, kmax=4):
    """VAF≥thresh となる最小 k（NMF）を返す。"""
    tot = (X ** 2).sum()
    for k in range(1, min(kmax, X.shape[1]) + 1):
        nmf = NMF(n_components=k, init="nndsvda", max_iter=2000, random_state=0)
        W = nmf.fit_transform(X)
        H = nmf.components_
        vaf = 1 - ((X - W @ H) ** 2).sum() / tot
        if vaf >= thresh:
            return k, H, vaf
    return k, H, vaf  # 最大kでも届かなければ最大kを返す


def vaf_with_ref(X, Href):
    """X(各行=4ch) を Href(k×4) で NNLS 再構成した VAF。"""
    tot = (X ** 2).sum()
    if tot <= 0:
        return np.nan
    err = 0.0
    A = Href.T  # (4×k)
    for x in X:
        a, _ = nnls(A, x)
        err += ((x - A @ a) ** 2).sum()
    return 1 - err / tot


def scr(burst, delta, cond, c):
    sel = (cond == c) & (delta >= 0)
    X, D = burst[sel], delta[sel]
    X = np.clip(X, 0, None)  # NMF は非負
    ref = D <= REF_DELTA_MAX
    if ref.sum() < 3:
        return None
    k, Href, vaf_ref = pick_k_vaf(X[ref])
    rows = []
    # 参照ビンも含めて (1-VAF) を Δt で
    xs, ys = [], []
    for center, m in [(float(D[ref].mean()), ref)] + bin_centers_and_idx(D):
        v = vaf_with_ref(X[m], Href)
        rows.append((center, float(v), int(m.sum())))
        xs.append(center); ys.append(1 - v)
    xs, ys = np.array(xs), np.array(ys)
    slope = np.polyfit(xs, ys, 1)[0] if len(xs) >= 2 else np.nan  # per ms
    return dict(k=int(k), vaf_ref=float(vaf_ref), Href=Href.round(3).tolist(),
                bins=rows, scr_per100ms=float(slope * 100))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("npz_paths", nargs="+")
    args = ap.parse_args()

    for path in args.npz_paths:
        prefix = os.path.basename(path).replace("_analysis.npz", "")
        d = np.load(path, allow_pickle=True)
        raw, t = d["taskB_raw"], d["taskB_time"]
        cond = d["taskB_cond"].astype(str)
        delta = d["taskB_delta"].astype(float)
        prep = win_mean(raw, t, PREP_WIN)
        burst = win_mean(raw, t, BURST_WIN)

        print("\n" + "=" * 70)
        print(f"[{prefix}] TaskB multivariate  (flexions per cond:",
              dict(zip(*np.unique(cond, return_counts=True))), ")")

        # PRI（async/sync 比較）
        pra, prs = pri(prep, cond, "async"), pri(prep, cond, "sync")
        print(f"\n PRI (prep-pattern consistency, cosine 0-1):  async={pra:.4f}  sync={prs:.4f}")

        for c in ("async", "sync"):
            print(f"\n --- condition: {c} ---")
            ts = tau_soa_emg(prep, delta, cond, c)
            if ts:
                print(f"  tau_SoA-EMG: ref(dt<={REF_DELTA_MAX:.0f}ms) n={ts['ref_n']} "
                      f"mean={ts['ref_mean']:.2f} thr95={ts['thr95']:.2f}")
                for ctr, dm, n in ts["bins"]:
                    flag = " <-- EXCEED" if dm > ts["thr95"] else ""
                    print(f"      dt~{ctr:5.0f}ms  maha={dm:.2f} (n={n}){flag}")
                tau = f"{ts['tau']:.0f}ms" if ts["tau"] is not None else ">max (none)"
                print(f"      => tau_SoA-EMG = {tau}   |  Spearman(dist vs dt) "
                      f"rho={ts['spearman'][0]:+.2f} p={ts['spearman'][1]:.3g}")
            sc = scr(burst, delta, cond, c)
            if sc:
                print(f"  SCR: NMF k={sc['k']} (VAF_ref={sc['vaf_ref']:.2f})  "
                      f"W_ref(synergy x 4ch)={sc['Href']}")
                for ctr, v, n in sc["bins"]:
                    print(f"      dt~{ctr:5.0f}ms  VAF_ref={v:.3f} (n={n})")
                print(f"      => SCR (delta(1-VAF)/100ms) = {sc['scr_per100ms']:+.4f}")

        # 保存
        outdir = os.path.dirname(path)
        out = os.path.join(outdir, f"{prefix}_taskb_multivar.npz")
        np.savez_compressed(out, prefix=prefix,
                            PRI_async=pra if pra else np.nan,
                            PRI_sync=prs if prs else np.nan)
        print(" Saved:", out)


if __name__ == "__main__":
    main()
