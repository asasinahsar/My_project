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

    [Header("Test Mode Settings (MP Flexion)")]
    [SerializeField] private Transform[] testMpJoints = new Transform[4];
    [SerializeField] private Vector3 testFlexionAxis = Vector3.right;
    [SerializeField] private float testFlexionAngle = 90f;

    public Action OnMovementDetected;
    public Action<string> OnMarkerRequested;

    private RingBuffer<HandPose> poseBuffer;
    private bool hasDetectedMotionThisTrial = false;
    private Vector3 previousPosition;
    private Coroutine autoMotionCoroutine;
    private Quaternion autoMotionBaseRotation;
    private bool hasAutoMotionBaseRotation = false;

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


    private void Awake()
    {
        int jointCount = (actualJoints != null) ? actualJoints.Length : 0;
        poseBuffer = new RingBuffer<HandPose>(1000, () => new HandPose(jointCount));
    }

    private UnityEngine.XR.Hands.XRHandSkeletonDriver _skeletonDriver;
    private void Start()
    {
        if (actualHandWrist != null)
            previousPosition = actualHandWrist.position;
         
            
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

    private void ApplyDelayedPose()
    {
        HandPose delayedPose = poseBuffer.GetAtDelay(delayMs);
        if (delayedPose == null) return;

        virtualHandWrist.position = delayedPose.wristPosition;
        virtualHandWrist.rotation = delayedPose.wristRotation;

        if (virtualJoints != null && delayedPose.jointPositions != null)
        {
            for (int i = 0; i < virtualJoints.Length; i++)
            {
                if (virtualJoints[i] != null && i < delayedPose.jointPositions.Length)
                {
                    virtualJoints[i].position = delayedPose.jointPositions[i];
                    virtualJoints[i].rotation = delayedPose.jointRotations[i];
                }
            }
        }
    }

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
        }

        ResetAutoMotionState();
    }

    private IEnumerator AutoMotionRoutine(AutoMotionType motionType)
    {
        isAutoMode = true;
        OnMarkerRequested?.Invoke($"MotionOnset_A_{motionType}");

        float duration = 2.0f;
        float elapsed = 0f;
        Quaternion baseRot = GetAutoMotionBaseRotation();

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t);

            float currentFlexion = Mathf.Lerp(0, flexionAngle, t);
            float currentUlnar = Mathf.Lerp(0, ulnarDeviationAngle, t);

            Quaternion flexRot = Quaternion.AngleAxis(currentFlexion, flexionAxis);
            Quaternion ulnarRot = Quaternion.AngleAxis(currentUlnar, ulnarDeviationAxis);

            if (virtualHandWrist != null)
            {
                virtualHandWrist.localRotation = baseRot * flexRot * ulnarRot;
            }

            yield return null;
        }

        if (virtualHandWrist != null)
        {
            virtualHandWrist.localRotation = baseRot;
        }

        ResetAutoMotionState();
    }

    

    private IEnumerator TestMotionRoutine()
{
    isAutoMode = true;

    // ★追加：SDKドライバーを停止してボーン上書きを防ぐ
    if (_skeletonDriver != null)
        _skeletonDriver.enabled = false;

    float duration = 2.0f;
    float elapsed = 0f;

    if (testMpJoints == null || testMpJoints.Length == 0)
    {
        Debug.LogWarning("[HandVisualizer] testMpJoints が未設定です。");
        if (_skeletonDriver != null) _skeletonDriver.enabled = true;
        ResetAutoMotionState();
        yield break;
    }

    Quaternion[] baseMpRots = new Quaternion[testMpJoints.Length];
    for (int i = 0; i < testMpJoints.Length; i++)
    {
        baseMpRots[i] = (testMpJoints[i] != null)
            ? testMpJoints[i].localRotation
            : Quaternion.identity;
    }

    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;
        float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
        t = Mathf.SmoothStep(0, 1, t);

        float currentAngle = Mathf.Lerp(0, testFlexionAngle, t);
        Quaternion flexRot = Quaternion.AngleAxis(currentAngle, testFlexionAxis);

        for (int i = 0; i < testMpJoints.Length; i++)
        {
            if (testMpJoints[i] != null)
                testMpJoints[i].localRotation = baseMpRots[i] * flexRot;
        }

        yield return null;
    }

    // リセット
    for (int i = 0; i < testMpJoints.Length; i++)
    {
        if (testMpJoints[i] != null)
            testMpJoints[i].localRotation = baseMpRots[i];
    }

    // ★追加：SDKドライバーを再開
    if (_skeletonDriver != null)
        _skeletonDriver.enabled = true;

    ResetAutoMotionState();
}

    private Quaternion GetAutoMotionBaseRotation()
    {
        if (hasAutoMotionBaseRotation)
            return autoMotionBaseRotation;

        return virtualHandWrist != null ? virtualHandWrist.localRotation : Quaternion.identity;
    }

    private void ResetAutoMotionState()
    {
        if (hasAutoMotionBaseRotation && virtualHandWrist != null)
        {
            virtualHandWrist.localRotation = autoMotionBaseRotation;
        }

        hasAutoMotionBaseRotation = false;
        autoMotionCoroutine = null;
        isAutoMode = false;
    }

    public void ResetMotionDetection()
    {
        hasDetectedMotionThisTrial = false;
    }
}