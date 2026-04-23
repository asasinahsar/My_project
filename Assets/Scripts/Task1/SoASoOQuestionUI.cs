using UnityEngine;

namespace UnityVirtual.Task1
{
    public enum AgencyOwnershipQuestion
    {
        SoA,
        SoO
    }

    public class SoASoOQuestionUI : MonoBehaviour
    {
        public bool? LastAnswer { get; private set; }
        public AgencyOwnershipQuestion LastQuestion { get; private set; }

        public void SubmitAnswer(AgencyOwnershipQuestion question, bool yes)
        {
            LastQuestion = question;
            LastAnswer = yes;
        }
    }
}
