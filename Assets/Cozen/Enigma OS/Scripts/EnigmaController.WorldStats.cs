
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  WORLD STATS — VRChat API polling for Display Stat (type 21) buttons
        // ------------------------------------------------------------------------

        private const string StatsApiPrefix = "https://api.vrchat.cloud/api/1/worlds/";

        private void InitializeWorldStats()
        {
            _statsInitialized    = false;
            _statsPendingRequest = false;
            _statsNextFetchTime  = float.PositiveInfinity;

            // Schedule periodic local display updates (e.g. for Time, Age, Players).
            if (HasAnyDisplayStatAction2() && !_statsLocalUpdateScheduled)
            {
                _statsLocalUpdateScheduled = true;
                SendCustomEventDelayedSeconds(nameof(ScheduledStatsLocalUpdate), 60f);
            }

            // Only arm API polling when we are the instance master and have an API URL.
            if (!Networking.IsOwner(gameObject)) return;
            if (!HasAnyDisplayStatApiMetric()) return;
            if (worldStatsBuiltApiUrl == null || string.IsNullOrEmpty(worldStatsBuiltApiUrl.Get())) return;

            _statsInitialized = true;
            if (worldStatsAutoStart)
            {
                float jitter = UnityEngine.Random.Range(0f, 10f);
                _statsNextFetchTime = Time.time + jitter;
            }
        }

        /// <summary>Periodic local update for time-sensitive Display Stat entries (Time, Age, Players).</summary>
        public void ScheduledStatsLocalUpdate()
        {
            // Refresh display so clock, age, player count etc. stay current.
            UpdateDisplay();
            // Re-schedule every 60 seconds as long as we have stat entries.
            if (HasAnyDisplayStatAction2())
                SendCustomEventDelayedSeconds(nameof(ScheduledStatsLocalUpdate), 60f);
            else
                _statsLocalUpdateScheduled = false;
        }

        private bool HasAnyDisplayStatAction2()
        {
            if (executor == null || executor.rtActionTypes == null) return false;
            int len = executor.rtActionTypes.Length;
            for (int a = 0; a < len; a++)
                if (executor.rtActionTypes[a] == 21) return true;
            return false;
        }

        /// <summary>Returns true when at least one type-21 action needs the VRChat API.</summary>
        private bool HasAnyDisplayStatApiMetric()
        {
            if (executor == null || executor.rtActionTypes == null || executor.rtActionStatMetrics == null) return false;
            int len = executor.rtActionTypes.Length;
            for (int a = 0; a < len; a++)
            {
                if (executor.rtActionTypes[a] != 21) continue;
                int m = a < executor.rtActionStatMetrics.Length ? executor.rtActionStatMetrics[a] : 0;
                if (StatMetricRequiresApi(m)) return true;
            }
            return false;
        }

        private static bool StatMetricRequiresApi(int metric)
        {
            // WorldStatMetric enum values that require API polling
            return metric == 0  // Visits
                || metric == 1  // Favorites
                || metric == 2  // Occupancy
                || metric == 3  // Popularity
                || metric == 4  // Heat
                || metric == 10;// Capacity
        }

        private void BeginStatsFetch()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (_statsPendingRequest) return;
            if (worldStatsBuiltApiUrl == null || string.IsNullOrEmpty(worldStatsBuiltApiUrl.Get())) return;

            _statsPendingRequest = true;
            VRCStringDownloader.LoadUrl(worldStatsBuiltApiUrl, (IUdonEventReceiver)this);

            // Schedule the next fetch after the configured interval (+10% jitter)
            float interval = Mathf.Clamp(worldStatsUpdateInterval, StatsMinInterval, StatsMaxInterval);
            float jitter = interval * UnityEngine.Random.Range(-0.1f, 0.1f);
            _statsNextFetchTime = Time.time + interval + jitter;
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (!Networking.IsOwner(gameObject)) { _statsPendingRequest = false; return; }
            _statsPendingRequest = false;
            ParseStats(result.Result);
            DeferredRequestSerialization();
            UpdateDisplay();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (!Networking.IsOwner(gameObject)) { _statsPendingRequest = false; return; }
            _statsPendingRequest = false;
            Debug.LogWarning($"[EnigmaController][WorldStats] Load error {result.ErrorCode}: {result.Error}");
            if (!worldStatsPreserveOnError)
            {
                statsVisits     = -1;
                statsFavorites  = -1;
                statsOccupants  = -1;
                statsPopularity = -1;
                statsHeat       = -1;
                statsCapacity   = -1;
                DeferredRequestSerialization();
            }
            UpdateDisplay();
        }

        private void ParseStats(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            statsOccupants  = StatsExtractInt(json, "\"occupants\":");
            statsVisits     = StatsExtractInt(json, "\"visits\":");
            statsFavorites  = StatsExtractInt(json, "\"favorites\":");
            statsPopularity = StatsExtractInt(json, "\"popularity\":");
            statsHeat       = StatsExtractInt(json, "\"heat\":");
            statsCapacity   = StatsExtractInt(json, "\"capacity\":");
        }

        private int StatsExtractInt(string json, string key)
        {
            int idx = json.IndexOf(key);
            if (idx < 0) return -1;
            idx += key.Length;
            int len = json.Length;
            while (idx < len && char.IsWhiteSpace(json[idx])) idx++;
            if (idx >= len) return -1;
            int start = idx;
            while (idx < len && json[idx] >= '0' && json[idx] <= '9') idx++;
            if (start == idx) return -1;
            string numStr = json.Substring(start, idx - start);
            int val;
            return int.TryParse(numStr, out val) ? val : -1;
        }

        /// <summary>Returns the display name for a stat metric (WorldStatMetric int value).</summary>
        public string GetStatDisplayName(int metric)
        {
            switch (metric)
            {
                case  0: return "Visits";
                case  1: return "Favorites";
                case  2: return "Occupancy";
                case  3: return "Popularity";
                case  4: return "Heat";
                case  5: return "Players";
                case  6: return "Age";
                case  7: return "Time";
                case  8: return "VR Users";
                case  9: return "Desktop";
                case 10: return "Capacity";
                case 11: return "Peak";
                case 12: return "Master";
                case 13: return "Auth'd";
                default: return "Stat";
            }
        }

        /// <summary>Returns the formatted value string for a stat metric (WorldStatMetric int value).</summary>
        public string FormatStatValue(int metric)
        {
            switch (metric)
            {
                case 0: return FormatStatNumber(statsVisits);
                case 1: return FormatStatNumber(statsFavorites);
                case 2: return FormatStatNumber(statsOccupants);
                case 3: return FormatStatNumber(statsPopularity);
                case 4: return FormatStatNumber(statsHeat);
                case 5: return FormatStatNumber(VRCPlayerApi.GetPlayerCount());
                case 6: return FormatStatAge();
                case 7: return FormatStatClock();
                case 8: return FormatStatNumber(CountVRPlayers());
                case 9: return FormatStatNumber(VRCPlayerApi.GetPlayerCount() - CountVRPlayers());
                case 10: return FormatStatNumber(statsCapacity);
                case 11: return FormatStatNumber(GetStatPeakPlayers());
                case 12: return GetStatMasterName();
                case 13: return FormatStatNumber(CountAuthPlayers());
                default: return "-";
            }
        }

        private string FormatStatNumber(int value)
        {
            if (value < 0) return "-";
            if (!worldStatsUseThousandsSeparators) return value.ToString();
            // Manual thousands separator (UdonSharp cannot use "N0" format string on all platforms)
            string s = value.ToString();
            if (s.Length <= 3) return s;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int rem = s.Length % 3;
            if (rem > 0) sb.Append(s.Substring(0, rem));
            for (int i = rem; i < s.Length; i += 3)
            {
                if (sb.Length > 0) sb.Append(",");
                sb.Append(s.Substring(i, 3));
            }
            return sb.ToString();
        }

        private string FormatStatAge()
        {
            double secs = Networking.GetServerTimeInSeconds();
            if (secs < 0.0) secs = 0.0;
            int total = (int)secs;
            int days  = total / 86400;
            int rem   = total % 86400;
            int hrs   = rem / 3600;
            rem      %= 3600;
            int mins  = rem / 60;
            if (days > 0)  return days == 1 ? "1d " + hrs.ToString("D2") + "h" : days + "d " + hrs.ToString("D2") + "h";
            if (hrs > 0)   return hrs.ToString("D2") + ":" + mins.ToString("D2");
            return "00:" + mins.ToString("D2");
        }

        private string FormatStatClock()
        {
            System.DateTime now = System.DateTime.Now;
            return now.ToString("HH:mm");
        }

        private int CountVRPlayers()
        {
            EnsureStatsPlayerBuffer();
            int n = VRCPlayerApi.GetPlayerCount();
            if (n <= 0) return 0;
            VRCPlayerApi.GetPlayers(_statsPlayerBuffer);
            int vr = 0;
            for (int i = 0; i < n && i < _statsPlayerBuffer.Length; i++)
            {
                VRCPlayerApi p = _statsPlayerBuffer[i];
                if (p != null && Utilities.IsValid(p) && p.IsUserInVR()) vr++;
            }
            return vr;
        }

        private int CountAuthPlayers()
        {
            EnsureStatsPlayerBuffer();
            int n = VRCPlayerApi.GetPlayerCount();
            if (n <= 0) return 0;
            VRCPlayerApi.GetPlayers(_statsPlayerBuffer);
            int auth = 0;
            for (int i = 0; i < n && i < _statsPlayerBuffer.Length; i++)
            {
                VRCPlayerApi p = _statsPlayerBuffer[i];
                // VRCPlayerApi has no per-player auth flag; matching Gen 2 behaviour by counting
                // all valid players (VRChat does not expose a guest/auth distinction at runtime).
                if (p != null && Utilities.IsValid(p)) auth++;
            }
            return auth;
        }

        private int GetStatPeakPlayers()
        {
            int current = VRCPlayerApi.GetPlayerCount();
            if (current > _statsPeakPlayers) _statsPeakPlayers = current;
            return _statsPeakPlayers;
        }

        private string GetStatMasterName()
        {
            VRCPlayerApi master = Networking.GetOwner(gameObject);
            return (master != null && Utilities.IsValid(master)) ? master.displayName : "-";
        }

        private void EnsureStatsPlayerBuffer()
        {
            if (_statsPlayerBuffer == null || _statsPlayerBuffer.Length < 100)
                _statsPlayerBuffer = new VRCPlayerApi[100];
        }

        /// <summary>
        /// Called by the custom editor to rebuild the API URL from the world ID.
        /// Must be called from editor-only code (not compiled into Udon).
        /// </summary>
        public void EditorBuildWorldStatsApiUrl()
        {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            string trimmed = string.IsNullOrEmpty(worldStatsWorldId) ? "" : worldStatsWorldId.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                if (!trimmed.StartsWith("wrld_", System.StringComparison.OrdinalIgnoreCase)) trimmed = "wrld_" + trimmed;
                worldStatsWorldId   = trimmed;
                worldStatsBuiltApiUrl = new VRCUrl(StatsApiPrefix + trimmed);
            }
            else
            {
                worldStatsBuiltApiUrl = new VRCUrl("");
            }
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
