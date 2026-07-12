// Fixture for collection JSON preview smoke verification. Line numbers are referenced by manual E2E checks.
namespace io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures
{
    using System.Collections.Generic;

    public sealed class CollectionPreviewPausePointFixture
    {
        public List<int> BuildScores()
        {
            List<int> scores = new() { 10, 20, 30 };
            return scores;
        }
    }
}
