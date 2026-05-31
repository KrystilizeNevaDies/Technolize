using System.Numerics;
using Technolize.World;
namespace Technolize.Utils;

public static class Coords {

    public static void WorldToRegionCoords(Vector2 worldPos, out int regionX, out int regionY, out int localX, out int localY)
    {
        regionX = (int)Math.Floor(worldPos.X / TickableWorld.RegionSize);
        regionY = (int)Math.Floor(worldPos.Y / TickableWorld.RegionSize);
        WorldToLocal(worldPos, out localX, out localY);
    }

    private static int Mod(int value, int modulus)
    {
        return (value % modulus + modulus) % modulus;
    }

    public static void WorldToLocal(Vector2 worldPos, out int localX, out int localY)
    {
        localX = Mod((int) worldPos.X, TickableWorld.RegionSize);
        localY = Mod((int) worldPos.Y, TickableWorld.RegionSize);
    }
}
