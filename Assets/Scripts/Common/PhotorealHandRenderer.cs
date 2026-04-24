using UnityEngine;
using System.Collections.Generic;

namespace UnityVirtual.Common
{
    // 被験者の属性に応じたモデルのタイプを定義
    public enum HandModelType
    {
        Male_Average,
        Female_Average,
        Male_Large,
        Female_Small
    }

    public class PhotorealHandRenderer : MonoBehaviour
    {
        [SerializeField] private QuestHandTrackingProvider trackingProvider;
        [SerializeField] private Transform leftHandRoot;
        [SerializeField] private Transform rightHandRoot;

        // インスペクターで設定するためのペアクラス
        [System.Serializable]
        public class HandModelPair
        {
            public HandModelType modelType;
            public GameObject leftHandObject;
            public GameObject rightHandObject;
        }

        [SerializeField] private List<HandModelPair> handModels = new List<HandModelPair>();
        [SerializeField] private HandModelType initialModel = HandModelType.Male_Average;

        private void Start()
        {
            // 初期状態のモデルを適用
            ApplyHandModel(initialModel);
        }

        // 外部（UIや実験管理スクリプト）からモデルを切り替えるためのメソッド
        public void ApplyHandModel(HandModelType targetType)
        {
            foreach (var pair in handModels)
            {
                bool isTarget = (pair.modelType == targetType);
                if (pair.leftHandObject != null) pair.leftHandObject.SetActive(isTarget);
                if (pair.rightHandObject != null) pair.rightHandObject.SetActive(isTarget);
            }
        }

        private void LateUpdate()
        {
            if (trackingProvider == null) return;

            // 選択されているモデルに関わらず、ルートのTransformを同期
            if (leftHandRoot != null)
            {
                leftHandRoot.SetPositionAndRotation(
                    trackingProvider.LeftHandPose.position,
                    trackingProvider.LeftHandPose.rotation);
            }

            if (rightHandRoot != null)
            {
                rightHandRoot.SetPositionAndRotation(
                    trackingProvider.RightHandPose.position,
                    trackingProvider.RightHandPose.rotation);
            }
        }
    }
}