using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Tool Settings presenter's catalog rendering, its catalog cache, and the
    /// unavailable-registry state, against the real settings view over a bare root.
    /// </summary>
    public sealed class UnityCliLoopSettingsToolSettingsPresenterTests
    {
        private VisualElement _root;
        private UnityCliLoopSettingsWindowUI _view;
        private RecordingToolSettingsPort _toolSettingsPort;
        private CountingSkillDescriptionProvider _descriptionProvider;
        private UnityCliLoopToolRegistrarService _registrarService;
        private UnityCliLoopSettingsToolSettingsPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _view = new UnityCliLoopSettingsWindowUI(_root);
            _toolSettingsPort = new RecordingToolSettingsPort();
            _toolSettingsPort.DisabledToolNames.Add("zeta-tool");
            _descriptionProvider = new CountingSkillDescriptionProvider();
            _registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                _toolSettingsPort,
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                () => new IUnityCliLoopTool[]
                {
                    new ZetaTool(),
                    new AlphaTool(),
                    new DevelopmentOnlyTool()
                });
            _presenter = new UnityCliLoopSettingsToolSettingsPresenter(
                _view,
                new ToolSettingsUseCase(_toolSettingsPort, _registrarService, _descriptionProvider));
        }

        [TearDown]
        public void TearDown()
        {
            // Unsubscribes any registry warmup the unavailable-registry test scheduled on EditorApplication.update.
            _presenter.SetViewReady(false);
            _view.Dispose();
        }

        [Test]
        public void RefreshCatalogIfNeeded_WithAWarmRegistry_ListsSortedUserFacingTools()
        {
            // Verifies the catalog groups built-in and third-party tools under their headers, sorts each
            // group by name, drops development-only tools, and carries each tool's enabled state and
            // skill description.
            _registrarService.WarmupRegistry();
            _presenter.SetViewReady(true);

            _presenter.RefreshCatalogIfNeeded(true);

            List<ToolListRowData> rows = GetRows();
            Assert.That(
                rows.Select(row => row.Label).ToArray(),
                Is.EqualTo(new[]
                {
                    "Built-in Tools",
                    UnityCliLoopConstants.SETTINGS_TOOL_NAME_PAUSE_POINT,
                    "Third Party Tools",
                    "alpha-tool",
                    "zeta-tool"
                }));
            Assert.That(rows[3].IsEnabled, Is.True);
            Assert.That(rows[4].IsEnabled, Is.False);
            Assert.That(rows[3].SkillDescription, Is.EqualTo("Alpha description"));
            Assert.That(rows[4].SkillDescription, Is.Empty);
            Assert.That(_root.Q<VisualElement>("tool-settings-info-container").style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void RefreshCatalogIfNeeded_AfterARefresh_ReusesTheCatalogUntilInvalidated()
        {
            // Verifies a clean catalog is not rebuilt on the next refresh, and an invalidation rebuilds it.
            _registrarService.WarmupRegistry();
            _presenter.SetViewReady(true);
            _presenter.RefreshCatalogIfNeeded(true);

            _presenter.RefreshCatalogIfNeeded(true);
            int callsBeforeInvalidation = _descriptionProvider.CallCount;
            _presenter.InvalidateCatalog();
            _presenter.RefreshCatalogIfNeeded(true);

            Assert.That(callsBeforeInvalidation, Is.EqualTo(1));
            Assert.That(_descriptionProvider.CallCount, Is.EqualTo(2));
        }

        [Test]
        public void RefreshCatalogIfNeeded_BeforeTheViewIsReady_SkipsTheCatalog()
        {
            // Verifies the catalog is not read while the window is still building its view.
            _registrarService.WarmupRegistry();

            _presenter.RefreshCatalogIfNeeded(true);

            Assert.That(_descriptionProvider.CallCount, Is.EqualTo(0));
            Assert.That(GetRows(), Is.Empty);
        }

        [Test]
        public void RefreshCatalogIfNeeded_WhenHidden_SkipsTheCatalog()
        {
            // Verifies a collapsed Tool Settings section never reads the catalog.
            _registrarService.WarmupRegistry();
            _presenter.SetViewReady(true);

            _presenter.RefreshCatalogIfNeeded(false);

            Assert.That(_descriptionProvider.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void RefreshCatalogIfNeeded_WithoutARegistry_ShowsUnavailableAndRetriesLater()
        {
            // Verifies a missing registry shows the unavailable status and keeps the catalog dirty, so the
            // next refresh after the registry warms up lists the tools.
            _presenter.SetViewReady(true);

            _presenter.RefreshCatalogIfNeeded(true);
            string unavailableStatus = _root.Q<Label>("tool-list-status-label").text;
            _registrarService.WarmupRegistry();
            _presenter.RefreshCatalogIfNeeded(true);

            Assert.That(unavailableStatus, Is.EqualTo("Tool registry not yet initialized. Start the server first."));
            Assert.That(GetRows().Count, Is.EqualTo(5));
        }

        [Test]
        public void HandleShowToolSettingsChanged_CollapsingMarksTheCatalogDirty()
        {
            // Verifies collapsing the section forgets the catalog, so expanding it again rebuilds the list,
            // and the foldout follows each change.
            _registrarService.WarmupRegistry();
            _presenter.SetViewReady(true);
            _presenter.RefreshCatalogIfNeeded(true);

            _presenter.HandleShowToolSettingsChanged(false);
            bool foldoutAfterCollapse = _root.Q<Foldout>("tool-settings-foldout").value;
            _presenter.HandleShowToolSettingsChanged(true);

            Assert.That(foldoutAfterCollapse, Is.False);
            Assert.That(_root.Q<Foldout>("tool-settings-foldout").value, Is.True);
            Assert.That(_descriptionProvider.CallCount, Is.EqualTo(2));
        }

        [Test]
        public void UpdateHeader_RendersTheFoldoutWithoutReadingTheCatalog()
        {
            // Verifies the header-only update sets the foldout state and leaves the catalog untouched.
            _registrarService.WarmupRegistry();
            _presenter.SetViewReady(true);

            _presenter.UpdateHeader(false);

            Assert.That(_root.Q<Foldout>("tool-settings-foldout").value, Is.False);
            Assert.That(_descriptionProvider.CallCount, Is.EqualTo(0));
        }

        private List<ToolListRowData> GetRows()
        {
            IList itemsSource = _root.Q<VisualElement>("tool-list-container").Q<ListView>().itemsSource;
            return itemsSource.Cast<ToolListRowData>().ToList();
        }

        private sealed class RecordingToolSettingsPort : IToolSettingsPort
        {
            internal HashSet<string> DisabledToolNames { get; } = new HashSet<string>();

            public bool IsToolEnabled(string toolName)
            {
                return !DisabledToolNames.Contains(toolName);
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
                throw new NotSupportedException();
            }

            public string[] GetDisabledTools()
            {
                return DisabledToolNames.ToArray();
            }

            public void InvalidateCache()
            {
            }
        }

        private sealed class CountingSkillDescriptionProvider : IToolSkillDescriptionProvider
        {
            internal int CallCount { get; private set; }

            public IReadOnlyDictionary<string, string> GetSkillDescriptionsByToolName()
            {
                CallCount++;
                return new Dictionary<string, string>
                {
                    { "alpha-tool", "Alpha description" },
                    { "zeta-tool", "   " }
                };
            }
        }

        private abstract class StubTool : IUnityCliLoopTool
        {
            public abstract string ToolName { get; }

            public ToolParameterSchema ParameterSchema => new ToolParameterSchema();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class AlphaTool : StubTool
        {
            public override string ToolName => "alpha-tool";
        }

        private sealed class ZetaTool : StubTool
        {
            public override string ToolName => "zeta-tool";
        }

        [UnityCliLoopTool(DisplayDevelopmentOnly = true)]
        private sealed class DevelopmentOnlyTool : StubTool
        {
            public override string ToolName => "development-only-tool";
        }
    }
}
