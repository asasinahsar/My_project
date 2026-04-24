using UnityEngine;
using System.Collections;
using System.IO;
using System;

public class TaskAController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [SerializeField] private LSLMarkerSender markerSender;

    [Header("Settings")]
    public int trialsPerBlock = 20;

    // 現在のブロック状態（0: sync, 1: async）
    private int currentBlockIndex = 0;
    public string CurrentCondition => currentBlockIndex == 0 ? "sync" : "async";

    private string logFilePath;
    private bool isExcludedBlock = false;

    private void Start()
    {
        ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
        InitializeLogFile();
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
        }
    }

    private void InitializeLogFile()
    {
        // PersistentDataPathに保存（Windowsの場合は AppData/LocalLow/Company/AppName 内）
        string directory = Path.Combine(Application.persistentDataPath, "SessionData", DateTime.Now.ToString("yyyyMMdd"));
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        logFilePath = Path.Combine(directory, "TaskA_log.csv");
        
        // ヘッダーの書き込み
        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "trial_no,condition,motion_type,trial_start_time,motion_onset_time,trial_end_time,excluded\n");
        }
    }

    private void HandleStateChanged(ExperimentState state)
    {
        if (state == ExperimentState.TaskA_Main)
        {
            isExcludedBlock = false; // VAS判定による除外フラグがあればここで受け取る設計も可能
            StartCoroutine(TaskAMainRoutine());
        }
        else if (state == ExperimentState.BlockRest)
        {
            // 休憩ステートに入ったら、次のブロック（async）に向けてインデックスを進める
            if (currentBlockIndex == 0)
            {
                currentBlockIndex++;
            }
        }
    }

    private IEnumerator TaskAMainRoutine()
    {
        Debug.Log($"[Task A] Starting {CurrentCondition} block. ({trialsPerBlock} trials)");

        for (int trial = 1; trial <= trialsPerBlock; trial++)
        {
            // 1. 試行間インターバル（2〜4秒ランダム）
            float iti = UnityEngine.Random.Range(2.0f, 4.0f);
            yield return new WaitForSeconds(iti);

            float trialStartTime = Time.realtimeSinceStartup;

            // 2. 試行開始マーカー送出
            string markerPrefix = $"TrialStart_A_{CurrentCondition}_{trial}";
            markerSender.SendMarker(markerPrefix);

            // 3. 自動動作の選択とトリガー
            AutoMotionType motionType = (AutoMotionType)UnityEngine.Random.Range(0, 4);
            handVisualizer.StartAutoMotion(motionType);

            float motionOnsetTime = Time.realtimeSinceStartup; // 実際のオンセットマーカーはHandVisualizerから飛ぶ

            // 4. 動作完了を待機（往復2秒）
            yield return new WaitForSeconds(2.0f);

            float trialEndTime = Time.realtimeSinceStartup;

            // 5. 試行終了マーカー送出
            markerSender.SendMarker($"TrialEnd_A_{CurrentCondition}_{trial}");

            // 6. CSVへのデータ記録
            LogTrialData(trial, CurrentCondition, motionType.ToString(), trialStartTime, motionOnsetTime, trialEndTime, isExcludedBlock);
        }

        Debug.Log($"[Task A] Block {CurrentCondition} Completed. Transitioning to Rest.");
        
        // 20試行終わったらブロック間休憩へ
        ExperimentManager.Instance.ChangeState(ExperimentState.BlockRest);
    }

    private void LogTrialData(int trialNo, string condition, string motionType, float startTime, float onsetTime, float endTime, bool excluded)
    {
        string logLine = $"{trialNo},{condition},{motionType},{startTime:F3},{onsetTime:F3},{endTime:F3},{(excluded ? 1 : 0)}\n";
        File.AppendAllText(logFilePath, logLine);
    }
}