using System;
using System.Collections;
using UnityEngine;

namespace UnityVirtual.Common
{
    public class HandExercisePhaseController : MonoBehaviour
    {
        [SerializeField] private float exerciseDurationSeconds = 30f;
        public float ExerciseDurationSeconds => exerciseDurationSeconds;

        public event Action ExerciseStarted;
        public event Action ExerciseCompleted;

        public Coroutine StartExercisePhase() => StartCoroutine(ExerciseRoutine());

        private IEnumerator ExerciseRoutine()
        {
            ExerciseStarted?.Invoke();
            yield return new WaitForSeconds(exerciseDurationSeconds);
            ExerciseCompleted?.Invoke();
        }
    }
}
