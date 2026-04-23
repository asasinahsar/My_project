using System;

namespace UnityVirtual.Data
{
    [Serializable]
    public class TrialResult
    {
        public bool? soaAnswer;
        public bool? sooAnswer;
        public float? libetW;
        public float? libetJ;
        public float vas;
        public bool isExcluded;
        public double onsetTime;
        public string lslMarkerId;
    }
}
