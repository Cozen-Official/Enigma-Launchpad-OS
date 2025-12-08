using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Cozen
{
    public enum WorldStatMetric
    {
        Visits,
        Favorites,
        Occupancy,
        Popularity,
        Heat,
        Players,
        Age,
        Time
    }
    
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class StatsHandler : UdonSharpBehaviour
    {
        public EnigmaLaunchpad launchpad;
        
        public const float WorldStatsMinInterval = 30f;
        public const float WorldStatsMaxInterval = 300f;
        
        [Header("Stats")]
        [Tooltip("Enter the world ID (e.g. wrld_669a8e54-85fe-4822-94c4-a97c261f26c8). The full API URL is built in the editor.")]
        public string worldStatsWorldId = "";
        
        [Header("Built Api Url (Read Only)")]
        [Tooltip("Editor-built URL used at runtime. Do not modify at runtime.")]
        [SerializeField] private VRCUrl worldStatsBuiltApiUrl;
        
        public VRCUrl WorldStatsBuiltApiUrl => worldStatsBuiltApiUrl;
        
        public VRCUrl WorldStatsBuiltApiUrlField
        {
            get => worldStatsBuiltApiUrl;
            set => worldStatsBuiltApiUrl = value;
        }
        
        [Range(WorldStatsMinInterval, WorldStatsMaxInterval)]
        [Tooltip("Base interval between successful fetches (seconds). Clamped to 30 - 300.")]
        public float worldStatsUpdateIntervalSeconds = 120f;
        
        [Tooltip("If true, numbers get thousands separators (N0). Otherwise raw int.ToString().")]
        public bool worldStatsUseThousandsSeparators = true;
        
        [Tooltip("Automatically start fetching on Start().")]
        public bool worldStatsAutoStart = true;
        
        [Tooltip("Adds a random 0-10s jitter to the first request to reduce simultaneous spikes.")]
        public bool worldStatsJitterFirstRequest = true;
        
        [Tooltip("If true, keeps displaying last known value on errors.")]
        public bool worldStatsPreserveOnError = true;
        
        [Tooltip("Apply per-fetch jitter (+/- percent of base interval) to avoid synchronization.")]
        [Range(0f, 0.5f)] public float worldStatsPerFetchJitterFraction = 0.1f; // +/-10% default
        
        [Tooltip("Enable exponential style backoff on repeated errors (especially 429 / server errors).")]
        public bool worldStatsEnableBackoff = true;
        [Tooltip("Initial backoff seconds added after first error.")]
        [Range(5f, 600f)] public float worldStatsInitialBackoffSeconds = 30f;
        [Tooltip("Maximum backoff seconds cap.")]
        [Range(30f, 1800f)] public float worldStatsMaxBackoffSeconds = 600f;
        [Tooltip("Backoff growth factor per consecutive qualifying error.")]
        [Range(1.1f, 3f)] public float worldStatsBackoffGrowthFactor = 2f;
        [Tooltip("Only escalate backoff for these HTTP status ranges: 429 or >=500.")]
        public bool worldStatsOnlyBackoffOn429And5xx = true;
        
        [Tooltip("Flattened metric selections for Stats folders (per entry order).")]
        public WorldStatMetric[] worldStatsMetricsFlat;
        
        [Header("Stats Utility Buttons")]
        [Tooltip("Optional UI button for the World ID helper highlight.")]
        public ButtonHandler worldStatsWorldIdButton;
        
        [Tooltip("Optional UI button for the URL builder helper highlight.")]
        public ButtonHandler worldStatsUrlBuilderButton;
        
        private const string WorldStatsApiPrefix = "https://api.vrchat.cloud/api/1/worlds/";
        private const float WorldStatsClockUpdateIntervalSeconds = 60f;
        private const float WorldStatsTimingToleranceSeconds = 0.01f;
        private const float WorldStatsSchedulerRetrySeconds = 5f;
        
        private int[] worldStatsFolderOffsets;
        private int worldStatsTotalEntries = 0;
        
        private float worldStatsNextFetchTime = float.PositiveInfinity;
        private bool worldStatsPendingRequest = false;
        
        // Synced stats data - only instance owner updates these
        [UdonSynced] private int worldStatsOccupants = -1;
        [UdonSynced] private int worldStatsVisits = -1;
        [UdonSynced] private int worldStatsFavorites = -1;
        [UdonSynced] private int worldStatsPopularity = -1;
        [UdonSynced] private int worldStatsHeat = -1;
        
        private int worldStatsConsecutiveErrorCount = 0;
        private float worldStatsCurrentBackoffSeconds = 0f;
        
        private float worldStatsNextLocalUpdateTime = float.PositiveInfinity;
        private float worldStatsNextClockUpdateTime = float.PositiveInfinity;
        private bool worldStatsLocalUpdateScheduled = false;
        private bool worldStatsInitialFetchAttempted = false;
        private bool worldStatsInitialFetchArmed = false;
        private int activeStatsFolderIndex = -1;
        
        
        public void Awake()
        {
            if (launchpad == null)
            {
                launchpad = GetComponent<EnigmaLaunchpad>();
                if (launchpad == null)
                {
                    launchpad = GetComponentInParent<EnigmaLaunchpad>();
                }
            }
        }
        
        public void Start()
        {
            if (launchpad == null)
            {
                Awake();
            }
        }
        
        public void SetLaunchpad(EnigmaLaunchpad pad)
        {
            Debug.Log($"[StatsHandler] SetLaunchpad called, pad is {(pad != null ? "NOT NULL" : "NULL")}");
            launchpad = pad;
        }
        
        public override void OnDeserialization()
        {
            // When synced stats data arrives from the instance owner, update the display directly.
            // We call UpdateDisplay() instead of RequestDisplayUpdateFromHandler() because this is
            // the standard pattern for handlers responding to deserialization events.
            if (launchpad != null)
            {
                launchpad.UpdateDisplay();
            }
        }
        
        public void SetActiveStatsFolderIndex(int folderIndex)
        {
            activeStatsFolderIndex = folderIndex;
        }
        
        public bool IsReady()
        {
            return launchpad != null;
        }
        
        public void InitializeStatsRuntime()
        {
            InitializeWorldStatsRuntime();
        }
        
        public void InitializeWorldStatsRuntime()
        {
            if (!IsReady() || !launchpad.HasStatsFolderConfigured())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                worldStatsInitialFetchArmed = false;
                worldStatsPendingRequest = false;
                return;
            }
            
            // Initialize folder offsets for runtime lookups
            int statsFolderCount = (launchpad.GetFolderNames() != null) ? launchpad.GetFolderNames().Length : 0;
            RebuildWorldStatsFolderOffsets(statsFolderCount);
            
            ClampWorldStatsParameters();
            worldStatsPendingRequest = false;
            worldStatsConsecutiveErrorCount = 0;
            worldStatsCurrentBackoffSeconds = 0f;
            worldStatsNextFetchTime = float.PositiveInfinity;
            worldStatsNextLocalUpdateTime = float.PositiveInfinity;
            worldStatsNextClockUpdateTime = float.PositiveInfinity;
            worldStatsLocalUpdateScheduled = false;
            worldStatsInitialFetchAttempted = false;
            worldStatsInitialFetchArmed = false;
            UpdateWorldStatsPollingState();
        }
        
        public void RefreshDisplay()
        {
            if (!IsReady())
            {
                return;
            }
            
            UpdateWorldStatsPollingState();
        }
        
        /// <summary>
        /// Get the number of entries for the stats folder.
        /// Handlers should use this instead of launchpad.folderEntryCounts.
        /// </summary>
        public int GetEntryCount()
        {
            if (!IsReady() || launchpad == null)
            {
                return 0;
            }
            
            int statsFolderIndex = activeStatsFolderIndex;
            if (!HasValidConfigurationForFolder(statsFolderIndex) && !TryGetActiveStatsFolderIndex(out statsFolderIndex))
            {
                return 0;
            }
            
            // Use the legacy folderEntryCounts during migration
            if (launchpad.folderEntryCounts != null && 
            statsFolderIndex >= 0 && 
            statsFolderIndex < launchpad.folderEntryCounts.Length)
            {
                return launchpad.folderEntryCounts[statsFolderIndex];
            }
            
            return 0;
        }
        
        public string GetLabel(int buttonIndex)
        {
            int statsFolderIndex = activeStatsFolderIndex;
            if (launchpad != null && buttonIndex == 10)
            {
                return launchpad.GetFolderLabelForIndex(statsFolderIndex, false);
            }
            
            if (!IsReady() || launchpad == null || !HasValidConfigurationForFolder(statsFolderIndex))
            {
                return buttonIndex == 9 ? "0/0" : string.Empty;
            }
            
            if (buttonIndex == 9)
            {
                return GetPageLabel(statsFolderIndex);
            }
            
            int itemCount = GetEntryCount();
            int page = GetCurrentStatsPage(statsFolderIndex);
            Color unusedColor;
            bool unusedInteractable;
            PopulateButtonVisual(statsFolderIndex, page, itemCount, buttonIndex, out string label, out unusedColor, out unusedInteractable);
            return label;
        }
        
        public bool IsInteractable(int buttonIndex)
        {
            if (!IsReady() || launchpad == null)
            {
                return false;
            }
            
            int statsFolderIndex = activeStatsFolderIndex;
            bool hasConfig = HasValidConfigurationForFolder(statsFolderIndex);
            
            if (buttonIndex == 10)
            {
                return hasConfig;
            }
            
            if (buttonIndex == 9)
            {
                return hasConfig && GetPageCount() > 1;
            }
            
            if (!hasConfig)
            {
                return false;
            }
            
            int itemCount = GetEntryCount();
            int page = GetCurrentStatsPage(statsFolderIndex);
            string unusedLabel;
            Color unusedColor;
            PopulateButtonVisual(statsFolderIndex, page, itemCount, buttonIndex, out unusedLabel, out unusedColor, out bool interactable);
            return interactable;
        }
        
        public bool IsActive(int buttonIndex)
        {
            if (!IsReady() || launchpad == null)
            {
                return false;
            }
            
            if (buttonIndex >= 9)
            {
                return HasValidConfigurationForFolder(activeStatsFolderIndex);
            }
            
            int statsFolderIndex = activeStatsFolderIndex;
            if (!HasValidConfigurationForFolder(statsFolderIndex))
            {
                return false;
            }
            
            int itemCount = GetEntryCount();
            int page = GetCurrentStatsPage(statsFolderIndex);
            string unusedLabel;
            Color unusedColor;
            PopulateButtonVisual(statsFolderIndex, page, itemCount, buttonIndex, out unusedLabel, out unusedColor, out bool active);
            return active;
        }
        
        public void PopulateButtonVisual(int statsFolderIndex, int page, int itemCount, int buttonIndex,
        out string label, out Color color, out bool interactable)
        {
            label = string.Empty;
            color = launchpad != null ? launchpad.GetInactiveColor() : Color.white;
            interactable = false;
            
            if (!IsReady() || launchpad == null)
            {
                return;
            }
            
            if (!launchpad.IsFolderIndexValid(statsFolderIndex))
            {
                return;
            }
            
            if (itemCount <= 0)
            {
                return;
            }
            
            BuildWorldStatsButtonText(statsFolderIndex, page, itemCount, buttonIndex, out label);
            interactable = !string.IsNullOrEmpty(label);
        }
        
        public string GetPageLabel(int statsFolderIndex)
        {
            if (!IsReady() || launchpad == null || !launchpad.IsFolderIndexValid(statsFolderIndex))
            {
                return "0/0";
            }
            
            int count = GetEntryCount();
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
            int currentPage = GetCurrentStatsPage(statsFolderIndex);
            return $"{currentPage + 1}/{totalPages}";
        }
        
        public void UpdateWorldStatsTimeMetrics()
        {
            if (launchpad == null)
            {
                return;
            }
            
            launchpad.UpdateDisplay();
        }
        
        public int GetPageCount()
        {
            if (!IsReady())
            {
                return 1;
            }
            
            int statsFolderIndex = activeStatsFolderIndex;
            if (!HasValidConfigurationForFolder(statsFolderIndex) && !TryGetActiveStatsFolderIndex(out statsFolderIndex))
            {
                return 1;
            }
            
            int count = GetEntryCount();
            return Mathf.Max(1, Mathf.CeilToInt((float)count / launchpad.GetItemsPerPage()));
        }
        
        public int GetCurrentStatsPage(int statsFolderIndex)
        {
            if (!IsReady())
            {
                return 0;
            }
            
            return launchpad.GetFolderPage(statsFolderIndex);
        }
        
        public void OnPageChange(int direction)
        {
            if (!IsReady())
            {
                return;
            }
            
            int statsFolderIndex = activeStatsFolderIndex;
            if (!HasValidConfigurationForFolder(statsFolderIndex) && !TryGetActiveStatsFolderIndex(out statsFolderIndex))
            {
                return;
            }
            
            launchpad.EnsureLocalOwnership();
            
            int totalPages = GetPageCount();
            int current = GetCurrentStatsPage(statsFolderIndex);
            current = (current + direction + totalPages) % totalPages;
            launchpad.SetFolderPage(statsFolderIndex, current);
            
            launchpad.RequestSerialization();
            // UpdateDisplay is called by EnigmaLaunchpad.ChangePage after OnPageChange returns
        }
        
        public void OnSelect(int buttonIndex)
        {
            if (!IsReady())
            {
                return;
            }
        }
        
        public bool HasValidConfigurationForFolder(int statsFolderIndex)
        {
            if (!IsReady())
            {
                return false;
            }
            
            if (!launchpad.IsFolderIndexValid(statsFolderIndex))
            {
                return false;
            }
            
            if (launchpad.GetFolderTypes() == null || statsFolderIndex >= launchpad.GetFolderTypes().Length)
            {
                return false;
            }
            
            if (launchpad.GetFolderTypes()[statsFolderIndex] != ToggleFolderType.Stats)
            {
                return false;
            }
            
            // Simplified check - assume all folders are valid at runtime (editor validates)
            int count = launchpad.folderEntryCounts[statsFolderIndex];
            return count > 0;
        }
        
        public void RebuildWorldStatsFolderOffsets(int statsFolderCount)
        {
            if (!IsReady())
            {
                return;
            }
            
            if (statsFolderCount < 0) statsFolderCount = 0;
            ResizeArray(ref worldStatsFolderOffsets, statsFolderCount);
            
            int running = 0;
            for (int m = 0; m < statsFolderCount; m++)
            {
                worldStatsFolderOffsets[m] = running;
                ToggleFolderType folderType = (launchpad.GetFolderTypes() != null && m < launchpad.GetFolderTypes().Length)
                ? launchpad.GetFolderTypes()[m]
                : ToggleFolderType.Objects;
                if (folderType != ToggleFolderType.Stats)
                continue;
                
                int count = (launchpad.folderEntryCounts != null && m < launchpad.folderEntryCounts.Length)
                ? launchpad.folderEntryCounts[m]
                : 0;
                if (count < 0) count = 0;
                running += count;
            }
            
            ResizeArray(ref worldStatsMetricsFlat, running);
            
            worldStatsTotalEntries = running;
        }
        
        public void ResetWorldStatsFolderOffsets(int statsFolderCount)
        {
            if (!IsReady())
            {
                return;
            }
            
            if (statsFolderCount < 0)
            {
                statsFolderCount = 0;
            }
            
            worldStatsTotalEntries = 0;
            ResizeArray(ref worldStatsFolderOffsets, statsFolderCount);
            
            if (worldStatsFolderOffsets != null)
            {
                for (int i = 0; i < worldStatsFolderOffsets.Length; i++)
                {
                    worldStatsFolderOffsets[i] = -1;
                }
            }
            
            ResizeArray(ref worldStatsMetricsFlat, 0);
        }
        
        public void OnValidateStats()
        {
            if (!IsReady())
            {
                return;
            }
            
            ClampWorldStatsParameters();
            int statsFolderCount = (launchpad.GetFolderNames() != null) ? launchpad.GetFolderNames().Length : 0;
            RebuildWorldStatsFolderOffsets(statsFolderCount);
        }
        
        public void UpdateWorldStatsPollingState()
        {
            if (!IsReady())
            {
                return;
            }
            
            // Only instance owner performs API polling. We check ownership on launchpad.gameObject
            // because that's where the synced variables live and ownership is managed.
            if (!Networking.IsOwner(launchpad.gameObject))
            {
                // Non-owners just update their local display from synced data
                UpdateWorldStatsLocalUpdateState();
                return;
            }
            
            if (!launchpad.HasStatsFolderConfigured())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                worldStatsNextClockUpdateTime = float.PositiveInfinity;
                worldStatsNextLocalUpdateTime = float.PositiveInfinity;
                worldStatsLocalUpdateScheduled = false;
                return;
            }
            
            if (worldStatsAutoStart)
            {
                TryPrimeWorldStatsFetch();
            }
            
            if (!ShouldPollWorldStats())
            {
                if (worldStatsPendingRequest)
                {
                    worldStatsPendingRequest = false;
                }
                UpdateWorldStatsLocalUpdateState();
                return;
            }
            
            if (!worldStatsPendingRequest && float.IsPositiveInfinity(worldStatsNextFetchTime))
            {
                worldStatsInitialFetchArmed = true;
                ScheduleNextWorldStatsFetch(true, true);
                UpdateWorldStatsLocalUpdateState();
                return;
            }
            
            if (worldStatsPendingRequest)
            {
                UpdateWorldStatsLocalUpdateState();
                return;
            }
            
            if (Time.time >= worldStatsNextFetchTime)
            {
                ScheduleNextWorldStatsFetch(true);
            }
            
            UpdateWorldStatsLocalUpdateState();
        }
        
        public void UpdateWorldStatsLocalUpdateState()
        {
            if (!IsReady())
            {
                return;
            }
            
            if (!ShouldUpdateWorldStatsLocally())
            {
                worldStatsNextLocalUpdateTime = float.PositiveInfinity;
                worldStatsNextClockUpdateTime = float.PositiveInfinity;
                worldStatsLocalUpdateScheduled = false;
                return;
            }
            
            float now = Time.time;
            bool scheduled = false;
            
            if (float.IsPositiveInfinity(worldStatsNextLocalUpdateTime))
            {
                worldStatsNextLocalUpdateTime = now;
                scheduled = true;
            }
            
            if (float.IsPositiveInfinity(worldStatsNextClockUpdateTime))
            {
                worldStatsNextClockUpdateTime = now;
                scheduled = true;
            }
            
            if (scheduled)
            {
                QueueWorldStatsLocalUpdate();
            }
        }
        
        public void QueueWorldStatsLocalUpdate()
        {
            if (worldStatsLocalUpdateScheduled) return;
            float next = Mathf.Min(worldStatsNextLocalUpdateTime, worldStatsNextClockUpdateTime);
            if (float.IsPositiveInfinity(next)) return;
            
            float delay = next - Time.time;
            if (delay < 0f) delay = 0f;
            worldStatsLocalUpdateScheduled = true;
            SendCustomEventDelayedSeconds(nameof(ExecuteWorldStatsLocalUpdate), delay);
        }
        
        public void ExecuteWorldStatsLocalUpdate()
        {
            worldStatsLocalUpdateScheduled = false;
            
            if (!ShouldUpdateWorldStatsLocally())
            {
                worldStatsNextLocalUpdateTime = float.PositiveInfinity;
                worldStatsNextClockUpdateTime = float.PositiveInfinity;
                return;
            }
            
            ClampWorldStatsParameters();
            
            float now = Time.time;
            bool localDue = false;
            bool clockDue = false;
            
            if (now + WorldStatsTimingToleranceSeconds >= worldStatsNextLocalUpdateTime)
            {
                float interval = worldStatsUpdateIntervalSeconds;
                if (interval < WorldStatsMinInterval) interval = WorldStatsMinInterval;
                else if (interval > WorldStatsMaxInterval) interval = WorldStatsMaxInterval;
                worldStatsNextLocalUpdateTime = now + interval;
                localDue = true;
            }
            
            if (now + WorldStatsTimingToleranceSeconds >= worldStatsNextClockUpdateTime)
            {
                worldStatsNextClockUpdateTime = now + WorldStatsClockUpdateIntervalSeconds;
                clockDue = true;
            }
            
            if (localDue)
            {
                TryPrimeWorldStatsFetch();
            }
            
            if (clockDue || localDue)
            {
                UpdateWorldStatsTimeMetrics();
            }
            
            QueueWorldStatsLocalUpdate();
        }
        
        public void ExecuteWorldStatsScheduler()
        {
            if (!IsReady() || !launchpad.HasStatsFolderConfigured())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                return;
            }
            
            UpdateWorldStatsScheduler();
            
            if (worldStatsPendingRequest)
            return;
            
            if (float.IsPositiveInfinity(worldStatsNextFetchTime))
            {
                if (ShouldPollWorldStats())
                {
                    ScheduleNextWorldStatsFetch(true);
                }
                return;
            }
            
            float remaining = worldStatsNextFetchTime - Time.time;
            if (remaining > WorldStatsTimingToleranceSeconds)
            {
                QueueWorldStatsScheduler(remaining);
                return;
            }
            
            if (!ShouldPollWorldStats()) return;
            
            QueueWorldStatsScheduler(WorldStatsSchedulerRetrySeconds);
        }
        
        public void ScheduleNextWorldStatsFetch(bool initial, bool allowInactive = false)
        {
            if (!IsReady() || !launchpad.HasStatsFolderConfigured())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                return;
            }
            
            if (!allowInactive && !ShouldPollWorldStats())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                return;
            }
            float jitter = (initial && worldStatsJitterFirstRequest) ? UnityEngine.Random.Range(0f, 10f) : 0f;
            worldStatsNextFetchTime = Time.time + jitter;
            QueueWorldStatsSchedulerForNextFetch();
        }
        
        public void BeginWorldStatsFetch()
        {
            // Only instance owner performs API requests
            if (!Networking.IsOwner(launchpad.gameObject))
            {
                return;
            }
            
            if (!IsReady() || !launchpad.HasStatsFolderConfigured())
            {
                return;
            }
            
            ClampWorldStatsParameters();
            bool priming = worldStatsInitialFetchArmed;
            if (!ShouldPollWorldStats())
            {
                if (priming) worldStatsInitialFetchArmed = false;
                return;
            }
            
            worldStatsInitialFetchArmed = false;
            
            worldStatsPendingRequest = true;
            VRCStringDownloader.LoadUrl(WorldStatsBuiltApiUrl, (IUdonEventReceiver)this);
        }
        
        public void ForceWorldStatsRefresh()
        {
            // Only instance owner performs API requests
            if (!Networking.IsOwner(launchpad.gameObject))
            {
                return;
            }
            
            if (worldStatsPendingRequest) return;
            if (!ShouldPollWorldStats()) return;
            worldStatsCurrentBackoffSeconds = 0f;
            worldStatsConsecutiveErrorCount = 0;
            worldStatsNextFetchTime = Time.time;
            QueueWorldStatsScheduler(0f);
        }
        
        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            // Only instance owner processes API results
            if (!Networking.IsOwner(launchpad.gameObject))
            {
                return;
            }
            
            if (!IsReady())
            {
                return;
            }
            
            worldStatsPendingRequest = false;
            
            worldStatsConsecutiveErrorCount = 0;
            worldStatsCurrentBackoffSeconds = 0f;
            
            ParseWorldStats(result.Result);
            
            // Request serialization to sync the updated stats to all clients
            launchpad.RequestSerialization();
            
            bool shouldContinue = ShouldPollWorldStats();
            if (shouldContinue)
            {
                float baseInterval = worldStatsUpdateIntervalSeconds;
                float jitterFactor = worldStatsPerFetchJitterFraction > 0f
                ? UnityEngine.Random.Range(-worldStatsPerFetchJitterFraction, worldStatsPerFetchJitterFraction)
                : 0f;
                float intervalWithJitter = baseInterval + baseInterval * jitterFactor;
                if (intervalWithJitter < WorldStatsMinInterval) intervalWithJitter = WorldStatsMinInterval;
                
                worldStatsNextFetchTime = Time.time + intervalWithJitter;
                QueueWorldStatsSchedulerForNextFetch();
            }
            else
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
            }
            
            launchpad.UpdateDisplay();
        }
        
        public override void OnStringLoadError(IVRCStringDownload result)
        {
            // Only instance owner processes API results
            if (!Networking.IsOwner(launchpad.gameObject))
            {
                return;
            }
            
            worldStatsPendingRequest = false;
            Debug.LogWarning($"[EnigmaLaunchpad][WorldStats] Load error {result.ErrorCode}: {result.Error}");
            
            bool qualifiesForBackoff = true;
            if (worldStatsOnlyBackoffOn429And5xx)
            {
                int code = result.ErrorCode;
                qualifiesForBackoff = (code == 429) || (code >= 500);
            }
            
            if (!worldStatsPreserveOnError)
            {
                worldStatsOccupants = -1;
                worldStatsVisits = -1;
                worldStatsFavorites = -1;
                worldStatsPopularity = -1;
                worldStatsHeat = -1;
                
                // Request serialization to sync cleared stats to all clients
                launchpad.RequestSerialization();
            }
            
            bool shouldContinue = ShouldPollWorldStats();
            if (shouldContinue)
            {
                if (worldStatsEnableBackoff && qualifiesForBackoff)
                {
                    worldStatsConsecutiveErrorCount++;
                    if (worldStatsConsecutiveErrorCount == 1)
                    {
                        worldStatsCurrentBackoffSeconds = worldStatsInitialBackoffSeconds;
                    }
                    else
                    {
                        float proposed = worldStatsCurrentBackoffSeconds * worldStatsBackoffGrowthFactor;
                        if (proposed < worldStatsInitialBackoffSeconds) proposed = worldStatsInitialBackoffSeconds;
                        worldStatsCurrentBackoffSeconds = proposed;
                    }
                    
                    if (worldStatsCurrentBackoffSeconds > worldStatsMaxBackoffSeconds)
                    worldStatsCurrentBackoffSeconds = worldStatsMaxBackoffSeconds;
                    
                    float backoffJitter = worldStatsCurrentBackoffSeconds * UnityEngine.Random.Range(0f, 0.2f);
                    worldStatsNextFetchTime = Time.time + worldStatsCurrentBackoffSeconds + backoffJitter;
                    QueueWorldStatsSchedulerForNextFetch();
                }
                else
                {
                    float baseInterval = worldStatsUpdateIntervalSeconds;
                    float jitterFactor = worldStatsPerFetchJitterFraction > 0f
                    ? UnityEngine.Random.Range(-worldStatsPerFetchJitterFraction, worldStatsPerFetchJitterFraction)
                    : 0f;
                    float intervalWithJitter = baseInterval + baseInterval * jitterFactor;
                    if (intervalWithJitter < WorldStatsMinInterval) intervalWithJitter = WorldStatsMinInterval;
                    worldStatsNextFetchTime = Time.time + intervalWithJitter;
                    QueueWorldStatsSchedulerForNextFetch();
                }
            }
            else
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
            }
            
            launchpad.UpdateDisplay();
        }
        
        public void TryPrimeWorldStatsFetch()
        {
            // Only instance owner performs API requests
            if (!Networking.IsOwner(launchpad.gameObject)) return;
            
            if (!ShouldPrimeWorldStats()) return;
            worldStatsInitialFetchAttempted = true;
            worldStatsInitialFetchArmed = true;
            ScheduleNextWorldStatsFetch(true, true);
        }
        
        public bool ShouldPollWorldStats()
        {
            if (!IsReady())
            {
                return false;
            }
            
            if (!launchpad.HasStatsFolderConfigured())
            {
                return false;
            }
            
            bool useAnyStatsFolder = worldStatsInitialFetchArmed;
            int statsFolderIndex;
            if (useAnyStatsFolder)
            {
                if (!TryGetAnyStatsFolderIndex(out statsFolderIndex)) return false;
            }
            else
            {
                if (!TryGetActiveStatsFolderIndex(out statsFolderIndex)) return false;
            }
            
            if (!DoesStatsFolderRequireApi(statsFolderIndex)) return false;
            if (!IsWorldStatsApiReady()) return false;
            return true;
        }
        
        public bool DoesStatsFolderRequireApi(int statsFolderIndex)
        {
            if (launchpad.folderEntryCounts == null || statsFolderIndex < 0 || statsFolderIndex >= launchpad.folderEntryCounts.Length) return false;
            int count = launchpad.folderEntryCounts[statsFolderIndex];
            if (count <= 0) return false;
            
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                if (!TryGetWorldStatsMetric(statsFolderIndex, localIndex, out WorldStatMetric metric)) continue;
                if (WorldStatsMetricRequiresApi(metric)) return true;
            }
            
            return false;
        }
        
        public bool DoesStatsFolderIncludeLocalMetric(int statsFolderIndex)
        {
            if (launchpad.folderEntryCounts == null || statsFolderIndex < 0 || statsFolderIndex >= launchpad.folderEntryCounts.Length) return false;
            int count = launchpad.folderEntryCounts[statsFolderIndex];
            if (count <= 0) return false;
            
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                if (!TryGetWorldStatsMetric(statsFolderIndex, localIndex, out WorldStatMetric metric)) continue;
                if (!WorldStatsMetricRequiresApi(metric)) return true;
            }
            
            return false;
        }
        
        public bool TryGetAnyStatsFolderIndex(out int statsFolderIndex)
        {
            statsFolderIndex = -1;
            if (launchpad.GetFolderTypes() == null) return false;
            
            int limit = launchpad.GetFolderTypes().Length;
            for (int i = 0; i < limit; i++)
            {
                if (launchpad.GetFolderTypes()[i] != ToggleFolderType.Stats) continue;
                statsFolderIndex = i;
                return true;
            }
            
            return false;
        }
        
        public bool TryGetActiveStatsFolderIndex(out int statsFolderIndex)
        {
            statsFolderIndex = -1;
            int defaultFolder = launchpad.GetDefaultFolderIndex();
            if (!launchpad.FolderRepresentsStats(defaultFolder)) return false;
            if (!launchpad.TryGetObjectFolderIndex(defaultFolder, out int objectFolderIdx)) return false;
            if (launchpad.GetFolderTypes() == null || objectFolderIdx >= launchpad.GetFolderTypes().Length) return false;
            statsFolderIndex = objectFolderIdx;
            return true;
        }
        
        public bool ShouldUpdateWorldStatsLocally()
        {
            if (!TryGetActiveStatsFolderIndex(out int statsFolderIndex)) return false;
            return DoesStatsFolderIncludeLocalMetric(statsFolderIndex);
        }
        
        public void UpdateWorldStatsScheduler()
        {
            if (!IsReady() || !launchpad.HasStatsFolderConfigured())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                return;
            }
            
            if (!ShouldPollWorldStats())
            {
                worldStatsNextFetchTime = float.PositiveInfinity;
                return;
            }
            if (worldStatsPendingRequest) return;
            if (Time.time < worldStatsNextFetchTime) return;
            
            BeginWorldStatsFetch();
        }
        
        public void QueueWorldStatsScheduler(float delaySeconds)
        {
            if (delaySeconds < 0f) delaySeconds = 0f;
            SendCustomEventDelayedSeconds(nameof(ExecuteWorldStatsScheduler), delaySeconds);
        }
        
        public void QueueWorldStatsSchedulerForNextFetch()
        {
            if (float.IsPositiveInfinity(worldStatsNextFetchTime)) return;
            float delay = worldStatsNextFetchTime - Time.time;
            if (delay < 0f) delay = 0f;
            QueueWorldStatsScheduler(delay);
        }
        
        public void OnPageChanged()
        {
            UpdateWorldStatsTimeMetrics();
        }
        
        public static bool WorldStatsMetricRequiresApi(WorldStatMetric metric)
        {
            switch (metric)
            {
                case WorldStatMetric.Visits:
                case WorldStatMetric.Favorites:
                case WorldStatMetric.Occupancy:
                case WorldStatMetric.Popularity:
                case WorldStatMetric.Heat:
                return true;
                default:
                return false;
            }
        }
        
        public static string GetWorldStatsDisplayNameShared(WorldStatMetric metric)
        {
            switch (metric)
            {
                case WorldStatMetric.Visits:
                return "Visits";
                case WorldStatMetric.Favorites:
                return "Favorites";
                case WorldStatMetric.Occupancy:
                return "Occupancy";
                case WorldStatMetric.Popularity:
                return "Popularity";
                case WorldStatMetric.Heat:
                return "Heat";
                case WorldStatMetric.Players:
                return "Players";
                case WorldStatMetric.Age:
                return "Age";
                case WorldStatMetric.Time:
                return "Time";
                default:
                return metric.ToString();
            }
        }
        
        public void EditorBuildWorldStatsApiUrl()
        {
            #if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!IsReady())
            {
                return;
            }
            
            string trimmed = string.IsNullOrEmpty(worldStatsWorldId) ? "" : worldStatsWorldId.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                if (!trimmed.StartsWith("wrld_"))
                trimmed = "wrld_" + trimmed;
                worldStatsWorldId = trimmed;
                string finalUrl = WorldStatsApiPrefix + trimmed;
                WorldStatsBuiltApiUrlField = new VRCUrl(finalUrl);
            }
            else
            {
                WorldStatsBuiltApiUrlField = new VRCUrl("");
            }
            UnityEditor.EditorUtility.SetDirty(launchpad);
            #endif
        }
        
        private bool ShouldPrimeWorldStats()
        {
            if (worldStatsInitialFetchAttempted) return false;
            if (!TryGetAnyStatsFolderIndex(out int statsFolderIndex)) return false;
            if (!DoesStatsFolderRequireApi(statsFolderIndex)) return false;
            if (!IsWorldStatsApiReady()) return false;
            return true;
        }
        
        private bool IsWorldStatsApiReady()
        {
            return WorldStatsBuiltApiUrl != null && !string.IsNullOrEmpty(WorldStatsBuiltApiUrl.Get());
        }
        
        private void ClampWorldStatsParameters()
        {
            if (worldStatsUpdateIntervalSeconds < WorldStatsMinInterval) worldStatsUpdateIntervalSeconds = WorldStatsMinInterval;
            else if (worldStatsUpdateIntervalSeconds > WorldStatsMaxInterval) worldStatsUpdateIntervalSeconds = WorldStatsMaxInterval;
            
            if (worldStatsInitialBackoffSeconds > worldStatsMaxBackoffSeconds)
            worldStatsInitialBackoffSeconds = worldStatsMaxBackoffSeconds;
        }
        
        private bool TryGetWorldStatsMetric(int statsFolderIndex, int localIndex, out WorldStatMetric metric)
        {
            metric = WorldStatMetric.Visits;
            int start = GetWorldStatsFolderStartOffset(statsFolderIndex);
            if (start < 0) return false;
            int index = start + localIndex;
            if (worldStatsMetricsFlat == null || index < 0 || index >= worldStatsMetricsFlat.Length) return false;
            metric = worldStatsMetricsFlat[index];
            return true;
        }
        
        private int GetWorldStatsFolderStartOffset(int statsFolderIndex)
        {
            if (launchpad.GetFolderTypes() == null) return -1;
            if (statsFolderIndex < 0 || statsFolderIndex >= launchpad.GetFolderTypes().Length) return -1;
            if (launchpad.GetFolderTypes()[statsFolderIndex] != ToggleFolderType.Stats) return -1;
            if (worldStatsFolderOffsets == null || statsFolderIndex >= worldStatsFolderOffsets.Length) return -1;
            return worldStatsFolderOffsets[statsFolderIndex];
        }
        
        private string GetWorldStatsDisplayName(WorldStatMetric metric)
        {
            return GetWorldStatsDisplayNameShared(metric);
        }
        
        private string FormatWorldStatsValue(WorldStatMetric metric)
        {
            switch (metric)
            {
                case WorldStatMetric.Players:
                return FormatWorldStatsNumber(GetWorldStatsPlayerCount());
                case WorldStatMetric.Age:
                return FormatWorldStatsInstanceAge();
                case WorldStatMetric.Time:
                return GetWorldStatsClockTime();
                default:
                int value = GetWorldStatsMetricValue(metric);
                return FormatWorldStatsNumber(value);
            }
        }
        
        private int GetWorldStatsMetricValue(WorldStatMetric metric)
        {
            switch (metric)
            {
                case WorldStatMetric.Visits:
                return worldStatsVisits;
                case WorldStatMetric.Favorites:
                return worldStatsFavorites;
                case WorldStatMetric.Occupancy:
                return worldStatsOccupants;
                case WorldStatMetric.Popularity:
                return worldStatsPopularity;
                case WorldStatMetric.Heat:
                return worldStatsHeat;
                default:
                return -1;
            }
        }
        
        private string FormatWorldStatsNumber(int value)
        {
            if (value < 0) return "-";
            return worldStatsUseThousandsSeparators ? value.ToString("N0") : value.ToString();
        }
        
        private int GetWorldStatsPlayerCount()
        {
            return VRCPlayerApi.GetPlayerCount();
        }
        
        private string FormatWorldStatsInstanceAge()
        {
            double seconds = Networking.GetServerTimeInSeconds();
            if (seconds < 0.0) seconds = 0.0;
            
            int totalSeconds = (int)Math.Floor(seconds);
            int days = totalSeconds / 86400;
            int remainder = totalSeconds % 86400;
            int hours = remainder / 3600;
            remainder %= 3600;
            int minutes = remainder / 60;
            
            if (days > 0)
            {
                return days == 1
                ? $"1d {hours:00}h"
                : $"{days}d {hours:00}h";
            }
            
            if (hours > 0)
            {
                return $"{hours:00}:{minutes:00}";
            }
            
            return $"00:{minutes:00}";
        }
        
        private string GetWorldStatsClockTime()
        {
            DateTime now = DateTime.Now;
            return now.ToString("HH:mm");
        }
        
        private void ParseWorldStats(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            worldStatsOccupants = ExtractWorldStatsInt(json, "\"occupants\":");
            worldStatsVisits = ExtractWorldStatsInt(json, "\"visits\":");
            worldStatsFavorites = ExtractWorldStatsInt(json, "\"favorites\":");
            worldStatsPopularity = ExtractWorldStatsInt(json, "\"popularity\":");
            worldStatsHeat = ExtractWorldStatsInt(json, "\"heat\":");
        }
        
        private int ExtractWorldStatsInt(string json, string keyPattern)
        {
            int idx = json.IndexOf(keyPattern);
            if (idx == -1) return -1;
            idx += keyPattern.Length;
            
            int len = json.Length;
            while (idx < len)
            {
                char c = json[idx];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') idx++;
                else break;
            }
            if (idx >= len) return -1;
            
            int start = idx;
            while (idx < len)
            {
                char c = json[idx];
                if (c >= '0' && c <= '9') { idx++; continue; }
                break;
            }
            if (start == idx) return -1;
            
            string numStr = json.Substring(start, idx - start);
            int val;
            if (!int.TryParse(numStr, out val)) return -1;
            return val;
        }
        
        private void BuildWorldStatsButtonText(int statsFolderIndex, int page, int itemCount, int buttonIndex, out string label)
        {
            label = string.Empty;
            if (!launchpad.IsFolderIndexValid(statsFolderIndex)) return;
            
            if (itemCount <= 0) return;
            
            int localIndex = page * launchpad.GetItemsPerPage() + buttonIndex;
            if (localIndex < 0 || localIndex >= itemCount) return;
            
            if (!TryGetWorldStatsMetric(statsFolderIndex, localIndex, out WorldStatMetric metric)) return;
            
            string name = GetWorldStatsDisplayName(metric);
            string value = FormatWorldStatsValue(metric);
            
            if (string.IsNullOrEmpty(name))
            {
                label = value;
            }
            else if (string.IsNullOrEmpty(value))
            {
                label = name;
            }
            else
            {
                label = name + "\n" + value;
            }
        }
        
        public void UpdateWorldStatsAuxiliaryButtonColors(int statsFolderIndex)
        {
            if (!IsReady() || !launchpad.HasStatsFolderConfigured())
            {
                return;
            }
            
            Color targetColor = launchpad.GetInactiveColor();
            if (statsFolderIndex >= 0 && DoesStatsFolderRequireApi(statsFolderIndex))
            {
                targetColor = launchpad.GetActiveColor();
            }
            
            UpdateUtilityButtonVisual(worldStatsWorldIdButton, targetColor);
            UpdateUtilityButtonVisual(worldStatsUrlBuilderButton, targetColor);
        }
        
        private void UpdateUtilityButtonVisual(ButtonHandler handler, Color targetColor)
        {
            if (handler == null)
            {
                return;
            }
            
            string label = handler.buttonText != null ? handler.buttonText.text : string.Empty;
            handler.UpdateVisual(label, targetColor, true);
        }
        
        private static void ResizeArray<T>(ref T[] array, int length)
        {
            int targetLength = Mathf.Max(0, length);
            if (array != null && array.Length == targetLength)
            {
                return;
            }
            
            T[] resized = new T[targetLength];
            if (array != null && array.Length > 0 && targetLength > 0)
            {
                Array.Copy(array, resized, Mathf.Min(targetLength, array.Length));
            }
            
            array = resized;
        }
    }
}
