using System;

namespace UnityVirtual.Data
{
    [Serializable]
    public class SessionSummary
    {
        public int blockRetryCount;
        public float exclusionRate;
        public float thresholdEstimate;
        public bool completed;
    }
}
