using System;
using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Hides the verified source snapshot of a test-assembly file for the length of a scope, so a
    /// reload sees that file as one without a baseline.
    /// </summary>
    internal static class HotReloadVerifiedSnapshotHideScope
    {
        /// <summary>
        /// Temporarily moves a verified snapshot aside so LoadVerifiedSnapshotSource returns null.
        /// Restores the file on dispose.
        /// </summary>
        internal static IDisposable Hide(string projectRelativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload"
                + HotReloadConstants.CompiledAssemblyExtension);
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath);
            string snapshotFileName =
                HotReloadSourceSnapshotter.HashProjectRelativePath(
                    projectRelativePath.Replace('\\', '/')) + ".cs";
            string snapshotPath = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload-" + mvid,
                snapshotFileName);
            Assert.That(
                File.Exists(snapshotPath),
                Is.True,
                "Precondition: verified snapshot must exist to hide: " + snapshotPath);
            // Why LoadVerifiedSnapshotSource (not File.Exists alone): a stale/checksum-invalid
            // snapshot file already yields null, so hiding it would not change the precondition.
            Assert.That(
                HotReloadSourceBaseline.LoadVerifiedSnapshotSource(projectRelativePath, targetDllPath),
                Is.Not.Null,
                "Precondition: snapshot must be loadable before hide: " + projectRelativePath);

            string hiddenPath = snapshotPath + ".hidden-for-test";
            if (File.Exists(hiddenPath))
            {
                File.Delete(hiddenPath);
            }

            File.Move(snapshotPath, hiddenPath);
            return new RestoreOnDispose(snapshotPath, hiddenPath);
        }

        private sealed class RestoreOnDispose : IDisposable
        {
            private readonly string _snapshotPath;
            private readonly string _hiddenPath;
            private bool _disposed;

            public RestoreOnDispose(string snapshotPath, string hiddenPath)
            {
                _snapshotPath = snapshotPath;
                _hiddenPath = hiddenPath;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (File.Exists(_hiddenPath))
                {
                    if (File.Exists(_snapshotPath))
                    {
                        File.Delete(_snapshotPath);
                    }

                    File.Move(_hiddenPath, _snapshotPath);
                }
            }
        }
    }
}
