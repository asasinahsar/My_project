using UnityEngine;
using UnityEngine.UI;

// v5.3 Phase B: VAS 全廃に伴い、VAS 関連の SerializeField/メソッドを削除。
// SoA 応答 UI（soaPanel/Yes/No ボタン）は Phase E で「左手握りこぶし検出」に置換予定のため一旦残置。
// クラス名は SerializeField のシーン参照を壊さないため VASInputUI のままとし、Phase E でリネーム検討。
public class VASInputUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TaskAController taskAController;
    [SerializeField] private TaskBController taskBController;

    [Header("Panels")]
    [SerializeField] private GameObject soaPanel; // Task B SoA 応答用

    [Header("SoA Components (Task B)")]
    [SerializeField] private Button soaYesBtn;
    [SerializeField] private Button soaNoBtn;

    private void Start()
    {
        soaPanel.SetActive(false);

        soaYesBtn.onClick.AddListener(() => OnSoAAnswered(1));
        soaNoBtn.onClick.AddListener(() => OnSoAAnswered(0));

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
        // SoA パネルは TaskBController.OnSoAWindowOpened 経路で表示。
        // ステート変化時はとりあえず全 UI を隠して整合を取る。
        HideAll();
    }

    private void ShowSoAPanel()
    {
        soaPanel.SetActive(true);
    }

    private void HideAll()
    {
        soaPanel.SetActive(false);
    }

    private void OnSoAAnswered(int response)
    {
        taskBController.SubmitSoAResponse(response);
        HideAll();
    }
}
