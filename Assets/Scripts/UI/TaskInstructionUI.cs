using UnityEngine;
using TMPro;

/// <summary>
/// タスク指示テキストを各ステートに応じて表示する。
///
/// 指示文は v5.3 仕様（3指自動屈曲・ペース化屈曲3回・ピンチ申告）に準拠。
/// TaskA_Main は自動屈曲を予告しない（予測的注意による motor overflow の混入を避けるため）。
///
/// 「読む時間」は指示文の文字数から自動計算する（GetReadSecondsForState）。
/// 各 Controller がこの値を冒頭待機に使い、被験者が読み終えてからタスク本体を開始する。
/// </summary>
public class TaskInstructionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI taskAInstructionText;
    [SerializeField] private TextMeshProUGUI taskBInstructionText;

    [Header("Read Time（文字数ベース自動計算）")]
    [Tooltip("読み時間の基本秒（固定加算分）")]
    [SerializeField] private float readBaseSeconds = 2f;
    [Tooltip("読字速度（文字/秒）。大きいほど読み時間が短くなる")]
    [SerializeField] private float readCharsPerSecond = 6f;
    [Tooltip("読み時間の下限（秒）")]
    [SerializeField] private float readMinSeconds = 3f;
    [Tooltip("読み時間の上限（秒）")]
    [SerializeField] private float readMaxSeconds = 20f;

    private void Start()
    {
        ExperimentManager.Instance.OnStateChanged += UpdateInstructions;
        UpdateInstructions(ExperimentManager.Instance.CurrentState);
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
        {
            ExperimentManager.Instance.OnStateChanged -= UpdateInstructions;
        }
    }

    private void UpdateInstructions(ExperimentState state)
    {
        string instruction = GetInstructionForState(state);

        if (taskAInstructionText != null && IsTaskAInstruction(state))
            taskAInstructionText.text = instruction;

        if (taskBInstructionText != null && IsTaskBInstruction(state))
            taskBInstructionText.text = instruction;
    }

    /// <summary>
    /// ステートに対応する指示文を返す。対象外ステートは空文字。
    /// </summary>
    public static string GetInstructionForState(ExperimentState state)
    {
        switch (state)
        {
            // ---- Task A（受動観察・SoO）----
            case ExperimentState.TaskA_Induction:
                return "【VHI誘導：筆なぞり】\n" +
                       "左手をテーブルの上に置き、力を抜いて動かさないでください。\n" +
                       "実験者があなたの左手を筆でゆっくりなぞります。\n" +
                       "画面の中のバーチャルハンドも同じようになぞられます。\n\n" +
                       "最後まで手を動かさず、バーチャルハンドを\n" +
                       "「自分の手」と感じられるよう、よく見てください。";

            case ExperimentState.TaskA_Baseline:
                return "【安静計測】\n" +
                       "そのまま手の力を抜いて、約30秒間じっとしていてください。\n" +
                       "指も手首も動かさないようにお願いします。";

            // 自動屈曲は予告しない（動かさない指示のみ）
            case ExperimentState.TaskA_Main:
                return "【Task A 計測中】\n" +
                       "左手の力を抜いて、リラックスした状態を保ってください。\n" +
                       "画面の中のバーチャルハンドをそのまま眺めていてください。\n" +
                       "指も手首も、自分からは絶対に動かさないでください。";

            // ---- Task B（能動運動・SoA）----
            case ExperimentState.TaskB_Induction:
                return "【VHI誘導：筆なぞり】\n" +
                       "左手をテーブルの上に置き、力を抜いて動かさないでください。\n" +
                       "実験者があなたの左手を筆でゆっくりなぞります。\n" +
                       "画面の中のバーチャルハンドも同じようになぞられます。\n\n" +
                       "最後まで手を動かさず、バーチャルハンドを\n" +
                       "「自分の手」と感じられるよう、よく見てください。";

            case ExperimentState.TaskB_Baseline:
                return "【安静計測】\n" +
                       "手の力を抜いて、約30秒間じっとしていてください。";

            case ExperimentState.TaskB_Main:
                return "【Task B 計測中】\n" +
                       "5秒ごとの合図に合わせて、左手首を1回ずつ曲げてください（合計3回）。\n" +
                       "合図が出たら曲げ、それ以外は手を止めて待ちます。\n\n" +
                       "3回終わると「回答時間」になります。\n" +
                       "計測中に『自分が動かしている感じがしなかった』ときは、\n" +
                       "回答時間に親指と人差し指をつまんで（ピンチ）「はい」と申告してください。\n" +
                       "感じがあったときは何もしないでください。";

            default:
                return "";
        }
    }

    /// <summary>
    /// ステートの指示文を読むのに必要な時間（秒）。文字数から自動計算しクランプ。
    /// 対象外ステートは 0 を返す。
    /// </summary>
    public float GetReadSecondsForState(ExperimentState state)
    {
        string text = GetInstructionForState(state);
        if (string.IsNullOrEmpty(text)) return 0f;

        // 改行は文字数から除いて純粋な本文長で計算
        int length = text.Replace("\n", "").Length;
        float seconds = readBaseSeconds + length / Mathf.Max(0.1f, readCharsPerSecond);
        return Mathf.Clamp(seconds, readMinSeconds, readMaxSeconds);
    }

    private static bool IsTaskAInstruction(ExperimentState state)
    {
        return state == ExperimentState.TaskA_Induction
            || state == ExperimentState.TaskA_Baseline
            || state == ExperimentState.TaskA_Main;
    }

    private static bool IsTaskBInstruction(ExperimentState state)
    {
        return state == ExperimentState.TaskB_Induction
            || state == ExperimentState.TaskB_Baseline
            || state == ExperimentState.TaskB_Main;
    }
}
