using UnityEngine;

namespace UnityVirtual.Task2
{
    public class Task2Controller : MonoBehaviour
    {
        [SerializeField] private int currentConditionIndex;

        public int CurrentConditionIndex => currentConditionIndex;

        public void SetCondition(int index) => currentConditionIndex = Mathf.Max(0, index);
    }
}
