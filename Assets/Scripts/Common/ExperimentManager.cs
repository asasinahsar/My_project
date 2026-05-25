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
    // v5.3: Consent は VR 外でアナログ実施するため削除
    Practice,           // 練習ブロック（v5.3: Task B のみ・Phase G で整理予定）
    TaskA_Induction,    // Task A VHI誘導（筆なぞり受動、3分）
    // v5.3: TaskA_VASCheck は VAS 全廃に伴い削除
    TaskA_Baseline,     // Task A 安静ベースライン（30秒）
    TaskA_Main,         // Task A 計測（40試行）
    BlockRest,          // ブロック間休憩
    TaskB_Induction,    // Task B VHI誘導（筆なぞり1分 + 慣らし随意運動1分）
    // v5.3: TaskB_VASCheck は VAS 全廃に伴い削除
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
    // v5.3: consentPanel は Consent ステート削除に伴い削除
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
    private bool taskBCompletedFlag = false;
    // v5.3 Phase A.5 補正: BlockRest 戻る先の判定用。TaskA_Main/TaskB_Main からの BlockRest 流入時のみ更新。
    private ExperimentState lastStateBeforeBlockRest = ExperimentState.TaskB_Main;

    public ExperimentState CurrentState { get; private set; } = ExperimentState.Idle;

    public event Action<ExperimentState> OnStateChanged;

    // v5.3 Phase B: taskARetryCount / taskBRetryCount は VAS リトライ用だったため削除。

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
        taskBCompletedFlag = false;
        lastStateBeforeBlockRest = ExperimentState.TaskB_Main;
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

        // v5.3 Phase A.5 補正: BlockRest に「実タスクから」流入する時のみ直前状態を記録。
        // GoBackPhase 経由（TaskA_Induction → BlockRest 等）では記録を上書きしないため、
        // 元々の BlockRest 流入経路（TaskB_Main → BlockRest 等）が保持される。
        if (newState == ExperimentState.BlockRest
            && (CurrentState == ExperimentState.TaskA_Main || CurrentState == ExperimentState.TaskB_Main))
        {
            lastStateBeforeBlockRest = CurrentState;
        }

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
        // v5.3: Consent ステート削除に伴い、Practice から開始
        SwitchState(ExperimentState.Practice);
    }

    // v5.3 Phase B: EvaluateTaskAVAS / EvaluateTaskBVAS は VAS 全廃に伴い削除。
    // VHI 成立判定はオフライン解析と SoA 指標で代替する（限界は Experiment.md 12 節参照）。

    public void AdvanceState()
    {
        switch (CurrentState)
        {
            case ExperimentState.Idle:
                // v5.3: Consent 削除に伴い直接 Practice へ
                ChangeState(ExperimentState.Practice);
                break;
            case ExperimentState.Practice:
                // v5.3: Task B → Task A の順序
                ChangeState(ExperimentState.TaskB_Induction);
                break;
            case ExperimentState.BlockRest:
                // v5.3: Task B 完了後の Rest → Task A へ。Task A 内 Rest → 次ブロック or Finished。
                if (taskBCompletedFlag)
                {
                    if (taskAController != null && taskAController.HasRemainingBlocks)
                    {
                        ChangeState(ExperimentState.TaskA_Induction);
                    }
                    else
                    {
                        ChangeState(ExperimentState.Finished);
                    }
                }
                else
                {
                    // Phase D で Task B 内の async/sync 2 ブロック構造が入るまでは通常通らない経路。
                    // 暫定で Task A 開始にフォールバック（順序反転を維持）。
                    ChangeState(ExperimentState.TaskA_Induction);
                }
                break;
            default:
                Debug.Log("[ExperimentManager] AdvanceState ignored for current phase.");
                break;
        }
    }

    // v5.3: Task B 完了通知。Task A への Rest を経由して Task A に遷移するためのフック。
    public void NotifyTaskBCompleted()
    {
        taskBCompletedFlag = true;
        ChangeState(ExperimentState.BlockRest);
    }

    // v5.3 Phase A.5: 全パネルからの「次へ」ボタン。フロー定義に基づき次のフェーズへ。
    public void SkipCurrentPhase()
    {
        var next = GetNextState(CurrentState);
        if (!next.HasValue)
        {
            Debug.Log($"[ExperimentManager] No next phase from {CurrentState}");
            return;
        }

        // TaskB_Main をスキップする場合は完了扱いで Task A に流すため flag を立てる
        if (CurrentState == ExperimentState.TaskB_Main)
        {
            taskBCompletedFlag = true;
        }

        SendMarker($"PhaseSkipped_{CurrentState}");
        AbortActiveTasks();
        ChangeState(next.Value);
    }

    // v5.3 Phase A.5: 全パネルからの「戻る」ボタン。フロー定義の逆方向へ。
    public void GoBackPhase()
    {
        var prev = GetPreviousState(CurrentState);
        if (!prev.HasValue)
        {
            Debug.Log($"[ExperimentManager] No previous phase from {CurrentState}");
            return;
        }

        // Task B 系に戻るときは Task B 完了フラグをリセット
        if (prev.Value == ExperimentState.TaskB_Induction
            || prev.Value == ExperimentState.TaskB_Baseline
            || prev.Value == ExperimentState.TaskB_Main)
        {
            taskBCompletedFlag = false;
        }

        SendMarker($"PhaseBack_{CurrentState}");
        AbortActiveTasks();
        ChangeState(prev.Value);
    }

    private ExperimentState? GetNextState(ExperimentState state)
    {
        switch (state)
        {
            case ExperimentState.Idle: return ExperimentState.Practice;
            case ExperimentState.Practice: return ExperimentState.TaskB_Induction;
            case ExperimentState.TaskB_Induction: return ExperimentState.TaskB_Baseline;
            case ExperimentState.TaskB_Baseline: return ExperimentState.TaskB_Main;
            case ExperimentState.TaskB_Main: return ExperimentState.BlockRest;
            case ExperimentState.BlockRest:
                if (taskBCompletedFlag)
                {
                    if (taskAController != null && taskAController.HasRemainingBlocks)
                        return ExperimentState.TaskA_Induction;
                    return ExperimentState.Finished;
                }
                return ExperimentState.TaskA_Induction;
            case ExperimentState.TaskA_Induction: return ExperimentState.TaskA_Baseline;
            case ExperimentState.TaskA_Baseline: return ExperimentState.TaskA_Main;
            // v5.3 Phase A.5 補正: TaskA_Main の Next は無条件 Finished。
            // TaskAController が自然完了時に呼ぶ ChangeState(BlockRest) 経路は別途維持される。
            case ExperimentState.TaskA_Main: return ExperimentState.Finished;
            default: return null; // StartMenu/TestMenu/ExperimentMenu/Finished は対象外
        }
    }

    private ExperimentState? GetPreviousState(ExperimentState state)
    {
        switch (state)
        {
            case ExperimentState.Practice: return ExperimentState.Idle;
            case ExperimentState.TaskB_Induction: return ExperimentState.Practice;
            case ExperimentState.TaskB_Baseline: return ExperimentState.TaskB_Induction;
            case ExperimentState.TaskB_Main: return ExperimentState.TaskB_Baseline;
            case ExperimentState.BlockRest:
                // v5.3 Phase A.5 補正: BlockRest に流入した「実タスク」状態に戻す。
                // ChangeState で TaskA_Main/TaskB_Main からの流入時のみ記録するため、
                // GoBackPhase で TaskA_Induction → BlockRest と戻ってきた場合も元の経路（例：TaskB_Main）を尊重する。
                return lastStateBeforeBlockRest;
            case ExperimentState.TaskA_Induction: return ExperimentState.BlockRest;
            case ExperimentState.TaskA_Baseline: return ExperimentState.TaskA_Induction;
            case ExperimentState.TaskA_Main: return ExperimentState.TaskA_Baseline;
            case ExperimentState.Finished: return ExperimentState.TaskA_Main;
            default: return null; // Idle/StartMenu/TestMenu/ExperimentMenu は対象外
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
        // v5.3: consentPanel は Consent ステート削除に伴い削除
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
        // v5.3: メニュー画面へ戻る際は実験フラグをリセット（再実行時の整合性確保）。
        if (targetState == ExperimentState.StartMenu
            || targetState == ExperimentState.TestMenu
            || targetState == ExperimentState.ExperimentMenu)
        {
            taskBCompletedFlag = false;
            lastStateBeforeBlockRest = ExperimentState.TaskB_Main; // 初期値（TaskB → BlockRest → TaskA を前提）
        }
        ChangeState(targetState);
    }

    private static bool IsTaskAState(ExperimentState state)
    {
        return state == ExperimentState.TaskA_Induction
            || state == ExperimentState.TaskA_Baseline
            || state == ExperimentState.TaskA_Main;
    }

    private static bool IsTaskBState(ExperimentState state)
    {
        return state == ExperimentState.TaskB_Induction
            || state == ExperimentState.TaskB_Baseline
            || state == ExperimentState.TaskB_Main;
    }
}