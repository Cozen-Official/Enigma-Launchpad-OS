
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  EXCLUSIVE GROUP HELPERS
        //  All methods use pre-baked flat peer arrays (rtEntryExclusivePeer*)
        //  populated by BuildExclusivePeerLinks at build time.
        // ------------------------------------------------------------------------

        /// <summary>
        /// Deactivates all active entries that share any exclusive group with the
        /// given entry. Does NOT change the given entry's own state.
        /// Also deactivates peer EnigmaButton instances.
        /// </summary>
        private void DeactivateExclusiveGroupPeers(int entryIdx)
        {
            if (rtEntryExclusivePeerStart == null || entryIdx >= rtEntryExclusivePeerStart.Length
                || rtEntryExclusivePeerCount == null || rtEntryExclusivePeerFlat == null)
                return;

            int start = rtEntryExclusivePeerStart[entryIdx];
            int count = rtEntryExclusivePeerCount[entryIdx];
            for (int p = start; p < start + count; p++)
            {
                if (p >= rtEntryExclusivePeerFlat.Length) break;
                int peer = rtEntryExclusivePeerFlat[p];
                if (peer != entryIdx && peer < entryStates.Length && entryStates[peer])
                {
                    entryStates[peer] = false;
                    ExecuteEntryActions(peer, false);
                }
            }

            // Deactivate peer EnigmaButton instances via the group-based button peer arrays.
            if (rtEntryExclusiveGroupCount != null && entryIdx < rtEntryExclusiveGroupCount.Length
                && rtEntryExclusiveGroupStart != null && rtEntryExclusiveGroupFlat != null
                && rtExclusiveButtonPeers != null && rtExclusiveButtonPeerGroupStart != null
                && rtExclusiveButtonPeerGroupCount != null)
            {
                int myStart = rtEntryExclusiveGroupStart[entryIdx];
                int myCount = rtEntryExclusiveGroupCount[entryIdx];
                for (int g = myStart; g < myStart + myCount; g++)
                {
                    int gid = rtEntryExclusiveGroupFlat[g];
                    if (gid < 0 || gid >= rtExclusiveButtonPeerGroupStart.Length
                        || gid >= rtExclusiveButtonPeerGroupCount.Length) continue;
                    int bStart = rtExclusiveButtonPeerGroupStart[gid];
                    int bCount = rtExclusiveButtonPeerGroupCount[gid];
                    for (int b = bStart; b < bStart + bCount; b++)
                        if (b < rtExclusiveButtonPeers.Length && rtExclusiveButtonPeers[b] != null)
                            rtExclusiveButtonPeers[b].ForceDeactivate();
                }
            }
        }

        /// <summary>Returns true if the entry has any exclusive group peers.</summary>
        private bool HasExclusiveGroup(int entryIdx)
        {
            return rtEntryExclusivePeerCount != null
                && entryIdx < rtEntryExclusivePeerCount.Length
                && rtEntryExclusivePeerCount[entryIdx] > 0;
        }

        /// <summary>
        /// Returns the pre-baked Exclusive Off peer for this entry, or -1 if none.
        /// </summary>
        private int FindExclusiveOffInGroup(int entryIdx)
        {
            if (rtEntryExclusiveOffPeer != null && entryIdx < rtEntryExclusiveOffPeer.Length)
                return rtEntryExclusiveOffPeer[entryIdx];
            return -1;
        }

        /// <summary>
        /// Called after default-on state is applied in InitializeRuntimeState.
        /// For each Exclusive Off button whose group has no other active member,
        /// activates it so the group always shows a defined initial state.
        /// </summary>
        private void ActivateExclusiveOffButtons()
        {
            if (entryStates == null || rtEntryExclusiveOff == null
                || rtEntryExclusivePeerStart == null || rtEntryExclusivePeerCount == null
                || rtEntryExclusivePeerFlat == null) return;

            for (int i = 0; i < rtEntryExclusiveOff.Length; i++)
            {
                if (!rtEntryExclusiveOff[i]) continue;
                if (i >= entryStates.Length || entryStates[i]) continue;
                if (i >= rtEntryExclusivePeerStart.Length) continue;

                int start = rtEntryExclusivePeerStart[i];
                int count = rtEntryExclusivePeerCount[i];
                bool anyActive = false;
                for (int p = start; p < start + count && !anyActive; p++)
                {
                    if (p >= rtEntryExclusivePeerFlat.Length) break;
                    int peer = rtEntryExclusivePeerFlat[p];
                    if (peer < entryStates.Length && entryStates[peer]) anyActive = true;
                }

                if (!anyActive)
                {
                    entryStates[i] = true;
                    ExecuteEntryActions(i, true);
                }
            }
        }

        /// <summary>
        /// Returns true if any peer entry sharing an exclusive group with
        /// the given entry is currently active.
        /// </summary>
        private bool SharesExclusiveGroupWithActiveEntry(int entryIdx)
        {
            if (rtEntryExclusivePeerStart == null || entryIdx >= rtEntryExclusivePeerStart.Length
                || rtEntryExclusivePeerCount == null || rtEntryExclusivePeerFlat == null)
                return false;

            int start = rtEntryExclusivePeerStart[entryIdx];
            int count = rtEntryExclusivePeerCount[entryIdx];
            for (int p = start; p < start + count; p++)
            {
                if (p >= rtEntryExclusivePeerFlat.Length) break;
                int peer = rtEntryExclusivePeerFlat[p];
                if (peer < entryStates.Length && entryStates[peer]) return true;
            }
            return false;
        }

        /// <summary>
        /// Called by EnigmaButton when it has an exclusive group and a linkedController.
        /// Deactivates all entries in this controller that share the given exclusive
        /// group tag so that standalone buttons and controller entries are mutually exclusive.
        /// </summary>
        public void DeactivateExclusiveGroup(string groupTag)
        {
            if (string.IsNullOrEmpty(groupTag) || entryStates == null) return;
            if (rtGroupTagNames == null || rtGroupTagNames.Length == 0
                || rtEntryExclusiveGroupFlat == null || rtEntryExclusiveGroupCount == null)
                return;

            // Resolve the tag name to a group ID.
            int groupId = -1;
            for (int i = 0; i < rtGroupTagNames.Length; i++)
            {
                if (rtGroupTagNames[i] == groupTag) { groupId = i; break; }
            }
            if (groupId < 0) return;

            EnsureLocalOwnership();
            for (int i = 0; i < entryStates.Length; i++)
            {
                if (!entryStates[i]) continue;
                if (i >= rtEntryExclusiveGroupCount.Length || rtEntryExclusiveGroupCount[i] == 0) continue;
                if (i >= rtEntryExclusiveGroupStart.Length) continue;

                int start = rtEntryExclusiveGroupStart[i];
                int count = rtEntryExclusiveGroupCount[i];
                for (int g = start; g < start + count; g++)
                {
                    if (rtEntryExclusiveGroupFlat[g] == groupId)
                    {
                        entryStates[i] = false;
                        ExecuteEntryActions(i, false);
                        break;
                    }
                }
            }
            // Snapshot the post-mutation state so OnDeserialization's diff reads
            // against this client's authoritative view rather than a stale _prev.
            SnapshotEntryState();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }
    }
}
