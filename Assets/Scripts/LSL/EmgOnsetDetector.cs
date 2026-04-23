using System;
using UnityEngine;

namespace UnityVirtual.LSL
{
    public class EmgOnsetDetector : MonoBehaviour
    {
        [SerializeField] private EmgLslInletReceiver inletReceiver;
        [SerializeField] private float threshold = 0.2f;

        public event Action<double> OnsetDetected;

        private bool wasAboveThreshold;

        private void Update()
        {
            if (inletReceiver == null) return;
            var sample = inletReceiver.GetLatestSample();
            if (sample.Length == 0) return;

            var envelope = 0f;
            for (var channelIndex = 0; channelIndex < sample.Length; channelIndex++)
            {
                envelope += Mathf.Abs(sample[channelIndex]);
            }
            envelope /= sample.Length;

            var isAbove = envelope >= threshold;
            if (isAbove && !wasAboveThreshold)
            {
                OnsetDetected?.Invoke(Time.timeAsDouble);
            }

            wasAboveThreshold = isAbove;
        }
    }
}
