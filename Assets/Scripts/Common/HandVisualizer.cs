using UnityEngine;
using System;
using System.Collections;
using UnityVirtual.Common; // RingBuffer用

public enum AutoMotionType
{
    IndexFingerFlexion
}

public class HandVisualizer : MonoBehaviour
{
    [Header("Mode Settings")]
    public bool isAutoMode = false;
    public float delayMs = 0f;

    [Header("Tracking Roots")]
    [SerializeField] private Transform actualHandWrist;
    [SerializeField] private Transform virtualHandWrist;

    [Header("Joints Configuration")]
    [SerializeField] private Transform[] actualJoints;
    [SerializeField] private Transform[] virtualJoints;

    [Header("Onset Detection (Task B)")]
    [SerializeField] private float velocityThreshold = 0.05f;

    [Header("Index Finger Joints (Task A)")]
    [SerializeField] private Transform indexMCP;
    [SerializeField] private Transform indexPIP;
    [SerializeField] private Transform indexDIP;

    [Header("Index Finger Flexion Settings (Task A)")]
    [SerializeField] private Vector3 indexFlexionAxis = Vector3.right;
    [SerializeField] private float indexFlexionAngle = 30f;

    [Header("Test Mode Settings (Wrist Flexion)")]
    [SerializeField] private Vector3 testWristFlexionAxis = Vector3.right;
    [SerializeField] private float testWristFlexionAngle = 45f;

    [Header("Visibility Control")]
    [SerializeField] private GameObject virtualHandRoot;
    [SerializeField] private GameObject rightHandRoot;

    public Action OnMovementDetected;
    public Action<string> OnMarkerRequested;

    private RingBuffer<HandPose> poseBuffer;
    private bool hasDetectedMotionThisTrial = false;
    private Vector3 previousPosition;
    private Coroutine autoMotionCoroutine;
    private Quaternion autoMotionBaseRotation;
    private bool hasAutoMotionBaseRotation = false;
    private Quaternion _indexMcpBase;
    private Quaternion _indexPipBase;
    private Quaternion _indexDipBase;

    public float CurrentSpeed { get; private set; }
    public bool EnableOnsetDetection { get; set; } = false;

    public class HandPose
    {
        public Vector3 wristPosition;
        public Quaternion wristRotation;
        public Vector3[] jointPositions;
        public Quaternion[] jointRotations;

        public HandPose(int jointCount)
        {
            jointPositions = new Vector3[jointCount];
            jointRotations = new Quaternion[jointCount];
        }
    }

    // ----------------------------------------------------------------
    // Lifecycle
    // ----------------------------------------------------------------

    private void Awake()
    {
        // Inspector 未設定の場合、子Transformを深さ優先順で自動収集（親→子の正しい順序）
        if ((actualJoints == null || actualJoints.Length == 0) && actualHandWrist != null)
        {
            actualJoints = CollectChildTransforms(actualHandWrist);
            Debug.Log($"[HandVisualizer] actualJoints を自動検出: {actualJoints.Length} 個");
        }
        if ((virtualJoints == null || virtualJoints.Length == 0) && virtualHandWrist != null)
        {
            virtualJoints = CollectChildTransforms(virtualHandWrist);
            Debug.Log($"[HandVisualizer] virtualJoints を自動検出: {virtualJoints.Length} 個");
        }

        int jointCount = (actualJoints != null) ? actualJoints.Length : 0;
        poseBuffer = new RingBuffer<HandPose>(1000, () => new HandPose(jointCount));
    }

    private static Transform[] CollectChildTransforms(Transform root)
    {
        var list = new System.Collections.Generic.List<Transform>();
        CollectRecursive(root, list);
        return list.ToArray();
    }

    private static void CollectRecursive(Transform t, System.Collections.Generic.List<Transform> list)
    {
        foreach (Transform child in t)
        {
            list.Add(child);
            CollectRecursive(child, list);
        }
    }

