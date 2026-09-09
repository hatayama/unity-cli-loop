using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Hashes source bytes into the lowercase hex SHA-256 the applied-source records are keyed on.
    /// </summary>
    /// <remarks>
    /// Why a type of its own: the worker computes the same digest from its own side, so the one
    /// definition of "the hash of this source" has to be nameable from both the apply pipeline and
    /// the sibling-rebind probe without either reaching into the domain.
    /// </remarks>
    internal sealed class HotReloadSourceContentHasher
    {
        internal string ComputeContentHash(byte[] bytes)
        {
            Debug.Assert(bytes != null, "bytes must not be null.");

            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(bytes);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
