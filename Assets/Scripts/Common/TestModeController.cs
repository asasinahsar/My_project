using UnityEngine;
using System.Collections;

public class TestModeController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;

    [Header("UI Panels")]
    [SerializeField] private GameObject testMenuPanel;
    [SerializeField] private GameObject testRunningPanel;

    private Coroutine loopCoroutine;

    // テストメニューのTaskAボタンから呼び出される
    public void StartTestTaskA()
    {
        Debug.Log("[TestMode] Task A Test Button Clicked!");

        // UIの切り替え（メニューを隠して、実行中パネルを出す）
        if (testMenuPanel != null) testMenuPanel.SetActive(false);
        if (testRunningPanel != null) testRunningPanel.SetActive(true);

        if (loopCoroutine != null) StopCoroutine(loopCoroutine);
        loopCoroutine = StartCoroutine(TestLoopRoutine());
    }

    // テスト停止時（テスト実行中パネルの「停止」ボタンから呼び出される）
    public void StopTest()
    {
        Debug.Log("[TestMode] Test Stopped.");

        if (loopCoroutine != null)
        {
            StopCoroutine(loopCoroutine);
            loopCoroutine = null;
        }
        if (handVisualizer != null)
        {
            handVisualizer.StopAutoMotion();
        }

        // UIの切り替え（実行中パネルを隠して、メニューに戻す）
        if (testRunningPanel != null) testRunningPanel.SetActive(false);
        if (testMenuPanel != null) testMenuPanel.SetActive(true);
    }

    private IEnumerator TestLoopRoutine()
    {
        Debug.Log("[TestMode] Starting Task A Preview Loop (10s interval)");
        
        while (true)
        {
            if (handVisualizer != null)
            {
                // 2秒間のモーションを実行
                handVisualizer.StartTestModeMotion();
            }
            
            // 10秒待機（モーション稼働2秒 ＋ まっすぐ待機8秒）
            yield return new WaitForSeconds(10f);
        }
    }
}