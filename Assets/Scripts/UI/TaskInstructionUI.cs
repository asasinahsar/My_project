using UnityEngine;
using TMPro;

public class TaskInstructionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI taskAInstructionText;
    [SerializeField] private TextMeshProUGUI taskBInstructionText;

    private void Start()
    {
        // ステート変更イベントを購読
        ExperimentManager.Instance.OnStateChanged += UpdateInstructions;
        
        // 初期状態の表示を更新
        UpdateInstructions(ExperimentManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        // オブジェクト破棄時にイベント購読を解除（メモリリーク防止）
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnStateChanged -= UpdateInstructions;
        }
    }

    private void UpdateInstructions(ExperimentState state)
    {
        // --- Task A 向けのテキスト更新 ---
        if (taskAInstructionText != null)
        {
            switch (state)
            {
                case ExperimentState.TaskA_Induction:
                    taskAInstructionText.text = "【VHI誘導中】\n実験者が筆で左手をなぞります。\n手を完全にリラックスさせ、絶対に動かさないでください。";
                    break;
                case ExperimentState.TaskA_Baseline:
                    taskAInstructionText.text = "【ベースライン計測中】\nそのまま手をリラックスさせ、約30秒間安静にしてください。";
                    break;
                case ExperimentState.TaskA_Main:
                    taskAInstructionText.text = "【Task A 計測中】\nバーチャルハンドが自動で動きます。\n手を完全にリラックスさせ、絶対に動かさないでください。";
                    break;
            }
        }

        // --- Task B 向けのテキスト更新 ---
        if (taskBInstructionText != null)
        {
            switch (state)
            {
                case ExperimentState.TaskB_Induction:
                    taskBInstructionText.text = "【VHI誘導・慣らし運動】\n前半：実験者が筆で左手をなぞります。\n後半：ゆっくり手首を動かして、動きを慣らしてください。";
                    break;
                case ExperimentState.TaskB_Baseline:
                    taskBInstructionText.text = "【ベースライン計測中】\n手をリラックスさせ、約30秒間安静にしてください。";
                    break;
                case ExperimentState.TaskB_Main:
                    taskBInstructionText.text = "【Task B 計測中】\n自分のタイミングで、自発的に左手首伸展を行ってください。";
                    break;
            }
        }
    }
}