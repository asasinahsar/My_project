using UnityEngine;
using System.Collections;

public class VHIInductionController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [SerializeField] private LSLMarkerSender markerSender;
    [SerializeField] private TaskAController taskAController; // 条件(sync/async)の取得用

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
        
        markerSender.SendMarker($"InductionStart_A_{condition}");
        Debug.Log($"[VHI Induction] Task A Phase 1 (Brush Stroking) Started. Condition: {condition}");

        // Phase 1: 筆なぞり（受動的SoO最大化） 120秒
        yield return new WaitForSeconds(120f);

        markerSender.SendMarker($"InductionEnd_A_{condition}");
        Debug.Log("[VHI Induction] Task A Phase 1 Ended. Transitioning to VAS Check.");

        // VAS確認ステートへ自動遷移
        ExperimentManager.Instance.ChangeState(ExperimentState.TaskA_VASCheck);
    }

    private IEnumerator TaskBInductionRoutine()
    {
        markerSender.SendMarker("InductionStart_B");
        Debug.Log("[VHI Induction] Task B Phase 1 (Brush Stroking) Started.");

        // Phase 1: 筆なぞり 60秒
        yield return new WaitForSeconds(60f);

        // Phase 2: 慣らし随意運動（能動的SoA最大化） 60秒
        // Δt = 0ms（遅延なし）に設定して完全同期させる
        handVisualizer.delayMs = 0f;
        markerSender.SendMarker("ActiveMovementStart_B");
        Debug.Log("[VHI Induction] Task B Phase 2 (Active Movement, Δt=0) Started.");

        yield return new WaitForSeconds(60f);

        markerSender.SendMarker("ActiveMovementEnd_B");
        markerSender.SendMarker("InductionEnd_B");
        Debug.Log("[VHI Induction] Task B Induction Ended. Transitioning to VAS Check.");

        // VAS確認ステートへ自動遷移
        ExperimentManager.Instance.ChangeState(ExperimentState.TaskB_VASCheck);
    }

    private IEnumerator BaselineRoutine(string task, string condition)
    {
        string markerSuffix = task == "A" ? $"_{task}_{condition}" : $"_{task}";
        
        markerSender.SendMarker($"BaselineStart{markerSuffix}");
        Debug.Log($"[VHI Induction] Baseline {task} Started. Please keep hand static for 30s.");

        // 安静・ベースラインEMG確立 30秒
        yield return new WaitForSeconds(30f);

        markerSender.SendMarker($"BaselineEnd{markerSuffix}");
        
        // メインタスクへ自動遷移
        if (task == "A")
            ExperimentManager.Instance.ChangeState(ExperimentState.TaskA_Main);
        else
            ExperimentManager.Instance.ChangeState(ExperimentState.TaskB_Main);
    }
}