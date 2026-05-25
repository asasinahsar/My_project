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

    [Header("Timing (Task A / Phase F)")]
    [Tooltip("ブロック最初の試行前のみ適用する待機時間（秒）")]
    [SerializeField] private float autoMotionStartDelay = 5.0f;
    [Tooltip("人差し指自動屈曲の継続時間（秒）")]
    [SerializeField] private float autoMotionDuration = 0.5f;
    [Tooltip("屈曲完了後から次の屈曲開始までの待機時間（秒）")]
    [SerializeField] private float autoMotionInterval = 10.0f;

    // v5.3 Phase D: async 先 → sync 後の順序（旧 v5.2 は sync 先）
    // 現在のブロック状態（0: async, 1: sync）
    private int currentBlockIndex = 0;
    public string CurrentCondition => currentBlockIndex == 0 ? "async" : "sync";
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
            isExcludedBlock = false; // v5.3 Phase B: VAS 判定除外は廃止。Phase D で sync/async 除外フックとして再利用候補。
            blockCompletionRecorded = false;
            StartCoroutine(TaskAMainRoutine());
        }
    }

    private IEnumerator TaskAMainRoutine()
    {
        Debug.Log($"[Task A] Starting {CurrentCondition} block. ({trialsPerBlock} trials)");
        // v5.3 マーカー補完: Task A 全体の開始（最初のブロックのみ送出）
        if (completedBlocks == 0)
        {
            SendMarker("TaskA_Start");
        }
        // v5.3 Phase D: ブロック単位の開始マーカー
        SendMarker($"BlockStart_A_{CurrentCondition}");

        AutoMotionType motionType = AutoMotionType.IndexFingerFlexion;

        // v5.3 Phase F: ブロック最初の試行前に autoMotionStartDelay 秒待機。
        // 速度ベース静止確認（WaitForStillness）は廃止し、固定時間待機に変更。
        // 待機中もハンドトラッキングは継続され手の微細な揺れが VR にリアルタイム反映される。
        yield return new WaitForSeconds(autoMotionStartDelay);

        for (int trial = 1; trial <= trialsPerBlock; trial++)
        {
            float trialStartTime = Time.realtimeSinceStartup;

            // 1. 試行開始マーカー送出
            SendMarker($"TrialStart_A_{CurrentCondition}_{trial}");

            // 2. 自動屈曲開始（MotionOnset マーカーは HandVisualizer.OnMarkerRequested 経由でも送出）
            // v5.3 Phase F: autoMotionDuration を HandVisualizer に渡す
            SendMarker($"AutoMotionStart_A_{CurrentCondition}_{trial}");
            handVisualizer.StartAutoMotion(motionType, autoMotionDuration);

            float motionOnsetTime = Time.realtimeSinceStartup;

            // 3. 自動屈曲の完了を待機
            yield return new WaitForSeconds(autoMotionDuration);

            float trialEndTime = Time.realtimeSinceStartup;

            // 4. 試行終了マーカー送出
            SendMarker($"TrialEnd_A_{CurrentCondition}_{trial}");

            // 5. CSV ログ
            LogTrialData(trial, CurrentCondition, motionType.ToString(), trialStartTime, motionOnsetTime, trialEndTime, isExcludedBlock);

            // 6. 次の試行まで autoMotionInterval 秒待機（最終試行は不要）
            if (trial < trialsPerBlock)
            {
                yield return new WaitForSeconds(autoMotionInterval);
            }
        }

        Debug.Log($"[Task A] Block {CurrentCondition} Completed.");
        // v5.3 Phase D: ブロック単位の終了マーカー（CompleteCurrentBlock 前に condition を確定送出）
        SendMarker($"BlockEnd_A_{CurrentCondition}");
        CompleteCurrentBlock();
        // v5.3 マーカー補完: 全 Task A ブロック完了時に Task A 全体の終了マーカー
        if (completedBlocks >= TotalBlocks)
        {
            SendMarker("TaskA_End");
        }
        ExperimentManager.Instance.ChangeState(ExperimentState.BlockRest);
    }

    private void LogTrialData(int trialNo, string condition, string motionType, float startTime, float onsetTime, float endTime, bool excluded)
    {
        string logLine = $"{trialNo},{condition},{motionType},{startTime:F3},{onsetTime:F3},{endTime:F3},{(excluded ? 1 : 0)}\n";
        File.AppendAllText(logFilePath, logLine);
    }

    // v5.3 Phase B: VAS 判定による除外フックだったが、VAS 全廃で呼び出し元なし。
    // Phase D で sync/async 構造を入れる際に再利用される可能性があるため残置。
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