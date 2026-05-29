namespace Technolize.World.Ticking;

public static class TickingCache {
    private const int CachedShuffledRegionIndicesCount = 1024;
    private static readonly int[][] CachedShuffledRegionIndices;

    static TickingCache() {
        CachedShuffledRegionIndices = new int[CachedShuffledRegionIndicesCount][];
        int regionIndexCount = TickableWorld.RegionSize * TickableWorld.RegionSize;
        for (int k = 0; k < CachedShuffledRegionIndicesCount; k++) {
            int[] regionIndices = new int[regionIndexCount];
            for (int i = 0; i < regionIndexCount; i++) {
                regionIndices[i] = i;
            }

            for (int i = regionIndices.Length - 1; i > 0; i--) {
                int j = Random.Shared.Next(i + 1);
                (regionIndices[i], regionIndices[j]) = (regionIndices[j], regionIndices[i]);
            }

            CachedShuffledRegionIndices[k] = regionIndices;
        }
    }

    /// <summary>
    /// Gets a randomly shuffled array of region indices for iterating over a tickable region's blocks in random order.
    /// </summary>
    public static int[] GetRandomShuffledRegion() {
        int i = Random.Shared.Next(CachedShuffledRegionIndicesCount);
        return CachedShuffledRegionIndices[i];
    }
}
