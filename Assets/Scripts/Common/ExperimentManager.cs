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
    StartMenu
    // テストモード廃止（2026-06-21）：TestMenu / ExperimentMenu を削除。
    // スタート画面は StartMenu に「TaskAから」「TaskBから」の2ボタンを直接配置する。
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
    // テストモード廃止（2026-06-21）：testMenuPanel / experimentMenuPanel を削除。

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

    [Header("Navigation Safety (A/B)")]
    [Tooltip("A: 計測中（Main/Induction/Baseline）の手動 次へ/戻る を無効化する（本番 true / テスト false）")]
    [SerializeField] private bool lockNavigationDuringMeasurement = true;
    [Tooltip("B: 次へ/戻るの連続操作を無視するクールダウン（秒）。連打・Ray 連続クリックの暴走を防ぐ")]
    [SerializeField] private float navigationCooldown = 1.0f;
    private float lastNavigationTime = -Mathf.Infinity;

    private IMarkerSender markerSender;
    private MarkerSenderRouter markerSenderRouter;
    private bool hasLoggedMissingStartMenu = false;
    private Coroutine blockRestCoroutine;
    // v5.3 Phase A.5 補正: BlockRest 戻る先の判定用。TaskA_Main/TaskB_Main からの BlockRest 流入時のみ更新。
    private ExperimentState lastStateBeforeBlockRest = ExperimentState.TaskB_Main;

    public ExperimentState CurrentState { get; private set; } = ExperimentState.Idle;

    public event Action<ExperimentState> OnStateChanged;

    // 開始タスク分岐（2026-06-21）：
    //   false = TaskB-first（既存順序: TaskB-async → TaskA-async → TaskB-sync → TaskA-sync）
    //   true  = TaskA-first（新順序:  TaskA-async → TaskB-async → TaskA-sync → TaskB-sync）
    // 既定 false で既存挙動を保持。StartExperimentFromTaskA/B でセットする。
    private bool startWithTaskA = false;

    // 開始タスクに応じて「先タスク／後タスク」のステートを解決するヘルパー。
    private ExperimentState FirstTaskMain       => startWithTaskA ? ExperimentState.TaskA_Main      : ExperimentState.TaskB_Main;
    private ExperimentState SecondTaskMain      => startWithTaskA ? ExperimentState.TaskB_Main      : ExperimentState.TaskA_Main;
    private ExperimentState FirstTaskInduction  => startWithTaskA ? ExperimentState.TaskA_Induction : ExperimentState.TaskB_Induction;
    private ExperimentState SecondTaskInduction => startWithTaskA ? ExperimentState.TaskB_Induction : ExperimentState.TaskA_Induction;

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

    // 開始タスク分岐（2026-06-21）：スタート画面の2ボタンから呼ぶエントリ。
    //   「TaskBから」→ StartExperimentFromTaskB（既存順序）
    //   「TaskAから」→ StartExperimentFromTaskA（新順序）
    public void StartExperimentFromTaskA() => StartExperimentInternal(true);
    public void StartExperimentFromTaskB() => StartExperimentInternal(false);

    private void StartExperimentInternal(bool startWithA)
    {
        startWithTaskA = startWithA;

        // C（2026-06-01）: 冒頭の誤操作で進んだブロック進捗をリセットしてから開始。
        if (taskAController != null) taskAController.ResetBlockProgress();
        if (taskBController != null) taskBController.ResetBlockProgress();

        SendMarker("ExpStart");
        // 解析用に開始タスク（実験順序）を記録
        SendMarker(startWithA ? "ExpOrder_TaskAFirst" : "ExpOrder_TaskBFirst");

        // TaskB-first: 先頭で TaskB 練習（Practice → TaskB_Main(async)）
        // TaskA-first: TaskA(async) 直行。TaskB 練習は最初の TaskB ブロック直前に挿入される
        //              （DetermineNextStateAfterBlockRest が done==1 で Practice を返す）
        SwitchState(startWithA ? ExperimentState.TaskA_Main : ExperimentState.Practice);
    }

    // A/B（2026-06-01）: 次へ/戻る操作のガード。
    // B: navigationCooldown 秒以内の連続操作を無視（Ray 連続クリック・連打の暴走を防ぐ）。
    // A: lockNavigationDuringMeasurement=true のとき、計測中ステートでは手動操作を無効化。
    private bool BlockNavigation(string action)
    {
        if (Time.unscaledTime - lastNavigationTime < navigationCooldown)
        {
            Debug.Log($"[ExperimentManager] {action} ignored (cooldown {navigationCooldown}s).");
            return true;
        }
        if (lockNavigationDuringMeasurement && IsMeasurementState(CurrentState))
        {
            Debug.Log($"[ExperimentManager] {action} ignored (measurement in progress: {CurrentState}).");
            return true;
        }
        lastNavigationTime = Time.unscaledTime;
        return false;
    }

    private static bool IsMeasurementState(ExperimentState s)
    {
        return s == ExperimentState.TaskA_Main || s == ExperimentState.TaskB_Main
            || s == ExperimentState.TaskA_Induction || s == ExperimentState.TaskB_Induction
            || s == ExperimentState.TaskA_Baseline || s == ExperimentState.TaskB_Baseline;
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
                // 練習は常に「最初の TaskB ブロック直前」に置くため、Practice の次は必ず
                // TaskB_Main(async)。TaskBController.completedBlocks=0 なので async ブロックが走る。
                ChangeState(ExperimentState.TaskB_Main);
                break;
            case ExperimentState.BlockRest:
                // 2026-06-21: 開始タスク選択に応じて次ステートを決定（完了ブロック総数ベース）
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
    /// BlockRest からの次ステートを決定する（2026-06-21 改訂：完了ブロック総数ベース）。
    ///
    /// 4 つの計測ブロックは「先async → 後async → 先sync → 後sync」の順で進む。
    /// 各 BlockRest は _Main 完了直後にのみ流入するため、completedA+completedB（=完了ブロック総数 done）で
    /// 「いまどこまで終わったか」が一意に決まり、開始タスク（startWithTaskA）に依らず判定できる。
    ///
    ///   done==1: 先タスクの async 完了
    ///            - TaskA-first: 最初の TaskB ブロック直前に練習を挿入（Practice → TaskB_Main(async)）
    ///            - TaskB-first: そのまま後タスク(TaskA)の async へ
    ///   done==2: 後 async 完了 → 先タスクの sync 誘導へ
    ///   done==3: 先 sync 完了 → 後タスクの sync 誘導へ
    ///   done>=4: 全 4 ブロック完了 → 終了
    /// </summary>
    private ExperimentState DetermineNextStateAfterBlockRest()
    {
        int done = (taskAController != null ? taskAController.CompletedBlocks : 0)
                 + (taskBController != null ? taskBController.CompletedBlocks : 0);

        switch (done)
        {
            case 1:
                return startWithTaskA ? ExperimentState.Practice : SecondTaskMain;
            case 2:
                return FirstTaskInduction;
            case 3:
                return SecondTaskInduction;
            default:
                if (done < 1)
                {
                    Debug.LogWarning($"[ExperimentManager] BlockRest 後の遷移判定: 完了ブロック0（想定外）。FirstTaskMain にフォールバック。");
                    return FirstTaskMain;
                }
                return ExperimentState.Finished; // done >= 4
        }
    }

    // D-1: 旧 NotifyTaskBCompleted() は廃止（taskBCompletedFlag 削除に伴う）。
    // TaskB(sync) 完了時は TaskBController が直接 ChangeState(BlockRest) を呼ぶ。

    // v5.3 Phase A.5: 全パネルからの「次へ」ボタン。フロー定義に基づき次のフェーズへ。
    public void SkipCurrentPhase()
    {
        // A/B: クールダウン・計測中ロックのガード
        if (BlockNavigation("Skip")) return;

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
        if (next.Value == ExperimentState.StartMenu)
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
        // A/B: クールダウン・計測中ロックのガード
        if (BlockNavigation("Back")) return;

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
            default: return null; // StartMenu は対象外
        }
    }

    private ExperimentState? GetPreviousState(ExperimentState state)
    {
        // D-1 新フロー: Practice → TaskB(async) → BlockRest → TaskA(async) → BlockRest
        //            → TaskB_Induction → TaskB_Baseline → TaskB(sync) → BlockRest
        //            → TaskA_Induction → TaskA_Baseline → TaskA(sync) → Finished
        switch (state)
        {
            // 2026-06-21: TaskA-first では練習は BlockRest（TaskA-async 完了後）の後に来る。
            //             TaskB-first では練習は先頭（直前は Idle）。
            case ExperimentState.Practice:
                return startWithTaskA ? ExperimentState.BlockRest : ExperimentState.Idle;
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
            default: return null; // Idle/StartMenu は対象外
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

        if (showStartMenu && startMenuPanel == null && idlePanel != null && !hasLoggedMissingStartMenu)
        {
            Debug.LogWarning("[ExperimentManager] Start menu panel is not assigned. Falling back to idlePanel.");
            hasLoggedMissingStartMenu = true;
        }

        SetPanelActive(startMenuPanel, showStartMenu);

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
        if (targetState == ExperimentState.StartMenu)
        {
            lastStateBeforeBlockRest = ExperimentState.TaskB_Main; // 初期値
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