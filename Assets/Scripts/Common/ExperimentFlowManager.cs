using System;
using UnityEngine;

namespace UnityVirtual.Common
{
    public enum ExperimentState
    {
        Idle,
        Practice,
        MainBlock,
        Rest,
        Finished
    }

    public class ExperimentFlowManager : MonoBehaviour
    {
        [SerializeField] private ExperimentState currentState = ExperimentState.Idle;
        public ExperimentState CurrentState => currentState;

        public event Action<ExperimentState> StateChanged;

        public void SetState(ExperimentState next)
        {
            if (currentState == next) return;
            currentState = next;
            StateChanged?.Invoke(currentState);
        }
    }
}
