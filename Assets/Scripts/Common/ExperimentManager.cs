using UnityEngine;
using System;
using System.Collections;
using LSL;
using TMPro;
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
    [SerializeField] private GameObject taskAPanel;
    [SerializeField] private GameObject taskBPanel;
    [SerializeField] private GameObject blockRestPanel;
    [SerializeField] private GameObject finishedPanel;

    [Header("Block Rest UI")]
    [SerializeField] private TextMeshProUGUI blockRestTimerText;

    private IMarkerSender markerSender;
    private MarkerSenderRouter markerSenderRouter;
    private bool hasLoggedMissingStartMenu = false;
    private Coroutine blockRestCoroutine;

    public ExperimentState CurrentState { get; private set; } = ExperimentState.Idle;

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
    }

    private void Start()
    {
        if (startMenuPanel != null)
        {
            ChangeState(ExperimentState.StartMenu);
        }
        else
        {
            UpdateStatePanels(CurrentState);
        }
    }

    public void ChangeState(ExperimentState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Debug.Log($"[ExperimentManager] State Transition -> {newState}");
        UpdateStatePanels(newState);
        
        switch (newState)
        {
            case ExperimentState.Practice:
                SendMarker("PracticeStart");
                break;
            case ExperimentState.BlockRest:
                SendMarker("RestStart");
                if (blockRestCoroutine != null) StopCoroutine(blockRestCoroutine);
                blockRestCoroutine = StartCoroutine(BlockRestRoutine());
                break;
            case ExperimentState.Finished:
                SendMarker("ExpEnd");
                break;
        }

        OnStateChanged?.Invoke(newState);
    }

    public void ShowStartMenu()
    {
        SwitchState(ExperimentState.StartMenu);
    }

    public void ShowTestMenu()
    {
        SwitchState(ExperimentState.TestMenu);
    }

    public void ShowExperimentMenu()
    {
        SwitchState(ExperimentState.ExperimentMenu);
    }

    public void StartExperiment()
    {
        SwitchState(ExperimentState.Consent);
    }

    public void EvaluateTaskAVAS(int vasValue, string condition)
    {
        SendMarker($"VAS_A_{condition}_{vasValue}");

        if (vasValue >= 3)
        {
            taskARetryCount = 0;
            ChangeState(ExperimentState.TaskA_Baseline);
        }
        else
        {
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

    public void EvaluateTaskBVAS(int vasValue)
    {
        SendMarker($"VAS_B_{vasValue}");

        if (vasValue >= 3)
        {
            taskBRetryCount = 0;
            ChangeState(ExperimentState.TaskB_Baseline);
        }
        else
        {
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

    private void UpdateStatePanels(ExperimentState state)
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

        if (showStartMenu && startMenuPanel == null)
        {
            SetPanelActive(idlePanel, true);
        }
        else
        {
            SetPanelActive(idlePanel, state == ExperimentState.Idle);
        }
        SetPanelActive(consentPanel, state == ExperimentState.Consent);
        SetPanelActive(practicePanel, state == ExperimentState.Practice);
        SetPanelActive(taskAPanel, IsTaskAState(state));
        SetPanelActive(taskBPanel, IsTaskBState(state));
        SetPanelActive(blockRestPanel, state == ExperimentState.BlockRest);
        SetPanelActive(finishedPanel, state == ExperimentState.Finished);
    }

    private void SetPanelActive(GameObject panel, bool isActive)
    {
        if (panel != null)
        {
            panel.SetActive(isActive);
        }
    }

    private IEnumerator BlockRestRoutine()
    {
        float duration = 30f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            int remaining = Mathf.CeilToInt(duration - elapsed);
            if (blockRestTimerText != null)
                blockRestTimerText.text = $"残り: {remaining} 秒";
            yield return null;
        }
        if (blockRestTimerText != null)
            blockRestTimerText.text = "準備ができたら次へ進みます...";
        blockRestCoroutine = null;
        AdvanceState();
    }

    private void AbortActiveTasks()
    {
        if (blockRestCoroutine != null)
        {
            StopCoroutine(blockRestCoroutine);
            blockRestCoroutine = null;
        }
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

    private void SwitchState(ExperimentState targetState)
    {
        AbortActiveTasks();
        ChangeState(targetState);
    }

    private static bool IsTaskAState(ExperimentState state)
    {
        return state == ExperimentState.TaskA_Induction
            || state == ExperimentState.TaskA_VASCheck
            || state == ExperimentState.TaskA_Baseline
            || state == ExperimentState.TaskA_Main;
    }

    private static bool IsTaskBState(ExperimentState state)
    {
        return state == ExperimentState.TaskB_Induction
            || state == ExperimentState.TaskB_VASCheck
            || state == ExperimentState.TaskB_Baseline
            || state == ExperimentState.TaskB_Main;
    }
}