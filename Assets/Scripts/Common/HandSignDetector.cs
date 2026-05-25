using System;
using UnityEngine;

/// <summary>
/// v5.3 Phase E1 改訂: 左手のピンチ（親指 + 人差し指タッチ）ハンドサインを検出する独立コンポーネント。
///
/// 親指先端（<see cref="thumbTip"/>）と人差し指先端（<see cref="indexTip"/>）の距離が
/// <see cref="pinchThreshold"/> 以下になったとき「ピンチ成立」と判定し
/// <see cref="OnHandSignDetected"/> を発火する。Task B 回答フェーズの Yes 申告検出に使用する。
///
/// 計測フェーズでの屈曲運動と誤認しないよう、<see cref="EnableDetection"/> フラグで
/// 回答フェーズのみ検出を有効化する設計。
///
/// 注意: Meta Quest 標準のピンチ（PinchSelect）と衝突しないよう、Unity 側で
/// 左手 Interactor（Poke / Near-Far / DirectSelect）を無効化することが前提
/// （Experiment.md §4.3 参照）。
/// </summary>
public class HandSignDetector : MonoBehaviour
{
    [Header("Joint References")]
    [Tooltip("左手親指先端の Transform")]
    [SerializeField] private Transform thumbTip;

    [Tooltip("左手人差し指先端の Transform")]
    [SerializeField] private Transform indexTip;

    [Header("Detection Thresholds")]
    [Tooltip("親指先端と人差し指先端の距離がこの値以下のときピンチと判定（m 単位）。Meta Quest 標準と同等の 0.025 推奨")]
    [SerializeField] private float pinchThreshold = 0.025f;

    [Tooltip("一度検出してから次の検出までの最小間隔（秒）。連続発火防止用")]
    [SerializeField] private float detectionCooldown = 1.0f;

    /// <summary>
    /// 検出を有効化するフラグ。回答フェーズ開始時に true、終了時に false にする。
    /// false の間はクールダウンタイマーもリセットされ、イベント発火しない。
    /// </summary>
    public bool EnableDetection { get; set; } = false;

    /// <summary>
    /// ハンドサインを検出したときに発火するイベント。
    /// </summary>
    public event Action OnHandSignDetected;

    /// <summary>
    /// 直近フレームの親指-人差し指間距離（デバッグ・しきい値調整用、m 単位）。
    /// </summary>
    public float CurrentPinchDistance { get; private set; } = float.PositiveInfinity;

    private float lastDetectionTime = -Mathf.Infinity;

    private void Update()
    {
        // 両 Tip が設定されていない場合はスキップ
        if (thumbTip == null || indexTip == null)
        {
            CurrentPinchDistance = float.PositiveInfinity;
            return;
        }

        CurrentPinchDistance = Vector3.Distance(thumbTip.position, indexTip.position);

        // 検出が無効ならクールダウンもリセットして抜ける
        if (!EnableDetection)
        {
            lastDetectionTime = -Mathf.Infinity;
            return;
        }

        // クールダウン中は再発火しない
        if (Time.time - lastDetectionTime < detectionCooldown) return;

        // ピンチ判定
        if (CurrentPinchDistance < pinchThreshold)
        {
            lastDetectionTime = Time.time;
            OnHandSignDetected?.Invoke();
        }
    }

    /// <summary>
    /// 現在のフレームでピンチ状態かを同期的に判定する（EnableDetection / クールダウン無視）。
    /// テスト・デバッグ用。通常は OnHandSignDetected イベントを購読する。
    /// </summary>
    public bool IsHandSignDetectedNow()
    {
        return CurrentPinchDistance < pinchThreshold;
    }
}
