using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// v5.3 Phase E3: Task B 計測中に被験者向けに「今何をしているか」を提示する HUD。
///
/// - 試行進捗（Task B 試行 n/N）
/// - 屈曲進捗（屈曲 i/5）
/// - 指示テキスト（「ペース合図に合わせて屈曲」「回答時間です」など）
/// - ペース合図円（拍動アニメーション）
/// - 音再生（高音=ペース合図、低音=試行開始/回答開始の合図）
///
/// TaskBController のイベントを購読して自動更新する。音は AudioClip.Create で実行時生成。
/// </summary>
public class ParticipantHUD : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TaskBController taskBController;

    [Header("Display Elements")]
    [Tooltip("「Task B 試行 3/55」の表示")]
    [SerializeField] private TextMeshProUGUI trialProgressText;
    [Tooltip("「屈曲 3/5」の表示")]
    [SerializeField] private TextMeshProUGUI flexionProgressText;
    [Tooltip("「ペース合図に合わせて屈曲してください」などの指示文")]
    [SerializeField] private TextMeshProUGUI instructionText;
    [Tooltip("ペース合図用の円形 Image。拍動アニメーションで一瞬拡大する")]
    [SerializeField] private Image pacingCueCircle;

    [Header("Pacing Cue Animation")]
    [SerializeField] private float pacingCueScaleAmplitude = 1.3f; // 拍動時の拡大率
    [SerializeField] private float pacingCueDuration = 0.5f;        // 拍動時間（秒）

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("ペース合図音の周波数（Hz）。高音推奨")]
    [SerializeField] private float pacingFrequency = 880f;
    [Tooltip("試行開始/回答開始合図音の周波数（Hz）。低音推奨")]
    [SerializeField] private float responseFrequency = 440f;
    [SerializeField] private float beepDurationSec = 0.15f;
    [Range(0f, 1f)]
    [SerializeField] private float beepVolume = 0.5f;

    private AudioClip pacingBeep;
    private AudioClip responseBeep;
    private Coroutine pacingCueAnimCoroutine;
    private int currentTrialNumber = 0;
    private int totalTrialsForDisplay = 55;
    private int flexionDetectedCount = 0;            // 1試行内の屈曲検出累計（HandleFlexionDetected で +1）
    private int flexionTotalPerTrial = 5;            // OnPacingCue の total 引数を保持して屈曲表示に流用

    private void Awake()
    {
        // 実行時にビープ音 AudioClip を生成
        pacingBeep = GenerateBeep(pacingFrequency, beepDurationSec, "PacingBeep");
        responseBeep = GenerateBeep(responseFrequency, beepDurationSec, "ResponseBeep");
    }

    private void Start()
    {
        if (taskBController != null)
        {
            taskBController.OnTrialStartCue += HandleTrialStartCue;
            taskBController.OnPacingCue += HandlePacingCue;
            taskBController.OnFlexionDetected += HandleFlexionDetected;
            taskBController.OnResponseWindowOpened += HandleResponseWindowOpened;
            taskBController.OnSoAWindowClosed += HandleResponseWindowClosed;
        }

        // v5.3 Phase E3 修正: ステート変化に応じて HUD 全体を表示制御するため OnStateChanged を購読。
        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged += HandleStateChanged;

        // 初期表示は空 + HUD 自体を非表示（TaskB_Main 入りで再表示）
        SetTexts("", "", "");
        if (pacingCueCircle != null)
            pacingCueCircle.gameObject.SetActive(false);
        gameObject.SetActive(false);

        // taskBPanel が非アクティブな状態で Start() が遅延実行された場合、
        // イベントを見逃している可能性があるため現在ステートを即時適用する。
        if (ExperimentManager.Instance != null)
            HandleStateChanged(ExperimentManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        if (taskBController != null)
        {
            taskBController.OnTrialStartCue -= HandleTrialStartCue;
            taskBController.OnPacingCue -= HandlePacingCue;
            taskBController.OnFlexionDetected -= HandleFlexionDetected;
            taskBController.OnResponseWindowOpened -= HandleResponseWindowOpened;
            taskBController.OnSoAWindowClosed -= HandleResponseWindowClosed;
        }

        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    // v5.3 Phase E3 修正: TaskB_Main および Practice 中に HUD を表示する。
    // 誘導・BlockRest・他タスク中は非表示にして残留表示を防ぐ。
    private void HandleStateChanged(ExperimentState state)
    {
        bool shouldShow = state == ExperimentState.TaskB_Main || state == ExperimentState.Practice;
        gameObject.SetActive(shouldShow);

        if (shouldShow)
        {
            // 新しい TaskB_Main 入りでカウンタをリセット（ブロック切替時の継続表示を防ぐ）
            currentTrialNumber = 0;
            flexionDetectedCount = 0;
            SetTexts("", "", "");
            if (pacingCueCircle != null)
                pacingCueCircle.gameObject.SetActive(false);

            // B-6: ステートに応じて totalTrialsForDisplay を動的設定
            if (taskBController != null)
            {
                totalTrialsForDisplay = (state == ExperimentState.Practice)
                    ? taskBController.PracticeTrialCount
                    : taskBController.TotalTrialsPerBlock;
            }
        }
    }

    // ----------------------------------------------------------------
    // Event Handlers
    // ----------------------------------------------------------------

    private void HandleTrialStartCue()
    {
        currentTrialNumber++;
        flexionDetectedCount = 0;
        if (trialProgressText != null)
            trialProgressText.text = $"Task B 試行 {currentTrialNumber}/{totalTrialsForDisplay}";
        if (flexionProgressText != null)
            flexionProgressText.text = $"屈曲 0/{flexionTotalPerTrial}";
        if (instructionText != null)
            instructionText.text = "試行開始";

        PlayBeep(responseBeep);
    }

    // v5.3 Phase E3 修正: ペース合図ではカウンタを進めず、指示文と拍動アニメ・音のみ。
    // 屈曲カウンタの更新は HandleFlexionDetected で行う（最後の屈曲も反映できるように）。
    private void HandlePacingCue(int current, int total)
    {
        flexionTotalPerTrial = total;
        if (instructionText != null)
            instructionText.text = "ペース合図 — 左手首を屈曲してください";

        // 円形拍動アニメーション
        if (pacingCueCircle != null)
        {
            if (pacingCueAnimCoroutine != null) StopCoroutine(pacingCueAnimCoroutine);
            pacingCueAnimCoroutine = StartCoroutine(PulseCircle());
        }

        PlayBeep(pacingBeep);
    }

    // v5.3 Phase E3 修正: 屈曲が検出されたタイミングでローカルカウンタを進めて表示更新。
    // 最後の屈曲（5回目）も "屈曲 5/5" として表示されてから回答フェーズに移る。
    private void HandleFlexionDetected()
    {
        flexionDetectedCount++;
        if (flexionProgressText != null)
            flexionProgressText.text = $"屈曲 {flexionDetectedCount}/{flexionTotalPerTrial}";
        if (instructionText != null)
            instructionText.text = "屈曲検出";
    }

    private void HandleResponseWindowOpened()
    {
        if (instructionText != null)
            instructionText.text = "回答時間 — ピンチ（親指+人差し指）で Yes、無反応で No";
        if (pacingCueCircle != null)
            pacingCueCircle.gameObject.SetActive(false);
        if (flexionProgressText != null)
            flexionProgressText.text = "";

        PlayBeep(responseBeep);
    }

    private void HandleResponseWindowClosed()
    {
        if (instructionText != null)
            instructionText.text = "";
    }

    // ----------------------------------------------------------------
    // Animation
    // ----------------------------------------------------------------

    private IEnumerator PulseCircle()
    {
        if (pacingCueCircle == null) yield break;
        pacingCueCircle.gameObject.SetActive(true);

        Vector3 baseScale = Vector3.one;
        Vector3 peakScale = baseScale * pacingCueScaleAmplitude;
        float half = pacingCueDuration * 0.5f;

        // 拡大
        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            pacingCueCircle.transform.localScale = Vector3.Lerp(baseScale, peakScale, t / half);
            yield return null;
        }
        // 縮小
        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            pacingCueCircle.transform.localScale = Vector3.Lerp(peakScale, baseScale, t / half);
            yield return null;
        }
        pacingCueCircle.transform.localScale = baseScale;
        pacingCueAnimCoroutine = null;
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

    // ----------------------------------------------------------------
    // Utility
    // ----------------------------------------------------------------

    private void SetTexts(string trial, string flexion, string instruction)
    {
        if (trialProgressText != null) trialProgressText.text = trial;
        if (flexionProgressText != null) flexionProgressText.text = flexion;
        if (instructionText != null) instructionText.text = instruction;
    }

    /// <summary>
    /// 表示用の総試行数。Task B は ブロックあたり 55 試行のため初期値 55。
    /// 必要なら Inspector か外部から SetTotalTrials() で上書き可能。
    /// </summary>
    public void SetTotalTrials(int total)
    {
        totalTrialsForDisplay = total;
    }

    /// <summary>
    /// 新しいブロック開始時に呼ぶと試行カウンタがリセットされる。
    /// </summary>
    public void ResetTrialCounter()
    {
        currentTrialNumber = 0;
    }
}
