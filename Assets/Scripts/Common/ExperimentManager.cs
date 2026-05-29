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

        // v5.3 マーカー補完: 離脱マーカー（前ステートを離れる時に送信）
        switch (CurrentState)
        {
            case ExperimentState.Practice:
                SendMarker("PracticeEnd");
                break;
            case ExperimentState.BlockRest:
                SendMarker("RestEnd");
                break;
        }

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
        SendMarker("ExpStart");
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
                // v5.3 Phase D: async ブロック先（誘導なし）→ TaskB_Main 直行
                ChangeState(ExperimentState.TaskB_Main);
                break;
            case ExperimentState.BlockRest:
                // D-1: 新順序 TaskB(async) → TaskA(async) → TaskB(sync) → TaskA(sync)
                ChangeState(DetermineNextStateAfterBlockRest());
                break;
            case ExperimentState.Finished:
                // M-1: 終了画面で「次へ」→ スタートメニューに戻る（SwitchState 経由で初期化）
                ShowStartMenu();
                break;
            default:
                Debug.Log("[ExperimentManager] AdvanceState ignored for current phase.");
                break;
        }
    }

    /// <summary>
    /// D-1: BlockRest からの次ステートを決定する。
    /// 新順序: TaskB(async) → TaskA(async) → TaskB_Induction → TaskB(sync) → TaskA_Induction → TaskA(sync) → Finished
    /// 判定基準: lastStateBeforeBlockRest（直前の実タスク）と Controller.CompletedBlocks
    /// </summary>
    private ExperimentState DetermineNextStateAfterBlockRest()
    {
        if (lastStateBeforeBlockRest == ExperimentState.TaskB_Main)
        {
            // TaskB のブロック完了直後
            int doneB = taskBController != null ? taskBController.CompletedBlocks : 0;
            if (doneB <= 1)
            {
                // TaskB(async) 完了 → TaskA(async)（誘導なし）
                // doneB==0（想定外、スキップ未処理）でも安全側で TaskA(async) に進める
                return ExperimentState.TaskA_Main;
            }
            else // doneB >= 2
            {
                // TaskB(sync) 完了 → TaskA(sync) 誘導へ
                return ExperimentState.TaskA_Induction;
            }
        }
        else if (lastStateBeforeBlockRest == ExperimentState.TaskA_Main)
        {
            // TaskA のブロック完了直後
            int doneA = taskAController != null ? taskAController.CompletedBlocks : 0;
            if (doneA <= 1)
            {
                // TaskA(async) 完了 → TaskB(sync) 誘導へ
                // doneA==0（想定外、スキップ未処理）でも安全側で TaskB_Induction に進める
                return ExperimentState.TaskB_Induction;
            }
            else // doneA >= 2
            {
                // TaskA(sync) 完了 → 終了
                return ExperimentState.Finished;
            }
        }

        Debug.LogWarning($"[ExperimentManager] BlockRest からの遷移先が判定不能。lastStateBeforeBlockRest={lastStateBeforeBlockRest}, TaskB.completedBlocks={taskBController?.CompletedBlocks}, TaskA.completedBlocks={taskAController?.CompletedBlocks}");
        return ExperimentState.Finished; // フォールバック
    }

    // D-1: 旧 NotifyTaskBCompleted() は廃止（taskBCompletedFlag 削除に伴う）。
    // TaskB(sync) 完了時は TaskBController が直接 ChangeState(BlockRest) を呼ぶ。

    // v5.3 Phase A.5: 全パネルからの「次へ」ボタン。フロー定義に基づき次のフェーズへ。
    public void SkipCurrentPhase()
    {
        // D-2: TaskA_Main/TaskB_Main をスキップ時は Controller の CompletedBlocks をインクリメントし、
        // BlockRest 後の DetermineNextStateAfterBlockRest が正しく次ステートを判定できるようにする。
        if (CurrentState == ExperimentState.TaskA_Main && taskAController != null)
        {
            taskAController.MarkCurrentBlockExcluded();
        }
        else if (CurrentState == ExperimentState.TaskB_Main && taskBController != null)
        {
            taskBController.MarkCurrentBlockExcluded();
        }

        var next = GetNextState(CurrentState);
        if (!next.HasValue)
        {
            Debug.Log($"[ExperimentManager] No next phase from {CurrentState}");
            return;
        }

        SendMarker($"PhaseSkipped_{CurrentState}");

        // M-1: メニュー復帰時は SwitchState 経由で初期化処理を確実に実行
        if (next.Value == ExperimentState.StartMenu
            || next.Value == ExperimentState.TestMenu
            || next.Value == ExperimentState.ExperimentMenu)
        {
            SwitchState(next.Value);
            return;
        }

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

        // D-1: 旧 taskBCompletedFlag リセット処理は削除（フラグ自体が廃止されたため）

        SendMarker($"PhaseBack_{CurrentState}");
        AbortActiveTasks();
        ChangeState(prev.Value);
    }

    private ExperimentState? GetNextState(ExperimentState state)
    {
        switch (state)
        {
            case ExperimentState.Idle: return ExperimentState.Practice;
            // v5.3 Phase D: Practice → TaskB_Main（async 先、誘導なし）に直行
            case ExperimentState.Practice: return ExperimentState.TaskB_Main;
            // TaskB_Induction → TaskB_Baseline → TaskB_Main は sync ブロック専用フロー
            case ExperimentState.TaskB_Induction: return ExperimentState.TaskB_Baseline;
            case ExperimentState.TaskB_Baseline: return ExperimentState.TaskB_Main;
            case ExperimentState.TaskB_Main: return ExperimentState.BlockRest;
            // D-1: BlockRest は AdvanceState と同じ判定で次ステートを決定
            case ExperimentState.BlockRest: return DetermineNextStateAfterBlockRest();
            case ExperimentState.TaskA_Induction: return ExperimentState.TaskA_Baseline;
            case ExperimentState.TaskA_Baseline: return ExperimentState.TaskA_Main;
            // D-1: TaskA_Main の Next は CompletedBlocks に応じて分岐
            // async完了直後（CompletedBlocks=1）→ BlockRest → TaskB_Induction
            // sync完了直後（CompletedBlocks=2）→ Finished
            case ExperimentState.TaskA_Main: return ExperimentState.BlockRest;
            // M-1: 終了画面の次は StartMenu に戻る（SkipCurrentPhase 経由のケースもカバー）
            case ExperimentState.Finished: return ExperimentState.StartMenu;
            default: return null; // StartMenu/TestMenu/ExperimentMenu は対象外
        }
    }

    private ExperimentState? GetPreviousState(ExperimentState state)
    {
        // D-1 新フロー: Practice → TaskB(async) → BlockRest → TaskA(async) → BlockRest
        //            → TaskB_Induction → TaskB_Baseline → TaskB(sync) → BlockRest
        //            → TaskA_Induction → TaskA_Baseline → TaskA(sync) → Finished
        switch (state)
        {
            case ExperimentState.Practice: return ExperimentState.Idle;
            // TaskB_Induction は sync ブロック専用。直前は BlockRest（TaskA(async) 完了後）
            case ExperimentState.TaskB_Induction: return ExperimentState.BlockRest;
            case ExperimentState.TaskB_Baseline: return ExperimentState.TaskB_Induction;
            // TaskB_Main は async（直前 Practice）または sync（直前 TaskB_Baseline）
            case ExperimentState.TaskB_Main:
                return (taskBController != null && taskBController.CurrentCondition == "sync")
                    ? ExperimentState.TaskB_Baseline
                    : ExperimentState.Practice;
            case ExperimentState.BlockRest:
                // BlockRest に流入した「実タスク」状態に戻す
                return lastStateBeforeBlockRest;
            // TaskA_Induction は sync ブロック専用。直前は BlockRest（TaskB(sync) 完了後）
            case ExperimentState.TaskA_Induction: return ExperimentState.BlockRest;
            case ExperimentState.TaskA_Baseline: return ExperimentState.TaskA_Induction;
            // TaskA_Main は async（直前 BlockRest = TaskB(async)完了後）または sync（直前 TaskA_Baseline）
            case ExperimentState.TaskA_Main:
                return (taskAController != null && taskAController.CurrentCondition == "sync")
                    ? ExperimentState.TaskA_Baseline
                    : ExperimentState.BlockRest;
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