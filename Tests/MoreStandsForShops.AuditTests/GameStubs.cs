namespace UnityEngine
{
    public readonly struct Vector3
    {
        public readonly float x;
        public readonly float y;
        public readonly float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public readonly struct Quaternion
    {
        public readonly float x;
        public readonly float y;
        public readonly float z;
        public readonly float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }
    }
}

internal static class SemiFunc
{
    internal enum itemType
    {
        unknown,
        item_upgrade,
        player_upgrade,
        drone,
        power_crystal,
        orb,
        grenade,
        mine,
        melee,
        gun,
        launcher,
        tool,
        tracker,
        healthPack,
        cart,
        pocket_cart,
        vehicle
    }
}

internal sealed class Item
{
    internal SemiFunc.itemType itemType;
    internal string itemName;
    internal string name;
}
