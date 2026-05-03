using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class ChunkRenderer : MonoBehaviour
    {
        [SerializeField] private Vector2Int chunkPosition;

        private ChunkData chunkData;
        private BlockRegistry blockRegistry;
        private Func<int, int, int, BlockType> getWorldBlock;
        private MeshFilter meshFilter;
        private MeshCollider meshCollider;
        private Mesh mesh;

        public ChunkData Data => chunkData;
        public Vector2Int ChunkPosition => chunkPosition;

        public void Initialize(ChunkData data, BlockRegistry registry,
            Material material, Func<int, int, int, BlockType> worldBlockGetter)
        {
            Debug.Assert(data != null, "data must not be null");
            Debug.Assert(registry != null, "registry must not be null");
            Debug.Assert(material != null, "material must not be null");
            Debug.Assert(worldBlockGetter != null, "worldBlockGetter must not be null");

            chunkPosition = data.ChunkPosition;
            SetRuntimeState(data, registry, worldBlockGetter);
            CacheComponents();
            GetComponent<MeshRenderer>().sharedMaterial = material;

            float worldX = data.ChunkPosition.x * WorldConstants.ChunkSizeX;
            float worldZ = data.ChunkPosition.y * WorldConstants.ChunkSizeZ;
            transform.position = new Vector3(worldX, 0f, worldZ);

            RebuildMesh();
        }

        // Prebaked meshes are already on MeshFilter/MeshCollider from the scene
        public void InitializeFromScene(ChunkData data, BlockRegistry registry,
            Func<int, int, int, BlockType> worldBlockGetter)
        {
            Debug.Assert(data != null, "data must not be null");
            Debug.Assert(registry != null, "registry must not be null");
            Debug.Assert(worldBlockGetter != null, "worldBlockGetter must not be null");

            SetRuntimeState(data, registry, worldBlockGetter);
            CacheComponents();
            // mesh intentionally left null: RebuildMesh would Clear() it, destroying
            // the persistent .asset. A new Mesh is created on first SetBlock instead.
        }

        private void SetRuntimeState(ChunkData data, BlockRegistry registry,
            Func<int, int, int, BlockType> worldBlockGetter)
        {
            chunkData = data;
            blockRegistry = registry;
            getWorldBlock = worldBlockGetter;
        }

        private void CacheComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
        }

        public void RebuildMesh()
        {
            Debug.Assert(chunkData != null, "ChunkRenderer not initialized");

            // meshFilter.mesh setter creates a copy each time and leaks, so use sharedMesh
            if (mesh != null)
            {
                mesh.Clear();
            }
            mesh = ChunkMeshBuilder.BuildMesh(chunkData, blockRegistry, getWorldBlock);
            meshFilter.sharedMesh = mesh;
            meshCollider.sharedMesh = mesh;
        }
    }
}
