using UnityEngine;
using System;
using System.Collections;
using UnityVirtual.Common; // RingBuffer用

public enum AutoMotionType
{
    WristExtension,
    WristFlexion,
    ProSupination,
    SupProination
}

public class HandVisualizer : MonoBehaviour
{
    [Header("Mode Settings")]
    public bool isAutoMode = false;  // Task A用
    public float delayMs = 0f;       // Task B用（TaskBControllerから上書きされる）

    [Header("Tracking Roots")]
    [SerializeField] private Transform actualHandWrist;
    [SerializeField] private Transform virtualHandWrist;

    [Header("Joints Configuration")]
    [SerializeField] private Transform[] actualJoints;
    [SerializeField] private Transform[] virtualJoints;

    [Header("Onset Detection (Task B)")]
    [SerializeField] private float velocityThreshold = 0.05f; // 閾値 (m/s)
    
    public Action OnMovementDetected;
    public Action<string> OnMarkerRequested; // LSLマーカー送出要求

    private RingBuffer<HandPose> poseBuffer;
    private bool hasDetectedMotionThisTrial = false;
    private Vector3 previousPosition;
    private Coroutine autoMotionCoroutine;

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
        // 1000フレーム（約11秒分）のメモリを事前確保
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

        // --------------------------------------------------------
        // 1. 速度による運動開始（Onset）の検知 (Task B用)
        // --------------------------------------------------------
        float speed = Vector3.Distance(actualHandWrist.position, previousPosition) / Time.deltaTime;
        if (!isAutoMode && !hasDetectedMotionThisTrial && speed > velocityThreshold)
        {
            hasDetectedMotionThisTrial = true;
            OnMovementDetected?.Invoke();
        }
        previousPosition = actualHandWrist.position;

        // --------------------------------------------------------
        // 2. 現在の実際の姿勢をリングバッファに記録
        // --------------------------------------------------------
        HandPose currentPose = poseBuffer.GetNextWritableItem();
        currentPose.wristPosition = actualHandWrist.position;
        currentPose.wristRotation = actualHandWrist.rotation;
        for (int i = 0; i < actualJoints.Length; i++)
        {
            currentPose.jointPositions[i] = actualJoints[i].position;
            currentPose.jointRotations[i] = actualJoints[i].rotation;
        }
        poseBuffer.Commit(currentTime);

        // --------------------------------------------------------
        // 3. 仮想手の描画更新（AutoMode か 遅延Mode か）
        // --------------------------------------------------------
        if (!isAutoMode)
        {
            ApplyDelayedPose();
        }
    }

    // 遅延を適用した姿勢の反映 (Task B)
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

    // ==========================================================
    // Task A：自動アニメーション制御（スクリプト制御）
    // ==========================================================

    // Task Aの async 条件などで使用する 2cmの空間オフセット
    public void SetAsyncOffset(bool applyOffset)
    {
        if (applyOffset)
            virtualHandWrist.localPosition = new Vector3(0, 0, 0.02f); // 2cm遠位
        else
            virtualHandWrist.localPosition = Vector3.zero;
    }

    // プロシージャルアニメーションの開始
    public void StartAutoMotion(AutoMotionType motionType)
    {
        if (autoMotionCoroutine != null) StopCoroutine(autoMotionCoroutine);
        autoMotionCoroutine = StartCoroutine(AutoMotionRoutine(motionType));
    }

    private IEnumerator AutoMotionRoutine(AutoMotionType motionType)
    {
        isAutoMode = true;
        OnMarkerRequested?.Invoke($"MotionOnset_A_{motionType}");

        float duration = 2.0f; // 2秒かけて往復
        float elapsed = 0f;
        Quaternion baseRot = virtualHandWrist.localRotation;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            
            // 0 -> 1 -> 0 に滑らかに変化させる (PingPong)
            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t); // イーズイン・アウト

            float angle = 0f;
            Vector3 axis = Vector3.right;

            switch (motionType)
            {
                case AutoMotionType.WristExtension:
                    angle = Mathf.Lerp(0, 30f, t);
                    axis = Vector3.right;
                    break;
                case AutoMotionType.WristFlexion:
                    angle = Mathf.Lerp(0, -30f, t);
                    axis = Vector3.right;
                    break;
                case AutoMotionType.ProSupination:
                    angle = Mathf.Lerp(0, 45f, t);
                    axis = Vector3.forward;
                    break;
                case AutoMotionType.SupProination:
                    angle = Mathf.Lerp(0, -45f, t);
                    axis = Vector3.forward;
                    break;
            }

            // 回転を適用（実際のリグの軸設定に合わせて Vector3.right 等は要調整）
            virtualHandWrist.localRotation = baseRot * Quaternion.AngleAxis(angle, axis);

            yield return null;
        }

        virtualHandWrist.localRotation = baseRot;
    }

    // 試行ごとのオンセット検知フラグのリセット
    public void ResetMotionDetection()
    {
        hasDetectedMotionThisTrial = false;
    }
}