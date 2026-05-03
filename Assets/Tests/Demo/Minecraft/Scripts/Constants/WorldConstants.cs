namespace io.github.hatayama.UnityCliLoop
{
    public static class WorldConstants
    {
        public const int ChunkSizeX = 16;
        public const int ChunkSizeY = 64;
        public const int ChunkSizeZ = 16;
        public const int WorldSizeInChunks = 8;
        public const int SeaLevel = 20;
        public const float TerrainNoiseScale = 0.05f;
        public const int TerrainHeightAmplitude = 12;
        public const int DirtLayerDepth = 4;

        public const float BiomeNoiseScale = 0.02f;
        public const float BiomeNoiseOffsetX = 1000f;
        public const float BiomeNoiseOffsetZ = 1000f;

        public const float MountainHeightBonus = 16;
        public const float DesertHeightPenalty = 4;
        public const int TreeTrunkHeight = 5;
        public const float TreeDensity = 0.015f;

        public const int RiverWaterLevel = 18;
        public const float RiverNoiseScale = 0.03f;
        public const float RiverWidth = 0.08f;
        public const float RiverCenterZ = 64f;

        public const int SpawnX = 64;
        public const int SpawnZ = 40;
        public const int SpawnFlatRadius = 16;
        public const int SpawnFlatHeight = 20;
    }
}
