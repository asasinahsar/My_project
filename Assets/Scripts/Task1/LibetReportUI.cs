using UnityEngine;

namespace UnityVirtual.Task1
{
    public class LibetReportUI : MonoBehaviour
    {
        public float? LastWReport { get; private set; }
        public float? LastJReport { get; private set; }

        public void SubmitWReport(float normalizedClockPosition) => LastWReport = Mathf.Repeat(normalizedClockPosition, 1f);
        public void SubmitJReport(float normalizedClockPosition) => LastJReport = Mathf.Repeat(normalizedClockPosition, 1f);
    }
}
