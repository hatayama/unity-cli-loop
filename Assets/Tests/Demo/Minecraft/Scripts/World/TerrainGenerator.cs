using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public static class TerrainGenerator
    {
        private enum Biome
        {
            Desert,
            Grassland,
            Forest,
            Mountain
        }

        public static void GenerateChunk(ChunkData chunkData)
        {
            Debug.Assert(chunkData != null, "chunkData must not be null");

            int worldOffsetX = chunkData.ChunkPosition.x * WorldConstants.ChunkSizeX;
            int worldOffsetZ = chunkData.ChunkPosition.y * WorldConstants.ChunkSizeZ;

            for (int x = 0; x < WorldConstants.ChunkSizeX; x++)
            {
                for (int z = 0; z < WorldConstants.ChunkSizeZ; z++)
                {
                    int worldX = worldOffsetX + x;
                    int worldZ = worldOffsetZ + z;

                    Biome biome = SampleBiome(worldX, worldZ);
                    int surfaceHeight = CalculateSurfaceHeight(worldX, worldZ, biome);

                    FillColumn(chunkData, x, z, surfaceHeight, biome);
                    PlaceTreeIfNeeded(chunkData, x, z, surfaceHeight, biome, worldX, worldZ);
                    CarveRiver(chunkData, x, z, surfaceHeight, worldX, worldZ);
                }
            }
        }

        private static Biome SampleBiome(int worldX, int worldZ)
        {
            // Uses different offset/scale from terrain noise for global biome distribution
            float biomeNoise = Mathf.PerlinNoise(
                (worldX + WorldConstants.BiomeNoiseOffsetX) * WorldConstants.BiomeNoiseScale,
                (worldZ + WorldConstants.BiomeNoiseOffsetZ) * WorldConstants.BiomeNoiseScale
            );

            if (biomeNoise < 0.25f)
            {
                return Biome.Desert;
            }
            if (biomeNoise < 0.5f)
            {
                return Biome.Grassland;
            }
            if (biomeNoise < 0.75f)
            {
                return Biome.Forest;
            }
            return Biome.Mountain;
        }

        private static int CalculateSurfaceHeight(int worldX, int worldZ, Biome biome)
        {
            // Flatten area around spawn for easier movement
            float distFromSpawn = Mathf.Sqrt(
                (worldX - WorldConstants.SpawnX) * (worldX - WorldConstants.SpawnX) +
                (worldZ - WorldConstants.SpawnZ) * (worldZ - WorldConstants.SpawnZ)
            );
            if (distFromSpawn < WorldConstants.SpawnFlatRadius)
            {
                return WorldConstants.SpawnFlatHeight;
            }

            float baseNoise = Mathf.PerlinNoise(
                worldX * WorldConstants.TerrainNoiseScale,
                worldZ * WorldConstants.TerrainNoiseScale
            );
            float baseHeight = WorldConstants.SeaLevel + baseNoise * WorldConstants.TerrainHeightAmplitude;

            // Smooth blend between flat area and normal terrain
            float blendRadius = WorldConstants.SpawnFlatRadius + 8f;
            if (distFromSpawn < blendRadius)
            {
                float t = (distFromSpawn - WorldConstants.SpawnFlatRadius) / 8f;
                float normalHeight = CalculateNormalHeight(worldX, worldZ, biome, baseHeight);
                return Mathf.FloorToInt(Mathf.Lerp(WorldConstants.SpawnFlatHeight, normalHeight, t));
            }

            return Mathf.FloorToInt(CalculateNormalHeight(worldX, worldZ, biome, baseHeight));
        }

        private static float CalculateNormalHeight(int worldX, int worldZ, Biome biome, float baseHeight)
        {
            switch (biome)
            {
                case Biome.Desert:
                    return baseHeight - WorldConstants.DesertHeightPenalty;
                case Biome.Mountain:
                    // Second octave noise for steep mountain terrain
                    float mountainDetail = Mathf.PerlinNoise(
                        worldX * WorldConstants.TerrainNoiseScale * 2f,
                        worldZ * WorldConstants.TerrainNoiseScale * 2f
                    );
                    return baseHeight + mountainDetail * WorldConstants.MountainHeightBonus;
                default:
                    return baseHeight;
            }
        }

        private static void FillColumn(ChunkData chunkData, int x, int z, int surfaceHeight, Biome biome)
        {
            // blocks array defaults to Air(0), no need to write above surfaceHeight
            for (int y = 0; y <= surfaceHeight && y < WorldConstants.ChunkSizeY; y++)
            {
                if (y == surfaceHeight)
                {
                    chunkData.SetBlock(x, y, z, GetSurfaceBlock(biome));
                    continue;
                }

                if (y > surfaceHeight - WorldConstants.DirtLayerDepth)
                {
                    chunkData.SetBlock(x, y, z, GetSubSurfaceBlock(biome));
                    continue;
                }

                chunkData.SetBlock(x, y, z, BlockType.Stone);
            }
        }

        private static BlockType GetSurfaceBlock(Biome biome)
        {
            switch (biome)
            {
                case Biome.Desert: return BlockType.Sand;
                case Biome.Mountain: return BlockType.Stone;
                default: return BlockType.Grass;
            }
        }

        private static BlockType GetSubSurfaceBlock(Biome biome)
        {
            switch (biome)
            {
                case Biome.Desert: return BlockType.Sand;
                case Biome.Mountain: return BlockType.Stone;
                default: return BlockType.Dirt;
            }
        }

        private static void PlaceTreeIfNeeded(ChunkData chunkData, int x, int z,
            int surfaceHeight, Biome biome, int worldX, int worldZ)
        {
            if (biome != Biome.Forest)
            {
                return;
            }

            // Keep spawn area clear so the player starts in an open plain
            float distFromSpawn = Mathf.Sqrt(
                (worldX - WorldConstants.SpawnX) * (worldX - WorldConstants.SpawnX) +
                (worldZ - WorldConstants.SpawnZ) * (worldZ - WorldConstants.SpawnZ));
            if (distFromSpawn < WorldConstants.SpawnFlatRadius)
            {
                return;
            }

            // Sparse tree placement using pseudo-random Perlin noise
            float treeNoise = Mathf.PerlinNoise(worldX * 0.8f, worldZ * 0.8f);
            if (treeNoise > WorldConstants.TreeDensity * 100f)
            {
                return;
            }

            // Avoid chunk boundary to prevent leaves clipping into adjacent chunks
            if (x < 2 || x > WorldConstants.ChunkSizeX - 3 || z < 2 || z > WorldConstants.ChunkSizeZ - 3)
            {
                return;
            }

            int trunkBase = surfaceHeight + 1;
            int trunkTop = trunkBase + WorldConstants.TreeTrunkHeight;

            for (int y = trunkBase; y < trunkTop && y < WorldConstants.ChunkSizeY; y++)
            {
                chunkData.SetBlock(x, y, z, BlockType.Wood);
            }
        }

        private static void CarveRiver(ChunkData chunkData, int x, int z,
            int surfaceHeight, int worldX, int worldZ)
        {
            // Keep spawn flat area intact so the player doesn't start in water
            float distFromSpawn = Mathf.Sqrt(
                (worldX - WorldConstants.SpawnX) * (worldX - WorldConstants.SpawnX) +
                (worldZ - WorldConstants.SpawnZ) * (worldZ - WorldConstants.SpawnZ));
            if (distFromSpawn < WorldConstants.SpawnFlatRadius)
            {
                return;
            }

            float riverMeander = Mathf.PerlinNoise(worldX * WorldConstants.RiverNoiseScale, 0.5f);
            float riverCenterAtX = WorldConstants.RiverCenterZ + (riverMeander - 0.5f) * 20f;
            float distFromRiverCenter = Mathf.Abs(worldZ - riverCenterAtX);

            if (distFromRiverCenter > WorldConstants.RiverWidth * 100f)
            {
                return;
            }

            int waterLevel = WorldConstants.RiverWaterLevel;
            int riverDepth = 3;
            int riverFloor = waterLevel - riverDepth;

            for (int y = riverFloor; y <= waterLevel && y < WorldConstants.ChunkSizeY; y++)
            {
                chunkData.SetBlock(x, y, z, BlockType.Water);
            }

            // Replace terrain blocks above water level with Air to form valley
            for (int y = waterLevel + 1; y <= surfaceHeight && y < WorldConstants.ChunkSizeY; y++)
            {
                chunkData.SetBlock(x, y, z, BlockType.Air);
            }
        }
    }
}