    private void Start()
    {
        if (actualHandWrist != null)
            previousPosition = actualHandWrist.position;

        // 右手ハンドトラッキングメッシュを常時非表示
        if (rightHandRoot != null)
            rightHandRoot.SetActive(false);

        // 仮想左手はタスク開始まで非表示
        if (virtualHandRoot != null)
            virtualHandRoot.SetActive(false);

        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged += HandleStateChanged;
        else
            Debug.LogWarning("[HandVisualizer] ExperimentManager.Instance が null です。手の表示制御が無効です。");
    }

    private void OnDestroy()
    {
        if (ExperimentManager.Instance != null)
            ExperimentManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (actualHandWrist == null || virtualHandWrist == null) return;

        float currentTime = Time.realtimeSinceStartup;

        CurrentSpeed = Vector3.Distance(actualHandWrist.position, previousPosition) / Time.deltaTime;
        if (EnableOnsetDetection && !isAutoMode && !hasDetectedMotionThisTrial && CurrentSpeed > velocityThreshold)
        {
            hasDetectedMotionThisTrial = true;
            OnMovementDetected?.Invoke();
        }
        previousPosition = actualHandWrist.position;

        HandPose currentPose = poseBuffer.GetNextWritableItem();
        currentPose.wristPosition = actualHandWrist.position;
        currentPose.wristRotation = actualHandWrist.rotation;

        if (actualJoints != null)
        {
            for (int i = 0; i < actualJoints.Length; i++)
            {
                if (actualJoints[i] != null)
                {
                    currentPose.jointPositions[i] = actualJoints[i].position;
                    currentPose.jointRotations[i] = actualJoints[i].rotation;
                }
                else
                {
                    currentPose.jointPositions[i] = Vector3.zero;
                    currentPose.jointRotations[i] = Quaternion.identity;
                }
            }
        }

        poseBuffer.Commit(currentTime);

        ApplyDelayedPose();
    }

    // ----------------------------------------------------------------
    // Pose Application
    // ----------------------------------------------------------------

    private void ApplyDelayedPose()
    {
        HandPose delayedPose = poseBuffer.GetAtDelay(delayMs);
        if (delayedPose == null) return;

        // world 座標系で統一（position・rotation ともに world 直接代入）
        virtualHandWrist.position = delayedPose.wristPosition;
        virtualHandWrist.rotation = delayedPose.wristRotation;

        if (isAutoMode) return; // 自動屈曲中はジョイントをスキップ（AutoMotionRoutineが制御）

        if (virtualJoints != null)
        {
            for (int i = 0; i < virtualJoints.Length; i++)
            {
                if (virtualJoints[i] != null && i < delayedPose.jointRotations.Length)
                {
                    // position は親階層から自動計算されるため設定しない
                    virtualJoints[i].rotation = delayedPose.jointRotations[i];
                }
            }
        }
    }

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    public void StartAutoMotion(AutoMotionType motionType)
    {
        StopAutoMotion();

        if (virtualHandWrist != null)
        {
            autoMotionBaseRotation = virtualHandWrist.localRotation;
            hasAutoMotionBaseRotation = true;
        }

        // 中断時の復帰用に人差し指ボーンのベース姿勢を保存
        if (indexMCP != null) _indexMcpBase = indexMCP.localRotation;
        if (indexPIP != null) _indexPipBase = indexPIP.localRotation;
        if (indexDIP != null) _indexDipBase = indexDIP.localRotation;

        autoMotionCoroutine = StartCoroutine(AutoMotionRoutine(motionType));
    }

    public void StartTestModeMotion()
    {
        StopAutoMotion();
        autoMotionCoroutine = StartCoroutine(TestMotionRoutine());
    }

    public void StopAutoMotion()
    {
        if (autoMotionCoroutine != null)
        {
            StopCoroutine(autoMotionCoroutine);
            autoMotionCoroutine = null;
        }
        ResetAutoMotionState();
    }

    public void ResetMotionDetection()
    {
        hasDetectedMotionThisTrial = false;
    }

