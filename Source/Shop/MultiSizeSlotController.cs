using System.Collections.Generic;
using MoreStandsForShops.Utilities;

namespace MoreStandsForShops.Shop;

internal static class MultiSizeSlotController
{
    private static readonly HashSet<string> HandledGroups = new();

    internal static void ResetForShop()
    {
        HandledGroups.Clear();
    }

    internal static bool TrySkipHandledSlot(ItemVolume itemVolume, out bool result)
    {
        result = false;
        MoreStandsMultiSizeVolume marker = itemVolume == null ? null : itemVolume.GetComponent<MoreStandsMultiSizeVolume>();
        if (marker == null || string.IsNullOrEmpty(marker.GroupId))
            return false;

        if (HandledGroups.Contains(marker.GroupId))
        {
            result = false;
            return true;
        }

        return false;
    }

    internal static void NoteSpawnResult(ItemVolume itemVolume, bool spawned)
    {
        if (!spawned)
            return;

        MoreStandsMultiSizeVolume marker = itemVolume == null ? null : itemVolume.GetComponent<MoreStandsMultiSizeVolume>();
        if (marker == null || string.IsNullOrEmpty(marker.GroupId))
            return;

        HandledGroups.Add(marker.GroupId);

        if (Plugin.DebugLogs.Value)
            Plugin.Log.LogInfo($"[MultiSizeSlot] Group {marker.GroupId} handled by {itemVolume.itemVolume}.");
    }
}
