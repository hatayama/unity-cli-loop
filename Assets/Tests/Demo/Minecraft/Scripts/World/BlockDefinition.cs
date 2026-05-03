using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    [CreateAssetMenu(fileName = "NewBlock", menuName = "Minecraft/Block Definition")]
    public class BlockDefinition : ScriptableObject
    {
        private const float CellSize = 1f / BlockConstants.AtlasGridSize;

        public BlockType BlockType;
        public string DisplayName;
        public bool IsSolid = true;

        public Vector2 TopFaceAtlasPosition;
        public Vector2 BottomFaceAtlasPosition;
        public Vector2 SideFaceAtlasPosition;

        public Vector2 GetUVMin(Vector2 atlasPosition)
        {
            return new Vector2(atlasPosition.x * CellSize, atlasPosition.y * CellSize);
        }

        public Vector2 GetUVMax(Vector2 atlasPosition)
        {
            return new Vector2((atlasPosition.x + 1f) * CellSize, (atlasPosition.y + 1f) * CellSize);
        }
    }
}
