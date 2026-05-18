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

    public void StartTestTaskA()
    {
        Debug.Log("[TestMode] Task A Test Button Clicked!");

        if (testMenuPanel != null) testMenuPanel.SetActive(false);
        if (testRunningPanel != null) testRunningPanel.SetActive(true);

        if (loopCoroutine != null) StopCoroutine(loopCoroutine);
        loopCoroutine = StartCoroutine(TestLoopRoutine());
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
        }

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