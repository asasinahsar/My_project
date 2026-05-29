using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// v5.3 Phase E3: Task B 回答フェーズ用 UI。
///
/// 旧 VASInputUI.cs からリネーム。SoA Yes/No ボタンは廃止し、ハンドサイン（ピンチ：親指+人差し指タッチ）応答 +
/// 残り時間カウントダウン表示に変更。HandSignDetector の検出時は「Yes 受付！」フィードバック表示。
///
/// 表示制御は TaskBController.OnSoAWindowOpened / OnSoAWindowClosed で行う。
/// </summary>
public class SoAResponseUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TaskBController taskBController;
    [SerializeField] private HandSignDetector handSignDetector;

    [Header("Panels")]
    [SerializeField] private GameObject soaPanel; // Task B SoA 応答パネル

    [Header("Response UI Text")]
    [Tooltip("「握りこぶしで Yes、無反応で No」などの指示文")]
    [SerializeField] private TextMeshProUGUI promptText;
    [Tooltip("「残り: 2.3 秒」のカウントダウン表示")]
    [SerializeField] private TextMeshProUGUI countdownText;
    [Tooltip("「Yes 受付！」のフィードバック表示（初期非表示）")]
    [SerializeField] private TextMeshProUGUI fistFeedbackText;

    [Header("Timing")]
    [Tooltip("回答フェーズの制限時間（秒）。Start() 時に TaskBController.ResponseWindowSeconds と自動同期される。")]
    [SerializeField] private float responseWindowSeconds = 5.0f;

    private Coroutine countdownCoroutine;
    private bool fistDetectedThisWindow = false;

    private void Start()
    {
        if (soaPanel != null) soaPanel.SetActive(false);
        SetFistFeedbackActive(false);

        // B-8: TaskBController から responseWindowSeconds を取得して同期（Inspector の二重管理を解消）
        if (taskBController != null)
        {
            responseWindowSeconds = taskBController.ResponseWindowSeconds;
            Debug.Log($"[SoAResponseUI] responseWindowSeconds を TaskBController から同期: {responseWindowSeconds}秒");
        }

        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged += HandleStateChanged;

        if (taskBController != null)
        {
            taskBController.OnSoAWindowOpened += ShowSoAPanel;
            taskBController.OnSoAWindowClosed += HideAll;
        }

        if (handSignDetector != null)
        {
            handSignDetector.OnHandSignDetected += OnHandSignDetectedHandler;
        }

        // v5.3 Phase E3 修正: TaskB_Main 入り以外では SoAResponseUI 全体を非表示にして
        // 練習・誘導・BlockRest・他タスク中の残留表示を防ぐ。
        gameObject.SetActive(false);

        // Start() が遅延実行された場合でも現在ステートを即時反映
        if (ExperimentManager.Instance != null)
            HandleStateChanged(ExperimentManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;

        if (taskBController != null)
        {
            taskBController.OnSoAWindowOpened -= ShowSoAPanel;
            taskBController.OnSoAWindowClosed -= HideAll;
        }

        if (handSignDetector != null)
        {
            handSignDetector.OnHandSignDetected -= OnHandSignDetectedHandler;
        }
    }

    private void HandleStateChanged(ExperimentState state)
    {
        // v5.3 Phase E3 修正: TaskB_Main および Practice 中に SoAResponseUI を有効化する。
        // 誘導・BlockRest・他タスク中は GameObject ごと非表示にして残留表示を防ぐ。
        // soaPanel 子要素自体は OnSoAWindowOpened で表示開始するため、ここでは常に非表示にしておく。
        bool shouldShow = state == ExperimentState.TaskB_Main || state == ExperimentState.Practice;
        gameObject.SetActive(shouldShow);
        HideAll();
    }

    private void ShowSoAPanel()
    {
        if (soaPanel != null) soaPanel.SetActive(true);
        SetFistFeedbackActive(false);
        fistDetectedThisWindow = false;

        if (promptText != null)
            promptText.text = "回答時間\n親指と人差し指を合わせて（ピンチで）「Yes」、無反応で「No」";

        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(CountdownRoutine());
    }

    private void HideAll()
    {
        if (soaPanel != null) soaPanel.SetActive(false);
        SetFistFeedbackActive(false);

        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
    }

    private void OnHandSignDetectedHandler()
    {
        // 回答ウィンドウが開いているときのみフィードバック表示。
        // HandSignDetector.EnableDetection が false なら OnHandSignDetected は発火しないため、
        // 通常はこの分岐で十分。
        if (soaPanel != null && !soaPanel.activeSelf) return;
        if (fistDetectedThisWindow) return;

        fistDetectedThisWindow = true;
        SetFistFeedbackActive(true);
        if (fistFeedbackText != null)
            fistFeedbackText.text = "Yes 受付！";
    }

    private IEnumerator CountdownRoutine()
    {
        float remaining = responseWindowSeconds;
        while (remaining > 0f)
        {
            if (countdownText != null)
                countdownText.text = $"残り: {remaining:F1} 秒";
            yield return null;
            remaining -= Time.deltaTime;
        }
        if (countdownText != null)
            countdownText.text = "残り: 0.0 秒";
        countdownCoroutine = null;
    }

    private void SetFistFeedbackActive(bool active)
    {
        if (fistFeedbackText != null)
            fistFeedbackText.gameObject.SetActive(active);
    }
}
