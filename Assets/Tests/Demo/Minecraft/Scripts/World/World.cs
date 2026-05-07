using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class World : MonoBehaviour
    {
        [SerializeField] private BlockRegistry blockRegistry;
        [SerializeField] private Material blockMaterial;

        private Dictionary<Vector2Int, ChunkRenderer> chunks;

        private void Start()
        {
            Debug.Assert(blockRegistry != null, "blockRegistry must be assigned in Inspector");
            Debug.Assert(blockMaterial != null, "blockMaterial must be assigned in Inspector");

            blockRegistry.Initialize();
            chunks = new Dictionary<Vector2Int, ChunkRenderer>();

            ChunkRenderer[] existingChunks = GetComponentsInChildren<ChunkRenderer>();
            if (existingChunks.Length > 0)
            {
                RestoreFromPrebaked(existingChunks);
            }
            else
            {
                GenerateWorld();
            }
        }

        private void RestoreFromPrebaked(ChunkRenderer[] existingChunks)
        {
            for (int i = 0; i < existingChunks.Length; i++)
            {
                ChunkRenderer renderer = existingChunks[i];
                Vector2Int chunkPos = renderer.ChunkPosition;

                ChunkData data = new(chunkPos);
                TerrainGenerator.GenerateChunk(data);

                chunks[chunkPos] = renderer;
                renderer.InitializeFromScene(data, blockRegistry, GetBlock);
            }
        }

        public BlockType GetBlock(int worldX, int worldY, int worldZ)
        {
            if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
            {
                return BlockType.Air;
            }

            Vector2Int chunkPos = WorldToChunkPosition(worldX, worldZ);
            if (!chunks.TryGetValue(chunkPos, out ChunkRenderer chunk))
            {
                return BlockType.Air;
            }

            int localX = worldX - chunkPos.x * WorldConstants.ChunkSizeX;
            int localZ = worldZ - chunkPos.y * WorldConstants.ChunkSizeZ;
            return chunk.Data.GetBlock(localX, worldY, localZ);
        }

        public void SetBlock(int worldX, int worldY, int worldZ, BlockType type)
        {
            if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
            {
                return;
            }

            Vector2Int chunkPos = WorldToChunkPosition(worldX, worldZ);
            if (!chunks.TryGetValue(chunkPos, out ChunkRenderer chunk))
            {
                return;
            }

            int localX = worldX - chunkPos.x * WorldConstants.ChunkSizeX;
            int localZ = worldZ - chunkPos.y * WorldConstants.ChunkSizeZ;
            chunk.Data.SetBlock(localX, worldY, localZ, type);
            chunk.RebuildMesh();
            RebuildNeighborChunksIfNeeded(localX, localZ, chunkPos);
        }

        public static Vector2Int WorldToChunkPosition(int worldX, int worldZ)
        {
            int chunkX = Mathf.FloorToInt((float)worldX / WorldConstants.ChunkSizeX);
            int chunkZ = Mathf.FloorToInt((float)worldZ / WorldConstants.ChunkSizeZ);
            return new Vector2Int(chunkX, chunkZ);
        }

        private void GenerateWorld()
        {
            for (int cx = 0; cx < WorldConstants.WorldSizeInChunks; cx++)
            {
                for (int cz = 0; cz < WorldConstants.WorldSizeInChunks; cz++)
                {
                    Vector2Int chunkPos = new(cx, cz);
                    CreateChunk(chunkPos);
                }
            }
        }

        private void CreateChunk(Vector2Int chunkPos)
        {
            ChunkData data = new(chunkPos);
            TerrainGenerator.GenerateChunk(data);

            GameObject chunkObj = new($"Chunk_{chunkPos.x}_{chunkPos.y}");
            chunkObj.transform.parent = transform;
            chunkObj.AddComponent<MeshFilter>();
            chunkObj.AddComponent<MeshRenderer>();
            chunkObj.AddComponent<MeshCollider>();

            ChunkRenderer renderer = chunkObj.AddComponent<ChunkRenderer>();
            chunks[chunkPos] = renderer;
            renderer.Initialize(data, blockRegistry, blockMaterial, GetBlock);
        }

        private void RebuildNeighborChunksIfNeeded(int localX, int localZ, Vector2Int chunkPos)
        {
            if (localX == 0)
            {
                RebuildChunkAt(new Vector2Int(chunkPos.x - 1, chunkPos.y));
            }
            if (localX == WorldConstants.ChunkSizeX - 1)
            {
                RebuildChunkAt(new Vector2Int(chunkPos.x + 1, chunkPos.y));
            }
            if (localZ == 0)
            {
                RebuildChunkAt(new Vector2Int(chunkPos.x, chunkPos.y - 1));
            }
            if (localZ == WorldConstants.ChunkSizeZ - 1)
            {
                RebuildChunkAt(new Vector2Int(chunkPos.x, chunkPos.y + 1));
            }
        }

        private void RebuildChunkAt(Vector2Int chunkPos)
        {
            if (!chunks.TryGetValue(chunkPos, out ChunkRenderer chunk))
            {
                return;
            }

            chunk.RebuildMesh();
        }
    }
}
