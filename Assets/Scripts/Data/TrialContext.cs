using System;

namespace UnityVirtual.Data
{
    [Serializable]
    public class TrialContext
    {
        public string participantId;
        public string blockType;
        public string taskType;
        public string conditionId;
        public float delaySeconds;
        public double trialStartTime;
        public double trialEndTime;
    }
}
