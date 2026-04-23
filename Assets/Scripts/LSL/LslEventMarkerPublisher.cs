using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityVirtual.LSL
{
    public class LslEventMarkerPublisher : MonoBehaviour
    {
        private readonly List<string> publishedMarkers = new();

        public IReadOnlyList<string> PublishedMarkers => publishedMarkers;
        public event Action<string, double> MarkerPublished;

        public void Publish(string marker)
        {
            if (string.IsNullOrWhiteSpace(marker)) return;
            publishedMarkers.Add(marker);
            MarkerPublished?.Invoke(marker, Time.timeAsDouble);
        }
    }
}
