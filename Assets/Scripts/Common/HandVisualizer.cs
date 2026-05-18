using UnityEngine;
using System;
using System.Collections;
using UnityVirtual.Common; // RingBuffer用

public enum AutoMotionType
{
    CompoundMotion
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

    [Header("Compound Motion Settings (Task A)")]
    [SerializeField] private Vector3 flexionAxis = Vector3.right;
    [SerializeField] private float flexionAngle = 45f;
    [SerializeField] private Vector3 ulnarDeviationAxis = Vector3.forward;
    [SerializeField] private float ulnarDeviationAngle = 20f;

    [Header("Test Mode Settings (Wrist Flexion)")]
    [SerializeField] private Vector3 testWristFlexionAxis = Vector3.right;
    [SerializeField] private float testWristFlexionAngle = 45f;

    public Action OnMovementDetected;
    public Action<string> OnMarkerRequested;

    private RingBuffer<HandPose> poseBuffer;
    private bool hasDetectedMotionThisTrial = false;
    private Vector3 previousPosition;
    private Coroutine autoMotionCoroutine;
    private Quaternion autoMotionBaseRotation;
    private bool hasAutoMotionBaseRotation = false;

    private UnityEngine.XR.Hands.XRHandSkeletonDriver _skeletonDriver;

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
        int jointCount = (actualJoints != null) ? actualJoints.Length : 0;
        poseBuffer = new RingBuffer<HandPose>(1000, () => new HandPose(jointCount));
    }

    private void Start()
    {
        if (actualHandWrist != null)
            previousPosition = actualHandWrist.position;

        // virtualHandWrist を起点に親・子の順で XRHandSkeletonDriver を探す
        if (virtualHandWrist != null)
        {
            _skeletonDriver = virtualHandWrist
                .GetComponentInParent<UnityEngine.XR.Hands.XRHandSkeletonDriver>();

            if (_skeletonDriver == null)
                _skeletonDriver = virtualHandWrist
                    .GetComponentInChildren<UnityEngine.XR.Hands.XRHandSkeletonDriver>();
        }

        // 見つからなければシーン全体から Left 側を探す
        if (_skeletonDriver == null)
        {
            var all = FindObjectsByType<UnityEngine.XR.Hands.XRHandSkeletonDriver>(
                FindObjectsInactive.Include);
            foreach (var d in all)
            {
                if (d.gameObject.name.Contains("L_") ||
                    d.gameObject.name.ToLower().Contains("left"))
                {
                    _skeletonDriver = d;
                    Debug.Log("[HandVisualizer] フォールバック取得: " + d.gameObject.name);
                    break;
                }
            }
        }

        if (_skeletonDriver != null)
            Debug.Log("[HandVisualizer] XRHandSkeletonDriver 取得成功: "
                + _skeletonDriver.gameObject.name);
        else
            Debug.LogWarning("[HandVisualizer] XRHandSkeletonDriver が見つかりません。");
    }

    private void Update()
    {
        if (actualHandWrist == null || virtualHandWrist == null) return;

        float currentTime = Time.realtimeSinceStartup;

        float speed = Vector3.Distance(actualHandWrist.position, previousPosition) / Time.deltaTime;
        if (!isAutoMode && !hasDetectedMotionThisTrial && speed > velocityThreshold)
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

        if (!isAutoMode)
        {
            ApplyDelayedPose();
        }
    }

    // ----------------------------------------------------------------
    // Pose Application
    // ----------------------------------------------------------------

    private void ApplyDelayedPose()
{
    HandPose delayedPose = poseBuffer.GetAtDelay(delayMs);
    if (delayedPose == null) return;

    virtualHandWrist.position = delayedPose.wristPosition;

    // ★修正：world rotation → localRotation に変換して適用
    virtualHandWrist.localRotation = virtualHandWrist.parent != null
        ? Quaternion.Inverse(virtualHandWrist.parent.rotation) * delayedPose.wristRotation
        : delayedPose.wristRotation;

    if (virtualJoints != null && delayedPose.jointPositions != null)
    {
        for (int i = 0; i < virtualJoints.Length; i++)
        {
            if (virtualJoints[i] != null && i < delayedPose.jointRotations.Length)
            {
                virtualJoints[i].position = delayedPose.jointPositions[i];
                virtualJoints[i].localRotation = virtualJoints[i].parent != null
                    ? Quaternion.Inverse(virtualJoints[i].parent.rotation)
                      * delayedPose.jointRotations[i]
                    : delayedPose.jointRotations[i];
            }
        }
    }
}

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    public void SetAsyncOffset(bool applyOffset)
    {
        if (virtualHandWrist == null) return;
        virtualHandWrist.localPosition = applyOffset
            ? new Vector3(0, 0, 0.02f)
            : Vector3.zero;
    }

    public void StartAutoMotion(AutoMotionType motionType)
    {
        StopAutoMotion();

        if (virtualHandWrist != null)
        {
            autoMotionBaseRotation = virtualHandWrist.localRotation;
            hasAutoMotionBaseRotation = true;
        }

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

        if (_skeletonDriver != null)
            _skeletonDriver.enabled = false;

        // SDK停止を1フレーム待って反映
        yield return null;

        Quaternion baseRot = virtualHandWrist != null
            ? virtualHandWrist.localRotation
            : Quaternion.identity;

        OnMarkerRequested?.Invoke($"MotionOnset_A_{motionType}");

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t);

            float currentFlexion = Mathf.Lerp(0, flexionAngle, t);
            float currentUlnar   = Mathf.Lerp(0, ulnarDeviationAngle, t);

            Quaternion flexRot  = Quaternion.AngleAxis(currentFlexion, flexionAxis);
            Quaternion ulnarRot = Quaternion.AngleAxis(currentUlnar, ulnarDeviationAxis);

            if (virtualHandWrist != null)
                virtualHandWrist.localRotation = baseRot * flexRot * ulnarRot;

            yield return null;
        }

        if (virtualHandWrist != null)
            virtualHandWrist.localRotation = baseRot;

        if (_skeletonDriver != null)
            _skeletonDriver.enabled = true;

        hasAutoMotionBaseRotation = false;
        ResetAutoMotionState();
    }

    private IEnumerator TestMotionRoutine()
    {
        Debug.Log("[HandVisualizer] TestMotionRoutine 開始");
        isAutoMode = true;

        if (_skeletonDriver != null)
            _skeletonDriver.enabled = false;

        if (virtualHandWrist == null)
        {
            Debug.LogWarning("[HandVisualizer] virtualHandWrist が未設定です。");
            if (_skeletonDriver != null) _skeletonDriver.enabled = true;
            ResetAutoMotionState();
            yield break;
        }

        // SDK停止を1フレーム待って反映
        yield return null;

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

        if (_skeletonDriver != null)
            _skeletonDriver.enabled = true;

        Debug.Log("[HandVisualizer] TestMotionRoutine 終了");
        ResetAutoMotionState();
    }

    // ----------------------------------------------------------------
    // Internal Helpers
    // ----------------------------------------------------------------

    private void ResetAutoMotionState()
    {
        if (hasAutoMotionBaseRotation && virtualHandWrist != null)
            virtualHandWrist.localRotation = autoMotionBaseRotation;

        if (_skeletonDriver != null)
            _skeletonDriver.enabled = true;

        hasAutoMotionBaseRotation = false;
        autoMotionCoroutine = null;
        isAutoMode = false;
    }
}