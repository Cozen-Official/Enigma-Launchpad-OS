
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  HELPERS
        // ------------------------------------------------------------------------

        public int GetItemsPerPage()
        {
            return buttonSlots != null ? buttonSlots.Length : 0;
        }

        public int GetPageCount(int folderIndex)
        {
            int itemsPerPage = GetItemsPerPage();
            if (itemsPerPage <= 0) return 1;
            if (rtFolderEntryCount == null || folderIndex >= rtFolderEntryCount.Length) return 1;
            int entryCount = rtFolderEntryCount[folderIndex];
            return Mathf.Max(1, Mathf.CeilToInt((float)entryCount / itemsPerPage));
        }

        // ────────────────────────────────────────────────────────────────────────
        //  WHITELIST — INITIALIZATION & MONITORING
        // ────────────────────────────────────────────────────────────────────────

        private string NormalizeUsername(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Trim().ToLower();
        }

        private bool StringArraysEqual(string[] a, string[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private string[] CopyStringArray(string[] source)
        {
            if (source == null) return null;
            string[] copy = new string[source.Length];
            System.Array.Copy(source, copy, source.Length);
            return copy;
        }

        private string[] FilterEmptyStrings(string[] source)
        {
            if (source == null) return new string[0];
            int count = 0;
            for (int i = 0; i < source.Length; i++)
                if (!string.IsNullOrEmpty(source[i])) count++;
            if (count == source.Length) return source;
            string[] result = new string[count];
            int idx = 0;
            for (int i = 0; i < source.Length; i++)
                if (!string.IsNullOrEmpty(source[i])) result[idx++] = source[i];
            return result;
        }

        private void InitializeWhitelist()
        {
            if (!whitelistEnabled) return;
            _whitelistInitialized = false;
            float t = Time.time;

            // Attempt initial load; if the third-party system hasn't synced yet
            // set up retry state so MonitorWhitelistSources() will keep trying.
            if (ohGeezCmonAccessControl != null)
            {
                object obj = ohGeezCmonAccessControl.GetProgramVariable("fullAccessUsers");
                if (obj == null)
                {
                    _ohGeezWaitingForSync = true;
                    _ohGeezSyncRetryCount = 0;
                    _ohGeezNextCheckTime = t + OhGeezRetryDelay;
                }
                else
                {
                    _ohGeezNextCheckTime = t + OhGeezCheckInterval;
                    object vObj = ohGeezCmonAccessControl.GetProgramVariable("syncedVersion");
                    _lastKnownOhGeezSyncVersion = vObj != null ? (int)vObj : 0;
                }
            }
            else if (proTVManagedWhitelist != null)
            {
                object obj = proTVManagedWhitelist.GetProgramVariable("authorizedList");
                if (obj == null)
                {
                    _proTVWaitingForSync = true;
                    _proTVSyncRetryCount = 0;
                    _proTVNextCheckTime = t + ProTVRetryDelay;
                }
                else
                {
                    _lastKnownProTVAuthorizedList = CopyStringArray((string[])obj);
                    _proTVNextCheckTime = t + ProTVCheckInterval;
                }
            }
            else if (flatlineSync != null)
            {
                object obj = flatlineSync.GetProgramVariable("bakedWhitelist");
                if (obj == null)
                {
                    _flatlineWaitingForSync = true;
                    _flatlineSyncRetryCount = 0;
                    _flatlineNextCheckTime = t + FlatlineRetryDelay;
                }
                else
                {
                    _lastKnownFlatlineWhitelistList = CopyStringArray((string[])obj);
                    _flatlineNextCheckTime = t + FlatlineCheckInterval;
                }
            }

            RebuildNormalizedWhitelist();
        }

        /// <summary>
        /// Reads the highest-priority whitelist source, normalizes every username,
        /// caches the result in <see cref="_normalizedAuthorizedUsernames"/>, and
        /// pushes the list to lower-priority downstream systems.
        /// </summary>
        private void RebuildNormalizedWhitelist()
        {
            _whitelistInitialized = true;
            string[] source = GetWhitelistSourceEntries();

            if (source == null || source.Length == 0)
            {
                _normalizedAuthorizedUsernames = new string[0];
                PushWhitelistDownstream(new string[0]);
                return;
            }

            // Build a normalized copy (trim + lowercase) for fast comparison later
            int validCount = 0;
            for (int i = 0; i < source.Length; i++)
                if (!string.IsNullOrEmpty(source[i])) validCount++;

            _normalizedAuthorizedUsernames = new string[validCount];
            int idx = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (string.IsNullOrEmpty(source[i])) continue;
                _normalizedAuthorizedUsernames[idx++] = NormalizeUsername(source[i]);
            }

            PushWhitelistDownstream(source);
        }

        /// <summary>
        /// Returns the raw username array from the highest-priority integration
        /// that is assigned, falling back to the manual <see cref="authorizedUsernames"/>.
        /// </summary>
        private string[] GetWhitelistSourceEntries()
        {
            // Priority 1: OhGeezCmon
            if (ohGeezCmonAccessControl != null)
            {
                object obj = ohGeezCmonAccessControl.GetProgramVariable("fullAccessUsers");
                if (obj != null) return (string[])obj;
            }

            // Priority 2: ProTV explicit authorizedList (implicit checks are separate)
            if (proTVManagedWhitelist != null)
            {
                object obj = proTVManagedWhitelist.GetProgramVariable("authorizedList");
                if (obj != null) return (string[])obj;
            }

            // Priority 3: Flatline
            if (flatlineSync != null)
            {
                object obj = flatlineSync.GetProgramVariable("bakedWhitelist");
                if (obj != null) return (string[])obj;
            }

            // Fallback: manual list (used when no integration is assigned,
            // or all assigned integrations failed to provide data)
            return authorizedUsernames;
        }

        /// <summary>
        /// Pushes the resolved whitelist to lower-priority systems so they stay in
        /// sync. OhGeez → ProTV + Flatline, ProTV → Flatline.
        /// </summary>
        private void PushWhitelistDownstream(string[] usernames)
        {
            // Filter out empty-string tombstones before pushing to downstream systems
            string[] cleaned = FilterEmptyStrings(usernames);

            if (ohGeezCmonAccessControl != null)
            {
                // OhGeez is source — push to both ProTV and Flatline
                if (proTVManagedWhitelist != null)
                {
                    Networking.SetOwner(Networking.LocalPlayer, proTVManagedWhitelist.gameObject);
                    proTVManagedWhitelist.SetProgramVariable("authorizedList", cleaned);
                    proTVManagedWhitelist.SendCustomEvent("RequestSerialization");
                }
                if (flatlineSync != null)
                {
                    Networking.SetOwner(Networking.LocalPlayer, flatlineSync.gameObject);
                    flatlineSync.SetProgramVariable("bakedWhitelist", cleaned);
                    flatlineSync.SendCustomEvent("RequestSerialization");
                }
            }
            else if (proTVManagedWhitelist != null)
            {
                // ProTV is source — push to Flatline only
                if (flatlineSync != null)
                {
                    Networking.SetOwner(Networking.LocalPlayer, flatlineSync.gameObject);
                    flatlineSync.SetProgramVariable("bakedWhitelist", cleaned);
                    flatlineSync.SendCustomEvent("RequestSerialization");
                }
            }
        }

        /// <summary>
        /// Called every frame from Update(). Polls third-party integrations for
        /// changes at low frequency (every ~2 seconds). When a change is detected
        /// the normalized list is rebuilt and pushed downstream.
        /// Also handles initial retry logic for systems that load asynchronously.
        /// </summary>
        private void MonitorWhitelistSources()
        {
            if (!whitelistEnabled) return;
            float t = Time.time;

            // ── OhGeezCmon monitoring ──
            if (ohGeezCmonAccessControl != null)
            {
                if (_ohGeezWaitingForSync)
                {
                    if (t >= _ohGeezNextCheckTime)
                    {
                        object obj = ohGeezCmonAccessControl.GetProgramVariable("fullAccessUsers");
                        if (obj != null)
                        {
                            _ohGeezWaitingForSync = false;
                            RebuildNormalizedWhitelist();
                        }
                        else if (++_ohGeezSyncRetryCount < OhGeezMaxRetries)
                        {
                            _ohGeezNextCheckTime = t + OhGeezRetryDelay;
                        }
                        else
                        {
                            _ohGeezWaitingForSync = false;
                        }
                    }
                }
                else if (t >= _ohGeezNextCheckTime)
                {
                    _ohGeezNextCheckTime = t + OhGeezCheckInterval;
                    object versionObj = ohGeezCmonAccessControl.GetProgramVariable("syncedVersion");
                    int version = versionObj != null ? (int)versionObj : 0;
                    if (version != _lastKnownOhGeezSyncVersion)
                    {
                        _lastKnownOhGeezSyncVersion = version;
                        RebuildNormalizedWhitelist();
                    }
                }
                return; // OhGeez is authoritative; skip lower-priority monitoring
            }

            // ── ProTV monitoring ──
            if (proTVManagedWhitelist != null)
            {
                if (_proTVWaitingForSync)
                {
                    if (t >= _proTVNextCheckTime)
                    {
                        object obj = proTVManagedWhitelist.GetProgramVariable("authorizedList");
                        if (obj != null)
                        {
                            _proTVWaitingForSync = false;
                            RebuildNormalizedWhitelist();
                        }
                        else if (++_proTVSyncRetryCount < ProTVMaxRetries)
                        {
                            _proTVNextCheckTime = t + ProTVRetryDelay;
                        }
                        else
                        {
                            _proTVWaitingForSync = false;
                        }
                    }
                }
                else if (t >= _proTVNextCheckTime)
                {
                    _proTVNextCheckTime = t + ProTVCheckInterval;
                    object obj = proTVManagedWhitelist.GetProgramVariable("authorizedList");
                    string[] current = obj != null ? (string[])obj : new string[0];
                    if (!StringArraysEqual(current, _lastKnownProTVAuthorizedList))
                    {
                        _lastKnownProTVAuthorizedList = CopyStringArray(current);
                        RebuildNormalizedWhitelist();
                    }
                }
                return; // ProTV present; skip Flatline monitoring
            }

            // ── Flatline monitoring ──
            if (flatlineSync != null)
            {
                if (_flatlineWaitingForSync)
                {
                    if (t >= _flatlineNextCheckTime)
                    {
                        object obj = flatlineSync.GetProgramVariable("bakedWhitelist");
                        if (obj != null)
                        {
                            _flatlineWaitingForSync = false;
                            RebuildNormalizedWhitelist();
                        }
                        else if (++_flatlineSyncRetryCount < FlatlineMaxRetries)
                        {
                            _flatlineNextCheckTime = t + FlatlineRetryDelay;
                        }
                        else
                        {
                            _flatlineWaitingForSync = false;
                        }
                    }
                }
                else if (t >= _flatlineNextCheckTime)
                {
                    _flatlineNextCheckTime = t + FlatlineCheckInterval;
                    object obj = flatlineSync.GetProgramVariable("bakedWhitelist");
                    string[] current = obj != null ? (string[])obj : new string[0];
                    if (!StringArraysEqual(current, _lastKnownFlatlineWhitelistList))
                    {
                        _lastKnownFlatlineWhitelistList = CopyStringArray(current);
                        RebuildNormalizedWhitelist();
                    }
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  WHITELIST — AUTHORIZATION CHECKS
        // ────────────────────────────────────────────────────────────────────────

        public bool CanLocalUserInteract()
        {
            if (!whitelistEnabled) return true;
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null) return false;
            return IsPlayerWhitelisted(player);
        }

        /// <summary>
        /// Checks whether <paramref name="player"/> is authorized to interact.
        /// Usable for both the local player and remote players.
        /// </summary>
        public bool IsPlayerWhitelisted(VRCPlayerApi player)
        {
            if (!whitelistEnabled) return true;
            if (player == null) return false;
            if (instanceOwnerAlwaysHasAccess && player.isInstanceOwner) return true;

            // Developer troubleshooting authorization, comment out the line below to remove if desired.
            if (NormalizeUsername(player.displayName) == "cøzen") return true;

            // -- ProTV implicit authorization (master/firstMaster/instanceOwner) --
            // Checked before the explicit list so that implicit grants work even when
            // a higher-priority system provided the explicit usernames.
            if (proTVManagedWhitelist != null && ohGeezCmonAccessControl == null)
            {
                if (IsPlayerProTVImplicitlyAuthorized(player)) return true;
            }

            // -- Normalized cached list (covers OhGeez, ProTV explicit, Flatline, manual) --
            if (!_whitelistInitialized) RebuildNormalizedWhitelist();
            if (_normalizedAuthorizedUsernames == null || _normalizedAuthorizedUsernames.Length == 0)
                return false;

            string normalized = NormalizeUsername(player.displayName);
            if (string.IsNullOrEmpty(normalized)) return false;

            for (int i = 0; i < _normalizedAuthorizedUsernames.Length; i++)
            {
                if (_normalizedAuthorizedUsernames[i] == normalized)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks ProTV's implicit authorization rules (syncToOwner bypass, master,
        /// firstMaster, instanceOwnerIsSuper) that go beyond the explicit username list.
        /// </summary>
        private bool IsPlayerProTVImplicitlyAuthorized(VRCPlayerApi player)
        {
            if (player == null || proTVManagedWhitelist == null) return false;

            UdonSharpBehaviour tvManager = GetProTVManager();
            if (tvManager == null) return false;

            // syncToOwner == false → everyone is authorized
            object syncToOwnerObj = tvManager.GetProgramVariable("syncToOwner");
            bool syncToOwner = syncToOwnerObj == null || (bool)syncToOwnerObj;
            if (!syncToOwner) return true;

            // Implicit: master control (checked first, matching old Launchpad order)
            object allowMasterObj = tvManager.GetProgramVariable("allowMasterControl");
            bool allowMaster = allowMasterObj != null && (bool)allowMasterObj;
            if (allowMaster && player.isMaster) return true;

            // Implicit: first-master control
            object allowFirstMasterObj = tvManager.GetProgramVariable("allowFirstMasterControl");
            bool allowFirstMaster = allowFirstMasterObj != null && (bool)allowFirstMasterObj;
            if (allowFirstMaster)
            {
                object firstMasterObj = tvManager.GetProgramVariable("firstMaster");
                string firstMaster = firstMasterObj != null ? (string)firstMasterObj : null;
                if (!string.IsNullOrEmpty(firstMaster)
                    && NormalizeUsername(player.displayName) == NormalizeUsername(firstMaster))
                    return true;
            }

            // Implicit: instance owner super flag
            object instanceOwnerIsSuperObj = tvManager.GetProgramVariable("instanceOwnerIsSuper");
            bool instanceOwnerIsSuper = instanceOwnerIsSuperObj != null && (bool)instanceOwnerIsSuperObj;
            if (instanceOwnerIsSuper && player.isInstanceOwner) return true;

            return false;
        }

        /// <summary>
        /// Lazily resolves and caches the TVManager reference from ProTV's
        /// TVManagedWhitelist component.
        /// </summary>
        private UdonSharpBehaviour GetProTVManager()
        {
            if (_proTVManagerResolved) return _proTVManager;
            _proTVManagerResolved = true;
            if (proTVManagedWhitelist == null) { _proTVManager = null; return null; }

            object tvObj = proTVManagedWhitelist.GetProgramVariable("tv");
            _proTVManager = tvObj != null ? (UdonSharpBehaviour)tvObj : null;
            return _proTVManager;
        }

        private void EnsureLocalOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Log($"EnsureLocalOwnership() taking ownership from current owner");
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
        }

        // ------------------------------------------------------------------------
        //  EDITOR ACCESSOR — used by the custom editor build step
        // ------------------------------------------------------------------------

        // GetFolders/SetFolders moved to EnigmaControllerData (non-Udon companion).
        // Editor code accesses folders via: ctrl.GetComponent<EnigmaControllerData>().folders

        /// <summary>
        /// Persists the button-slot count that was in effect when the folder entry
        /// arrays were last organised.  The custom editor uses this to remap entries
        /// when the slot count changes so every entry keeps its (page, slot) position.
        /// </summary>
        [SerializeField] private int lastButtonSlotCount = 0;

        public int GetLastButtonSlotCount() => lastButtonSlotCount;
        public void SetLastButtonSlotCount(int count) { lastButtonSlotCount = count; }
    }
}
