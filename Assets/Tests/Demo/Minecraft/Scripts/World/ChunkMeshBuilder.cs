using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public static class ChunkMeshBuilder
    {
        private static readonly Vector3Int[] FaceDirections = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        private static readonly Vector3[][] FaceVertices = new Vector3[][]
        {
            // Right (+X)
            new Vector3[] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
            // Left (-X)
            new Vector3[] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
            // Up (+Y)
            new Vector3[] { new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) },
            // Down (-Y)
            new Vector3[] { new Vector3(0,0,1), new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1) },
            // Forward (+Z)
            new Vector3[] { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
            // Back (-Z)
            new Vector3[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }
        };

        private enum FaceType
        {
            Right = 0,
            Left = 1,
            Up = 2,
            Down = 3,
            Forward = 4,
            Back = 5
        }

        public static Mesh BuildMesh(ChunkData chunkData, BlockRegistry registry,
            Func<int, int, int, BlockType> getWorldBlock)
        {
            Debug.Assert(chunkData != null, "chunkData must not be null");
            Debug.Assert(registry != null, "registry must not be null");
            Debug.Assert(getWorldBlock != null, "getWorldBlock must not be null");

            List<Vector3> vertices = new List<Vector3>(4096);
            List<int> triangles = new List<int>(6144);
            List<Vector2> uvs = new List<Vector2>(4096);

            int worldOffsetX = chunkData.ChunkPosition.x * WorldConstants.ChunkSizeX;
            int worldOffsetZ = chunkData.ChunkPosition.y * WorldConstants.ChunkSizeZ;

            for (int x = 0; x < WorldConstants.ChunkSizeX; x++)
            {
                for (int y = 0; y < WorldConstants.ChunkSizeY; y++)
                {
                    for (int z = 0; z < WorldConstants.ChunkSizeZ; z++)
                    {
                        BlockType blockType = chunkData.GetBlock(x, y, z);
                        if (blockType == BlockType.Air)
                        {
                            continue;
                        }

                        BlockDefinition definition = registry.GetDefinition(blockType);

                        for (int face = 0; face < 6; face++)
                        {
                            Vector3Int dir = FaceDirections[face];
                            int nx = x + dir.x;
                            int ny = y + dir.y;
                            int nz = z + dir.z;

                            BlockType neighborType = GetNeighborBlockType(chunkData, nx, ny, nz,
                                worldOffsetX, worldOffsetZ, getWorldBlock);

                            if (IsBlockSolid(neighborType, registry))
                            {
                                continue;
                            }

                            AddFace(vertices, triangles, uvs, x, y, z, face, definition);
                        }
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static BlockType GetNeighborBlockType(ChunkData chunkData, int localX, int localY, int localZ,
            int worldOffsetX, int worldOffsetZ, Func<int, int, int, BlockType> getWorldBlock)
        {
            if (localY < 0 || localY >= WorldConstants.ChunkSizeY)
            {
                return BlockType.Air;
            }

            if (chunkData.IsInBounds(localX, localY, localZ))
            {
                return chunkData.GetBlock(localX, localY, localZ);
            }

            int worldX = worldOffsetX + localX;
            int worldZ = worldOffsetZ + localZ;
            return getWorldBlock(worldX, localY, worldZ);
        }

        private static bool IsBlockSolid(BlockType type, BlockRegistry registry)
        {
            if (type == BlockType.Air)
            {
                return false;
            }

            BlockDefinition definition = registry.GetDefinition(type);
            return definition.IsSolid;
        }

        private static void AddFace(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
            int x, int y, int z, int faceIndex, BlockDefinition definition)
        {
            int vertexStart = vertices.Count;
            Vector3 blockPos = new Vector3(x, y, z);

            Vector3[] faceVerts = FaceVertices[faceIndex];
            vertices.Add(blockPos + faceVerts[0]);
            vertices.Add(blockPos + faceVerts[1]);
            vertices.Add(blockPos + faceVerts[2]);
            vertices.Add(blockPos + faceVerts[3]);

            triangles.Add(vertexStart);
            triangles.Add(vertexStart + 1);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart);
            triangles.Add(vertexStart + 2);
            triangles.Add(vertexStart + 3);

            Vector2 atlasPos = GetAtlasPositionForFace(faceIndex, definition);
            Vector2 uvMin = definition.GetUVMin(atlasPos);
            Vector2 uvMax = definition.GetUVMax(atlasPos);

            uvs.Add(new Vector2(uvMin.x, uvMin.y));
            uvs.Add(new Vector2(uvMin.x, uvMax.y));
            uvs.Add(new Vector2(uvMax.x, uvMax.y));
            uvs.Add(new Vector2(uvMax.x, uvMin.y));
        }

        private static Vector2 GetAtlasPositionForFace(int faceIndex, BlockDefinition definition)
        {
            if (faceIndex == (int)FaceType.Up)
            {
                return definition.TopFaceAtlasPosition;
            }

            if (faceIndex == (int)FaceType.Down)
            {
                return definition.BottomFaceAtlasPosition;
            }

            return definition.SideFaceAtlasPosition;
        }
    }
}
