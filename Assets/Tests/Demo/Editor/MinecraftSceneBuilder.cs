using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.Tests.Demo;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo.Editor
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public static class MinecraftSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Minecraft.unity";
        private const string InputActionsPath = "Assets/Tests/Demo/Minecraft/InputSystem_Actions.inputactions";
        private const string BlockRegistryPath = "Assets/Tests/Demo/Minecraft/ScriptableObjects/BlockRegistry.asset";
        private const string BlockAtlasMaterialPath = "Assets/Tests/Demo/Minecraft/Materials/BlockAtlas.mat";
        private const string MeshAssetDir = "Assets/Tests/Demo/Minecraft/Meshes";

        [MenuItem("Minecraft/Build Scene")]
        public static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Debug.Assert(inputActions != null, "InputActions not found at " + InputActionsPath);

            BlockRegistry blockRegistry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(BlockRegistryPath);
            Debug.Assert(blockRegistry != null, "BlockRegistry not found at " + BlockRegistryPath);

            Material blockMaterial = AssetDatabase.LoadAssetAtPath<Material>(BlockAtlasMaterialPath);
            Debug.Assert(blockMaterial != null, "BlockAtlas material not found at " + BlockAtlasMaterialPath);

            CreateDirectionalLight();
            GameObject worldObj = CreateWorld(blockRegistry, blockMaterial);
            GameObject playerObj = CreatePlayer(inputActions, worldObj);
            CreateGameCanvas(inputActions, blockRegistry, playerObj);
            CreateEventSystem();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Minecraft scene built and saved to " + ScenePath);
        }

        private static void CreateDirectionalLight()
        {
            GameObject lightObj = new("Directional Light");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.84f);
            light.intensity = 1f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject CreateWorld(BlockRegistry blockRegistry, Material blockMaterial)
        {
            GameObject worldObj = new("World");
            World world = worldObj.AddComponent<World>();

            SerializedObject so = new(world);
            so.FindProperty("blockRegistry").objectReferenceValue = blockRegistry;
            so.FindProperty("blockMaterial").objectReferenceValue = blockMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrebakeChunks(worldObj, blockRegistry, blockMaterial);

            return worldObj;
        }

        private static void PrebakeChunks(GameObject worldObj, BlockRegistry blockRegistry, Material blockMaterial)
        {
            blockRegistry.Initialize();

            if (!Directory.Exists(MeshAssetDir))
            {
                Directory.CreateDirectory(MeshAssetDir);
            }

            int totalChunks = WorldConstants.WorldSizeInChunks * WorldConstants.WorldSizeInChunks;
            ChunkData[,] allChunkData = new ChunkData[WorldConstants.WorldSizeInChunks, WorldConstants.WorldSizeInChunks];

            for (int cx = 0; cx < WorldConstants.WorldSizeInChunks; cx++)
            {
                for (int cz = 0; cz < WorldConstants.WorldSizeInChunks; cz++)
                {
                    Vector2Int chunkPos = new(cx, cz);
                    ChunkData data = new(chunkPos);
                    TerrainGenerator.GenerateChunk(data);
                    allChunkData[cx, cz] = data;
                }
            }

            // ChunkMeshBuilder needs neighbor blocks for face culling across chunk boundaries
            BlockType GetWorldBlock(ChunkData[,] allData, int worldX, int worldY, int worldZ)
            {
                if (worldY < 0 || worldY >= WorldConstants.ChunkSizeY)
                {
                    return BlockType.Air;
                }

                Vector2Int chunkPos = World.WorldToChunkPosition(worldX, worldZ);

                if (chunkPos.x < 0 || chunkPos.x >= WorldConstants.WorldSizeInChunks
                    || chunkPos.y < 0 || chunkPos.y >= WorldConstants.WorldSizeInChunks)
                {
                    return BlockType.Air;
                }

                int localX = worldX - chunkPos.x * WorldConstants.ChunkSizeX;
                int localZ = worldZ - chunkPos.y * WorldConstants.ChunkSizeZ;
                return allData[chunkPos.x, chunkPos.y].GetBlock(localX, worldY, localZ);
            }

            int progress = 0;
            for (int cx = 0; cx < WorldConstants.WorldSizeInChunks; cx++)
            {
                for (int cz = 0; cz < WorldConstants.WorldSizeInChunks; cz++)
                {
                    progress++;
                    EditorUtility.DisplayProgressBar("Prebaking Chunks",
                        $"Building mesh {progress}/{totalChunks}", (float)progress / totalChunks);

                    Vector2Int chunkPos = new(cx, cz);
                    ChunkData data = allChunkData[cx, cz];

                    Mesh mesh = ChunkMeshBuilder.BuildMesh(data, blockRegistry,
                        (wx, wy, wz) => GetWorldBlock(allChunkData, wx, wy, wz));

                    string meshPath = $"{MeshAssetDir}/Chunk_{cx}_{cz}.asset";
                    AssetDatabase.DeleteAsset(meshPath);
                    AssetDatabase.CreateAsset(mesh, meshPath);

                    GameObject chunkObj = new($"Chunk_{cx}_{cz}");
                    chunkObj.transform.parent = worldObj.transform;

                    float worldX = cx * WorldConstants.ChunkSizeX;
                    float worldZ = cz * WorldConstants.ChunkSizeZ;
                    chunkObj.transform.position = new Vector3(worldX, 0f, worldZ);

                    MeshFilter meshFilter = chunkObj.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = mesh;

                    MeshRenderer meshRenderer = chunkObj.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = blockMaterial;

                    MeshCollider meshCollider = chunkObj.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = mesh;

                    ChunkRenderer chunkRenderer = chunkObj.AddComponent<ChunkRenderer>();
                    SerializedObject chunkSo = new(chunkRenderer);
                    chunkSo.FindProperty("chunkPosition").vector2IntValue = chunkPos;
                    chunkSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            Debug.Log($"Prebaked {totalChunks} chunks to {MeshAssetDir}");
        }

        private static GameObject CreatePlayer(InputActionAsset inputActions, GameObject worldObj)
        {
            GameObject playerObj = new("Player");
            // CharacterController is a Collider; without this, downward raycasts
            // hit the player's own capsule before reaching ground blocks
            playerObj.layer = LayerMask.NameToLayer("Ignore Raycast");
            // +1: surface block top face, +0.1: small margin above ground
            playerObj.transform.position = new Vector3(
                WorldConstants.SpawnX + 0.5f,
                WorldConstants.SpawnFlatHeight + 1f + 0.1f,
                WorldConstants.SpawnZ + 0.5f);

            CharacterController cc = playerObj.AddComponent<CharacterController>();
            cc.height = PlayerConstants.PlayerHeight;
            cc.radius = PlayerConstants.PlayerRadius;
            cc.center = new Vector3(0f, PlayerConstants.PlayerHeight / 2f, 0f);

            MinecraftPlayerController controller = playerObj.AddComponent<MinecraftPlayerController>();
            WireInputAction(controller, "moveAction", inputActions, "Player/Move");
            WireInputAction(controller, "jumpAction", inputActions, "Player/Jump");
            WireInputAction(controller, "sprintAction", inputActions, "Player/Sprint");

            GameObject cameraObj = new("PlayerCamera");
            cameraObj.transform.SetParent(playerObj.transform);
            cameraObj.transform.localPosition = new Vector3(0f, PlayerConstants.PlayerHeight - 0.1f, 0f);
            cameraObj.tag = "MainCamera";

            Camera cam = cameraObj.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cameraObj.AddComponent<AudioListener>();

            MinecraftPlayerCamera playerCamera = cameraObj.AddComponent<MinecraftPlayerCamera>();
            SerializedObject camSo = new(playerCamera);
            camSo.FindProperty("playerBody").objectReferenceValue = playerObj.transform;
            camSo.ApplyModifiedPropertiesWithoutUndo();
            WireInputAction(playerCamera, "lookAction", inputActions, "Player/Look");

            BlockInteraction blockInteraction = playerObj.AddComponent<BlockInteraction>();
            SerializedObject biSo = new(blockInteraction);
            biSo.FindProperty("world").objectReferenceValue = worldObj.GetComponent<World>();
            biSo.FindProperty("playerCamera").objectReferenceValue = cam;
            biSo.ApplyModifiedPropertiesWithoutUndo();
            WireInputAction(blockInteraction, "attackAction", inputActions, "Player/Attack");
            WireInputAction(blockInteraction, "placeAction", inputActions, "Player/PlaceBlock");
            WireInputAction(blockInteraction, "scrollSlotAction", inputActions, "Player/ScrollSlot");
            WireInputAction(blockInteraction, "nextSlotAction", inputActions, "Player/Next");
            WireInputAction(blockInteraction, "prevSlotAction", inputActions, "Player/Previous");

            return playerObj;
        }

        private static void CreateGameCanvas(InputActionAsset inputActions, BlockRegistry blockRegistry, GameObject playerObj)
        {
            GameObject canvasObj = new("GameCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();

            CreateCrosshair(canvasObj);
            HotbarSlotUI[] slots = CreateHotbar(canvasObj);

            HotbarUI hotbarUI = canvasObj.GetComponentInChildren<HotbarUI>();
            SerializedObject hotbarSo = new(hotbarUI);
            hotbarSo.FindProperty("blockRegistry").objectReferenceValue = blockRegistry;
            SerializedProperty slotsProperty = hotbarSo.FindProperty("slots");
            slotsProperty.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }
            hotbarSo.ApplyModifiedPropertiesWithoutUndo();

            // Wire hotbarUI to BlockInteraction
            BlockInteraction blockInteraction = playerObj.GetComponent<BlockInteraction>();
            SerializedObject biSo = new(blockInteraction);
            biSo.FindProperty("hotbarUI").objectReferenceValue = hotbarUI;
            biSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateCrosshair(GameObject canvasObj)
        {
            GameObject crosshairObj = new("Crosshair");
            crosshairObj.transform.SetParent(canvasObj.transform, false);

            RectTransform rt = crosshairObj.AddComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(4, 4);

            Image crosshairImage = crosshairObj.AddComponent<Image>();
            crosshairImage.color = new Color(1f, 1f, 1f, 0.8f);

            CrosshairUI crosshairUI = crosshairObj.AddComponent<CrosshairUI>();
            SerializedObject so = new(crosshairUI);
            so.FindProperty("crosshairImage").objectReferenceValue = crosshairImage;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static HotbarSlotUI[] CreateHotbar(GameObject canvasObj)
        {
            GameObject hotbarObj = new("Hotbar");
            hotbarObj.transform.SetParent(canvasObj.transform, false);

            RectTransform hotbarRt = hotbarObj.AddComponent<RectTransform>();
            hotbarRt.anchorMin = new Vector2(0.5f, 0f);
            hotbarRt.anchorMax = new Vector2(0.5f, 0f);
            hotbarRt.pivot = new Vector2(0.5f, 0f);
            hotbarRt.anchoredPosition = new Vector2(0f, 20f);
            hotbarRt.sizeDelta = new Vector2(560f, 60f);

            HorizontalLayoutGroup layout = hotbarObj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            hotbarObj.AddComponent<HotbarUI>();

            HotbarSlotUI[] slots = new HotbarSlotUI[BlockConstants.HotbarSlotCount];
            for (int i = 0; i < BlockConstants.HotbarSlotCount; i++)
            {
                slots[i] = CreateSlot(hotbarObj, i);
            }

            return slots;
        }

        private static HotbarSlotUI CreateSlot(GameObject hotbarObj, int index)
        {
            GameObject slotObj = new($"Slot_{index}");
            slotObj.transform.SetParent(hotbarObj.transform, false);

            RectTransform slotRt = slotObj.AddComponent<RectTransform>();
            slotRt.sizeDelta = new Vector2(60f, 60f);

            Image selectionFrame = slotObj.AddComponent<Image>();
            selectionFrame.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            // Block color child
            GameObject colorObj = new("BlockColor");
            colorObj.transform.SetParent(slotObj.transform, false);

            RectTransform colorRt = colorObj.AddComponent<RectTransform>();
            colorRt.anchorMin = new Vector2(0.1f, 0.1f);
            colorRt.anchorMax = new Vector2(0.9f, 0.9f);
            colorRt.offsetMin = Vector2.zero;
            colorRt.offsetMax = Vector2.zero;

            Image blockColorImage = colorObj.AddComponent<Image>();

            HotbarSlotUI slotUI = slotObj.AddComponent<HotbarSlotUI>();
            SerializedObject so = new(slotUI);
            so.FindProperty("blockColorImage").objectReferenceValue = blockColorImage;
            so.FindProperty("selectionFrame").objectReferenceValue = selectionFrame;
            so.ApplyModifiedPropertiesWithoutUndo();

            return slotUI;
        }

        private static void CreateEventSystem()
        {
            GameObject esObj = new("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void WireInputAction(Component target, string propertyName,
            InputActionAsset inputActions, string actionPath)
        {
            InputActionReference actionRef = GetActionReference(inputActions, actionPath);
            Debug.Assert(actionRef != null, $"InputAction '{actionPath}' not found in InputActions asset");

            SerializedObject so = new(target);
            so.FindProperty(propertyName).objectReferenceValue = actionRef;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static InputActionReference GetActionReference(InputActionAsset asset, string actionPath)
        {
            InputAction action = asset.FindAction(actionPath);
            if (action == null)
            {
                return null;
            }
            return InputActionReference.Create(action);
        }
    }
}
