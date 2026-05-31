using System.Collections;
using UnityEngine;

/// <summary>
/// VHI誘導 Phase 1（筆なぞり）の視覚アニメーション。
///
/// 既存の筆モデル（<see cref="brush"/>）を、なぞり経路の始点（<see cref="strokeStart"/>）と
/// 終点（<see cref="strokeEnd"/>）の間で往復移動させる。経路の2点をバーチャル左手のボーン配下に
/// 配置しておけば、手のトラッキングに筆が追従する（手の甲を手首→指先方向になぞる想定）。
///
/// VHIInductionController から StartStroke()/StopStroke() で制御する。
/// </summary>
public class BrushStrokeAnimator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("筆モデルの Transform（既存モデルをアタッチ）")]
    [SerializeField] private Transform brush;
    [Tooltip("なぞり始点（手首側）。バーチャル左手のボーン配下に配置すると手に追従する")]
    [SerializeField] private Transform strokeStart;
    [Tooltip("なぞり終点（指先側）。同上")]
    [SerializeField] private Transform strokeEnd;

    [Header("Motion Settings")]
    [Tooltip("片道の所要時間（秒）。小さいほど速くなぞる")]
    [SerializeField] private float oneWayDuration = 2.0f;
    [Tooltip("筆先を経路の進行方向へ向ける（オフなら筆モデルの向きを固定）")]
    [SerializeField] private bool orientToPath = false;
    [Tooltip("停止時に筆を非表示にする")]
    [SerializeField] private bool hideBrushWhenStopped = true;

    private Coroutine strokeCoroutine;

    private void Start()
    {
        // 初期は非表示（誘導 Phase 1 でのみ表示）
        if (hideBrushWhenStopped && brush != null)
            brush.gameObject.SetActive(false);
    }

    /// <summary>
    /// 筆なぞりアニメーションを開始する。
    /// </summary>
    public void StartStroke()
    {
        if (brush == null || strokeStart == null || strokeEnd == null)
        {
            Debug.LogWarning("[BrushStrokeAnimator] brush / strokeStart / strokeEnd が未設定です。Inspector を確認してください。");
            return;
        }

        if (strokeCoroutine != null) StopCoroutine(strokeCoroutine);
        brush.gameObject.SetActive(true);
        strokeCoroutine = StartCoroutine(StrokeRoutine());
    }

    /// <summary>
    /// 筆なぞりアニメーションを停止し、筆を非表示にする。
    /// </summary>
    public void StopStroke()
    {
        if (strokeCoroutine != null)
        {
            StopCoroutine(strokeCoroutine);
            strokeCoroutine = null;
        }
        if (hideBrushWhenStopped && brush != null)
            brush.gameObject.SetActive(false);
    }

    private IEnumerator StrokeRoutine()
    {
        // strokeStart ↔ strokeEnd を往復（PingPong）。
        // 始点・終点はワールド座標で毎フレーム参照するため、手に追従する。
        while (true)
        {
            yield return MoveBrush(strokeStart, strokeEnd);
            yield return MoveBrush(strokeEnd, strokeStart);
        }
    }

    private IEnumerator MoveBrush(Transform from, Transform to)
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, oneWayDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, t / dur);
            brush.position = Vector3.Lerp(from.position, to.position, u);

            if (orientToPath)
            {
                Vector3 dir = (to.position - from.position);
                if (dir.sqrMagnitude > 1e-6f)
                    brush.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            yield return null;
        }
    }
}
