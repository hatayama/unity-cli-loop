using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockRegistry", menuName = "Minecraft/Block Registry")]
    public class BlockRegistry : ScriptableObject
    {
        public BlockDefinition[] Definitions;

        private BlockDefinition[] lookupTable;

        public void Initialize()
        {
            int maxIndex = 0;
            foreach (BlockDefinition definition in Definitions)
            {
                int index = (int)definition.BlockType;
                if (index > maxIndex)
                {
                    maxIndex = index;
                }
            }

            lookupTable = new BlockDefinition[maxIndex + 1];
            foreach (BlockDefinition definition in Definitions)
            {
                lookupTable[(int)definition.BlockType] = definition;
            }
        }

        public BlockDefinition GetDefinition(BlockType type)
        {
            if (lookupTable == null)
            {
                Initialize();
            }

            int index = (int)type;
            Debug.Assert(index >= 0 && index < lookupTable.Length, $"BlockType {type} is out of lookup table range");
            Debug.Assert(lookupTable[index] != null, $"BlockDefinition for {type} is not registered");
            return lookupTable[index];
        }
    }
}
