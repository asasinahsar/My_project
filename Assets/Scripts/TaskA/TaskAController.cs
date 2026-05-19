using UnityEngine;
using System.Collections;
using System.IO;
using System;
using LSL;
using UnityEngine.Serialization;
using UnityVirtual.LSL;

public class TaskAController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;
    [FormerlySerializedAs("markerSender")]
    [SerializeField] private MonoBehaviour markerSenderBehaviour;

    [Header("Settings")]
    public int trialsPerBlock = 20;

    [Header("Stillness Detection (Task A)")]
    [SerializeField] private float stillnessSpeedThreshold = 0.05f;
    [SerializeField] private float stillnessDuration = 1.0f;
    [SerializeField] private float preMotionWait = 10.0f;

    // 現在のブロック状態（0: sync, 1: async）
    private int currentBlockIndex = 0;
    public string CurrentCondition => currentBlockIndex == 0 ? "sync" : "async";
    public bool HasRemainingBlocks => completedBlocks < TotalBlocks;

    private string logFilePath;
    private bool isExcludedBlock = false;
    // sync/async の2ブロック固定（変更する場合は条件分岐も更新）
    private const int TotalBlocks = 2;
    private int completedBlocks = 0;
    private bool blockCompletionRecorded = false;
    private IMarkerSender markerSender;

    private void Awake()
    {
        markerSender = markerSenderBehaviour as IMarkerSender;
        if (markerSender == null)
        {
            Debug.LogWarning("[TaskAController] Marker sender is not assigned or does not implement IMarkerSender.");
        }
    }

    private void Start()
    {
        ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
        InitializeLogFile();
        if (handVisualizer != null)
        {
            handVisualizer.OnMarkerRequested += HandleMarkerRequested;
        }
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
        }
        if (handVisualizer != null)
        {
            handVisualizer.OnMarkerRequested -= HandleMarkerRequested;
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
        if (state == ExperimentState.TaskA_Induction)
        {
            blockCompletionRecorded = false;
        }
        else if (state == ExperimentState.TaskA_Main)
        {
            isExcludedBlock = false; // VAS判定による除外フラグがあればここで受け取る設計も可能
            blockCompletionRecorded = false;
            StartCoroutine(TaskAMainRoutine());
        }
    }

    private IEnumerator TaskAMainRoutine()
    {
        Debug.Log($"[Task A] Starting {CurrentCondition} block. ({trialsPerBlock} trials)");

        for (int trial = 1; trial <= trialsPerBlock; trial++)
        {
            // 1. 静止確認（速度 < stillnessSpeedThreshold が stillnessDuration 秒継続）
            yield return StartCoroutine(WaitForStillness());

            // 2. 静止確認後 preMotionWait 秒待機
            yield return new WaitForSeconds(preMotionWait);

            float trialStartTime = Time.realtimeSinceStartup;

            // 3. 試行開始マーカー送出
            SendMarker($"TrialStart_A_{CurrentCondition}_{trial}");

            // 4. 自動屈曲開始（MotionOnset マーカーは HandVisualizer.OnMarkerRequested 経由で送出）
            AutoMotionType motionType = AutoMotionType.IndexFingerFlexion;
            handVisualizer.StartAutoMotion(motionType);

            float motionOnsetTime = Time.realtimeSinceStartup;

            // 5. 動作完了待機（往復 2 秒）
            yield return new WaitForSeconds(2.0f);

            float trialEndTime = Time.realtimeSinceStartup;

            // 6. 試行終了マーカー送出
            SendMarker($"TrialEnd_A_{CurrentCondition}_{trial}");

            // 7. CSV ログ
            LogTrialData(trial, CurrentCondition, motionType.ToString(), trialStartTime, motionOnsetTime, trialEndTime, isExcludedBlock);
        }

        Debug.Log($"[Task A] Block {CurrentCondition} Completed.");
        CompleteCurrentBlock();
        ExperimentManager.Instance.ChangeState(ExperimentState.BlockRest);
    }

    private IEnumerator WaitForStillness()
    {
        float stillTimer = 0f;
        while (stillTimer < stillnessDuration)
        {
            if (handVisualizer.CurrentSpeed < stillnessSpeedThreshold)
                stillTimer += Time.deltaTime;
            else
                stillTimer = 0f;
            yield return null;
        }
    }

    private void LogTrialData(int trialNo, string condition, string motionType, float startTime, float onsetTime, float endTime, bool excluded)
    {
        string logLine = $"{trialNo},{condition},{motionType},{startTime:F3},{onsetTime:F3},{endTime:F3},{(excluded ? 1 : 0)}\n";
        File.AppendAllText(logFilePath, logLine);
    }

    public void MarkCurrentBlockExcluded()
    {
        isExcludedBlock = true;
        CompleteCurrentBlock();
    }

    public void AbortTask()
    {
        StopAllCoroutines();
        if (handVisualizer != null)
        {
            handVisualizer.StopAutoMotion();
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

    private void HandleMarkerRequested(string marker)
    {
        SendMarker(marker);
    }

    private void SendMarker(string marker)
    {
        if (markerSender == null) return;
        markerSender.SendMarker(marker);
    }
}