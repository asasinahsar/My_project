using UnityEngine;
using System;
using System.Collections;
using UnityVirtual.Common; // RingBuffer用

public enum AutoMotionType
{
    IndexFingerFlexion
}

// v5.3 Phase E2: Task B 計測フェーズの屈曲検出方式
public enum FlexionDetectionMode
{
    VelocityBased,       // 手首位置速度ベース（既存）。シンプル・どの動作にも反応
    AngleVelocityBased,  // 手首回転角速度ベース。屈曲軸成分が閾値超過時のみ反応
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

    // v5.3 Phase E2: 検出方式の切替
    [SerializeField] private FlexionDetectionMode flexionDetectionMode = FlexionDetectionMode.VelocityBased;
    [SerializeField] private float angularVelocityThreshold = 90f; // 角速度しきい値（deg/s）
    [SerializeField] private Vector3 flexionAxisLocal = Vector3.right; // 屈曲軸（手首ローカル座標系）

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
    private Quaternion previousRotation = Quaternion.identity;
    private bool hasPreviousRotation = false;
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

        // joints 対応確認用デバッグログ（確認後に削除予定）
        int compareLen = Mathf.Min(
            actualJoints != null ? actualJoints.Length : 0,
            virtualJoints != null ? virtualJoints.Length : 0
        );
        for (int i = 0; i < compareLen; i++)
        {
            string actualName = actualJoints[i] != null ? actualJoints[i].name : "null";
            string virtualName = virtualJoints[i] != null ? virtualJoints[i].name : "null";
            string match = (actualName == virtualName) ? "OK" : "MISMATCH";
            Debug.Log($"[Joints] [{i:D2}] actual: {actualName} | virtual: {virtualName} {match}");
        }

        // SkinnedMeshRenderer bones 参照確認（確認後に削除予定）
        var virtualSmr = virtualHandWrist != null ? virtualHandWrist.GetComponentInChildren<SkinnedMeshRenderer>() : null;
        if (virtualSmr != null)
        {
            Debug.Log($"[Bones] virtual SMR bones count: {virtualSmr.bones.Length}");
            for (int i = 0; i < virtualSmr.bones.Length; i++)
            {
                var b = virtualSmr.bones[i];
                if (b == null)
                {
                    Debug.Log($"[Bones] [{i:D2}] null");
                    continue;
                }
                bool inVirtual = IsUnder(b, virtualHandWrist);
                bool inActual = IsUnder(b, actualHandWrist);
                string location = inVirtual ? "in_virtual" : (inActual ? "IN_ACTUAL_BUG" : "other");
                Debug.Log($"[Bones] [{i:D2}] {b.name} ({location})");
            }
        }
        else
        {
            Debug.LogWarning("[Bones] virtual SkinnedMeshRenderer が見つかりません");
        }
    }

    private static bool IsUnder(Transform child, Transform ancestor)
    {
        if (child == null || ancestor == null) return false;
        var t = child;
        while (t != null)
        {
            if (t == ancestor) return true;
            t = t.parent;
        }
        return false;
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

        // v5.3 Phase E2: 屈曲検出（速度ベース or 角速度ベース）
        if (EnableOnsetDetection && !isAutoMode && !hasDetectedMotionThisTrial)
        {
            bool detected = false;
            if (flexionDetectionMode == FlexionDetectionMode.VelocityBased)
            {
                detected = CurrentSpeed > velocityThreshold;
            }
            else if (flexionDetectionMode == FlexionDetectionMode.AngleVelocityBased && hasPreviousRotation)
            {
                // 手首回転の屈曲軸成分の角速度（deg/s）を計算
                Quaternion delta = actualHandWrist.rotation * Quaternion.Inverse(previousRotation);
                delta.ToAngleAxis(out float angleDeg, out Vector3 axisWorld);
                if (angleDeg > 180f) angleDeg -= 360f; // 連続性を保つ
                Vector3 flexionAxisWorld = actualHandWrist.TransformDirection(flexionAxisLocal.normalized);
                float projectedAngle = angleDeg * Vector3.Dot(axisWorld.normalized, flexionAxisWorld);
                float angularSpeed = Mathf.Abs(projectedAngle) / Mathf.Max(Time.deltaTime, 0.0001f);
                detected = angularSpeed > angularVelocityThreshold;
            }

            if (detected)
            {
                hasDetectedMotionThisTrial = true;
                OnMovementDetected?.Invoke();
            }
        }
        previousPosition = actualHandWrist.position;
        previousRotation = actualHandWrist.rotation;
        hasPreviousRotation = true;

        HandPose currentPose = poseBuffer.GetNextWritableItem();
        currentPose.wristPosition = actualHandWrist.position;
        currentPose.wristRotation = actualHandWrist.rotation;

        if (actualJoints != null)
        {
            for (int i = 0; i < actualJoints.Length; i++)
            {
                if (actualJoints[i] != null)
                {
                    currentPose.jointPositions[i] = actualJoints[i].localPosition;
                    currentPose.jointRotations[i] = actualJoints[i].localRotation;
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
                    // XRHandSkeletonDriver は localPosition も毎フレーム更新するため両方同期
                    virtualJoints[i].localPosition = delayedPose.jointPositions[i];
                    virtualJoints[i].localRotation = delayedPose.jointRotations[i];
                }
            }
        }
    }

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    // v5.3 Phase F: duration 引数を追加。デフォルト 2.0f で TestModeController 等との互換性を維持。
    public void StartAutoMotion(AutoMotionType motionType, float duration = 2.0f)
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

        autoMotionCoroutine = StartCoroutine(AutoMotionRoutine(motionType, duration));
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

    // v5.3 Phase F: duration を引数で受け取る。
    private IEnumerator AutoMotionRoutine(AutoMotionType motionType, float duration)
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

        // v5.3 修正: TaskB_Main 以外のステートでは固定遅延を 0 にリセットして
        // 練習・誘導・ベースライン・他タスク中に前回の delayMs が残らないようにする。
        // TaskB_Main の各試行では TaskBController.TaskBMainRoutine が delayMs を毎試行設定する。
        if (state != ExperimentState.TaskB_Main)
        {
            delayMs = 0f;
        }
    }

    private static bool ShouldShowHand(ExperimentState state)
    {
        // v5.3: VASCheck 削除に伴い行削除
        return state == ExperimentState.TaskA_Induction
            || state == ExperimentState.TaskA_Baseline
            || state == ExperimentState.TaskA_Main
            || state == ExperimentState.TaskB_Induction
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