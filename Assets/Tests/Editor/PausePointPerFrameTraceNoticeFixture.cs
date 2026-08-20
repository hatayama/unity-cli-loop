namespace io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures
{
    internal sealed class PerFrameTraceNoticeFixture
    {
        private int _probe;

        public void Update()
        {
            // per-frame-trace-notice-probe-unique
            _probe = 1;
        }
    }
}
