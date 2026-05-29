using UnityEngine;
using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("A-2: 21試行（人差し指・中指・薬指 各7回ずつ）")]
    public int trialsPerBlock = 21;

    [Header("Timing (Task A / Phase F)")]
    [Tooltip("ブロック最初の試行前のみ適用する待機時間（秒）")]
    [SerializeField] private float autoMotionStartDelay = 5.0f;
    [Tooltip("自動屈曲の継続時間（秒）")]
    [SerializeField] private float autoMotionDuration = 0.5f;
    [Tooltip("A-2: 屈曲完了後から次の屈曲開始までのランダム待機の最小値（秒）")]
    [SerializeField] private float autoMotionIntervalMin = 5.0f;
    [Tooltip("A-2: 屈曲完了後から次の屈曲開始までのランダム待機の最大値（秒）")]
    [SerializeField] private float autoMotionIntervalMax = 10.0f;

    // A-1: HUD 向けイベント
    public event Action<int, int> OnTrialStartCue;             // (currentTrial, totalTrials)
    public event Action<AutoMotionType> OnAutoMotionStart;     // 屈曲開始時、屈曲対象の指
    public event Action OnAutoMotionEnd;                       // 屈曲終了時
    public event Action<float> OnIntervalCountdown;            // 次屈曲までの残り秒数（毎フレーム更新）
    // A-3: マイルストーン通知用イベント (残り%, 残り秒数)
    public event Action<int, float> OnProgressMilestone;
    public int TotalTrialsPerBlock => trialsPerBlock;

    // A-3: 進捗マイルストーン（残り%）。完了試行ベースで初回到達時に通知。
    private static readonly int[] ProgressMilestonesPercent = { 75, 50, 25, 10 };
    private HashSet<int> reachedMilestones;

    // D-3: メインコルーチン参照（多重起動防止用）
    private Coroutine taskAMainCoroutine;
    // D-4: 同一ステート再エントリ防止
    private ExperimentState lastHandledState = ExperimentState.Idle;

    // v5.3 Phase D: async 先 → sync 後の順序（旧 v5.2 は sync 先）
    // 現在のブロック状態（0: async, 1: sync）
    private int currentBlockIndex = 0;
    public string CurrentCondition => currentBlockIndex == 0 ? "async" : "sync";
    public bool HasRemainingBlocks => completedBlocks < TotalBlocks;
    // D-1: 順序判定用に CompletedBlocks 公開
    public int CompletedBlocks => completedBlocks;

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
        // D-4: 同一ステート再エントリ防止（OnStateChanged が誤って複数発火した場合の保護）
        if (state == lastHandledState) return;
        lastHandledState = state;

        if (state == ExperimentState.TaskA_Induction)
        {
            blockCompletionRecorded = false;
        }
        else if (state == ExperimentState.TaskA_Main)
        {
            isExcludedBlock = false; // v5.3 Phase B: VAS 判定除外は廃止。Phase D で sync/async 除外フックとして再利用候補。
            blockCompletionRecorded = false;
            // D-3: 前回のコルーチンが残っていれば停止して新規起動
            if (taskAMainCoroutine != null) StopCoroutine(taskAMainCoroutine);
            taskAMainCoroutine = StartCoroutine(TaskAMainRoutine());
        }
    }

    private IEnumerator TaskAMainRoutine()
    {
        Debug.Log($"[Task A] Starting {CurrentCondition} block. ({trialsPerBlock} trials, 3指ランダム)");
        // v5.3 マーカー補完: Task A 全体の開始（最初のブロックのみ送出）
        if (completedBlocks == 0)
        {
            SendMarker("TaskA_Start");
        }
        // v5.3 Phase D: ブロック単位の開始マーカー
        SendMarker($"BlockStart_A_{CurrentCondition}");

        // A-3: マイルストーン状態をブロックごとにリセット
        reachedMilestones = new HashSet<int>();

        // A-2: 試行シーケンスを生成（各指 trialsPerBlock/3 回ずつ → Fisher-Yates シャッフル）
        List<AutoMotionType> motionSequence = GenerateMotionSequence(trialsPerBlock);

        // v5.3 Phase F: ブロック最初の試行前に autoMotionStartDelay 秒待機。
        // 待機中もハンドトラッキングは継続され手の微細な揺れが VR にリアルタイム反映される。
        yield return new WaitForSeconds(autoMotionStartDelay);

        for (int trial = 1; trial <= trialsPerBlock; trial++)
        {
            AutoMotionType motionType = motionSequence[trial - 1];

            float trialStartTime = Time.realtimeSinceStartup;

            // A-3: 屈曲する指を Debug.Log に明示出力
            Debug.Log($"[Task A] Trial {trial}/{trialsPerBlock} ({CurrentCondition}): Flexing {motionType}");

            // 1. 試行開始マーカー送出 + HUD 通知
            SendMarker($"TrialStart_A_{CurrentCondition}_{trial}_{motionType}");
            OnTrialStartCue?.Invoke(trial, trialsPerBlock);

            // 2. 自動屈曲開始
            SendMarker($"AutoMotionStart_A_{CurrentCondition}_{trial}_{motionType}");
            OnAutoMotionStart?.Invoke(motionType);
            // A-5: handVisualizer 呼び出し前後のデバッグログ（屈曲が起こらない問題の調査用）
            if (handVisualizer == null)
            {
                Debug.LogError("[Task A] handVisualizer が null です。Inspector でアタッチしてください。");
            }
            else
            {
                Debug.Log($"[Task A] handVisualizer.StartAutoMotion({motionType}, {autoMotionDuration}) を呼び出し");
                handVisualizer.StartAutoMotion(motionType, autoMotionDuration);
                Debug.Log($"[Task A] StartAutoMotion 完了。handVisualizer.isAutoMode={handVisualizer.isAutoMode}");
            }

            float motionOnsetTime = Time.realtimeSinceStartup;

            // 3. 自動屈曲の完了を待機
            yield return new WaitForSeconds(autoMotionDuration);
            OnAutoMotionEnd?.Invoke();

            float trialEndTime = Time.realtimeSinceStartup;

            // 4. 試行終了マーカー送出
            SendMarker($"TrialEnd_A_{CurrentCondition}_{trial}");

            // 5. CSV ログ
            LogTrialData(trial, CurrentCondition, motionType.ToString(), trialStartTime, motionOnsetTime, trialEndTime, isExcludedBlock);

            // A-3: マイルストーン到達判定（残り 75/50/25/10%、初回到達時のみ通知）
            CheckProgressMilestone(trial, trialsPerBlock);

            // 6. 次の試行までランダム待機（最終試行は不要）
            if (trial < trialsPerBlock)
            {
                float intervalSeconds = UnityEngine.Random.Range(autoMotionIntervalMin, autoMotionIntervalMax);
                float remaining = intervalSeconds;
                while (remaining > 0f)
                {
                    OnIntervalCountdown?.Invoke(remaining);
                    yield return null;
                    remaining -= Time.deltaTime;
                }
                OnIntervalCountdown?.Invoke(0f);
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

    /// <summary>
    /// A-2: trialsPerBlock を 3指（人差し指・中指・薬指）で均等に割り振り、Fisher-Yates シャッフル。
    /// trialsPerBlock が 3 の倍数でない場合は余りを最初の指（人差し指→中指→薬指）に1つずつ追加。
    /// </summary>
    private List<AutoMotionType> GenerateMotionSequence(int total)
    {
        var sequence = new List<AutoMotionType>();
        int perFinger = total / 3;
        int remainder = total - perFinger * 3;

        // 各指の基本回数を追加
        for (int i = 0; i < perFinger; i++) sequence.Add(AutoMotionType.IndexFingerFlexion);
        for (int i = 0; i < perFinger; i++) sequence.Add(AutoMotionType.MiddleFingerFlexion);
        for (int i = 0; i < perFinger; i++) sequence.Add(AutoMotionType.RingFingerFlexion);

        // 余りを分配（21試行なら remainder=0、22なら index に+1、23なら index/middle に+1）
        AutoMotionType[] order = { AutoMotionType.IndexFingerFlexion, AutoMotionType.MiddleFingerFlexion, AutoMotionType.RingFingerFlexion };
        for (int i = 0; i < remainder; i++) sequence.Add(order[i]);

        // Fisher-Yates シャッフル
        for (int i = sequence.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (sequence[i], sequence[j]) = (sequence[j], sequence[i]);
        }

        Debug.Log($"[Task A] MotionSequence (n={total}): {string.Join(",", sequence)}");
        return sequence;
    }

    /// <summary>
    /// A-3: 完了した試行数から残り進行率を計算し、マイルストーン（残り 75/50/25/10%）に
    /// 初回到達したタイミングで OnProgressMilestone を発火する。
    /// 残り秒数は (残り試行数) × (autoMotionDuration + 平均待機時間) で概算。
    /// </summary>
    private void CheckProgressMilestone(int completedTrial, int totalTrials)
    {
        if (reachedMilestones == null) return;

        // 残り% = (残り試行数 / 全試行数) × 100
        int remainingTrials = totalTrials - completedTrial;
        float remainingPercent = (float)remainingTrials / totalTrials * 100f;

        // ProgressMilestonesPercent は降順（75, 50, 25, 10）なので、各マイルストーン以下に達したか判定
        foreach (int milestone in ProgressMilestonesPercent)
        {
            // remainingPercent が milestone 以下に降りた瞬間（初回のみ）
            if (remainingPercent <= milestone && !reachedMilestones.Contains(milestone))
            {
                reachedMilestones.Add(milestone);

                // 残り秒数を概算（残り試行 × 1試行平均時間）
                float avgIntervalSec = (autoMotionIntervalMin + autoMotionIntervalMax) * 0.5f;
                float avgTrialSec = autoMotionDuration + avgIntervalSec;
                float remainingSeconds = remainingTrials * avgTrialSec;

                Debug.Log($"[Task A] Milestone reached: 残り {milestone}% (≈ {remainingSeconds:F0}秒, 残り試行 {remainingTrials}/{totalTrials})");
                SendMarker($"ProgressMilestone_A_{CurrentCondition}_Remaining{milestone}pct");
                OnProgressMilestone?.Invoke(milestone, remainingSeconds);
                break; // 1試行で1マイルストーンのみ発火
            }
        }
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
        // D-3: コルーチン参照もクリア
        taskAMainCoroutine = null;
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