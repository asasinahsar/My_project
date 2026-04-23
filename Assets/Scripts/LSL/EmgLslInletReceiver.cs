using System;
using UnityEngine;
using LSL; // LSL4Unityが必要

namespace UnityVirtual.LSL
{
    public class EmgLslInletReceiver : MonoBehaviour
    {
        [SerializeField] private string streamType = "EMG";
        [SerializeField] private int channelCount = 32;
        [SerializeField] private int sampleRateHz = 1000;
        [SerializeField] private int bufferSize = 4096;

        private float[][] ringBuffer;
        private int writeIndex;

        private StreamInlet inlet;
        private float[] sampleBuffer;

        public int ChannelCount => channelCount;
        public int SampleRateHz => sampleRateHz;

        private void Awake()
        {
            ringBuffer = new float[Mathf.Max(1, bufferSize)][];
            for (var i = 0; i < ringBuffer.Length; i++)
            {
                ringBuffer[i] = new float[channelCount];
            }
            sampleBuffer = new float[channelCount];
        }

        private void Update()
        {
            // LSLストリームの解決と接続
            if (inlet == null)
            {
                var results = LSL.LSL.resolve_stream("type", streamType, 1, 0.0);
                if (results.Length > 0)
                {
                    inlet = new StreamInlet(results[0]);
                }
                return;
            }

            // データの取得（最新のサンプルをすべてバッファに引き込む）
            while (inlet.pull_sample(sampleBuffer, 0.0) != 0.0)
            {
                PushSample(sampleBuffer);
            }
        }

        private void PushSample(float[] sample)
        {
            if (sample == null || sample.Length < channelCount || ringBuffer == null) return;
            Array.Copy(sample, ringBuffer[writeIndex], channelCount);
            writeIndex = (writeIndex + 1) % ringBuffer.Length;
        }

        public float[] GetLatestSample()
        {
            if (ringBuffer == null || ringBuffer.Length == 0) return Array.Empty<float>();
            var latestIndex = (writeIndex - 1 + ringBuffer.Length) % ringBuffer.Length;
            var copy = new float[channelCount];
            Array.Copy(ringBuffer[latestIndex], copy, channelCount);
            return copy;
        }

        private void OnDestroy()
        {
            inlet?.Dispose();
        }
    }
}