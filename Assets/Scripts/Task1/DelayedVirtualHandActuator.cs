using System;
using System.Collections;
using UnityEngine;

namespace UnityVirtual.Task1
{
    public class DelayedVirtualHandActuator : MonoBehaviour
    {
        public event Action Actuated;

        public Coroutine ActuateAfterDelay(float delaySeconds)
        {
            return StartCoroutine(ActuationRoutine(delaySeconds));
        }

        private IEnumerator ActuationRoutine(float delaySeconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
            Actuated?.Invoke();
        }
    }
}
