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
    public bool isAutoMode = false;     // Task A用
    public float delayMs = 0f;          // Task B用（TaskBControllerから上書きされる）

    [Header("Tracking Roots")]
    [SerializeField] private Transform actualHandWrist;
    [SerializeField] private Transform virtualHandWrist;

    [Header("Joints Configuration")]
    [SerializeField] private Transform[] actualJoints;
    [SerializeField] private Transform[] virtualJoints;

    [Header("Onset Detection (Task B)")]
    [SerializeField] private float velocityThreshold = 0.05f; // 閾値 (m/s)

    [Header("Compound Motion Settings (Task A)")]
    [SerializeField] private Vector3 flexionAxis = Vector3.right;          // 掌屈の回転軸
    [SerializeField] private float flexionAngle = 45f;                     // 掌屈の最大角度
    [SerializeField] private Vector3 ulnarDeviationAxis = Vector3.forward; // 尺屈の回転軸
    [SerializeField] private float ulnarDeviationAngle = 20f;              // 尺屈の最大角度

    [Header("Test Mode Settings (MP Flexion)")]
    [SerializeField] private Transform[] testMpJoints = new Transform[4]; // Index, Middle, Ring, Pinky のMP関節をアサイン
    [SerializeField] private Vector3 testFlexionAxis = Vector3.right;      // お辞儀の回転軸
    [SerializeField] private float testFlexionAngle = 90f;                 // お辞儀の最大角度

    public Action OnMovementDetected;
    public Action<string> OnMarkerRequested; // LSLマーカー送出要求

    private RingBuffer<HandPose> poseBuffer;
    private bool hasDetectedMotionThisTrial = false;
    private Vector3 previousPosition;
    private Coroutine autoMotionCoroutine;
    private Quaternion autoMotionBaseRotation;
    private bool hasAutoMotionBaseRotation = false;

    // ★追加: テストモード専用フィールド
    private bool _isTestMotionActive = false;
    private Quaternion[] _testMpTargetRots;

    // バッファに保存する姿勢データのクラス
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
        poseBuffer = new RingBuffer<HandPose>(1000, () => new HandPose(actualJoints.Length));
    }

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
        for (int i = 0; i < actualJoints.Length; i++)
        {
            currentPose.jointPositions[i] = actualJoints[i].position;
            currentPose.jointRotations[i] = actualJoints[i].rotation;
        }
        poseBuffer.Commit(currentTime);

        if (!isAutoMode)
        {
            ApplyDelayedPose();
        }
    }

    // ★追加: LateUpdate() — XR Hands SDK の上書き後にテストモードの回転を強制適用
    private void LateUpdate()
    {
        if (_isTestMotionActive && _testMpTargetRots != null)
        {
            for (int i = 0; i < testMpJoints.Length; i++)
            {
                if (testMpJoints[i] != null)
                    testMpJoints[i].localRotation = _testMpTargetRots[i];
            }
        }
    }

    private void ApplyDelayedPose()
    {
        HandPose delayedPose = poseBuffer.GetAtDelay(delayMs);

        virtualHandWrist.position = delayedPose.wristPosition;
        virtualHandWrist.rotation = delayedPose.wristRotation;

        for (int i = 0; i < virtualJoints.Length; i++)
        {
            virtualJoints[i].position = delayedPose.jointPositions[i];
            virtualJoints[i].rotation = delayedPose.jointRotations[i];
        }
    }

    public void SetAsyncOffset(bool applyOffset)
    {
        if (applyOffset)
            virtualHandWrist.localPosition = new Vector3(0, 0, 0.02f);
        else
            virtualHandWrist.localPosition = Vector3.zero;
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

    // ★変更: localRotation を直接書かず _testMpTargetRots に格納し、
    //         LateUpdate() 経由で SDK の上書き後に適用する
    private IEnumerator TestMotionRoutine()
    {
        isAutoMode = true;
        _isTestMotionActive = true;

        // XR Hands SDK が LateUpdate() でボーンを確定させた後の値を取るため1フレーム待つ
        yield return new WaitForEndOfFrame();

        Quaternion[] baseMpRots = new Quaternion[testMpJoints.Length];
        _testMpTargetRots = new Quaternion[testMpJoints.Length];

        for (int i = 0; i < testMpJoints.Length; i++)
        {
            baseMpRots[i] = testMpJoints[i] != null
                ? testMpJoints[i].localRotation
                : Quaternion.identity;
            _testMpTargetRots[i] = baseMpRots[i];
        }

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t);

            float currentAngle = Mathf.Lerp(0, testFlexionAngle, t);
            Quaternion flexRot = Quaternion.AngleAxis(currentAngle, testFlexionAxis);

            for (int i = 0; i < testMpJoints.Length; i++)
            {
                // localRotation を直接書かず配列に格納するだけ
                // → LateUpdate() が SDK の上書き後に実際の適用を行う
                _testMpTargetRots[i] = baseMpRots[i] * flexRot;
            }

            yield return null;
        }

        // モーション完了後にフラグを落とす（LateUpdate の適用も停止する）
        _isTestMotionActive = false;
        ResetAutoMotionState();
    }

    private Quaternion GetAutoMotionBaseRotation()
    {
        if (hasAutoMotionBaseRotation)
        {
            return autoMotionBaseRotation;
        }

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
        _isTestMotionActive = false; // ★追加: StopAutoMotion() 経由でも確実にリセット
    }

    public void ResetMotionDetection()
    {
        hasDetectedMotionThisTrial = false;
    }
}