using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pins the order of the production warm-up items.
    /// </summary>
    public sealed class HotReloadWarmUpItemsTests
    {
        /// <summary>
        /// What: the items run costliest first, with the PDB documents last because their lookup
        /// can throw an exception that stops the items after it.
        /// </summary>
        [Test]
        public void CreateProduction_ReturnsCallSitesThenReferencedMethodSetsThenPdbDocuments()
        {
            List<string> names = new List<string>();
            foreach (IHotReloadWarmUpItem item in HotReloadWarmUpItems.CreateProduction())
            {
                names.Add(item.Name);
            }

            Assert.That(names, Is.EqualTo(new[] { "call_sites", "referenced_method_sets", "pdb_documents" }));
        }
    }
}
