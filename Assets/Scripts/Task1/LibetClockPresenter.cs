using UnityEngine;

namespace UnityVirtual.Task1
{
    public class LibetClockPresenter : MonoBehaviour
    {
        [SerializeField] private float revolutionDurationSeconds = 2.56f;

        public float GetNormalizedPosition(double timeSeconds)
        {
            var t = (float)(timeSeconds % revolutionDurationSeconds);
            return t / revolutionDurationSeconds;
        }
    }
}
