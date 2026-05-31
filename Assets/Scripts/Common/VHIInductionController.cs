using UnityEngine;
using System.Collections;
using LSL;
using UnityEngine.Serialization;
using UnityVirtual.LSL;

public class VHIInductionController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [FormerlySerializedAs("markerSender")]
    [SerializeField] private MonoBehaviour markerSenderBehaviour;
    [SerializeField] private TaskAController taskAController; // 条件(sync/async)の取得用
    [Tooltip("VHI誘導 Phase 1 の筆なぞりアニメーション（未設定なら筆演出なし）")]
    [SerializeField] private BrushStrokeAnimator brushStrokeAnimator;
    [Tooltip("誘導開始から手を固定するまでの準備時間（秒）。被験者がテーブルに手を置く時間")]
    [SerializeField] private float prepSeconds = 15f;
    [Tooltip("筆なぞりの時間（秒）。手を固定している間")]
    [SerializeField] private float brushStrokeSeconds = 60f;

    private IMarkerSender markerSender;

    private void Awake()
    {
        markerSender = markerSenderBehaviour as IMarkerSender;
        if (markerSender == null)
        {
            Debug.LogWarning("[VHIInductionController] Marker sender is not assigned or does not implement IMarkerSender.");
        }
    }

    private void Start()
    {
        // ExperimentManagerのステート遷移イベントを購読
        ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
        }
    }

    private void HandleStateChanged(ExperimentState state)
    {
        switch (state)
        {
            case ExperimentState.TaskA_Induction:
                StartCoroutine(TaskAInductionRoutine());
                break;
            case ExperimentState.TaskA_Baseline:
                StartCoroutine(BaselineRoutine("A", taskAController.CurrentCondition));
                break;
            case ExperimentState.TaskB_Induction:
                StartCoroutine(TaskBInductionRoutine());
                break;
            case ExperimentState.TaskB_Baseline:
                StartCoroutine(BaselineRoutine("B", ""));
                break;
        }
    }

    private IEnumerator TaskAInductionRoutine()
    {
        string condition = taskAController.CurrentCondition;

        // v5.3 Phase D: async = 誘導なし、sync = 誘導あり に再定義。
        // 旧 v5.2 の「async = Δt=500ms 固定遅延」は廃止。誘導フローでは遅延を 0 に固定する。
        handVisualizer.delayMs = 0f;

        SendMarker($"InductionStart_A_{condition}");
        Debug.Log($"[VHI Induction] Task A Induction Started. Condition: {condition}");

        // 準備時間：被験者がテーブルに手を置いて静止（手はまだトラッキング継続）
        SendMarker($"InductionPrep_A_{condition}");
        yield return new WaitForSeconds(prepSeconds);

        // 手の表示を固定（トラッキング更新停止）→ 筆が手を隠してもカクつかない
        if (handVisualizer != null) handVisualizer.FreezePose = true;
        SendMarker($"HandFrozen_A_{condition}");

        // 筆なぞり開始（手は固定中）
        if (brushStrokeAnimator != null) brushStrokeAnimator.StartStroke();
        yield return new WaitForSeconds(brushStrokeSeconds);
        if (brushStrokeAnimator != null) brushStrokeAnimator.StopStroke();

        // 手の固定解除（通常トラッキングに復帰）
        if (handVisualizer != null) handVisualizer.FreezePose = false;
        SendMarker($"HandUnfrozen_A_{condition}");

        SendMarker($"InductionEnd_A_{condition}");
        // v5.3: VAS 全廃に伴い VASCheck をスキップして直接 Baseline へ
        Debug.Log("[VHI Induction] Task A Phase 1 Ended. Transitioning to Baseline.");

        ExperimentManager.Instance.ChangeState(ExperimentState.TaskA_Baseline);
    }

    private IEnumerator TaskBInductionRoutine()
    {
        // 誘導中は遅延なし（旧 Phase2 で設定していた delayMs=0 を冒頭へ移動）
        handVisualizer.delayMs = 0f;
        SendMarker("InductionStart_B");
        Debug.Log("[VHI Induction] Task B Induction Started.");

        // 準備時間：被験者がテーブルに手を置いて静止（手はまだトラッキング継続）
        SendMarker("InductionPrep_B");
        yield return new WaitForSeconds(prepSeconds);

        // 手の表示を固定（トラッキング更新停止）→ 筆が手を隠してもカクつかない
        if (handVisualizer != null) handVisualizer.FreezePose = true;
        SendMarker("HandFrozen_B");

        // 筆なぞり開始（手は固定中）
        if (brushStrokeAnimator != null) brushStrokeAnimator.StartStroke();
        yield return new WaitForSeconds(brushStrokeSeconds);
        if (brushStrokeAnimator != null) brushStrokeAnimator.StopStroke();

        // 手の固定解除（通常トラッキングに復帰）
        if (handVisualizer != null) handVisualizer.FreezePose = false;
        SendMarker("HandUnfrozen_B");

        // 旧 Phase 2（慣らし随意運動60秒）は廃止。両タスクとも 60秒筆なぞりのみに統一。
        SendMarker("InductionEnd_B");
        // v5.3: VAS 全廃に伴い VASCheck をスキップして直接 Baseline へ
        Debug.Log("[VHI Induction] Task B Induction Ended. Transitioning to Baseline.");

        ExperimentManager.Instance.ChangeState(ExperimentState.TaskB_Baseline);
    }

    private IEnumerator BaselineRoutine(string task, string condition)
    {
        string markerSuffix = task == "A" ? $"_{task}_{condition}" : $"_{task}";
        
        SendMarker($"BaselineStart{markerSuffix}");
        Debug.Log($"[VHI Induction] Baseline {task} Started. Please keep hand static for 30s.");

        // 本番用待機時間（30秒）
        yield return new WaitForSeconds(30f);

        SendMarker($"BaselineEnd{markerSuffix}");
        
        // メインタスクへ自動遷移
        if (task == "A")
            ExperimentManager.Instance.ChangeState(ExperimentState.TaskA_Main);
        else
            ExperimentManager.Instance.ChangeState(ExperimentState.TaskB_Main);
    }

    private void SendMarker(string marker)
    {
        if (markerSender == null) return;
        markerSender.SendMarker(marker);
    }

    public void AbortInduction()
    {
        StopAllCoroutines();
        // D: 中断時も筆なぞりを停止・非表示
        if (brushStrokeAnimator != null) brushStrokeAnimator.StopStroke();
        if (handVisualizer != null)
        {
            handVisualizer.delayMs = 0f;
            handVisualizer.isAutoMode = false;
            // 中断時は手の固定を必ず解除（通常トラッキングに復帰）
            handVisualizer.FreezePose = false;
        }
    }
}
