using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LSL;
using TMPro;
using UnityEngine.Serialization;
using UnityVirtual.LSL;

public class TaskBController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [FormerlySerializedAs("markerSender")]
    [SerializeField] private MonoBehaviour markerSenderBehaviour;

    [Header("Trial Settings")]
    public int questTrialsCount = 15;
    private int totalTrials = 23; // QUEST 15回 + 固定 8回（4Δt×2回、1 ブロックあたり）
    [Tooltip("本計測前の練習試行数（解析対象外）")]
    [SerializeField] private int practiceTrialCount = 3;
    [Tooltip("最後の屈曲検出後、回答フェーズ開始までの待機時間（秒）")]
    [SerializeField] private float postFlexionDelaySeconds = 3.0f;
    [Tooltip("練習完了後、本計測（TaskB_Main）に遷移するまでの待機時間（秒）")]
    [SerializeField] private float postPracticeDelaySeconds = 3.0f;

    [Header("Pre-Main Notice (B-7)")]
    [Tooltip("TaskB_Panel 内の説明テキスト。本番開始時に「これから本番です」を表示する")]
    [SerializeField] private TextMeshProUGUI taskBPanelMessageText;
    [Tooltip("「これから本番です」を表示する時間（秒）")]
    [SerializeField] private float preMainNoticeSeconds = 5.0f;
    [Tooltip("本番開始前に表示するメッセージ")]
    [SerializeField] private string preMainNoticeMessage = "これから本番です。\nまもなく開始します。";

    [Header("Phase E: Pacing & Response")]
    [SerializeField] private float pacingInterval = 5.0f;       // 屈曲検出後の最小静止区間（秒）
    [SerializeField] private int flexionCountPerTrial = 3;       // 1試行あたりの屈曲回数
    [SerializeField] private float responseWindowSeconds = 5.0f; // 回答フェーズ制限時間（秒）

    [Header("Phase E: Hand Sign Detector")]
    [SerializeField] private HandSignDetector handSignDetector;

    [Tooltip("指示文の読み時間取得用（未設定なら preMainNoticeSeconds のみ）")]
    [SerializeField] private TaskInstructionUI taskInstructionUI;

    // v5.3 Phase D: async / sync の 2 ブロック構造（async 先 → sync 後）
    private const int TotalBlocks = 2;
    private int currentBlockIndex = 0;        // 0:async, 1:sync
    private int completedBlocks = 0;
    private bool blockCompletionRecorded = false;
    public string CurrentCondition => currentBlockIndex == 0 ? "async" : "sync";
    public bool HasRemainingBlocks => completedBlocks < TotalBlocks;
    // B-6: HUD 等から参照するための完了ブロック数 / 試行数公開
    public int CompletedBlocks => completedBlocks;
    public int TotalTrialsPerBlock => totalTrials;
    public int PracticeTrialCount => practiceTrialCount;
    // B-8: SoAResponseUI から取得して同期するため、回答ウィンドウ時間を公開
    public float ResponseWindowSeconds => responseWindowSeconds;

    // SoA回答受付用
    private const int InvalidSoAResponse = -1;
    private int currentSoAResponse = InvalidSoAResponse;

    // D-3: メインコルーチン参照（多重起動防止用）
    private Coroutine taskBMainCoroutine;
    private Coroutine taskBPracticeCoroutine;
    // D-4: 同一ステート再エントリ防止
    private ExperimentState lastHandledState = ExperimentState.Idle;
    
    // UI表示制御用のイベント（SoAResponseUI / ParticipantHUD から購読する）
    public event Action OnSoAWindowOpened;
    public event Action OnSoAWindowClosed;

    // v5.3 Phase E2: 試行・ペース合図のフック（UI/音は Phase E3 で実装）
    public event Action OnTrialStartCue;             // 試行開始の合図
    public event Action<int, int> OnPacingCue;       // ペース合図（current, total）
    public event Action OnResponseWindowOpened;      // 回答フェーズ開始合図
    public event Action OnFlexionDetected;           // 屈曲検出時の視覚フィードバック

    private string logFilePath;
    private IMarkerSender markerSender;
    private Action movementDetectedHandler;
    
    // QUEST法用の確率密度関数（0〜1000msの各遅延閾値に対する確率）
    // B-10: Staircase 法（QUEST 法から書き換え）
    [Header("Staircase Settings (B-10)")]
    [Tooltip("Staircase 法の初期遅延値（ms）")]
    [SerializeField] private float staircaseInitialDelta = 0f;
    [Tooltip("No 応答時の遅延増加量（ms）")]
    [SerializeField] private float staircaseStepUp = 50f;
    [Tooltip("Yes 応答時の遅延減少量（ms）")]
    [SerializeField] private float staircaseStepDown = 30f;
    [Tooltip("Staircase 遅延の下限（ms）")]
    [SerializeField] private float staircaseMinDelta = 0f;
    [Tooltip("Staircase 遅延の上限（ms）")]
    [SerializeField] private float staircaseMaxDelta = 600f;

    // 現在の staircase 遅延値（試行ごとに QuestUpdate で更新）
    private float currentStaircaseDelta;

    private List<float> fixedTrialsDelay;

    private void Awake()
    {
        markerSender = markerSenderBehaviour as IMarkerSender;
        if (markerSender == null)
        {
            Debug.LogWarning("[TaskBController] Marker sender is not assigned or does not implement IMarkerSender.");
        }
    }

    private void Start()
    {
        ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
        InitializeLogFile();

        // v5.3 Phase E2: ハンドサイン検出を SubmitSoAResponse(1) に接続
        // v5.3 Phase E1 改訂: ピンチ（親指+人差し指タッチ）検出にロジック変更
        if (handSignDetector != null)
        {
            handSignDetector.OnHandSignDetected += OnHandSignDetectedHandler;
        }
        else
        {
            Debug.LogWarning("[TaskBController] HandSignDetector が未割当。回答フェーズはキーボード Y/N のみで応答受付。");
        }
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;

        if (handSignDetector != null)
        {
            handSignDetector.OnHandSignDetected -= OnHandSignDetectedHandler;
        }

        UnsubscribeMovementDetectedHandler();
    }

    private void OnHandSignDetectedHandler()
    {
        // 回答フェーズ中にハンドサイン（ピンチ）が検出された → Yes 申告
        Debug.Log($"[TaskBController] OnHandSignDetectedHandler called → SubmitSoAResponse(1)");
        SubmitSoAResponse(1);
    }

    private void InitializeLogFile()
    {
        string directory = Path.Combine(Application.persistentDataPath, "SessionData", DateTime.Now.ToString("yyyyMMdd"));
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        logFilePath = Path.Combine(directory, "TaskB_log.csv");
        if (!File.Exists(logFilePath))
        {
            // v5.3 Phase E2: motion_onset_time（単一）を削除し、flexion_count / response_time / condition を追加
            File.WriteAllText(logFilePath, "trial_no,condition,delta_ms,soa_response,trial_start_time,trial_end_time,flexion_count,response_time,quest_estimate\n");
        }
    }

    private void HandleStateChanged(ExperimentState state)
    {
        // D-4: 同一ステート再エントリ防止（OnStateChanged が誤って複数発火した場合の保護）
        if (state == lastHandledState) return;
        lastHandledState = state;

        if (state == ExperimentState.Practice)
        {
            // Phase G: 本計測前の練習ブロック（解析対象外）
            // D-3: 前回のコルーチンが残っていれば停止して新規起動
            if (taskBPracticeCoroutine != null) StopCoroutine(taskBPracticeCoroutine);
            taskBPracticeCoroutine = StartCoroutine(TaskBPracticeRoutine());
        }
        else if (state == ExperimentState.TaskB_Induction)
        {
            // v5.3 Phase D: sync ブロックの誘導フロー進入時もブロック完了フラグをリセット
            blockCompletionRecorded = false;
        }
        else if (state == ExperimentState.TaskB_Main)
        {
            // v5.3 Phase D: 各ブロック開始時に QUEST を再初期化（async/sync で独立推定）
            blockCompletionRecorded = false;
            if (handVisualizer != null)
                handVisualizer.EnableOnsetDetection = true;
            InitializeQuest();
            GenerateFixedTrials();
            // D-3: 前回のコルーチンが残っていれば停止して新規起動
            if (taskBMainCoroutine != null) StopCoroutine(taskBMainCoroutine);
            taskBMainCoroutine = StartCoroutine(TaskBMainRoutine());
        }
    }

    // ==========================================================
    // Phase G: 練習試行コルーチン（解析対象外、Δt=0ms固定）
    // ==========================================================
    private IEnumerator TaskBPracticeRoutine()
    {
        Debug.Log($"[Task B] Starting practice block. ({practiceTrialCount} trials, Δt=0ms)");
        SendMarker("PracticeStart_B");

        handVisualizer.delayMs = 0f;

        for (int trial = 1; trial <= practiceTrialCount; trial++)
        {
            // 試行間インターバル（2〜4秒ランダム）
            float iti = UnityEngine.Random.Range(2.0f, 4.0f);
            yield return new WaitForSeconds(iti);

            float trialStartTime = Time.realtimeSinceStartup;

            // 試行開始マーカー + 視覚/音合図
            SendMarker($"TrialStart_B_Practice_{trial}_Delta0ms");
            OnTrialStartCue?.Invoke();

            // ============ 計測フェーズ ============
            int flexionCount = 0;
            UnsubscribeMovementDetectedHandler();
            handVisualizer.EnableOnsetDetection = true;

            for (int cycle = 1; cycle <= flexionCountPerTrial; cycle++)
            {
                OnPacingCue?.Invoke(cycle, flexionCountPerTrial);

                bool detectedThisCycle = false;
                movementDetectedHandler = () => detectedThisCycle = true;
                handVisualizer.OnMovementDetected += movementDetectedHandler;
                handVisualizer.ResetMotionDetection();

                while (!detectedThisCycle) yield return null;

                UnsubscribeMovementDetectedHandler();

                flexionCount++;
                SendMarker($"FlexionDetected_B_Practice_{trial}_count{flexionCount}");
                OnFlexionDetected?.Invoke();

                if (cycle < flexionCountPerTrial)
                    yield return new WaitForSeconds(pacingInterval);
            }

            handVisualizer.EnableOnsetDetection = false;

            // B-3: 最後の屈曲検出後、回答フェーズ開始までの待機（被験者に間を与える）
            yield return new WaitForSeconds(postFlexionDelaySeconds);

            // ============ 回答フェーズ ============
            SendMarker($"ResponseWindowStart_B_Practice_{trial}");
            OnResponseWindowOpened?.Invoke();
            OnSoAWindowOpened?.Invoke();

            currentSoAResponse = InvalidSoAResponse;
            if (handSignDetector != null)
                handSignDetector.EnableDetection = true;

            float responseTimer = 0f;
            while (currentSoAResponse == InvalidSoAResponse && responseTimer < responseWindowSeconds)
            {
                responseTimer += Time.deltaTime;
                yield return null;
            }

            if (handSignDetector != null)
                handSignDetector.EnableDetection = false;
            OnSoAWindowClosed?.Invoke();

            int response = currentSoAResponse == 1 ? 1 : 0;
            float trialEndTime = Time.realtimeSinceStartup;

            // B-9: Practice でも Trial 番号付き Yes/No ログ
            if (response == 1)
            {
                SendMarker($"SoA_Yes_Practice_{trial}_Dt0ms");
                Debug.Log($"[Task B Practice] Trial {trial}: Yes");
            }
            else
            {
                SendMarker($"SoA_No_Practice_{trial}_Dt0ms");
                string noteSuffix = (currentSoAResponse == InvalidSoAResponse) ? " (no response, treated as No)" : "";
                Debug.Log($"[Task B Practice] Trial {trial}: No{noteSuffix}");
            }

            SendMarker($"TrialEnd_B_Practice_{trial}");
            // condition="practice", quest_estimate=0 で既存ログファイルに記録
            LogTrialData(trial, "practice", 0f, response, trialStartTime, trialEndTime, flexionCount, responseTimer, 0f);
        }

        handVisualizer.delayMs = 0f;
        SendMarker("PracticeEnd_B");
        Debug.Log($"[Task B] Practice block completed. Waiting {postPracticeDelaySeconds}s before advancing to TaskB_Main (async).");

        // B-1: 練習完了 → 数秒待機 → AdvanceState() で TaskB_Panel (TaskB_Main) へ
        yield return new WaitForSeconds(postPracticeDelaySeconds);

        Debug.Log("[Task B] Advancing to TaskB_Main (async).");
        ExperimentManager.Instance.AdvanceState();
    }

    // ==========================================================
    // メイン試行コルーチン
    // ==========================================================
    private IEnumerator TaskBMainRoutine()
    {
        Debug.Log($"[Task B] Starting {CurrentCondition} block. ({totalTrials} trials)");
        // v5.3 マーカー補完: Task B 全体の開始（最初のブロックのみ送出）
        if (completedBlocks == 0)
        {
            SendMarker("TaskB_Start");
        }
        // v5.3 Phase D: ブロック単位の開始マーカー
        SendMarker($"BlockStart_B_{CurrentCondition}");

        // B-7: TaskB_Panel に「これから本番です」を表示してから試行開始
        // 指示文の読み時間（文字数ベース自動計算）と preMainNoticeSeconds の大きい方を採用し、
        // 被験者が指示を読み終えてから試行を開始する。
        if (taskBPanelMessageText != null)
        {
            float readSec = taskInstructionUI != null
                ? taskInstructionUI.GetReadSecondsForState(ExperimentState.TaskB_Main) : 0f;
            taskBPanelMessageText.text = preMainNoticeMessage;
            SendMarker($"PreMainNotice_B_{CurrentCondition}");
            yield return new WaitForSeconds(Mathf.Max(preMainNoticeSeconds, readSec));
            taskBPanelMessageText.text = "";
        }

        for (int trial = 1; trial <= totalTrials; trial++)
        {
            // 1. 次のΔtを決定（QUESTまたは固定試行）
            float currentDeltaMs = 0f;
            if (trial <= questTrialsCount)
                currentDeltaMs = Mathf.Round(QuestMean());
            else
                currentDeltaMs = fixedTrialsDelay[trial - questTrialsCount - 1];

            // v5.3 Phase E2: 試行中は常時この遅延でバーチャルハンドが描画される（HandVisualizer.ApplyDelayedPose 経由）
            handVisualizer.delayMs = currentDeltaMs;
            float currentQuestEstimate = QuestMean();

            // 2. 試行間インターバル（2〜4秒ランダム）
            float iti = UnityEngine.Random.Range(2.0f, 4.0f);
            yield return new WaitForSeconds(iti);

            float trialStartTime = Time.realtimeSinceStartup;

            // 3. 試行開始マーカー送出 + 視覚/音合図フック
            SendMarker($"TrialStart_B_{trial}_Delta{currentDeltaMs}ms");
            OnTrialStartCue?.Invoke();

            // ============ 計測フェーズ ============
            // v5.3 Phase E2: ペース化屈曲を flexionCountPerTrial 回検出する。
            // 各屈曲は最小静止区間 pacingInterval 秒で分離（直前運動の余波が運動準備窓に混入しないように）。
            int flexionCount = 0;
            UnsubscribeMovementDetectedHandler();
            handVisualizer.EnableOnsetDetection = true;

            for (int cycle = 1; cycle <= flexionCountPerTrial; cycle++)
            {
                // ペース合図（UI/音は Phase E3 で実装）
                OnPacingCue?.Invoke(cycle, flexionCountPerTrial);

                // 屈曲検出ハンドラを毎周期登録 → 検出されるまで待機（無制限）
                bool detectedThisCycle = false;
                movementDetectedHandler = () => detectedThisCycle = true;
                handVisualizer.OnMovementDetected += movementDetectedHandler;
                handVisualizer.ResetMotionDetection();

                while (!detectedThisCycle) yield return null;

                UnsubscribeMovementDetectedHandler();

                flexionCount++;
                SendMarker($"FlexionDetected_B_{trial}_count{flexionCount}");
                OnFlexionDetected?.Invoke();

                // 最後の屈曲以外は次の合図まで pacingInterval 秒の静止区間
                if (cycle < flexionCountPerTrial)
                {
                    yield return new WaitForSeconds(pacingInterval);
                }
            }

            handVisualizer.EnableOnsetDetection = false;

            // B-3: 最後の屈曲検出後、回答フェーズ開始までの待機（被験者に間を与える）
            yield return new WaitForSeconds(postFlexionDelaySeconds);

            // ============ 回答フェーズ ============
            SendMarker($"ResponseWindowStart_B_{trial}");
            OnResponseWindowOpened?.Invoke();
            OnSoAWindowOpened?.Invoke();

            currentSoAResponse = InvalidSoAResponse;
            if (handSignDetector != null)
            {
                handSignDetector.EnableDetection = true;
            }

            float responseTimer = 0f;
            while (currentSoAResponse == InvalidSoAResponse && responseTimer < responseWindowSeconds)
            {
                responseTimer += Time.deltaTime;
                yield return null;
            }

            if (handSignDetector != null)
            {
                handSignDetector.EnableDetection = false;
            }
            OnSoAWindowClosed?.Invoke();

            float responseTime = (currentSoAResponse != InvalidSoAResponse) ? responseTimer : -1f;
            float trialEndTime = Time.realtimeSinceStartup;

            // 応答結果（無反応＝No に統合）
            // B-9: Yes/No 両方で Trial 番号付きの統一ログを出力
            int loggedResponse;
            if (currentSoAResponse == 1)
            {
                loggedResponse = 1;
                SendMarker($"SoA_Yes_Trial{trial}_Dt{currentDeltaMs}ms");
                Debug.Log($"[Task B] Trial {trial}: Yes (Δt={currentDeltaMs}ms)");
            }
            else
            {
                loggedResponse = 0;
                SendMarker($"SoA_No_Trial{trial}_Dt{currentDeltaMs}ms");
                string noteSuffix = (currentSoAResponse == InvalidSoAResponse) ? " (no response, treated as No)" : "";
                Debug.Log($"[Task B] Trial {trial}: No{noteSuffix} (Δt={currentDeltaMs}ms)");
            }

            // QUEST 更新（QUEST フェーズ内のみ）
            if (trial <= questTrialsCount)
            {
                QuestUpdate(currentDeltaMs, loggedResponse);
            }

            // 試行終了マーカーとロギング
            SendMarker($"TrialEnd_B_{trial}");
            LogTrialData(trial, CurrentCondition, currentDeltaMs, loggedResponse, trialStartTime, trialEndTime, flexionCount, responseTime, currentQuestEstimate);
        }

        Debug.Log($"[Task B] Block {CurrentCondition} completed. Block τ_SoA estimate: {QuestMean()}ms");
        if (handVisualizer != null)
            handVisualizer.EnableOnsetDetection = false;

        // v5.3 Phase D: ブロック単位の終了マーカー
        SendMarker($"BlockEnd_B_{CurrentCondition}");

        CompleteCurrentBlock();

        // D-1: 新フローでは TaskB(async)/TaskB(sync) いずれの完了後も BlockRest に遷移
        // 次のステート判定は ExperimentManager.DetermineNextStateAfterBlockRest が行う
        //   TaskB(async) 完了 → BlockRest → TaskA(async)
        //   TaskB(sync)  完了 → BlockRest → TaskA_Induction
        if (!HasRemainingBlocks)
        {
            SendMarker("TaskB_End");
        }
        ExperimentManager.Instance.ChangeState(ExperimentState.BlockRest);
    }

    private void CompleteCurrentBlock()
    {
        if (blockCompletionRecorded) return;
        if (completedBlocks >= TotalBlocks) return;

        blockCompletionRecorded = true;
        int nextCompletedBlocks = completedBlocks + 1;
        if (nextCompletedBlocks > TotalBlocks) return;

        completedBlocks = nextCompletedBlocks;
        if (completedBlocks < TotalBlocks)
        {
            currentBlockIndex = completedBlocks;
        }
    }

    // UIやキーボードから応答をセットするためのパブリックメソッド
    public void SubmitSoAResponse(int response)
    {
        Debug.Log($"[TaskBController] SubmitSoAResponse({response}) called. Previous currentSoAResponse={currentSoAResponse}, EnableDetection={(handSignDetector != null ? handSignDetector.EnableDetection.ToString() : "N/A")}");
        currentSoAResponse = response;
    }

    // D-2: SkipCurrentPhase で TaskB_Main をスキップ時に呼ぶ。
    // CompletedBlocks をインクリメントして次ブロックへの進行を確定させる。
    public void MarkCurrentBlockExcluded()
    {
        CompleteCurrentBlock();
    }

    // C（2026-06-01）: ExpStart 時にブロック進捗をリセットする。
    // 冒頭の誤操作（PhaseSkipped 連発）で completedBlocks が進み、async ブロックが
    // 飛ばされて sync から始まる問題を防ぐ。
    public void ResetBlockProgress()
    {
        completedBlocks = 0;
        currentBlockIndex = 0;
        blockCompletionRecorded = false;
        lastHandledState = ExperimentState.Idle;
    }

    public void AbortTask()
    {
        StopAllCoroutines();
        // D-3: コルーチン参照もクリア（多重起動防止用）
        taskBMainCoroutine = null;
        taskBPracticeCoroutine = null;
        UnsubscribeMovementDetectedHandler();
        currentSoAResponse = InvalidSoAResponse;
        OnSoAWindowClosed?.Invoke();
        if (handVisualizer != null)
        {
            handVisualizer.EnableOnsetDetection = false;
            handVisualizer.delayMs = 0f;
            handVisualizer.ResetMotionDetection();
        }
        // v5.3 Phase E2: 中断時にハンドサイン検出も無効化
        if (handSignDetector != null)
        {
            handSignDetector.EnableDetection = false;
        }
    }

    private void UnsubscribeMovementDetectedHandler()
    {
        if (handVisualizer == null || movementDetectedHandler == null) return;

        handVisualizer.OnMovementDetected -= movementDetectedHandler;
        movementDetectedHandler = null;
    }

    private void LogTrialData(int trialNo, string condition, float deltaMs, int response, float startTime, float endTime, int flexionCount, float responseTime, float questEst)
    {
        // v5.3 Phase E2: motion_onset_time を廃止し condition / flexion_count / response_time を追加。
        string logLine = $"{trialNo},{condition},{deltaMs},{response},{startTime:F3},{endTime:F3},{flexionCount},{responseTime:F3},{questEst:F2}\n";
        File.AppendAllText(logFilePath, logLine);
    }

    private void SendMarker(string marker)
    {
        if (markerSender == null) return;
        markerSender.SendMarker(marker);
    }

    // ==========================================================
    // B-10: Staircase 法（旧 QUEST 法から書き換え）
    //   - 初期遅延 = staircaseInitialDelta（既定 0ms）
    //   - No 応答（SoA 崩壊未検出）→ currentStaircaseDelta += staircaseStepUp
    //   - Yes 応答（SoA 崩壊検出）→ currentStaircaseDelta -= staircaseStepDown
    //   - 範囲 [staircaseMinDelta, staircaseMaxDelta] にクランプ
    //   ※ InitializeQuest/QuestMean/QuestUpdate のメソッド名は呼び出し側互換のため維持
    // ==========================================================
    private void InitializeQuest()
    {
        currentStaircaseDelta = staircaseInitialDelta;
        Debug.Log($"[Task B] Staircase initialized: delta={currentStaircaseDelta}ms (stepUp={staircaseStepUp}, stepDown={staircaseStepDown}, range=[{staircaseMinDelta},{staircaseMaxDelta}])");
    }

    /// <summary>
    /// 現在の staircase 遅延値を返す（旧 QUEST 平均値の代替）。
    /// </summary>
    private float QuestMean()
    {
        return currentStaircaseDelta;
    }

    /// <summary>
    /// 応答に応じて staircase 遅延を更新する。
    /// response==1（Yes）で減少、それ以外（No）で増加。
    /// </summary>
    private void QuestUpdate(float appliedDelay, int response)
    {
        float before = currentStaircaseDelta;
        if (response == 1) // Yes（SoA 崩壊検出）→ 遅延を小さく
        {
            currentStaircaseDelta -= staircaseStepDown;
        }
        else               // No（SoA 崩壊未検出）→ 遅延を大きく
        {
            currentStaircaseDelta += staircaseStepUp;
        }
        currentStaircaseDelta = Mathf.Clamp(currentStaircaseDelta, staircaseMinDelta, staircaseMaxDelta);
        Debug.Log($"[Task B] Staircase update: {before}ms → {currentStaircaseDelta}ms (response={response})");
    }

    // ==========================================================
    // 固定Δt試行 (20試行) ロジック
    // ==========================================================
    private void GenerateFixedTrials()
    {
        fixedTrialsDelay = new List<float>();
        float[] conditions = { 0f, 150f, 300f, 500f };

        // 各2回ずつ追加（4Δt × 2 = 8試行）
        for (int i = 0; i < 2; i++)
            fixedTrialsDelay.AddRange(conditions);

        // Fisher-Yates シャッフル
        for (int i = 0; i < fixedTrialsDelay.Count; i++)
        {
            int randIndex = UnityEngine.Random.Range(i, fixedTrialsDelay.Count);
            float temp = fixedTrialsDelay[i];
            fixedTrialsDelay[i] = fixedTrialsDelay[randIndex];
            fixedTrialsDelay[randIndex] = temp;
        }
    }
}
