using UnityEngine;
using UnityEditor;
using System.IO;

namespace io.github.hatayama.UnityCliLoop
{
    public static class BlockAtlasGenerator
    {
        private const int PixelSize = BlockConstants.TexturePixelSize;
        private const int GridSize = BlockConstants.AtlasGridSize;
        private const int AtlasSize = PixelSize * GridSize;

        private const string BasePath = "Assets/Tests/Demo/Minecraft";
        private const string TexturePath = BasePath + "/Textures/BlockAtlas.png";
        private const string MaterialPath = BasePath + "/Materials/BlockAtlas.mat";
        private const string BlockDefinitionsPath = BasePath + "/ScriptableObjects/BlockDefinitions";
        private const string BlockRegistryPath = BasePath + "/ScriptableObjects/BlockRegistry.asset";
        private const string URPLitShaderName = "Universal Render Pipeline/Lit";

        // Atlas layout (col, row):
        // [0,0] Grass Top    [1,0] Grass Side   [2,0] Dirt    [3,0] Stone
        // [0,1] Wood Top     [1,1] Wood Side    [2,1] Sand    [3,1] Brick

        [MenuItem("Minecraft/Generate All Assets")]
        public static void GenerateAllAssets()
        {
            Texture2D atlas = GenerateAtlasTexture();
            SaveTexture(atlas);
            AssetDatabase.Refresh();

            ConfigureTextureImporter();
            Material material = CreateMaterial();
            CreateBlockDefinitions();
            CreateBlockRegistry();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("All Minecraft assets generated successfully.");
        }

        private static Texture2D GenerateAtlasTexture()
        {
            Texture2D atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[AtlasSize * AtlasSize];

            // Initialize with magenta to visually detect unpainted cells during debug
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.magenta;
            }

            DrawGrassTop(pixels, 0, 3);
            DrawGrassSide(pixels, 1, 3);
            DrawDirt(pixels, 2, 3);
            DrawStone(pixels, 3, 3);
            DrawWoodTop(pixels, 0, 2);
            DrawWoodSide(pixels, 1, 2);
            DrawSand(pixels, 2, 2);
            DrawBrick(pixels, 3, 2);
            DrawWater(pixels, 0, 1);

            atlas.SetPixels(pixels);
            atlas.Apply();
            return atlas;
        }

        private static void DrawGrassTop(Color[] pixels, int col, int row)
        {
            Color baseGreen = new Color(0.36f, 0.67f, 0.24f);
            Color darkGreen = new Color(0.28f, 0.55f, 0.18f);

            FillCell(pixels, col, row, baseGreen);
            SetCellPixel(pixels, col, row, 2, 3, darkGreen);
            SetCellPixel(pixels, col, row, 5, 7, darkGreen);
            SetCellPixel(pixels, col, row, 8, 2, darkGreen);
            SetCellPixel(pixels, col, row, 11, 9, darkGreen);
            SetCellPixel(pixels, col, row, 13, 5, darkGreen);
            SetCellPixel(pixels, col, row, 4, 12, darkGreen);
            SetCellPixel(pixels, col, row, 9, 14, darkGreen);
            SetCellPixel(pixels, col, row, 14, 11, darkGreen);
            SetCellPixel(pixels, col, row, 1, 8, darkGreen);
            SetCellPixel(pixels, col, row, 7, 6, darkGreen);
        }

        private static void DrawGrassSide(Color[] pixels, int col, int row)
        {
            Color green = new Color(0.36f, 0.67f, 0.24f);
            Color dirt = new Color(0.55f, 0.36f, 0.20f);
            Color darkDirt = new Color(0.45f, 0.28f, 0.15f);

            FillCell(pixels, col, row, dirt);

            for (int x = 0; x < PixelSize; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    SetCellPixel(pixels, col, row, x, y, green);
                }
            }

            // Wavy boundary between grass and dirt
            SetCellPixel(pixels, col, row, 1, 3, green);
            SetCellPixel(pixels, col, row, 4, 3, green);
            SetCellPixel(pixels, col, row, 5, 3, green);
            SetCellPixel(pixels, col, row, 9, 3, green);
            SetCellPixel(pixels, col, row, 12, 3, green);
            SetCellPixel(pixels, col, row, 13, 3, green);

