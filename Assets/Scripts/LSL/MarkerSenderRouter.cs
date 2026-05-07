using System;
using UnityEngine;

namespace LSL
{
    public class MarkerSenderRouter : MonoBehaviour, IMarkerSender
    {
        [Header("Mode Settings")]
        [SerializeField] private bool isTestMode = false;

        [Header("Marker Senders")]
        [SerializeField] private MonoBehaviour productionMarkerSender;
        [SerializeField] private MonoBehaviour testMarkerSender;

        private IMarkerSender activeSender;

        private void Awake()
        {
            SelectActiveSender();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                SelectActiveSender();
            }
        }

        private void SelectActiveSender()
        {
            IMarkerSender production = productionMarkerSender as IMarkerSender;
            IMarkerSender test = testMarkerSender as IMarkerSender;

            if (isTestMode)
            {
                activeSender = test ?? production;
                ToggleSenderComponent(productionMarkerSender, activeSender == production);
                ToggleSenderComponent(testMarkerSender, activeSender == test);
            }
            else
            {
                activeSender = production ?? test;
                ToggleSenderComponent(productionMarkerSender, activeSender == production);
                ToggleSenderComponent(testMarkerSender, activeSender == test);
            }
        }

        private static void ToggleSenderComponent(MonoBehaviour sender, bool shouldEnable)
        {
            if (sender != null)
            {
                sender.enabled = shouldEnable;
            }
        }

        public void SendMarker(string marker)
        {
            if (activeSender == null)
            {
                Debug.LogWarning("[MarkerSenderRouter] Active marker sender is not assigned. Marker skipped.");
                return;
            }

            try
            {
                activeSender.SendMarker(marker);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MarkerSenderRouter] Marker send failed: {ex.Message}");
            }
        }
    }
}
