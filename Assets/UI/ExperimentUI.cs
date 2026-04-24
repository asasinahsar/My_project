using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ExperimentUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TaskBController taskBController;
    [SerializeField] private TaskAController taskAController;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI stateText;
    [SerializeField] private TextMeshProUGUI trialInfoText; // "Trial: 15/55" など
    [SerializeField] private TextMeshProUGUI taskBInfoText; // "Delta: 150ms | Quest: 280ms" など
    [SerializeField] private TextMeshProUGUI logText;
    
    [Header("Control Buttons")]
    [SerializeField] private Button nextPhaseBtn;
    [SerializeField] private Button excludeBlockBtn;
    [SerializeField] private Button emergencyStopBtn;

    private List<string> logMessages = new List<string>();

    private void Start()
    {
        // ボタンのリスナー登録
        nextPhaseBtn.onClick.AddListener(OnNextPhaseClicked);
        excludeBlockBtn.onClick.AddListener(OnExcludeBlockClicked);
        emergencyStopBtn.onClick.AddListener(() => ExperimentManager.Instance.EmergencyStop());

        // ステート監視
        ExperimentManager.Instance.OnStateChanged += UpdateStateUI;
        
        // UnityのDebug.LogをフックしてLSLマーカーのログを抽出
        Application.logMessageReceived += HandleLogMessage;

        UpdateStateUI(ExperimentManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= UpdateStateUI;
            
        Application.logMessageReceived -= HandleLogMessage;
    }

    private void Update()
    {
        // --------------------------------------------------------
        // Task B: 実験者によるキーボードレスポンス (Y/N)
        // --------------------------------------------------------
        if (ExperimentManager.Instance.CurrentState == ExperimentState.TaskB_Main)
        {
            if (Input.GetKeyDown(KeyCode.Y))
            {
                taskBController.SubmitSoAResponse(1); // SoA有
                AddLog("[Experimenter Input] SoA: Yes (1)");
            }
            else if (Input.GetKeyDown(KeyCode.N))
            {
                taskBController.SubmitSoAResponse(0); // SoA無
                AddLog("[Experimenter Input] SoA: No (0)");
            }
        }
    }

    private void UpdateStateUI(ExperimentState state)
    {
        if (stateText != null) stateText.text = $"Current State: {state}";
    }

    // ログ受取（LSLマーカーやVASの履歴などを表示）
    private void HandleLogMessage(string logString, string stackTrace, LogType type)
    {
        if (logString.Contains("[LSL Marker]") || logString.Contains("[Task") || logString.Contains("[VAS]"))
        {
            AddLog(logString);
        }
    }

    private void AddLog(string msg)
    {
        logMessages.Add(msg);
        if (logMessages.Count > 20) logMessages.RemoveAt(0); // 直近20件を保持

        if (logText != null)
            logText.text = string.Join("\n", logMessages);
    }

    // --- ボタンアクション ---
    private void OnNextPhaseClicked()
    {
        // ※ 本来はExperimentManagerの現在のステートを評価して適切なNextStateへ遷移させるロジックを記述します
        AddLog("Next Phase forced by Experimenter.");
    }

    private void OnExcludeBlockClicked()
    {
        ExperimentState state = ExperimentManager.Instance.CurrentState;
        if (state.ToString().Contains("TaskA"))
        {
            ExperimentManager.Instance.ChangeState(ExperimentState.BlockRest);
        }
        else if (state.ToString().Contains("TaskB"))
        {
            ExperimentManager.Instance.ChangeState(ExperimentState.Finished);
        }
    }
}