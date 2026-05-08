using UnityEngine;
using System;
using UnityEngine.Serialization;

public enum ExperimentState
{
    Idle,
    Consent,
    Practice,           // 練習ブロック（TaskA×5試行 + TaskB×5試行）
    TaskA_Induction,    // Task A VHI誘導（筆なぞり受動、3分）
    TaskA_VASCheck,     // Task A VAS確認
    TaskA_Baseline,     // Task A 安静ベースライン（30秒）
    TaskA_Main,         // Task A 計測（40試行）
    BlockRest,          // ブロック間休憩（5分）
    TaskB_Induction,    // Task B VHI誘導（筆なぞり1分 + 慣らし随意運動1分）
    TaskB_VASCheck,     // Task B VAS確認
    TaskB_Baseline,     // Task B 安静ベースライン（30秒）
    TaskB_Main,         // Task B 計測（55試行）
    Finished
}

public class ExperimentManager : MonoBehaviour
{
    public static ExperimentManager Instance { get; private set; }

    [Header("Dependencies")]
    [FormerlySerializedAs("markerSender")]
    [SerializeField] private MonoBehaviour lslMarkerSender;
    [SerializeField] private MonoBehaviour debugMarkerSender;
    [SerializeField] private bool isTestMode;

    public ExperimentState CurrentState { get; private set; } = ExperimentState.Idle;
    public bool IsTestMode => isTestMode;

    // ステート変更時に他のコントローラー（TaskA/B ControllerやUI等）へ通知するイベント
    public event Action<ExperimentState> OnStateChanged;

    private int taskARetryCount = 0;
    private int taskBRetryCount = 0;
    private IMarkerSender markerSender;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        RefreshMarkerSender();
    }

    private void OnValidate()
    {
        ValidateMarkerSender(lslMarkerSender, "LslMarkerSender");
        ValidateMarkerSender(debugMarkerSender, "DebugMarkerSender");
    }

    /// <summary>
    /// ステートを強制的に変更し、必要なマーカーを送出します
    /// </summary>
    public void ChangeState(ExperimentState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Debug.Log($"[ExperimentManager] State Transition -> {newState}");
        
        // ステート突入時の汎用マーカー送出
        switch (newState)
        {
            case ExperimentState.Practice:
                SendMarker("PracticeStart");
                break;
            case ExperimentState.BlockRest:
                SendMarker("RestStart");
                break;
            case ExperimentState.Finished:
                SendMarker("ExpEnd");
                break;
        }

        OnStateChanged?.Invoke(newState);
    }

    public void SwitchState(ExperimentState targetState, bool testMode)
    {
        bool modeChanged = isTestMode != testMode;
        isTestMode = testMode;

        if (modeChanged)
        {
            RefreshMarkerSender();
        }

        ChangeState(targetState);
    }

    /// <summary>
    /// Task A のVAS確認結果を評価し、次のステートを決定します
    /// </summary>
    public void EvaluateTaskAVAS(int vasValue, string condition)
    {
        SendMarker($"VAS_A_{condition}_{vasValue}");

        if (vasValue >= 3)
        {
            // 成功：ベースラインへ進行し、再試行カウンタをリセット
            taskARetryCount = 0;
            ChangeState(ExperimentState.TaskA_Baseline);
        }
        else
        {
            // 失敗：条件分岐
            if (taskARetryCount < 1)
            {
                taskARetryCount++;
                Debug.Log($"[ExperimentManager] Task A VAS < 3. Retrying Induction (Retry: {taskARetryCount})");
                ChangeState(ExperimentState.TaskA_Induction);
            }
            else
            {
                Debug.LogWarning("[ExperimentManager] Task A VAS < 3 after retry. Excluding Block.");
                SendMarker($"BlockExcluded_A_{condition}");
                taskARetryCount = 0;
                ChangeState(ExperimentState.BlockRest);
            }
        }
    }

    /// <summary>
    /// Task B のVAS確認結果を評価し、次のステートを決定します
    /// </summary>
    public void EvaluateTaskBVAS(int vasValue)
    {
        SendMarker($"VAS_B_{vasValue}");

        if (vasValue >= 3)
        {
            // 成功：ベースラインへ進行し、再試行カウンタをリセット
            taskBRetryCount = 0;
            ChangeState(ExperimentState.TaskB_Baseline);
        }
        else
        {
            // 失敗：条件分岐
            if (taskBRetryCount < 1)
            {
                taskBRetryCount++;
                Debug.Log($"[ExperimentManager] Task B VAS < 3. Retrying Induction (Retry: {taskBRetryCount})");
                ChangeState(ExperimentState.TaskB_Induction);
            }
            else
            {
                Debug.LogWarning("[ExperimentManager] Task B VAS < 3 after retry. Excluding Block.");
                SendMarker("BlockExcluded_B");
                taskBRetryCount = 0;
                ChangeState(ExperimentState.Finished);
            }
        }
    }

    /// <summary>
    /// UIの「緊急停止」ボタンなどから呼び出されるメソッド
    /// </summary>
    public void EmergencyStop()
    {
        Debug.LogError("[ExperimentManager] Emergency Stop Triggered!");
        ChangeState(ExperimentState.Finished);
    }

    private void RefreshMarkerSender()
    {
        MonoBehaviour senderBehaviour = isTestMode ? debugMarkerSender : lslMarkerSender;
        markerSender = senderBehaviour as IMarkerSender;

        if (senderBehaviour == null)
        {
            Debug.LogWarning($"[ExperimentManager] {(isTestMode ? "DebugMarkerSender" : "LslMarkerSender")} is not assigned.");
        }
        else if (markerSender == null)
        {
            Debug.LogWarning($"[ExperimentManager] Assigned {(isTestMode ? "DebugMarkerSender" : "LslMarkerSender")} does not implement IMarkerSender.");
        }
    }

    private void SendMarker(string marker)
    {
        markerSender?.SendMarker(marker);
    }

    private void ValidateMarkerSender(MonoBehaviour senderBehaviour, string senderName)
    {
        if (senderBehaviour != null && senderBehaviour is not IMarkerSender)
        {
            Debug.LogWarning($"[ExperimentManager] Assigned {senderName} does not implement IMarkerSender.");
        }
    }
}
