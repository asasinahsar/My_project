using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LSL;
using UnityEngine.Serialization;
using UnityVirtual.LSL;

public class TaskBController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [FormerlySerializedAs("markerSender")]
    [SerializeField] private MonoBehaviour markerSenderBehaviour;

    [Header("Trial Settings")]
    public int questTrialsCount = 35;
    private int totalTrials = 55; // QUEST 35回 + 固定 20回（1 ブロックあたり）

    [Header("Phase E: Pacing & Response")]
    [SerializeField] private float pacingInterval = 5.0f;       // 屈曲検出後の最小静止区間（秒）
    [SerializeField] private int flexionCountPerTrial = 5;       // 1試行あたりの屈曲回数
    [SerializeField] private float responseWindowSeconds = 5.0f; // 回答フェーズ制限時間（秒）

    [Header("Phase E: Hand Sign Detector")]
    [SerializeField] private HandSignDetector handSignDetector;

    // v5.3 Phase D: async / sync の 2 ブロック構造（async 先 → sync 後）
    private const int TotalBlocks = 2;
    private int currentBlockIndex = 0;        // 0:async, 1:sync
    private int completedBlocks = 0;
    private bool blockCompletionRecorded = false;
    public string CurrentCondition => currentBlockIndex == 0 ? "async" : "sync";
    public bool HasRemainingBlocks => completedBlocks < TotalBlocks;

    // SoA回答受付用
    private const int InvalidSoAResponse = -1;
    private int currentSoAResponse = InvalidSoAResponse;
    
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
    private float[] questPdf;
    private const int MaxDelayMs = 1000;
    
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
        if (state == ExperimentState.TaskB_Induction)
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
            StartCoroutine(TaskBMainRoutine());
        }
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
            int loggedResponse;
            if (currentSoAResponse == 1)
            {
                loggedResponse = 1;
                SendMarker($"SoA_Yes_Trial{trial}_Dt{currentDeltaMs}ms");
            }
            else
            {
                loggedResponse = 0;
                SendMarker($"SoA_No_Trial{trial}_Dt{currentDeltaMs}ms");
                if (currentSoAResponse == InvalidSoAResponse)
                {
                    Debug.Log($"[Task B] Trial {trial}: No response (treated as No).");
                }
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

        if (HasRemainingBlocks)
        {
            // まだ sync ブロックが残っている → BlockRest 経由で sync 誘導へ
            ExperimentManager.Instance.ChangeState(ExperimentState.BlockRest);
        }
        else
        {
            // 全ブロック完了 → Task B 全体終了 → Task A へ
            SendMarker("TaskB_End");
            ExperimentManager.Instance.NotifyTaskBCompleted();
        }
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
        currentSoAResponse = response;
    }

    public void AbortTask()
    {
        StopAllCoroutines();
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
    // QUEST法 (Bayesian Threshold Estimation) ロジック
    // ==========================================================
    private void InitializeQuest()
    {
        questPdf = new float[MaxDelayMs + 1];
        float sum = 0f;
        float tGuess = 300f;
        float tGuessSd = 150f;

        // 初期分布（事前分布）として正規分布をセット
        for (int i = 0; i <= MaxDelayMs; i++)
        {
            questPdf[i] = Mathf.Exp(-Mathf.Pow(i - tGuess, 2) / (2f * tGuessSd * tGuessSd));
            sum += questPdf[i];
        }
        
        // 正規化
        for (int i = 0; i <= MaxDelayMs; i++) questPdf[i] /= sum;
    }

    private float QuestMean()
    {
        float expectedValue = 0f;
        for (int i = 0; i <= MaxDelayMs; i++) expectedValue += i * questPdf[i];
        return expectedValue;
    }

    private void QuestUpdate(float appliedDelay, int response)
    {
        float beta = 0.02f;   // 傾き（msスケールに合わせて調整）
        float gamma = 0.5f;   // チャンス水準（2択）
        float delta = 0.05f;  // 誤答率（Lapse rate）
        float sum = 0f;

        // ベイズ更新
        for (int T = 0; T <= MaxDelayMs; T++)
        {
            // 遅延検出におけるロジスティック関数型の心理測定関数
            // appliedDelay < T のとき pSoA は 1-delta に近づき、appliedDelay > T のとき gamma に近づく
            float pSoA = gamma + (1f - gamma - delta) / (1f + Mathf.Exp(beta * (appliedDelay - T)));
            
            if (response == 1) // SoAあり（遅延を検出できなかった）
                questPdf[T] *= pSoA;
            else               // SoAなし（遅延を検出した）
                questPdf[T] *= (1f - pSoA);
                
            sum += questPdf[T];
        }

        // 再正規化
        if (sum > 0)
        {
            for (int i = 0; i <= MaxDelayMs; i++) questPdf[i] /= sum;
        }
    }

    // ==========================================================
    // 固定Δt試行 (20試行) ロジック
    // ==========================================================
    private void GenerateFixedTrials()
    {
        fixedTrialsDelay = new List<float>();
        float[] conditions = { 0f, 150f, 300f, 500f };
        
        // 各5回ずつ追加
        for (int i = 0; i < 5; i++)
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
