using UnityEngine;
using System;
using LSL;
using UnityVirtual.LSL;
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
    Finished,
    StartMenu,
    TestMenu,
    ExperimentMenu
}

public class ExperimentManager : MonoBehaviour
{
    public static ExperimentManager Instance { get; private set; }

    [Header("Dependencies")]
    [FormerlySerializedAs("markerSender")]
    [SerializeField] private MonoBehaviour markerSenderBehaviour;
    [SerializeField] private TaskAController taskAController;
    [SerializeField] private TaskBController taskBController;
    [SerializeField] private VHIInductionController vhiInductionController;

    [Header("UI Panels (Menu)")]
    [SerializeField] private GameObject startMenuPanel;
    [SerializeField] private GameObject testMenuPanel;
    [SerializeField] private GameObject experimentMenuPanel;

    [Header("UI Panels (State Based)")]
    [SerializeField] private GameObject idlePanel;
    [SerializeField] private GameObject consentPanel;
    [SerializeField] private GameObject practicePanel;
    [SerializeField] private GameObject taskAInductionPanel;
    [SerializeField] private GameObject taskAVASCheckPanel;
    [SerializeField] private GameObject taskABaselinePanel;
    [SerializeField] private GameObject taskAMainPanel;
    [SerializeField] private GameObject blockRestPanel;
    [SerializeField] private GameObject taskBInductionPanel;
    [SerializeField] private GameObject taskBVASCheckPanel;
    [SerializeField] private GameObject taskBBaselinePanel;
    [SerializeField] private GameObject taskBMainPanel;
    [SerializeField] private GameObject finishedPanel;

    private IMarkerSender markerSender;
    private MarkerSenderRouter markerSenderRouter;
    private bool isTestMode = false;
    private bool hasLoggedMissingStartMenu = false;

    public ExperimentState CurrentState { get; private set; } = ExperimentState.Idle;
    public bool IsTestMode => isTestMode;

    // ステート変更時に他のコントローラー（TaskA/B ControllerやUI等）へ通知するイベント
    public event Action<ExperimentState> OnStateChanged;

    private int taskARetryCount = 0;
    private int taskBRetryCount = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        markerSender = markerSenderBehaviour as IMarkerSender;
        markerSenderRouter = markerSenderBehaviour as MarkerSenderRouter;
        if (markerSender == null)
        {
            Debug.LogWarning("[ExperimentManager] Marker sender is not assigned or does not implement IMarkerSender.");
        }

