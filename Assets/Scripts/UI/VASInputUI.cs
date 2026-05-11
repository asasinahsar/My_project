using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections; // コルーチン用に必要

public class VASInputUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TaskAController taskAController;
    [SerializeField] private TaskBController taskBController;

    [Header("Panels")]
    [SerializeField] private GameObject vasPanel;
    [SerializeField] private GameObject soaPanel; // Task B 応答用

    [Header("VAS Components")]
    [SerializeField] private Slider vasSlider;
    [SerializeField] private TextMeshProUGUI vasValueText;
    [SerializeField] private Button vasConfirmBtn;
    [SerializeField] private TextMeshProUGUI vasTitleText;

    [Header("SoA Components (Task B)")]
    [SerializeField] private Button soaYesBtn;
    [SerializeField] private Button soaNoBtn;

    private string currentTaskForVAS = "";
    private Coroutine autoSkipCoroutine; // スキップ制御用

    private void Start()
    {
        // 初期状態は非表示
        vasPanel.SetActive(false);
        soaPanel.SetActive(false);

        // リスナー登録
        vasSlider.onValueChanged.AddListener(OnSliderValueChanged);
        vasConfirmBtn.onClick.AddListener(OnVASConfirmed);
        
        soaYesBtn.onClick.AddListener(() => OnSoAAnswered(1));
        soaNoBtn.onClick.AddListener(() => OnSoAAnswered(0));

        // イベント購読
        ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
        taskBController.OnSoAWindowOpened += ShowSoAPanel;
        taskBController.OnSoAWindowClosed += HideAll;
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
    }

    private void HandleStateChanged(ExperimentState state)
    {
        // ステートが切り替わったら過去のスキップ処理はキャンセルする
        if (autoSkipCoroutine != null)
        {
            StopCoroutine(autoSkipCoroutine);
            autoSkipCoroutine = null;
        }

        if (state == ExperimentState.TaskA_VASCheck)
        {
            currentTaskForVAS = "A";
            vasTitleText.text = "自分の手のように感じましたか？\n(0:全く感じない - 10:非常に強く感じる)";
            ShowVASPanel();

            // テストモードなら自動スキップ
            if (ExperimentManager.Instance.IsTestMode)
                autoSkipCoroutine = StartCoroutine(AutoSkipVASCheck());
        }
        else if (state == ExperimentState.TaskB_VASCheck)
        {
            currentTaskForVAS = "B";
            vasTitleText.text = "自分で動かしているように感じましたか？\n(0:全く感じない - 10:非常に強く感じる)";
            ShowVASPanel();

            // テストモードなら自動スキップ
            if (ExperimentManager.Instance.IsTestMode)
                autoSkipCoroutine = StartCoroutine(AutoSkipVASCheck());
        }
        else
        {
            HideAll();
        }
    }

    private IEnumerator AutoSkipVASCheck()
    {
        // 2秒間だけパネルを表示して待機
        yield return new WaitForSeconds(2.0f);
        
        // VAS値が「3」以上なら成功としてベースラインに進む設計なので、適当な成功値(5)をセット
        vasSlider.value = 5;
        
        Debug.Log($"[VASInputUI] Test mode: Auto-skipping VAS check for Task {currentTaskForVAS}.");
        
        // 決定ボタンを押したのと同じ処理を呼び出す
        OnVASConfirmed();
    }

    private void ShowVASPanel()
    {
        vasSlider.value = 5; // 初期値
        vasValueText.text = "5";
        soaPanel.SetActive(false);
        vasPanel.SetActive(true);
    }

    private void ShowSoAPanel()
    {
        vasPanel.SetActive(false);
        soaPanel.SetActive(true);
    }

    private void HideAll()
    {
        vasPanel.SetActive(false);
        soaPanel.SetActive(false);
    }

    private void OnSliderValueChanged(float val)
    {
        vasValueText.text = Mathf.RoundToInt(val).ToString();
    }

    private void OnVASConfirmed()
    {
        int vasValue = Mathf.RoundToInt(vasSlider.value);
        HideAll();

        // 記録用イベントの発火とステート遷移の呼び出し
        if (currentTaskForVAS == "A")
        {
            VASRecorder.Instance.RecordVAS("A", taskAController.CurrentCondition, vasValue);
            ExperimentManager.Instance.EvaluateTaskAVAS(vasValue, taskAController.CurrentCondition);
        }
        else if (currentTaskForVAS == "B")
        {
            VASRecorder.Instance.RecordVAS("B", "none", vasValue);
            ExperimentManager.Instance.EvaluateTaskBVAS(vasValue);
        }
    }

    private void OnSoAAnswered(int response)
    {
        // ボタンが押されたらTaskBControllerへ送信
        taskBController.SubmitSoAResponse(response);
        HideAll();
    }
}