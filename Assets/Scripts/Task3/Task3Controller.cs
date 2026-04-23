using UnityEngine;

namespace UnityVirtual.Task3
{
    public class Task3Controller : MonoBehaviour
    {
        [SerializeField] private bool isTaskRunning;

        public bool Running => isTaskRunning;

        public void StartTask() => isTaskRunning = true;
        public void StopTask() => isTaskRunning = false;
    }
}
