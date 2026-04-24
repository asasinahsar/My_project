using UnityEngine;
using System.IO;
using System;

public class VASRecorder : MonoBehaviour
{
    public static VASRecorder Instance { get; private set; }

    private string logFilePath;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        InitializeLogFile();
    }

    private void InitializeLogFile()
    {
        string directory = Path.Combine(Application.persistentDataPath, "SessionData", DateTime.Now.ToString("yyyyMMdd"));
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        logFilePath = Path.Combine(directory, "VAS_log.csv");
        
        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "timestamp,task,condition,vas_value,block_excluded\n");
        }
    }

    public void RecordVAS(string task, string condition, int vasValue)
    {
        bool isExcluded = vasValue < 3;
        float currentTime = Time.realtimeSinceStartup;
        
        string logLine = $"{currentTime:F3},{task},{condition},{vasValue},{(isExcluded ? 1 : 0)}\n";
        File.AppendAllText(logFilePath, logLine);
        
        Debug.Log($"[VAS Recorder] Logged: Task {task}, Condition {condition}, Value {vasValue}");
    }
}