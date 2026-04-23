using UnityEngine;

namespace UnityVirtual.Task1
{
    public class QuestThresholdEstimator : MonoBehaviour
    {
        [SerializeField] private float currentEstimateSeconds = 0.15f;
        [SerializeField] private float initialStepSize = 0.04f;
        [SerializeField] private float minStepSize = 0.005f;

        private float currentStepSize;
        private bool? lastResponseWasSynchronous = null;
        private int reversalCount = 0;

        public float CurrentEstimateSeconds => currentEstimateSeconds;

        private void Awake()
        {
            currentStepSize = initialStepSize;
        }

        public void RegisterResponse(bool perceivedAsSynchronous)
        {
            // 前回と回答が逆になった（反転した）場合、ステップサイズを小さくする
            if (lastResponseWasSynchronous.HasValue && lastResponseWasSynchronous.Value != perceivedAsSynchronous)
            {
                reversalCount++;
                currentStepSize = Mathf.Max(minStepSize, currentStepSize * 0.5f);
            }

            // 同期していると感じた場合は遅延を伸ばし、遅延を感じた場合は遅延を短くする
            currentEstimateSeconds += perceivedAsSynchronous ? currentStepSize : -currentStepSize;
            
            // 値を 0.0秒 〜 1.0秒の間にクランプ
            currentEstimateSeconds = Mathf.Clamp(currentEstimateSeconds, 0f, 1.0f);
            
            lastResponseWasSynchronous = perceivedAsSynchronous;
        }

        public void ResetEstimator()
        {
            currentEstimateSeconds = 0.15f;
            currentStepSize = initialStepSize;
            lastResponseWasSynchronous = null;
            reversalCount = 0;
        }
    }
}