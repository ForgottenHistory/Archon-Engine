using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProvinceSystem.Services;

namespace ProvinceSystem.UI
{
    /// <summary>
    /// UI component for displaying ParadoxDataLib loading progress
    /// Shows detailed progress, current stage, and error information
    /// </summary>
    public class ParadoxDataLoadingUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject loadingPanel;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private TextMeshProUGUI stageText;
        [SerializeField] private TextMeshProUGUI detailsText;
        [SerializeField] private Button cancelButton;
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private TextMeshProUGUI errorText;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button dismissButton;

        [Header("Animation Settings")]
        [SerializeField] private bool enableProgressAnimation = true;
        [SerializeField] private float progressUpdateSpeed = 2f;
        [SerializeField] private bool enablePulseAnimation = true;
        [SerializeField] private float pulseSpeed = 1f;

        [Header("Display Settings")]
        [SerializeField] private bool showPercentage = true;
        [SerializeField] private bool showStageDetails = true;
        [SerializeField] private bool showTimeEstimate = true;
        [SerializeField] private int maxDetailLines = 5;

        // Internal state
        private ParadoxDataManager _dataManager;
        private bool _isVisible = false;
        private float _targetProgress = 0f;
        private float _currentProgress = 0f;
        private System.DateTime _loadStartTime;
        private List<string> _stageHistory = new List<string>();
        private Coroutine _animationCoroutine;

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeUI();
        }

        private void Start()
        {
            // Find and subscribe to ParadoxDataManager
            _dataManager = ParadoxDataManager.Instance;
            if (_dataManager != null)
            {
                SubscribeToEvents();
            }
            else
            {
                Debug.LogWarning("[ParadoxDataLoadingUI] ParadoxDataManager not found!");
            }

            // Initially hide the loading UI
            SetVisible(false);
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Manually show the loading UI (useful for testing)
        /// </summary>
        [ContextMenu("Show Loading UI")]
        public void ShowLoadingUI()
        {
            SetVisible(true);
            UpdateProgress(0f);
            UpdateStage("Testing UI Display");
            UpdateDetails("Manual UI test initiated");
        }

        /// <summary>
        /// Manually hide the loading UI
        /// </summary>
        [ContextMenu("Hide Loading UI")]
        public void HideLoadingUI()
        {
            SetVisible(false);
        }

        /// <summary>
        /// Show error message with retry option
        /// </summary>
        public void ShowError(string errorMessage, bool canRetry = true)
        {
            if (errorPanel != null)
            {
                errorPanel.SetActive(true);
                if (errorText != null)
                    errorText.text = errorMessage;
                if (retryButton != null)
                    retryButton.gameObject.SetActive(canRetry);
            }
        }

        /// <summary>
        /// Hide error panel
        /// </summary>
        public void HideError()
        {
            if (errorPanel != null)
                errorPanel.SetActive(false);
        }

        #endregion

        #region Private Methods

        private void InitializeUI()
        {
            // Setup button events
            if (cancelButton != null)
                cancelButton.onClick.AddListener(OnCancelClicked);
            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryClicked);
            if (dismissButton != null)
                dismissButton.onClick.AddListener(OnDismissClicked);

            // Initialize progress bar
            if (progressBar != null)
            {
                progressBar.minValue = 0f;
                progressBar.maxValue = 1f;
                progressBar.value = 0f;
            }

            // Initialize error panel
            if (errorPanel != null)
                errorPanel.SetActive(false);
        }

        private void SubscribeToEvents()
        {
            if (_dataManager != null)
            {
                _dataManager.OnLoadProgress += OnLoadProgress;
                _dataManager.OnLoadStageChanged += OnLoadStageChanged;
                _dataManager.OnLoadComplete += OnLoadComplete;
                _dataManager.OnLoadError += OnLoadError;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (_dataManager != null)
            {
                _dataManager.OnLoadProgress -= OnLoadProgress;
                _dataManager.OnLoadStageChanged -= OnLoadStageChanged;
                _dataManager.OnLoadComplete -= OnLoadComplete;
                _dataManager.OnLoadError -= OnLoadError;
            }
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (loadingPanel != null)
                loadingPanel.SetActive(visible);

            if (visible)
            {
                _loadStartTime = System.DateTime.Now;
                _stageHistory.Clear();

                if (enableProgressAnimation && _animationCoroutine == null)
                    _animationCoroutine = StartCoroutine(AnimationUpdate());
            }
            else
            {
                if (_animationCoroutine != null)
                {
                    StopCoroutine(_animationCoroutine);
                    _animationCoroutine = null;
                }
            }
        }

        private void UpdateProgress(float progress)
        {
            _targetProgress = Mathf.Clamp01(progress);

            if (!enableProgressAnimation)
            {
                _currentProgress = _targetProgress;
                if (progressBar != null)
                    progressBar.value = _currentProgress;
            }

            // Update progress text
            if (progressText != null && showPercentage)
            {
                string progressStr = $"{(_targetProgress * 100f):F0}%";

                if (showTimeEstimate && _targetProgress > 0.01f)
                {
                    var elapsed = System.DateTime.Now - _loadStartTime;
                    var estimated = System.TimeSpan.FromTicks((long)(elapsed.Ticks / _targetProgress));
                    var remaining = estimated - elapsed;

                    if (remaining.TotalSeconds > 0)
                    {
                        progressStr += $" (ETA: {remaining.TotalSeconds:F0}s)";
                    }
                }

                progressText.text = progressStr;
            }
        }

        private void UpdateStage(string stageName)
        {
            if (stageText != null)
                stageText.text = stageName;

            // Add to stage history
            _stageHistory.Add($"[{System.DateTime.Now:HH:mm:ss}] {stageName}");

            // Update details with recent stages
            if (showStageDetails)
                UpdateStageDetails();
        }

        private void UpdateDetails(string details)
        {
            if (detailsText != null)
                detailsText.text = details;
        }

        private void UpdateStageDetails()
        {
            if (detailsText != null && _stageHistory.Count > 0)
            {
                var recentStages = _stageHistory.Count > maxDetailLines
                    ? _stageHistory.GetRange(_stageHistory.Count - maxDetailLines, maxDetailLines)
                    : _stageHistory;

                detailsText.text = string.Join("\n", recentStages);
            }
        }

        private IEnumerator AnimationUpdate()
        {
            while (_isVisible)
            {
                // Smooth progress bar animation
                if (enableProgressAnimation && progressBar != null)
                {
                    _currentProgress = Mathf.MoveTowards(_currentProgress, _targetProgress,
                        progressUpdateSpeed * Time.deltaTime);
                    progressBar.value = _currentProgress;
                }

                // Pulse animation for loading indicator
                if (enablePulseAnimation && loadingPanel != null)
                {
                    var alpha = 0.8f + 0.2f * Mathf.Sin(Time.time * pulseSpeed);
                    var canvasGroup = loadingPanel.GetComponent<CanvasGroup>();
                    if (canvasGroup != null)
                        canvasGroup.alpha = alpha;
                }

                yield return null;
            }
        }

        #endregion

        #region Event Handlers

        private void OnLoadProgress(float progress)
        {
            if (!_isVisible)
                SetVisible(true);

            UpdateProgress(progress);
        }

        private void OnLoadStageChanged(string stageName)
        {
            if (!_isVisible)
                SetVisible(true);

            UpdateStage(stageName);
        }

        private void OnLoadComplete()
        {
            UpdateProgress(1f);
            UpdateStage("Loading Complete");

            // Hide UI after a short delay
            StartCoroutine(HideAfterDelay(1f));
        }

        private void OnLoadError(ParadoxDataException error)
        {
            SetVisible(false);

            string errorMessage = $"Loading failed: {error.Message}";
            if (error.ErrorDetails != null && !string.IsNullOrEmpty(error.ErrorDetails.RecoverySuggestion))
            {
                errorMessage += $"\n\nSuggestion: {error.ErrorDetails.RecoverySuggestion}";
            }

            ShowError(errorMessage, true);
        }

        private void OnCancelClicked()
        {
            if (_dataManager != null)
            {
                _dataManager.CancelLoading();
            }
            SetVisible(false);
        }

        private void OnRetryClicked()
        {
            HideError();

            if (_dataManager != null)
            {
                StartCoroutine(_dataManager.LoadAllDataCoroutine());
            }
        }

        private void OnDismissClicked()
        {
            HideError();
        }

        #endregion

        #region Utility Methods

        private IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            SetVisible(false);
        }

        #endregion

        #region Public API for Manual Control

        /// <summary>
        /// Start loading process with UI display
        /// </summary>
        public void StartLoading()
        {
            if (_dataManager != null && !_dataManager.IsLoading)
            {
                StartCoroutine(_dataManager.LoadAllDataCoroutine());
            }
        }

        /// <summary>
        /// Get current loading statistics
        /// </summary>
        public string GetLoadingStats()
        {
            if (_dataManager == null)
                return "No data manager available";

            var stats = $"State: {_dataManager.CurrentState}\n";
            stats += $"Progress: {(_dataManager.LoadingProgress * 100f):F1}%\n";
            stats += $"Memory Usage: {(_dataManager.MemoryUsageBytes / 1024f / 1024f):F1} MB\n";

            var elapsed = System.DateTime.Now - _loadStartTime;
            stats += $"Elapsed Time: {elapsed.TotalSeconds:F1}s\n";
            stats += $"Stages Completed: {_stageHistory.Count}";

            return stats;
        }

        #endregion
    }
}