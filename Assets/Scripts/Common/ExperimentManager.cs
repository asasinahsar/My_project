using UnityEngine;
using System;

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
    [SerializeField] private LSLMarkerSender markerSender;

    public ExperimentState CurrentState { get; private set; } = ExperimentState.Idle;

    // ステート変更時に他のコントローラー（TaskA/B ControllerやUI等）へ通知するイベント
    public event Action<ExperimentState> OnStateChanged;

    private int taskARetryCount = 0;
    private int taskBRetryCount = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
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
                markerSender.SendMarker("PracticeStart");
                break;
            case ExperimentState.BlockRest:
                markerSender.SendMarker("RestStart");
                break;
            case ExperimentState.Finished:
                markerSender.SendMarker("ExpEnd");
                break;
        }

        OnStateChanged?.Invoke(newState);
    }

    /// <summary>
    /// Task A のVAS確認結果を評価し、次のステートを決定します
    /// </summary>
    public void EvaluateTaskAVAS(int vasValue, string condition)
    {
        markerSender.SendMarker($"VAS_A_{condition}_{vasValue}");

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
                markerSender.SendMarker($"BlockExcluded_A_{condition}");
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
        markerSender.SendMarker($"VAS_B_{vasValue}");

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
                markerSender.SendMarker("BlockExcluded_B");
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
}