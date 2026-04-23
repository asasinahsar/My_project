using UnityEngine;

namespace UnityVirtual.Task1
{
    public class Task1Controller : MonoBehaviour
    {
        [SerializeField] private int totalTrials = 20;
        [SerializeField] private int currentTrial;

        public int TotalTrials => totalTrials;
        public int CurrentTrial => currentTrial;
        public bool IsCompleted => currentTrial >= totalTrials;

        public void StartTask() => currentTrial = 0;

        public void CompleteCurrentTrial()
        {
            if (!IsCompleted) currentTrial++;
        }
    }
}
