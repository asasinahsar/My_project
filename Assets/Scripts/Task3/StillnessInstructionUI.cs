using UnityEngine;

namespace UnityVirtual.Task3
{
    public class StillnessInstructionUI : MonoBehaviour
    {
        [SerializeField] private bool isVisible;
        [SerializeField] private GameObject instructionRoot;
        public bool Visible => isVisible;

        private void Awake()
        {
            ApplyVisibility();
        }

        public void Show()
        {
            isVisible = true;
            ApplyVisibility();
        }

        public void Hide()
        {
            isVisible = false;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (instructionRoot != null)
            {
                instructionRoot.SetActive(isVisible);
            }
        }
    }
}