    // ----------------------------------------------------------------
    // Coroutines
    // ----------------------------------------------------------------

    private IEnumerator AutoMotionRoutine(AutoMotionType motionType)
    {
        isAutoMode = true;

        if (indexMCP == null || indexPIP == null || indexDIP == null)
        {
            Debug.LogWarning("[HandVisualizer] 人差し指ボーン（indexMCP/PIP/DIP）が未設定です。Inspector を確認してください。");
            ResetAutoMotionState();
            yield break;
        }

        Quaternion mcpBase = indexMCP.localRotation;
        Quaternion pipBase = indexPIP.localRotation;
        Quaternion dipBase = indexDIP.localRotation;

        OnMarkerRequested?.Invoke($"MotionOnset_A_{motionType}");

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t);

            float angle = Mathf.Lerp(0, indexFlexionAngle, t);
            Quaternion flexDelta = Quaternion.AngleAxis(angle, indexFlexionAxis);

            indexMCP.localRotation = mcpBase * flexDelta;
            indexPIP.localRotation = pipBase * flexDelta;
            indexDIP.localRotation = dipBase * flexDelta;

            yield return null;
        }

        // 自然終了時：指ボーンを元の姿勢に復帰
        indexMCP.localRotation = mcpBase;
        indexPIP.localRotation = pipBase;
        indexDIP.localRotation = dipBase;

        hasAutoMotionBaseRotation = false;
        ResetAutoMotionState();
    }

    private IEnumerator TestMotionRoutine()
    {
        Debug.Log("[HandVisualizer] TestMotionRoutine 開始");
        isAutoMode = true;

        if (virtualHandWrist == null)
        {
            Debug.LogWarning("[HandVisualizer] virtualHandWrist が未設定です。");
            ResetAutoMotionState();
            yield break;
        }

        Quaternion baseRot = virtualHandWrist.localRotation;
        Debug.Log("[HandVisualizer] baseRot = " + baseRot.eulerAngles);

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t);

            float currentAngle = Mathf.Lerp(0, testWristFlexionAngle, t);
            Quaternion flexRot = Quaternion.AngleAxis(currentAngle, testWristFlexionAxis);

            virtualHandWrist.localRotation = baseRot * flexRot;

            yield return null;
        }

        virtualHandWrist.localRotation = baseRot;
        hasAutoMotionBaseRotation = false;

        Debug.Log("[HandVisualizer] TestMotionRoutine 終了");
        ResetAutoMotionState();
    }

    // ----------------------------------------------------------------
    // State Change Handling
    // ----------------------------------------------------------------

    private void HandleStateChanged(ExperimentState state)
    {
        if (virtualHandRoot != null)
            virtualHandRoot.SetActive(ShouldShowHand(state));
    }

    private static bool ShouldShowHand(ExperimentState state)
    {
        return state == ExperimentState.TaskA_Induction
            || state == ExperimentState.TaskA_VASCheck
            || state == ExperimentState.TaskA_Baseline
            || state == ExperimentState.TaskA_Main
            || state == ExperimentState.TaskB_Induction
            || state == ExperimentState.TaskB_VASCheck
            || state == ExperimentState.TaskB_Baseline
            || state == ExperimentState.TaskB_Main;
    }

    // ----------------------------------------------------------------
    // Internal Helpers
    // ----------------------------------------------------------------

    private void ResetAutoMotionState()
    {
        if (hasAutoMotionBaseRotation && virtualHandWrist != null)
            virtualHandWrist.localRotation = autoMotionBaseRotation;

        // StopAutoMotion による中断時：人差し指ボーンをベース姿勢に復帰
        if (hasAutoMotionBaseRotation)
        {
            if (indexMCP != null) indexMCP.localRotation = _indexMcpBase;
            if (indexPIP != null) indexPIP.localRotation = _indexPipBase;
            if (indexDIP != null) indexDIP.localRotation = _indexDipBase;
        }

        hasAutoMotionBaseRotation = false;
        autoMotionCoroutine = null;
        isAutoMode = false;
    }
}