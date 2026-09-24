namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reaches the Editor's Play Mode configuration system so Play and Stop can follow the
    /// active non-default configuration (for example a Multiplayer Play Mode scenario).
    /// </summary>
    internal interface IPlayModeScenarioBridge
    {
        // False when the configuration system is unavailable or the default configuration is active.
        bool IsNonDefaultScenarioActive { get; }

        // True while PlayModeManager.CurrentState is Running; false when the manager is unavailable.
        bool IsScenarioRunning { get; }

        // Asset name of the active configuration; null whenever IsNonDefaultScenarioActive is false.
        string ActiveScenarioName { get; }

        // Precondition: IsNonDefaultScenarioActive. Throws InvalidOperationException when the
        // active configuration reports itself invalid, carrying Unity's reason.
        void Start();

        // Precondition: IsNonDefaultScenarioActive.
        void Stop();
    }
}
