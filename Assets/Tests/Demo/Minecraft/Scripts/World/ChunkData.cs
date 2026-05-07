using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class ChunkData
    {
        private readonly BlockType[] blocks;
        public Vector2Int ChunkPosition { get; }

        public ChunkData(Vector2Int chunkPosition)
        {
            ChunkPosition = chunkPosition;
            blocks = new BlockType[WorldConstants.ChunkSizeX * WorldConstants.ChunkSizeY * WorldConstants.ChunkSizeZ];
        }

        public BlockType GetBlock(int x, int y, int z)
        {
            Debug.Assert(IsInBounds(x, y, z), $"Block position ({x},{y},{z}) is out of chunk bounds");
            return blocks[GetIndex(x, y, z)];
        }

        public void SetBlock(int x, int y, int z, BlockType type)
        {
            Debug.Assert(IsInBounds(x, y, z), $"Block position ({x},{y},{z}) is out of chunk bounds");
            blocks[GetIndex(x, y, z)] = type;
        }

        public bool IsInBounds(int x, int y, int z)
        {
            return x >= 0 && x < WorldConstants.ChunkSizeX
                && y >= 0 && y < WorldConstants.ChunkSizeY
                && z >= 0 && z < WorldConstants.ChunkSizeZ;
        }

        private int GetIndex(int x, int y, int z)
        {
            return x + z * WorldConstants.ChunkSizeX + y * WorldConstants.ChunkSizeX * WorldConstants.ChunkSizeZ;
        }
    }
}
