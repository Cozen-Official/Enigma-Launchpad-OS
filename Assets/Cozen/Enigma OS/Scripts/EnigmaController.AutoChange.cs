
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Cozen.EnigmaOS
{
    public partial class EnigmaController
    {
        // ------------------------------------------------------------------------
        //  AUTO-CHANGE
        // ------------------------------------------------------------------------

        public void ToggleAutoChange()
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();
            autoChangeActive = !autoChangeActive;
            _autoChangeTimer = autoChangeInterval;
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void StartAutoChangeGroup(int groupId, float interval, bool random)
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();
            autoChangeGroupActive   = true;
            autoChangeGroupId       = groupId;
            autoChangeGroupInterval = interval;
            autoChangeGroupRandom   = random;
            _autoChangeGroupTimer   = interval;
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        public void StopAutoChangeGroup()
        {
            if (!CanLocalUserInteract()) return;
            EnsureLocalOwnership();
            autoChangeGroupActive = false;
            autoChangeGroupId     = -1;
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

        /// <summary>
        /// Advances the autochange group cycle by one step: deactivates all currently-active
        /// entries in the group and activates the next one in entry-index order.  Only called
        /// by the network master from the Update loop.
        /// </summary>
        private void CycleAutoChangeGroup()
        {
            if (entryStates == null || rtEntryAutoChangeGroupId == null) return;

            int groupId = autoChangeGroupId;
            int count   = Mathf.Min(entryStates.Length, rtEntryAutoChangeGroupId.Length);

            // Find the first currently-active entry in this group.
            int currentActiveIdx = -1;
            for (int i = 0; i < count; i++)
            {
                if (rtEntryAutoChangeGroupId[i] == groupId && entryStates[i])
                {
                    currentActiveIdx = i;
                    break;
                }
            }

            int nextIdx = -1;

            if (autoChangeGroupRandom)
            {
                // Collect all group members excluding the currently-active entry.
                int candidateCount = 0;
                for (int i = 0; i < count; i++)
                    if (rtEntryAutoChangeGroupId[i] == groupId && i != currentActiveIdx)
                        candidateCount++;

                if (candidateCount == 0) return; // Only one entry in the group; nothing to do.

                int[] candidates = new int[candidateCount];
                int ci = 0;
                for (int i = 0; i < count; i++)
                    if (rtEntryAutoChangeGroupId[i] == groupId && i != currentActiveIdx)
                        candidates[ci++] = i;

                nextIdx = candidates[UnityEngine.Random.Range(0, candidateCount)];
            }
            else
            {
                // Find the next entry in the group (wrap around).
                int searchFrom = currentActiveIdx >= 0 ? currentActiveIdx + 1 : 0;
                for (int i = searchFrom; i < count; i++)
                {
                    if (rtEntryAutoChangeGroupId[i] == groupId)
                    {
                        nextIdx = i;
                        break;
                    }
                }
                // Wrap around to the beginning if not found after the current position.
                if (nextIdx < 0)
                {
                    for (int i = 0; i < searchFrom && i < count; i++)
                    {
                        if (rtEntryAutoChangeGroupId[i] == groupId)
                        {
                            nextIdx = i;
                            break;
                        }
                    }
                }
            }

            if (nextIdx < 0) return;              // No entries tagged with this group.
            if (nextIdx == currentActiveIdx) return; // Only one entry in the group; nothing to do.

            EnsureLocalOwnership();

            // Deactivate all currently-active entries in the group.
            for (int i = 0; i < count; i++)
            {
                if (rtEntryAutoChangeGroupId[i] == groupId && entryStates[i])
                {
                    entryStates[i] = false;
                    ExecuteEntryActions(i, false);
                }
            }

            // Activate the next entry.
            entryStates[nextIdx] = true;
            ExecuteEntryActions(nextIdx, true);
            ScheduleEntryExpire(nextIdx);

            // Snapshot the post-mutation state so OnDeserialization's diff reads
            // against this client's authoritative view rather than a stale _prev.
            SnapshotEntryState();
            DeferredRequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncUpdateDisplay));
        }

    }
}
