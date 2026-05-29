using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// A-4: Task A 計測中に被験者向けにマイルストーン通知のみを表示する HUD。
///
/// 普段は空白。残り75%/50%/25%/10% に達したタイミングで、
/// 「あと約 N 分です」を3秒間大きく表示 + ビープ音で通知する。
///
/// TaskAController.OnProgressMilestone を購読して自動更新する。
/// 表示制御は ExperimentManager.OnStateChanged を購読し、TaskA_Main 中のみ表示。
/// </summary>
public class TaskAHUD : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TaskAController taskAController;

    [Header("Display Elements")]
    [Tooltip("「あと約 N 分です」のマイルストーン通知テキスト")]
    [SerializeField] private TextMeshProUGUI milestoneText;

    [Header("Notification Settings")]
    [Tooltip("マイルストーン通知の表示時間（秒）")]
    [SerializeField] private float notificationDuration = 3.0f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("マイルストーン通知音の周波数（Hz）")]
    [SerializeField] private float beepFrequency = 660f;
    [SerializeField] private float beepDurationSec = 0.25f;
    [Range(0f, 1f)]
    [SerializeField] private float beepVolume = 0.5f;

    private AudioClip milestoneBeep;
    private Coroutine notificationCoroutine;

    private void Awake()
    {
        milestoneBeep = GenerateBeep(beepFrequency, beepDurationSec, "MilestoneBeep");
    }

    private void Start()
    {
        if (taskAController != null)
        {
            taskAController.OnProgressMilestone += HandleProgressMilestone;
        }

        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged += HandleStateChanged;

        // 初期表示は空 + HUD 自体を非表示（TaskA_Main 入りで有効化）
        if (milestoneText != null) milestoneText.text = "";
        gameObject.SetActive(false);

        // Start() が遅延実行された場合でも現在ステートを即時反映
        if (ExperimentManager.Instance != null)
            HandleStateChanged(ExperimentManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        if (taskAController != null)
        {
            taskAController.OnProgressMilestone -= HandleProgressMilestone;
        }

        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    // ----------------------------------------------------------------
    // State Visibility Control
    // ----------------------------------------------------------------

    private void HandleStateChanged(ExperimentState state)
    {
        bool shouldShow = state == ExperimentState.TaskA_Main;
        gameObject.SetActive(shouldShow);

        if (shouldShow && milestoneText != null)
        {
            milestoneText.text = ""; // ブロック切替時に残留表示をリセット
        }
    }

    // ----------------------------------------------------------------
    // Event Handler
    // ----------------------------------------------------------------

    private void HandleProgressMilestone(int remainingPercent, float remainingSeconds)
    {
        string message = FormatRemainingMessage(remainingSeconds);
        Debug.Log($"[TaskAHUD] Milestone: {remainingPercent}% remaining → \"{message}\"");

        PlayBeep(milestoneBeep);

        if (notificationCoroutine != null) StopCoroutine(notificationCoroutine);
        notificationCoroutine = StartCoroutine(ShowNotificationRoutine(message));
    }

    private IEnumerator ShowNotificationRoutine(string message)
    {
        if (milestoneText != null)
            milestoneText.text = message;

        yield return new WaitForSeconds(notificationDuration);

        if (milestoneText != null)
            milestoneText.text = "";

        notificationCoroutine = null;
    }

    // ----------------------------------------------------------------
    // Formatting
    // ----------------------------------------------------------------

    /// <summary>
    /// 残り秒数から「あと約 N 分です」「あと約 N 秒です」のメッセージを生成する。
    /// - 60秒以上: 分単位で四捨五入（最小1分）
    /// - 60秒未満: 10秒単位で四捨五入（最小10秒）
    /// </summary>
    private static string FormatRemainingMessage(float remainingSeconds)
    {
        if (remainingSeconds >= 60f)
        {
            int minutes = Mathf.Max(1, Mathf.RoundToInt(remainingSeconds / 60f));
            return $"あと約 {minutes} 分です";
        }
        else
        {
            int seconds = Mathf.Max(10, Mathf.RoundToInt(remainingSeconds / 10f) * 10);
            return $"あと約 {seconds} 秒です";
        }
    }

    // ----------------------------------------------------------------
    // Audio
    // ----------------------------------------------------------------

    private void PlayBeep(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        audioSource.PlayOneShot(clip, beepVolume);
    }

    private static AudioClip GenerateBeep(float frequency, float duration, string name)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1f - (float)i / sampleCount; // 線形フェードアウトでクリック音を抑制
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
        }
        var clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
