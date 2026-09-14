using UnityEngine;

namespace MoreStandsForShops.Utilities;

public enum MoreStandsShelfZone
{
    Drone,
    Crystal,
    Grenade,
    Health
}

public sealed class MoreStandsShelfVolume : MonoBehaviour
{
    public MoreStandsShelfZone Zone;
    public bool Handled;
}
