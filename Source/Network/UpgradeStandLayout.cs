using UnityEngine;

namespace MoreStandsForShops.Network;

internal sealed class UpgradeStandLayout
{
    internal bool Enabled;
    internal string VariantId;
    internal Vector3 Position;
    internal Quaternion Rotation;
    internal string ParentPath;
    internal string[] DisabledPaths;
    internal int UpgradeSlotCount;
    internal int RerollCount;
    internal int MaxRerollCount;
    internal bool RerollBroken;
}
