using UnityEngine;
using System.Collections;

public class TestModeController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;

    [Header("UI Panels")]
    [SerializeField] private GameObject testMenuPanel;
    [SerializeField] private GameObject testRunningPanel;

    [Header("Hand Visibility")]
    [SerializeField] private GameObject virtualHandRoot;

    private Coroutine loopCoroutine;

    public void StartTestTaskA()
    {
        Debug.Log("[TestMode] Task A Test Button Clicked!");

        if (testMenuPanel != null) testMenuPanel.SetActive(false);
        if (testRunningPanel != null) testRunningPanel.SetActive(true);
        if (virtualHandRoot != null) virtualHandRoot.SetActive(true);

        if (loopCoroutine != null) StopCoroutine(loopCoroutine);
        loopCoroutine = StartCoroutine(TestLoopRoutine());
    }

    public void StartTestTaskB()
    {
        Debug.Log("[TestMode] Task B Test Button Clicked!");

        if (testMenuPanel != null) testMenuPanel.SetActive(false);
        if (testRunningPanel != null) testRunningPanel.SetActive(true);
        if (virtualHandRoot != null) virtualHandRoot.SetActive(true);

        if (handVisualizer != null)
            handVisualizer.delayMs = 500f;
    }

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
            handVisualizer.delayMs = 0f;
        }

        if (virtualHandRoot != null) virtualHandRoot.SetActive(false);
        if (testRunningPanel != null) testRunningPanel.SetActive(false);
        if (testMenuPanel != null) testMenuPanel.SetActive(true);
    }

    private IEnumerator TestLoopRoutine()
    {
        Debug.Log("[TestMode] Starting Task A Preview Loop (初回10秒待機後に開始)");

        while (true)
        {
            // ★修正：先に10秒待ってからモーション実行
            yield return new WaitForSeconds(10f);

            if (handVisualizer != null)
            {
                handVisualizer.StartTestModeMotion();
            }
        }
    }
}