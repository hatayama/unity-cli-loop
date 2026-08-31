namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal static class PatcherMethodEntryCountFixture
    {
        public static int ReturnBeforeArmedLine(bool shouldReturn)
        {
            if (shouldReturn)
            {
                return -1;
            }

            int result = 1;
            return result;
        }

        public static int CountDownBeforeArmedLine(int value)
        {
            do
            {
                value--;
            }
            while (value > 0);

            if (value < 0)
            {
                return value;
            }

            return value;
        }

        public static int AddWithTwoArmedLines(int value)
        {
            int doubled = value * 2;
            int result = doubled + 1;
            return result;
        }
    }
}
