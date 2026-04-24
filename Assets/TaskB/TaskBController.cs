using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

public class TaskBController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [SerializeField] private LSLMarkerSender markerSender;

    [Header("Trial Settings")]
    public int questTrialsCount = 35;
    private int totalTrials = 55; // QUEST 35回 + 固定 20回
    
    // SoA回答受付用
    private int currentSoAResponse = -1;
    
    // UI表示制御用のイベント（VASInputUI.cs等から購読する）
    public event Action OnSoAWindowOpened;
    public event Action OnSoAWindowClosed;

    private string logFilePath;
    
    // QUEST法用の確率密度関数（0〜1000msの各遅延閾値に対する確率）
    private float[] questPdf;
    private const int MaxDelayMs = 1000;
    
    private List<float> fixedTrialsDelay;

    private void Start()
    {
        ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
        InitializeLogFile();
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void InitializeLogFile()
    {
        string directory = Path.Combine(Application.persistentDataPath, "SessionData", DateTime.Now.ToString("yyyyMMdd"));
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        logFilePath = Path.Combine(directory, "TaskB_log.csv");
        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "trial_no,delta_ms,soa_response,trial_start_time,motion_onset_time,trial_end_time,quest_estimate\n");
        }
    }

    private void HandleStateChanged(ExperimentState state)
    {
        if (state == ExperimentState.TaskB_Main)
        {
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
        Debug.Log($"[Task B] Starting Main Phase. ({totalTrials} trials)");

        for (int trial = 1; trial <= totalTrials; trial++)
        {
            // 1. 次のΔtを決定（QUESTまたは固定試行）
            float currentDeltaMs = 0f;
            if (trial <= questTrialsCount)
                currentDeltaMs = Mathf.Round(QuestMean());
            else
                currentDeltaMs = fixedTrialsDelay[trial - questTrialsCount - 1];

            // HandVisualizerへ遅延をセット
            handVisualizer.delayMs = currentDeltaMs;
            float currentQuestEstimate = QuestMean();

            // 2. 試行間インターバル（2〜4秒ランダム）
            float iti = UnityEngine.Random.Range(2.0f, 4.0f);
            yield return new WaitForSeconds(iti);

            float trialStartTime = Time.realtimeSinceStartup;

            // 3. 試行開始マーカー送出
            markerSender.SendMarker($"TrialStart_B_{trial}_Delta{currentDeltaMs}ms");

            // 4. 運動開始（Onset）の待機
            bool motionDetected = false;
            Action onDetect = () => motionDetected = true;
            handVisualizer.OnMovementDetected += onDetect;
            handVisualizer.ResetMotionDetection();

            // 実際の運動が検知されるまで待機（タイムスタンプはマーカー側で記録）
            while (!motionDetected) yield return null;
            handVisualizer.OnMovementDetected -= onDetect;

            // 5. 仮想手が動くまでの遅延（Δt）を待機
            if (currentDeltaMs > 0)
                yield return new WaitForSeconds(currentDeltaMs / 1000f);

            float motionOnsetTime = Time.realtimeSinceStartup;

            // 6. バーチャルハンドが動いた瞬間にマーカー送出
            markerSender.SendMarker($"MotionOnset_B_Delta{currentDeltaMs}ms");

            // 7. 実験者または被験者からのSoA有無（1/0）を記録（最大3秒待機）
            currentSoAResponse = -1;
            OnSoAWindowOpened?.Invoke(); // UIを表示

            float responseTimer = 0f;
            while (currentSoAResponse == -1 && responseTimer < 3.0f)
            {
                responseTimer += Time.deltaTime;
                yield return null;
            }

            OnSoAWindowClosed?.Invoke(); // UIを非表示

            float trialEndTime = Time.realtimeSinceStartup;

            // 8. 応答処理とQUEST更新
            if (currentSoAResponse != -1)
            {
                markerSender.SendMarker($"SoAResponse_{currentSoAResponse}");
                
                // QUESTフェーズ中であれば事後分布を更新
                if (trial <= questTrialsCount)
                {
                    QuestUpdate(currentDeltaMs, currentSoAResponse);
                }
            }
            else
            {
                // 3秒以内に回答がなかった場合
                markerSender.SendMarker("SoAResponse_Missed");
                Debug.LogWarning($"[Task B] Trial {trial}: No response within 3s window.");
            }

            // 9. 試行終了マーカーとロギング
            markerSender.SendMarker($"TrialEnd_B_{trial}");
            LogTrialData(trial, currentDeltaMs, currentSoAResponse, trialStartTime, motionOnsetTime, trialEndTime, currentQuestEstimate);
        }

        Debug.Log($"[Task B] Completed! Final Estimated τ_SoA: {QuestMean()}ms");
        ExperimentManager.Instance.ChangeState(ExperimentState.Finished);
    }

    // UIやキーボードから応答をセットするためのパブリックメソッド
    public void SubmitSoAResponse(int response)
    {
        currentSoAResponse = response;
    }

    private void LogTrialData(int trialNo, float deltaMs, int response, float startTime, float onsetTime, float endTime, float questEst)
    {
        string logLine = $"{trialNo},{deltaMs},{response},{startTime:F3},{onsetTime:F3},{endTime:F3},{questEst:F2}\n";
        File.AppendAllText(logFilePath, logLine);
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