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
        
        // 空間的オフセットの適用（syncなら0cm, asyncなら2cm遠位）
        handVisualizer.SetAsyncOffset(condition == "async");
        
        SendMarker($"InductionStart_A_{condition}");
        Debug.Log($"[VHI Induction] Task A Phase 1 (Brush Stroking) Started. Condition: {condition}");

        // 本番用待機時間（120秒）
        yield return new WaitForSeconds(120f);

        SendMarker($"InductionEnd_A_{condition}");
        Debug.Log("[VHI Induction] Task A Phase 1 Ended. Transitioning to VAS Check.");

        // VAS確認ステートへ自動遷移
        ExperimentManager.Instance.ChangeState(ExperimentState.TaskA_VASCheck);
    }

    private IEnumerator TaskBInductionRoutine()
    {
        SendMarker("InductionStart_B");
        Debug.Log("[VHI Induction] Task B Phase 1 (Brush Stroking) Started.");

        // 本番用待機時間（60秒）
        yield return new WaitForSeconds(60f);

        // Phase 2: 慣らし随意運動（能動的SoA最大化） 60秒
        // Δt = 0ms（遅延なし）に設定して完全同期させる
        handVisualizer.delayMs = 0f;
        SendMarker("ActiveMovementStart_B");
        Debug.Log("[VHI Induction] Task B Phase 2 (Active Movement, Δt=0) Started.");

        // 本番用待機時間（60秒）
        yield return new WaitForSeconds(60f);

        SendMarker("ActiveMovementEnd_B");
        SendMarker("InductionEnd_B");
        Debug.Log("[VHI Induction] Task B Induction Ended. Transitioning to VAS Check.");

        // VAS確認ステートへ自動遷移
        ExperimentManager.Instance.ChangeState(ExperimentState.TaskB_VASCheck);
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
        if (handVisualizer != null)
        {
            handVisualizer.SetAsyncOffset(false);
            handVisualizer.delayMs = 0f;
            handVisualizer.isAutoMode = false;
        }
    }
}
