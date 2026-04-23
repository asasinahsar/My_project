using UnityEngine;

namespace UnityVirtual.LSL
{
    public class LslClockSynchronizer : MonoBehaviour
    {
        [SerializeField] private double lslToUnityOffsetSeconds;

        public void SetOffset(double offsetSeconds) => lslToUnityOffsetSeconds = offsetSeconds;
        public double ToLslTime(double unityTime) => unityTime + lslToUnityOffsetSeconds;
        public double ToUnityTime(double lslTime) => lslTime - lslToUnityOffsetSeconds;
    }
}
