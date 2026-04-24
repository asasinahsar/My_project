# Meta Quest 3 + sEMG VR実験システム

本プロジェクトは、Meta Quest 3を用いたハンドトラッキングVR環境において、外部のsEMG（表面筋電図）デバイスとLSL（Lab Streaming Layer）通信を用いて同期し、3つのタスク（インテンショナル・バインディング、ベイズ統合に基づく視覚・固有受容感覚の重み付け、ペリパーソナルスペース）を実行・計測するシステムです。

## 依存パッケージ・環境
- **Unity バージョン**: Unity 6 (6000.4.3f1)
- **テンプレート**: VR (Basic) または 3D (URP)
- **外部ライブラリ**: 
  - `LSL4Unity` (および `liblsl` DLLファイル): sEMGデータの受信に必須。

## 推奨シーン構成

本システムは、目的別に以下のシーンを作成・運用することを推奨します。

1. **`Bootstrap`**: 実験全体の初期化、被験者IDの入力、グローバルなデータ管理オブジェクト（DontDestroyOnLoad）を生成するエントリーシーン。
2. **`CalibrationAndPractice`**: 実際のタスクに入る前のLSL通信のキャリブレーション、およびQuestのハンドトラッキングの慣熟を行うシーン。
3. **`MainExperiment`**: 本実験を行うメインシーン。シンプルなVR空間内で、以下のオブジェクト階層に従ってタスクを進行する。
4. **`DeveloperSandbox`**: 開発・テスト用シーン。`MainExperiment` をベースに、sEMGのダミー入力（モック）や、各タスクの強制トリガーUIを配置し、単独での動作確認を行う。

## MainExperiment シーンのオブジェクト階層

シーン内は以下のルートオブジェクト（空のGameObject）を作成し、機能を分割して管理します。

- `[Systems]`
  - `ExperimentFlowManager`: 全体の状態遷移（練習、本番、休憩など）を管理。
  - `BlockRetryController`: ブロックごとの再試行回数（最大2回まで）を追跡。
  - `HandExercisePhaseController`: 30秒間の手指運動フェーズをコルーチンで制御。
- `[XR]`
  - `XR Origin (XR Rig)`: Meta Quest 3のカメラと基本トラッキング。Rendering設定でPost Processingを有効にすること。
  - `LeftVirtualHandRoot` / `RightVirtualHandRoot`: `PhotorealHandRenderer` をアタッチ。インスペクターから `HandModelType` (Male_Average, Female_Average等) に応じた3Dモデルの切り替えが可能。
- `[LSL]`
  - `EmgLslInletReceiver`: LSL4Unityを利用し、>=1000Hz, 32chのsEMGストリームをリングバッファで受信。
  - `EmgOnsetDetector`: 筋活動のオンセットを検出し、イベントを発火。
  - `LslClockSynchronizer`: LSLとUnityのタイムスタンプのズレを補正。
  - `LslEventMarkerPublisher`: 実験内の特定イベントをLSLのアウトレットとして送信。
- `[Task1_SoA_SoO_IB]`
  - `Task1Controller`: Task 1の進行を管理。
  - `DelayedVirtualHandActuator`: OnsetDetectorの検知後、指定されたΔtの遅延を伴って仮想手を伸展させる。
  - `QuestThresholdEstimator`: 回答の反転回数に応じた適応的階段法（QUEST法ベース）により遅延閾値を推定する。
  - `LibetClockPresenter`: インテンショナル・バインディング計測用の時計UI。
- `[Task2_BayesianWeight]`
  - `Task2Controller`: Task 2の進行を管理。
  - `VisualReliabilityManipulator`: URPの `Volume` コンポーネント（DepthOfField, ColorAdjustments等）を操作し、視覚精度（ガウシアンぼかし、コントラスト低下等）を動的に変更する。
  - `TrialExclusionMarker`: VASスコアが3未満の場合に除外フラグを立てる。
- `[Task3_PPS]`
  - `Task3Controller`: Task 3の進行を管理。
  - `KnifeApproachAnimator`: 特定の開始位置からターゲットに向けて等速でナイフを接近させる。
  - `PpsMarkerEmitter`: 接近時のPPS境界判定用マーカーを処理。
- `[UI]` (Canvas Render Mode: World Space)
  - `SoASoOQuestionUI`: Task 1用のYes/No回答パネル。
  - `LibetReportUI`: Task 1用の時計針位置の報告パネル。
  - `VASInputUI`: Task 2用の0〜10のVAS入力パネル。
  - `StillnessInstructionUI`: Task 3のナイフ接近時に静止を指示するテキストパネル。
- `[Data]`
  - `TrialResultStore`: 各試行のコンテキスト（`TrialContext`）と結果（`TrialResult`）のリストを保持し、セッションサマリー（`SessionSummary`）として記録・出力する。
