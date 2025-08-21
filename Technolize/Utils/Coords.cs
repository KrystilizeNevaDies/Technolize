using System.Numerics;
using Technolize.World;
namespace Technolize.Utils;

public static class Coords {
    public static (Vector2 regionPos, Vector2 localPos) WorldToRegionCoords(Vector2 worldPos)
    {
        int regionX = (int)Math.Floor(worldPos.X / TickableWorld.RegionSize);
        int regionY = (int)Math.Floor(worldPos.Y / TickableWorld.RegionSize);

        (int localX, int localY) = WorldToLocal(worldPos);

        return (new (regionX, regionY), new (localX, localY));
    }

    private static int Mod(int value, int modulus)
    {
        return (value % modulus + modulus) % modulus;
    }

    public static (int, int) WorldToLocal(Vector2 worldPos)
    {
        int localX = Mod((int) worldPos.X, TickableWorld.RegionSize);
        int localY = Mod((int) worldPos.Y, TickableWorld.RegionSize);
        return (localX, localY);
    }
}