        ApplyMarkerSenderMode();
    }

    private void Start()
    {
        if (startMenuPanel != null)
        {
            ChangeState(ExperimentState.StartMenu);
        }
        else
        {
            UpdateUIPanels(CurrentState);
        }
    }

    /// <summary>
    /// ステートを強制的に変更し、必要なマーカーを送出します
    /// </summary>
    public void ChangeState(ExperimentState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Debug.Log($"[ExperimentManager] State Transition -> {newState}");
        UpdateUIPanels(newState);
        
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

    public void ShowStartMenu()
    {
        SwitchState(ExperimentState.StartMenu, false);
    }

    public void ShowTestMenu()
    {
        SwitchState(ExperimentState.TestMenu, true);
    }

    public void ShowExperimentMenu()
    {
        SwitchState(ExperimentState.ExperimentMenu, false);
    }

    public void StartTestTaskA()
    {
        SwitchState(ExperimentState.TaskA_Induction, true);
    }

    public void StartTestTaskB()
    {
        SwitchState(ExperimentState.TaskB_Induction, true);
    }

    public void StartExperiment()
    {
        SwitchState(ExperimentState.Consent, false);
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
                if (taskAController != null)
                {
                    taskAController.MarkCurrentBlockExcluded();
                }
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

    public void AdvanceState()
    {
        switch (CurrentState)
        {
            case ExperimentState.Idle:
                ChangeState(ExperimentState.Consent);
                break;
            case ExperimentState.Consent:
                ChangeState(ExperimentState.Practice);
                break;
            case ExperimentState.Practice:
                ChangeState(ExperimentState.TaskA_Induction);
                break;
            case ExperimentState.BlockRest:
                if (taskAController != null && taskAController.HasRemainingBlocks)
                {
                    ChangeState(ExperimentState.TaskA_Induction);
                }
                else
                {
                    ChangeState(ExperimentState.TaskB_Induction);
                }
                break;
            default:
                Debug.Log("[ExperimentManager] AdvanceState ignored for current phase.");
                break;
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

    private void SendMarker(string marker)
    {
        if (markerSender == null) return;
        markerSender.SendMarker(marker);
    }

    // Task A（自動バーチャルハンド動作）と Task B（QUEST法 + Δt遅延）のUI切り替え
    private void UpdateUIPanels(ExperimentState state)
    {
        bool showStartMenu = state == ExperimentState.StartMenu;
        bool showTestMenu = state == ExperimentState.TestMenu;
        bool showExperimentMenu = state == ExperimentState.ExperimentMenu;

        if (showStartMenu && startMenuPanel == null && idlePanel != null && !hasLoggedMissingStartMenu)
        {
            Debug.LogWarning("[ExperimentManager] Start menu panel is not assigned. Falling back to idlePanel.");
            hasLoggedMissingStartMenu = true;
        }

        SetPanelActive(startMenuPanel, showStartMenu);
        SetPanelActive(testMenuPanel, showTestMenu);
        SetPanelActive(experimentMenuPanel, showExperimentMenu);

        SetAllStatePanelsActive(false);

        GameObject targetPanel = GetPanelForState(state);
        if (targetPanel != null)
        {
            SetPanelActive(targetPanel, true);
        }
        else if (showStartMenu && startMenuPanel == null)
        {
            SetPanelActive(idlePanel, true);
        }
    }

    private void SetPanelActive(GameObject panel, bool isActive)
    {
        if (panel != null)
        {
            panel.SetActive(isActive);
        }
    }

    private void AbortActiveTasks()
    {
        if (vhiInductionController != null)
        {
            vhiInductionController.AbortInduction();
        }

        if (taskAController != null)
        {
            taskAController.AbortTask();
        }

        if (taskBController != null)
        {
            taskBController.AbortTask();
        }
    }

    private void SwitchState(ExperimentState targetState, bool testMode)
    {
        UpdateTestMode(testMode);

        AbortActiveTasks();
        ChangeState(targetState);
    }

    private void UpdateTestMode(bool testMode)
    {
        if (isTestMode == testMode) return;
        isTestMode = testMode;
        ApplyMarkerSenderMode();
    }

    private void ApplyMarkerSenderMode()
    {
        if (markerSenderRouter != null)
        {
            markerSenderRouter.SetTestMode(isTestMode);
        }
    }

    private void SetAllStatePanelsActive(bool isActive)
    {
        SetPanelActive(idlePanel, isActive);
        SetPanelActive(consentPanel, isActive);
        SetPanelActive(practicePanel, isActive);
        SetPanelActive(taskAInductionPanel, isActive);
        SetPanelActive(taskAVASCheckPanel, isActive);
        SetPanelActive(taskABaselinePanel, isActive);
        SetPanelActive(taskAMainPanel, isActive);
        SetPanelActive(blockRestPanel, isActive);
        SetPanelActive(taskBInductionPanel, isActive);
        SetPanelActive(taskBVASCheckPanel, isActive);
        SetPanelActive(taskBBaselinePanel, isActive);
        SetPanelActive(taskBMainPanel, isActive);
        SetPanelActive(finishedPanel, isActive);
    }

    private GameObject GetPanelForState(ExperimentState state)
    {
        return state switch
        {
            ExperimentState.Idle => idlePanel,
            ExperimentState.Consent => consentPanel,
            ExperimentState.Practice => practicePanel,
            ExperimentState.TaskA_Induction => taskAInductionPanel,
            ExperimentState.TaskA_VASCheck => taskAVASCheckPanel,
            ExperimentState.TaskA_Baseline => taskABaselinePanel,
            ExperimentState.TaskA_Main => taskAMainPanel,
            ExperimentState.BlockRest => blockRestPanel,
            ExperimentState.TaskB_Induction => taskBInductionPanel,
            ExperimentState.TaskB_VASCheck => taskBVASCheckPanel,
            ExperimentState.TaskB_Baseline => taskBBaselinePanel,
            ExperimentState.TaskB_Main => taskBMainPanel,
            ExperimentState.Finished => finishedPanel,
            _ => null
        };
    }
}
