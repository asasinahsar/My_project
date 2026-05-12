using UnityEngine;
using System;
using System.Collections;
using UnityVirtual.Common; // RingBuffer用

// 【変更の理由】方針Aに基づき、既存の4種類の動作を削除し、新しい複合運動1つに固定しました。
public enum AutoMotionType
{
    CompoundMotion
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

    // --- 新規追加: Compound Motion用の設定変数 ---
    [Header("Compound Motion Settings (Task A)")]
    [SerializeField] private Vector3 flexionAxis = Vector3.right;          // 掌屈の回転軸
    [SerializeField] private float flexionAngle = 45f;                     // 掌屈の最大角度
    [SerializeField] private Vector3 ulnarDeviationAxis = Vector3.forward; // 尺屈の回転軸
    [SerializeField] private float ulnarDeviationAngle = 20f;              // 尺屈の最大角度
    
    public Action OnMovementDetected;
    public Action<string> OnMarkerRequested; // LSLマーカー送出要求

    private RingBuffer<HandPose> poseBuffer;
    private bool hasDetectedMotionThisTrial = false;
    private Vector3 previousPosition;
    private Coroutine autoMotionCoroutine;
    private Quaternion autoMotionBaseRotation;
    private bool hasAutoMotionBaseRotation = false;

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
        StopAutoMotion();

        if (virtualHandWrist != null)
        {
            autoMotionBaseRotation = virtualHandWrist.localRotation;
            hasAutoMotionBaseRotation = true;
        }

        autoMotionCoroutine = StartCoroutine(AutoMotionRoutine(motionType));
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

        float duration = 2.0f; // 2秒かけて往復
        float elapsed = 0f;
        Quaternion baseRot = GetAutoMotionBaseRotation();

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            
            // 0 -> 1 -> 0 に滑らかに変化させる (PingPong)
            float t = Mathf.PingPong(elapsed, duration / 2f) / (duration / 2f);
            t = Mathf.SmoothStep(0, 1, t); // イーズイン・アウト

            // 【削除と変更の理由】既存のswitch文による4種類の動作分岐を削除し、
            // Inspectorから設定された軸と角度に基づく掌屈・尺屈の複合運動のみを計算・合成する処理に置き換えました。
            float currentFlexion = Mathf.Lerp(0, flexionAngle, t);
            float currentUlnar = Mathf.Lerp(0, ulnarDeviationAngle, t);

            Quaternion flexRot = Quaternion.AngleAxis(currentFlexion, flexionAxis);
            Quaternion ulnarRot = Quaternion.AngleAxis(currentUlnar, ulnarDeviationAxis);

            if (virtualHandWrist != null)
            {
                // 合成して手首に適用（ベースの回転 × 掌屈 × 尺屈）
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
    }

    // 試行ごとのオンセット検知フラグのリセット
    public void ResetMotionDetection()
    {
        hasDetectedMotionThisTrial = false;
    }
}