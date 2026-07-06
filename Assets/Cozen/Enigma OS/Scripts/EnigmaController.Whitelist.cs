
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
                    _lastKnownOhGeezSyncVersion = ReadOhGeezSyncVersion();
                    // Seed the content baseline too, for the array-compare fallback
                    // used when no sync-version symbol resolves.
                    object fa = ohGeezCmonAccessControl.GetProgramVariable("fullAccessUsers");
                    _lastKnownOhGeezList = CopyStringArray(fa != null ? (string[])fa : new string[0]);
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

        // Cached name of OhGeez's sync-version program variable. The published
        // OhGeezCmon Access Control (v1.0–v1.3) declares it as "syncVersion";
        // a renamed/forked copy may expose "syncedVersion" instead. Resolved
        // once — lazily, only after fullAccessUsers is confirmed present so the
        // probe runs against a synced behaviour — by trying the upstream name
        // first, then the fork name. We then poll only the symbol that exists,
        // so stock users get zero "Could not find symbol" log spam and the
        // version-change detection (live whitelist updates) actually fires.
        //   null = not resolved yet; "" = resolved, neither symbol present.
        private string _ohGeezVersionSymbol;

        /// <summary>
        /// Reads OhGeez's current sync version, resolving (and caching) which
        /// program-variable name this behaviour actually exposes on first use.
        /// Returns 0 when no version symbol exists (version polling disabled).
        /// </summary>
        private int ReadOhGeezSyncVersion()
        {
            if (ohGeezCmonAccessControl == null) return 0;

            if (_ohGeezVersionSymbol == null)
            {
                // Upstream name first → the common case probes clean.
                object probe = ohGeezCmonAccessControl.GetProgramVariable("syncVersion");
                if (probe != null)
                {
                    _ohGeezVersionSymbol = "syncVersion";
                }
                else
                {
                    probe = ohGeezCmonAccessControl.GetProgramVariable("syncedVersion");
                    _ohGeezVersionSymbol = probe != null ? "syncedVersion" : "";
                }
            }

            if (_ohGeezVersionSymbol.Length == 0) return 0;
            object v = ohGeezCmonAccessControl.GetProgramVariable(_ohGeezVersionSymbol);
            return v != null ? (int)v : 0;
        }

        // Downstream-push gating: only mirror a source list to lower assets once
        // the source has been confirmed populated at least once. Prevents the
        // world-load race where an assigned-but-not-yet-synced source resolves to
        // an empty array and Enigma clobbers the downstream assets' real lists.
        // After the source has been non-empty once, later empties (a genuine
        // "clear everyone") are allowed through.
        private bool _sourceEverPopulated;

        // Array-compare fallback baseline for OhGeez when no sync-version symbol
        // resolves (forked/renamed asset, or a seed list that never serialized so
        // the version stayed 0). See ReadOhGeezSyncVersion / MonitorWhitelistSources.
        private string[] _lastKnownOhGeezList;

        // Last locally-evaluated Flatline admin-menu state, so the menu is only
        // touched on grant/revoke transitions (see PushWhitelistDownstream).
        // -1 = not yet evaluated, 0 = not authorized, 1 = authorized.
        private int _flatlineLocalState = -1;

        /// <summary>
        /// Builds the normalized (trim+lowercase) local authorization cache from
        /// the given source list. READ-ONLY: never pushes downstream, so it is
        /// safe to call from the authorization hot path without triggering
        /// ownership steals / serialization.
        /// </summary>
        private void BuildNormalizedCache(string[] source)
        {
            _whitelistInitialized = true;
            if (source == null || source.Length == 0)
            {
                _normalizedAuthorizedUsernames = new string[0];
                return;
            }
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
        }

        /// <summary>
        /// Rebuilds the local cache AND mirrors the resolved list to lower-priority
        /// downstream assets. Strict-priority model: the highest assigned source is
        /// authoritative; lower assets are read-only mirrors. Call when a source
        /// change is detected (NOT from the read/authorization path).
        /// </summary>
        private void RebuildNormalizedWhitelist()
        {
            string[] source = GetWhitelistSourceEntries();
            if (source != null && source.Length > 0) _sourceEverPopulated = true;
            BuildNormalizedCache(source);
            string src = ohGeezCmonAccessControl != null ? "OhGeez"
                       : proTVManagedWhitelist != null ? "ProTV"
                       : flatlineSync != null ? "Flatline" : "manual";
            Log($"RebuildNormalizedWhitelist() source={src} entries={(source != null ? source.Length : 0)}");
            PushWhitelistDownstream(source);
        }

        /// <summary>True when the local player is in the normalized cache.</summary>
        private bool IsLocalPlayerInNormalizedCache()
        {
            if (_normalizedAuthorizedUsernames == null) return false;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null || !Utilities.IsValid(lp)) return false;
            string n = NormalizeUsername(lp.displayName);
            if (string.IsNullOrEmpty(n)) return false;
            for (int i = 0; i < _normalizedAuthorizedUsernames.Length; i++)
                if (_normalizedAuthorizedUsernames[i] == n) return true;
            return false;
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
        /// Mirrors the resolved whitelist to lower-priority downstream assets.
        /// Strict-priority model: OhGeez → ProTV + Flatline; ProTV → Flatline.
        /// A downstream asset is a read-only mirror; this is the only writer.
        /// </summary>
        private void PushWhitelistDownstream(string[] usernames)
        {
            // World-load clobber guard: don't mirror a source that has never been
            // confirmed populated (an assigned-but-not-yet-synced source resolves
            // to an empty array and would otherwise wipe the downstream lists).
            if (!_sourceEverPopulated) { Log("PushWhitelistDownstream() skipped (source not yet populated)"); return; }

            // Filter out empty-string tombstones before pushing downstream.
            string[] cleaned = FilterEmptyStrings(usernames);

            bool ohGeezSource = ohGeezCmonAccessControl != null;
            bool proTVSource  = !ohGeezSource && proTVManagedWhitelist != null;
            Log($"PushWhitelistDownstream() {cleaned.Length} entries (ohGeezSource={ohGeezSource} proTVSource={proTVSource})");

            // ── Flatline (downstream mirror whenever a higher source is active) ──
            // bakedWhitelist is NOT [UdonSynced] and Flatline's admin-menu gate is
            // purely local per-client, so EVERY client mirrors its own copy — no
            // ownership/serialization. Flatline's own _CheckWhitelist only ever
            // GRANTS (SetActive true) and never re-runs after load, so we drive
            // adMenu directly from the authoritative local decision (grant + revoke).
            //
            // adMenu is only touched on an actual grant/revoke TRANSITION, and a
            // revoke is only applied after we have granted at least once this
            // session. Hiding the menu on the first evaluation races world-load:
            // the mirrored source may not name the local host yet (e.g. OhGeez's
            // fullAccessUsers only rebuilds on serialization), and a ProTV
            // MediaControls panel nested in the menu that is inactive while the
            // TV readies permanently skips its title/end-time displays.
            if (flatlineSync != null && (ohGeezSource || proTVSource))
            {
                flatlineSync.SetProgramVariable("bakedWhitelist", cleaned);
                GameObject adMenu = (GameObject)flatlineSync.GetProgramVariable("adMenu");
                // Full authorization check (implicit grants included) rather than
                // bare list membership: an instance-owner or otherwise implicitly
                // authorized host must not lose the admin menu just because the
                // mirrored list doesn't name them.
                bool flatlineLocalIn = IsPlayerWhitelisted(Networking.LocalPlayer);
                int newState = flatlineLocalIn ? 1 : 0;
                if (adMenu != null && newState != _flatlineLocalState)
                {
                    if (flatlineLocalIn)
                    {
                        bool wasInactive = !adMenu.activeSelf;
                        adMenu.SetActive(true);
                        // A ProTV panel inside the menu that was inactive while
                        // the TV readied has skipped its title/lock repaints;
                        // once its Start() has run (next frame), ask ProTV to
                        // rebroadcast auth state so it catches up.
                        if (wasInactive && GetProTVManager() != null)
                            SendCustomEventDelayedFrames(nameof(_PokeProTVAuthRefresh), 2);
                        Log($"  Flatline mirror: {cleaned.Length} entries, adMenu granted");
                    }
                    else if (_flatlineLocalState == 1)
                    {
                        adMenu.SetActive(false);
                        Log($"  Flatline mirror: {cleaned.Length} entries, adMenu revoked");
                    }
                    else
                    {
                        Log($"  Flatline mirror: {cleaned.Length} entries, adMenu untouched (never granted this session)");
                    }
                    _flatlineLocalState = newState;
                }
                else
                {
                    Log($"  Flatline mirror: {cleaned.Length} entries, local adMenu unchanged (in={flatlineLocalIn})");
                }
            }

            // ── ProTV (downstream mirror only when OhGeez is the source) ──
            // authorizedList IS [UdonSynced], and ProTV's TVManagedWhitelist only
            // grants Unity ownership of that object to ProTV "super" users (its
            // OnOwnershipRequest → tv._IsSuperAuthorized(requestingPlayer)). So the
            // push must run on a client ProTV will actually accept the write from —
            // NOT on whoever owns the Enigma controller, who may not be a ProTV
            // super-user (in which case the SetOwner is silently denied and the list
            // never propagates). We therefore gate on a local mirror of
            // tv._IsSuperAuthorized(localPlayer). Every present super-user runs this;
            // because the payload is identical (a mirror of the OhGeez source) the
            // last-writer-wins race is benign, and non-super clients skip it entirely
            // (no wasted ownership requests / serialization thrash).
            //
            // Readiness guard: ProTV's serialization callbacks (OnPostSerialization
            // → updateUI → tv._IsSuperAuthorized) dereference its TVManager. If the
            // ManagedWhitelist isn't wired to a TV, requesting serialization would
            // throw inside ProTV's own UI and halt it. Only push once it's wired.
            if (ohGeezSource && proTVManagedWhitelist != null)
            {
                bool tvWired = proTVManagedWhitelist.GetProgramVariable("tv") != null;
                bool localSuper = tvWired && IsLocalPlayerProTVSuperUser();
                if (localSuper)
                {
                    if (!Networking.IsOwner(proTVManagedWhitelist.gameObject))
                        Networking.SetOwner(Networking.LocalPlayer, proTVManagedWhitelist.gameObject);
                    proTVManagedWhitelist.SetProgramVariable("authorizedList", cleaned);
                    proTVManagedWhitelist.RequestSerialization();
                    Log($"  ProTV push: {cleaned.Length} entries (local is super-user)");
                }
                else
                {
                    Log($"  ProTV push skipped (tvWired={tvWired} localSuper={localSuper})");
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
                    // ReadOhGeezSyncVersion() resolves the version symbol lazily on
                    // first call, so _ohGeezVersionSymbol is set after this.
                    int version = ReadOhGeezSyncVersion();
                    bool changed;
                    if (_ohGeezVersionSymbol != null && _ohGeezVersionSymbol.Length > 0)
                    {
                        changed = version != _lastKnownOhGeezSyncVersion;
                        if (changed) _lastKnownOhGeezSyncVersion = version;
                    }
                    else
                    {
                        // No sync-version symbol on this (forked/renamed) OhGeez, or
                        // a seed that never serialized — fall back to comparing the
                        // fullAccessUsers content so live edits are still detected.
                        object obj = ohGeezCmonAccessControl.GetProgramVariable("fullAccessUsers");
                        string[] cur = obj != null ? (string[])obj : new string[0];
                        changed = !StringArraysEqual(cur, _lastKnownOhGeezList);
                        if (changed) _lastKnownOhGeezList = CopyStringArray(cur);
                    }
                    if (changed)
                    {
                        Log($"MonitorWhitelistSources() OhGeez change detected (version={version})");
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
                        Log("MonitorWhitelistSources() ProTV authorizedList change detected");
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
                        Log("MonitorWhitelistSources() Flatline bakedWhitelist change detected");
                        RebuildNormalizedWhitelist();
                    }
                }
            }
        }

        /// <summary>
        /// Forces an immediate re-read of the active whitelist source and re-mirror
        /// to the downstream assets, bypassing the poll interval. Public so it can
        /// be invoked as a manual "resync now" (e.g. wired to a button) and as a
        /// deterministic hook for the whitelist sync test harness. Honors the same
        /// gating as the poll path (does nothing when the whitelist is disabled or
        /// the source has never been populated).
        /// </summary>
        public void _RefreshWhitelist()
        {
            if (!whitelistEnabled) return;
            RebuildNormalizedWhitelist();
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
            if (player == null || !Utilities.IsValid(player)) return false;
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
            // READ-ONLY rebuild: never push downstream from the authorization path
            // (that previously caused ownership steals + serialization on a mere
            // access check, e.g. during hand-collider init before the whitelist
            // was initialized).
            if (!_whitelistInitialized) BuildNormalizedCache(GetWhitelistSourceEntries());
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
        /// firstMaster, instanceOwnerIsSuper, and super-user membership) that go
        /// beyond the explicit username list.
        /// </summary>
        private bool IsPlayerProTVImplicitlyAuthorized(VRCPlayerApi player)
        {
            if (player == null || !Utilities.IsValid(player) || proTVManagedWhitelist == null) return false;

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
                // Exact (ordinal) match — ProTV stores/compares firstMaster
                // case-sensitively, so normalizing here would over-grant.
                if (!string.IsNullOrEmpty(firstMaster)
                    && player.displayName == firstMaster)
                    return true;
            }

            // Implicit: instance owner super flag
            object instanceOwnerIsSuperObj = tvManager.GetProgramVariable("instanceOwnerIsSuper");
            bool instanceOwnerIsSuper = instanceOwnerIsSuperObj != null && (bool)instanceOwnerIsSuperObj;
            if (instanceOwnerIsSuper && player.isInstanceOwner) return true;

            // Explicit super user: ProTV's own _IsAuthorized ends with
            // `authPlugin._IsSuperUser(user)`, so super users ARE authorized to
            // control the TV. A world configured with only Super Users and an
            // empty "Default Authorized Users" therefore has an empty synced
            // authorizedList; without this check Enigma would authorize nobody but
            // the instance master ("only locks to master").
            if (IsPlayerInProTVSuperhash(player)) return true;

            return false;
        }

        /// <summary>
        /// True when <paramref name="player"/> is in ProTV's super-user set —
        /// a faithful mirror of TVManagedWhitelist._IsSuperUser
        /// (IndexOf(superhash, displayName.GetHashCode()) &gt; -1). superhash is
        /// baked once in the whitelist's Start() from the configured Super Users
        /// and is separate from the synced authorizedList. Runs in the same Udon
        /// runtime as ProTV, so GetHashCode() matches what ProTV computed.
        /// </summary>
        private bool IsPlayerInProTVSuperhash(VRCPlayerApi player)
        {
            if (proTVManagedWhitelist == null || player == null || !Utilities.IsValid(player))
                return false;
            object superhashObj = proTVManagedWhitelist.GetProgramVariable("superhash");
            if (superhashObj == null) return false;
            // U# binder crashes on `obj as int[]`; cast after the null guard.
            int[] superhash = (int[])superhashObj;
            int h = player.displayName.GetHashCode();
            for (int i = 0; i < superhash.Length; i++)
                if (superhash[i] == h) return true;
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

        /// <summary>
        /// Local mirror of ProTV's TVManager._IsSuperAuthorized(localPlayer). Gates
        /// the OhGeez→ProTV whitelist push: ProTV's TVManagedWhitelist.OnOwnershipRequest
        /// only grants Unity ownership of the synced authorizedList to a "super" user,
        /// so the push must run on such a client (rather than on whoever owns the Enigma
        /// controller). Reads ProTV's live config via program variables to avoid a hard
        /// reference to the ProTV assembly. Kept faithful to TVManager_Security.
        /// _IsSuperAuthorized — if ProTV changes that logic, update here too.
        /// </summary>
        private bool IsLocalPlayerProTVSuperUser()
        {
            if (proTVManagedWhitelist == null) return false;
            UdonSharpBehaviour tvManager = GetProTVManager();
            if (tvManager == null) return false;

            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null || !Utilities.IsValid(lp)) return false;

            // Not ready → ProTV's _IsSuperAuthorized implicitly returns false.
            object isReadyObj = tvManager.GetProgramVariable("isReady");
            if (isReadyObj != null && !(bool)isReadyObj) return false;

            // syncToOwner == false → ProTV treats everyone as super.
            object syncToOwnerObj = tvManager.GetProgramVariable("syncToOwner");
            bool syncToOwner = syncToOwnerObj == null || (bool)syncToOwnerObj;
            if (!syncToOwner) return true;

            // Implicit super: instance owner (when instanceOwnerIsSuper).
            object instanceOwnerIsSuperObj = tvManager.GetProgramVariable("instanceOwnerIsSuper");
            if (instanceOwnerIsSuperObj != null && (bool)instanceOwnerIsSuperObj && lp.isInstanceOwner)
                return true;

            // Implicit super: first master (when allowFirstMasterControl && firstMasterIsSuper).
            object allowFirstMasterObj = tvManager.GetProgramVariable("allowFirstMasterControl");
            object firstMasterIsSuperObj = tvManager.GetProgramVariable("firstMasterIsSuper");
            if (allowFirstMasterObj != null && (bool)allowFirstMasterObj
                && firstMasterIsSuperObj != null && (bool)firstMasterIsSuperObj)
            {
                object firstMasterObj = tvManager.GetProgramVariable("firstMaster");
                string firstMaster = firstMasterObj != null ? (string)firstMasterObj : null;
                if (!string.IsNullOrEmpty(firstMaster) && lp.displayName == firstMaster)
                    return true;
            }

            // Explicit super: membership in the auth plugin's superhash
            // (TVManagedWhitelist._IsSuperUser = IndexOf(superhash, name.GetHashCode())).
            return IsPlayerInProTVSuperhash(lp);
        }

        /// <summary>
        /// Deferred nudge sent after the Flatline admin menu transitions from
        /// hidden to shown: asks ProTV to rebroadcast its auth state
        /// (TVManager._Reauthorize → _TvAuthChange) so ProTV UI panels nested in
        /// the menu (e.g. MediaControls) repaint title/lock state now that their
        /// Start() has run. Public only because SendCustomEventDelayedFrames
        /// requires a public entry point.
        /// </summary>
        public void _PokeProTVAuthRefresh()
        {
            UdonSharpBehaviour tvManager = GetProTVManager();
            if (tvManager == null) return;
            tvManager.SendCustomEvent("_Reauthorize");
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
