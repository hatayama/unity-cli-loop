namespace io.github.hatayama.UnityCliLoop.Tests.PackageFixture
{
    /// <summary>
    /// Hot-reload target in an embedded package whose folder name differs from its package name,
    /// so the asset path Unity reports for this file and the path of the file on disk differ.
    /// </summary>
    public class HotReloadPackageFixture
    {
        public int First()
        {
            return 1;
        }

        public int Second()
        {
            return 2;
        }

        public int Third()
        {
            return 3;
        }
    }
}
