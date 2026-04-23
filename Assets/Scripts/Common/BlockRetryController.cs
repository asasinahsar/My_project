using UnityEngine;

namespace UnityVirtual.Common
{
    public class BlockRetryController : MonoBehaviour
    {
        [SerializeField] private int maxRetryCount = 2;
        [SerializeField] private int currentRetryCount;

        public int MaxRetryCount => maxRetryCount;
        public int CurrentRetryCount => currentRetryCount;
        public bool CanRetry => currentRetryCount < maxRetryCount;

        public bool TryConsumeRetry()
        {
            if (!CanRetry) return false;
            currentRetryCount++;
            return true;
        }

        public void ResetRetryCount() => currentRetryCount = 0;
    }
}