            SetCellPixel(pixels, col, row, 3, 8, darkDirt);
            SetCellPixel(pixels, col, row, 7, 10, darkDirt);
            SetCellPixel(pixels, col, row, 11, 7, darkDirt);
            SetCellPixel(pixels, col, row, 14, 12, darkDirt);
        }

        private static void DrawDirt(Color[] pixels, int col, int row)
        {
            Color dirt = new Color(0.55f, 0.36f, 0.20f);
            Color darkDirt = new Color(0.45f, 0.28f, 0.15f);
            Color lightDirt = new Color(0.62f, 0.42f, 0.25f);

            FillCell(pixels, col, row, dirt);
            SetCellPixel(pixels, col, row, 2, 2, darkDirt);
            SetCellPixel(pixels, col, row, 6, 4, lightDirt);
            SetCellPixel(pixels, col, row, 10, 1, darkDirt);
            SetCellPixel(pixels, col, row, 14, 6, lightDirt);
            SetCellPixel(pixels, col, row, 3, 9, darkDirt);
            SetCellPixel(pixels, col, row, 8, 12, lightDirt);
            SetCellPixel(pixels, col, row, 12, 10, darkDirt);
            SetCellPixel(pixels, col, row, 5, 14, lightDirt);
            SetCellPixel(pixels, col, row, 1, 6, darkDirt);
            SetCellPixel(pixels, col, row, 9, 7, lightDirt);
        }

        private static void DrawStone(Color[] pixels, int col, int row)
        {
            Color stone = new Color(0.50f, 0.50f, 0.50f);
            Color darkStone = new Color(0.38f, 0.38f, 0.38f);
            Color lightStone = new Color(0.60f, 0.60f, 0.60f);

            FillCell(pixels, col, row, stone);
            for (int x = 0; x < PixelSize; x++)
            {
                for (int y = 0; y < PixelSize; y++)
                {
                    int hash = (x * 7 + y * 13) % 5;
                    if (hash == 0)
                    {
                        SetCellPixel(pixels, col, row, x, y, darkStone);
                    }
                    else if (hash == 1)
                    {
                        SetCellPixel(pixels, col, row, x, y, lightStone);
                    }
                }
            }
        }

        private static void DrawWoodTop(Color[] pixels, int col, int row)
        {
            Color wood = new Color(0.65f, 0.50f, 0.28f);
            Color ring = new Color(0.50f, 0.38f, 0.20f);
            Color center = new Color(0.55f, 0.42f, 0.22f);

            FillCell(pixels, col, row, wood);
            for (int x = 0; x < PixelSize; x++)
            {
                for (int y = 0; y < PixelSize; y++)
                {
                    float dx = x - 7.5f;
                    float dy = y - 7.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist < 2f)
                    {
                        SetCellPixel(pixels, col, row, x, y, center);
                    }
                    else if (dist > 3f && dist < 4f)
                    {
                        SetCellPixel(pixels, col, row, x, y, ring);
                    }
                    else if (dist > 5.5f && dist < 6.5f)
                    {
                        SetCellPixel(pixels, col, row, x, y, ring);
                    }
                }
            }
        }

        private static void DrawWoodSide(Color[] pixels, int col, int row)
        {
            Color wood = new Color(0.55f, 0.38f, 0.18f);
            Color darkWood = new Color(0.45f, 0.30f, 0.14f);

            FillCell(pixels, col, row, wood);
            for (int x = 0; x < PixelSize; x++)
            {
                if (x % 3 == 0)
                {
                    for (int y = 0; y < PixelSize; y++)
                    {
                        SetCellPixel(pixels, col, row, x, y, darkWood);
                    }
                }
            }
        }

        private static void DrawSand(Color[] pixels, int col, int row)
        {
            Color sand = new Color(0.86f, 0.80f, 0.55f);
            Color darkSand = new Color(0.78f, 0.72f, 0.48f);
            Color lightSand = new Color(0.92f, 0.86f, 0.62f);

            FillCell(pixels, col, row, sand);
            for (int x = 0; x < PixelSize; x++)
            {
                for (int y = 0; y < PixelSize; y++)
                {
                    int hash = (x * 11 + y * 17) % 7;
                    if (hash == 0)
                    {
                        SetCellPixel(pixels, col, row, x, y, darkSand);
                    }
                    else if (hash == 2)
                    {
                        SetCellPixel(pixels, col, row, x, y, lightSand);
                    }
                }
            }
        }

        private static void DrawBrick(Color[] pixels, int col, int row)
        {
            Color brick = new Color(0.65f, 0.30f, 0.20f);
            Color mortar = new Color(0.75f, 0.72f, 0.68f);

            FillCell(pixels, col, row, brick);
            for (int y = 3; y < PixelSize; y += 4)
            {
                for (int x = 0; x < PixelSize; x++)
                {
                    SetCellPixel(pixels, col, row, x, y, mortar);
                }
            }
            for (int y = 0; y < PixelSize; y++)
            {
                int section = y / 4;
                int offset = (section % 2 == 0) ? 0 : 8;
                SetCellPixel(pixels, col, row, (0 + offset) % PixelSize, y, mortar);
                SetCellPixel(pixels, col, row, (8 + offset) % PixelSize, y, mortar);
            }
        }

        private static void FillCell(Color[] pixels, int col, int row, Color color)
        {
            // row=0 is atlas top row; Texture2D Y=0 is bottom, so flip
            int flippedRow = (GridSize - 1) - row;
            int baseX = col * PixelSize;
            int baseY = flippedRow * PixelSize;

            for (int x = 0; x < PixelSize; x++)
            {
                for (int y = 0; y < PixelSize; y++)
                {
                    pixels[(baseY + y) * AtlasSize + (baseX + x)] = color;
                }
            }
        }

        private static void SetCellPixel(Color[] pixels, int col, int row, int localX, int localY, Color color)
        {
            int flippedRow = (GridSize - 1) - row;
            int baseX = col * PixelSize;
            int baseY = flippedRow * PixelSize;

            // Flip localY: localY=0 is cell top → Texture2D cell top = baseY + (PixelSize - 1 - localY)
            int flippedLocalY = (PixelSize - 1) - localY;
            pixels[(baseY + flippedLocalY) * AtlasSize + (baseX + localX)] = color;
        }

        private static void DrawWater(Color[] pixels, int col, int row)
        {
            Color water = new Color(0.20f, 0.40f, 0.80f);
            Color deepWater = new Color(0.15f, 0.30f, 0.70f);
            Color lightWater = new Color(0.30f, 0.50f, 0.90f);

            FillCell(pixels, col, row, water);
            for (int x = 0; x < PixelSize; x++)
            {
                for (int y = 0; y < PixelSize; y++)
                {
                    int hash = (x * 13 + y * 7) % 6;
                    if (hash == 0)
                    {
                        SetCellPixel(pixels, col, row, x, y, deepWater);
                    }
                    else if (hash == 3)
                    {
                        SetCellPixel(pixels, col, row, x, y, lightWater);
                    }
                }
            }
        }

        private static void SaveTexture(Texture2D atlas)
        {
            string directory = Path.GetDirectoryName(TexturePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] pngData = atlas.EncodeToPNG();
            File.WriteAllBytes(TexturePath, pngData);
            Object.DestroyImmediate(atlas);
        }

        private static void ConfigureTextureImporter()
        {
            TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Debug.Assert(importer != null, "TextureImporter not found at " + TexturePath);

            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find(URPLitShaderName);
            Debug.Assert(shader != null, "URP Lit shader not found");

            Material material = new Material(shader);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Debug.Assert(texture != null, "Atlas texture not found at " + TexturePath);

            material.mainTexture = texture;
            // Zero metallic/smoothness for flat pixel art look
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0f);

            AssetDatabase.DeleteAsset(MaterialPath);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static void CreateBlockDefinitions()
        {
            CreateDefinition("Grass", BlockType.Grass, new Vector2(0, 0), new Vector2(2, 0), new Vector2(1, 0));
            CreateDefinition("Dirt", BlockType.Dirt, new Vector2(2, 0), new Vector2(2, 0), new Vector2(2, 0));
            CreateDefinition("Stone", BlockType.Stone, new Vector2(3, 0), new Vector2(3, 0), new Vector2(3, 0));
            CreateDefinition("Wood", BlockType.Wood, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1, 1));
            CreateDefinition("Sand", BlockType.Sand, new Vector2(2, 1), new Vector2(2, 1), new Vector2(2, 1));
            CreateDefinition("Brick", BlockType.Brick, new Vector2(3, 1), new Vector2(3, 1), new Vector2(3, 1));
            CreateDefinition("Water", BlockType.Water, new Vector2(0, 2), new Vector2(0, 2), new Vector2(0, 2));
        }

        private static void CreateDefinition(string displayName, BlockType type,
            Vector2 topAtlas, Vector2 bottomAtlas, Vector2 sideAtlas)
        {
            string path = $"{BlockDefinitionsPath}/{displayName}.asset";
            BlockDefinition definition = ScriptableObject.CreateInstance<BlockDefinition>();
            definition.DisplayName = displayName;
            definition.BlockType = type;
            definition.IsSolid = true;
            definition.TopFaceAtlasPosition = topAtlas;
            definition.BottomFaceAtlasPosition = bottomAtlas;
            definition.SideFaceAtlasPosition = sideAtlas;

            AssetDatabase.CreateAsset(definition, path);
        }

        private static void CreateBlockRegistry()
        {
            BlockRegistry registry = ScriptableObject.CreateInstance<BlockRegistry>();

            string[] guids = AssetDatabase.FindAssets("t:BlockDefinition", new[] { BlockDefinitionsPath });
            BlockDefinition[] definitions = new BlockDefinition[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                definitions[i] = AssetDatabase.LoadAssetAtPath<BlockDefinition>(assetPath);
            }

            registry.Definitions = definitions;
            AssetDatabase.CreateAsset(registry, BlockRegistryPath);
        }
    }
}
