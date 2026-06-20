using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Runs migration preflight search strategies before the full target scan is needed.
    /// </summary>
    internal static class ThirdPartyToolMigrationPreflightScanner
    {
        private static readonly ThirdPartyToolMigrationPreflightSearchPipeline DefaultPipeline =
            new ThirdPartyToolMigrationPreflightSearchPipeline(
                new IThirdPartyToolMigrationPreflightSearchStrategy[]
                {
                    new ThirdPartyToolMigrationRipgrepPreflightSearchStrategy(),
                    new ThirdPartyToolMigrationManagedPreflightSearchStrategy()
                });

        internal static Task<MigrationTargetPreflightResult> FindMigrationTargetAsync(
            string projectRoot,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DefaultPipeline.FindMigrationTargetAsync(projectRoot, ct);
        }
    }

    /// <summary>
    /// Defines one migration preflight search approach, such as ripgrep or managed file IO.
    /// </summary>
    internal interface IThirdPartyToolMigrationPreflightSearchStrategy
    {
        Task<MigrationTargetPreflightResult> FindMigrationTargetAsync(
            string projectRoot,
            CancellationToken ct);
    }

    /// <summary>
    /// Treats all preflight search strategies uniformly and stops on the first decisive result.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationPreflightSearchPipeline :
        IThirdPartyToolMigrationPreflightSearchStrategy
    {
        private readonly IThirdPartyToolMigrationPreflightSearchStrategy[] _strategies;

        public ThirdPartyToolMigrationPreflightSearchPipeline(
            IThirdPartyToolMigrationPreflightSearchStrategy[] strategies)
        {
            Debug.Assert(strategies != null, "strategies must not be null");

            _strategies = strategies;
        }

        public async Task<MigrationTargetPreflightResult> FindMigrationTargetAsync(
            string projectRoot,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            foreach (IThirdPartyToolMigrationPreflightSearchStrategy strategy in _strategies)
            {
                Debug.Assert(strategy != null, "strategy must not be null");
                if (ct.IsCancellationRequested)
                {
                    return MigrationTargetPreflightResult.NoTargets;
                }

                MigrationTargetPreflightResult result = await strategy.FindMigrationTargetAsync(projectRoot, ct);
                if (result != MigrationTargetPreflightResult.NeedsFullScan)
                {
                    return result;
                }
            }

            return MigrationTargetPreflightResult.NeedsFullScan;
        }
    }

    /// <summary>
    /// Provides shared fixed-string markers used by every migration preflight search strategy.
    /// </summary>
    internal static class ThirdPartyToolMigrationPreflightMarkerSet
    {
        internal static readonly string[] DirectCSharpCandidateMarkers =
        {
            ThirdPartyToolMigrationRuleCatalog.LegacyNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentApplicationNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentDomainNamespace,
            ThirdPartyToolMigrationRuleCatalog.CurrentFirstPartyToolsNamespace,
            "McpTool",
            "CustomToolManager",
            ThirdPartyToolMigrationRuleCatalog.LegacyEditorDelayTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyTimerDelayTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyMainThreadSwitcherTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyPlayerLoopTimingTypeName,
            ThirdPartyToolMigrationRuleCatalog.LegacyEditorWindowCaptureUtilityTypeName,
            "UnityCliLoopToolRegistrar",
            "ToolInfo"
        };

        internal static readonly string[] AsmdefCandidateMarkers =
        {
            ThirdPartyToolMigrationRuleCatalog.LegacyEditorAssemblyName,
            ThirdPartyToolMigrationRuleCatalog.LegacyRuntimeAssemblyName
        };

        internal static string[] CreateAllCandidateMarkers()
        {
            List<string> markers = new List<string>();
            AddMarkers(markers, DirectCSharpCandidateMarkers);
            AddMarkers(markers, AsmdefCandidateMarkers);
            AddTypeReplacementRuleMarkers(
                markers,
                ThirdPartyToolMigrationRuleCatalog.ToolContractTypeReplacementRules);
            AddTypeReplacementRuleMarkers(
                markers,
                ThirdPartyToolMigrationRuleCatalog.DomainTypeReplacementRules);
            AddTypeReplacementRuleMarkers(
                markers,
                ThirdPartyToolMigrationRuleCatalog.ApplicationTypeReplacementRules);
            AddTypeReplacementRuleMarkers(
                markers,
                ThirdPartyToolMigrationRuleCatalog.FirstPartyScreenshotTypeReplacementRules);
            return markers.ToArray();
        }

        private static void AddMarkers(List<string> markers, string[] additions)
        {
            Debug.Assert(markers != null, "markers must not be null");
            Debug.Assert(additions != null, "additions must not be null");

            foreach (string marker in additions)
            {
                AddMarker(markers, marker);
            }
        }

        private static void AddTypeReplacementRuleMarkers(
            List<string> markers,
            ThirdPartyToolMigrationParsingRules.TypeReplacementRule[] rules)
        {
            Debug.Assert(markers != null, "markers must not be null");
            Debug.Assert(rules != null, "rules must not be null");

            foreach (ThirdPartyToolMigrationParsingRules.TypeReplacementRule rule in rules)
            {
                AddMarker(markers, rule.LegacyName);
                AddMarker(markers, rule.CurrentName);
            }
        }

        private static void AddMarker(List<string> markers, string marker)
        {
            Debug.Assert(markers != null, "markers must not be null");
            Debug.Assert(!string.IsNullOrEmpty(marker), "marker must not be null or empty");

            if (markers.Contains(marker))
            {
                return;
            }

            markers.Add(marker);
        }
    }
}
