using UnityEngine;
using System.Collections;

public class TestModeController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private HandVisualizer handVisualizer;

    private Coroutine loopCoroutine;

    // テストメニューのTaskAボタンから呼び出される
    public void StartTestTaskA()
    {
        if (loopCoroutine != null) StopCoroutine(loopCoroutine);
        loopCoroutine = StartCoroutine(TestLoopRoutine());
    }

    // テスト停止時（元のメニューに戻る際など）に呼び出される
    public void StopTest()
    {
        if (loopCoroutine != null)
        {
            StopCoroutine(loopCoroutine);
            loopCoroutine = null;
        }
        if (handVisualizer != null)
        {
            handVisualizer.StopAutoMotion();
        }
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