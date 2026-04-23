using UnityEngine;

namespace UnityVirtual.Task2
{
    public class VASInputUI : MonoBehaviour
    {
        [SerializeField] private float latestScore = 5f;

        public float LatestScore => latestScore;

        public void Submit(float score)
        {
            latestScore = Mathf.Clamp(score, 0f, 10f);
        }
    }
}
