using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityVirtual.Task2
{
    // 先ほどのコードでこのenum（列挙型）の定義が漏れていました
    public enum VisualReliabilityCondition
    {
        Baseline,
        GaussianBlur,
        PositionNoise,
        ContrastReduction
    }

    public class VisualReliabilityManipulator : MonoBehaviour
    {
        [SerializeField] private VisualReliabilityCondition condition;
        [SerializeField] private Volume postProcessVolume; // InspectorでVolumeをアサイン

        private DepthOfField depthOfField;
        private ColorAdjustments colorAdjustments;

        public VisualReliabilityCondition Condition => condition;

        private void Start()
        {
            if (postProcessVolume != null && postProcessVolume.profile != null)
            {
                postProcessVolume.profile.TryGet(out depthOfField);
                postProcessVolume.profile.TryGet(out colorAdjustments);
            }
        }

        public void Apply(VisualReliabilityCondition nextCondition)
        {
            condition = nextCondition;
            ResetEffects();

            switch (condition)
            {
                case VisualReliabilityCondition.Baseline:
                    // エフェクトなし
                    break;
                case VisualReliabilityCondition.GaussianBlur:
                    if (depthOfField != null)
                    {
                        depthOfField.active = true;
                        depthOfField.focusDistance.value = 0.1f; // ぼかしを強くする
                    }
                    break;
                case VisualReliabilityCondition.PositionNoise:
                    // ※位置ノイズはシェーダーや手のTransformの更新時に別途揺らぎを加算する処理が必要です
                    break;
                case VisualReliabilityCondition.ContrastReduction:
                    if (colorAdjustments != null)
                    {
                        colorAdjustments.active = true;
                        colorAdjustments.contrast.value = -50f; // コントラストを下げる
                    }
                    break;
            }
        }

        private void ResetEffects()
        {
            if (depthOfField != null) depthOfField.active = false;
            if (colorAdjustments != null) colorAdjustments.active = false;
        }
    }
}