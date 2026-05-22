using UnityEngine;

namespace MoreStandsForShops.Utilities;

public enum MoreStandsShelfZone
{
    Drone,
    Crystal
}

public sealed class MoreStandsShelfVolume : MonoBehaviour
{
    public MoreStandsShelfZone Zone;
    public bool Handled;
}
