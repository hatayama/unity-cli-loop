using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public static class BlockHitHelper
    {
        public static Vector3Int GetHitBlockPosition(RaycastHit hit)
        {
            Vector3 blockCenter = hit.point - hit.normal * BlockConstants.RaycastNormalOffset;
            return new Vector3Int(
                Mathf.FloorToInt(blockCenter.x),
                Mathf.FloorToInt(blockCenter.y),
                Mathf.FloorToInt(blockCenter.z));
        }

        public static Vector3Int GetAdjacentBlockPosition(RaycastHit hit)
        {
            Vector3 placeCenter = hit.point + hit.normal * BlockConstants.RaycastNormalOffset;
            return new Vector3Int(
                Mathf.FloorToInt(placeCenter.x),
                Mathf.FloorToInt(placeCenter.y),
                Mathf.FloorToInt(placeCenter.z));
        }
    }
}
