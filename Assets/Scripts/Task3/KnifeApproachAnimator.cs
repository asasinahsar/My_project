using UnityEngine;

namespace UnityVirtual.Task3
{
    public class KnifeApproachAnimator : MonoBehaviour
    {
        [SerializeField] private Transform knife;
        [SerializeField] private Transform target;
        [SerializeField] private float speed = 0.2f;

        private void Update()
        {
            if (knife == null || target == null) return;
            knife.position = Vector3.MoveTowards(knife.position, target.position, speed * Time.deltaTime);
        }
    }
}
