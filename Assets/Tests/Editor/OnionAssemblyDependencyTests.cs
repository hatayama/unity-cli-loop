using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Onion Assembly Dependency behavior.
    /// </summary>
    [TestFixture]
    public sealed class OnionAssemblyDependencyTests
    {
        private const string ApplicationAssemblyName = "UnityCLILoop.Application";
        private const string CompositionRootAssemblyName = "UnityCLILoop.CompositionRoot.Editor";
        private const string DomainAssemblyName = "UnityCLILoop.Domain";
        private const string FirstPartyToolsAssemblyName = "UnityCLILoop.FirstPartyTools.Editor";
        private const string FirstPartyToolsAssemblyNamePrefix = "UnityCLILoop.FirstPartyTools.";
        private const string ClearConsoleAssemblyName = "UnityCLILoop.FirstPartyTools.ClearConsole.Editor";
        private const string CompileAssemblyName = "UnityCLILoop.FirstPartyTools.Compile.Editor";
        private const string ControlPlayModeAssemblyName = "UnityCLILoop.FirstPartyTools.ControlPlayMode.Editor";
        private const string ExecuteDynamicCodeAssemblyName = "UnityCLILoop.FirstPartyTools.ExecuteDynamicCode.Editor";
        private const string FindGameObjectsAssemblyName = "UnityCLILoop.FirstPartyTools.FindGameObjects.Editor";
        private const string GetHierarchyAssemblyName = "UnityCLILoop.FirstPartyTools.GetHierarchy.Editor";
        private const string GetLogsAssemblyName = "UnityCLILoop.FirstPartyTools.GetLogs.Editor";
        private const string RecordInputAssemblyName = "UnityCLILoop.FirstPartyTools.RecordInput.Editor";
        private const string ReplayInputAssemblyName = "UnityCLILoop.FirstPartyTools.ReplayInput.Editor";
        private const string RunTestsAssemblyName = "UnityCLILoop.FirstPartyTools.RunTests.Editor";
        private const string ScreenshotAssemblyName = "UnityCLILoop.FirstPartyTools.Screenshot.Editor";
        private const string SimulateKeyboardAssemblyName = "UnityCLILoop.FirstPartyTools.SimulateKeyboard.Editor";
        private const string SimulateMouseInputAssemblyName = "UnityCLILoop.FirstPartyTools.SimulateMouseInput.Editor";
        private const string SimulateMouseUiAssemblyName = "UnityCLILoop.FirstPartyTools.SimulateMouseUi.Editor";
        private const string InfrastructureAssemblyName = "UnityCLILoop.Infrastructure";
        private const string MetadataValidationAssemblyName =
            "UnityCLILoop.FirstPartyTools.ExecuteDynamicCode.MetadataValidation.Editor";
        private const string PresentationAssemblyName = "UnityCLILoop.Presentation";
        private const string ToolContractsAssemblyName = "UnityCLILoop.ToolContracts";
        private const string RemovedSharedAssemblyGuidReference = "GUID:290394860909340b7835eb7cc215ee75";

        [Test]
        public void DomainAsmdef_WhenLoaded_HasNoProjectAssemblyReferences()
        {
            // Tests that the domain layer stays independent from outer project assemblies.
            string[] references = ReadResolvedReferences("Packages/src/Editor/Domain/UnityCLILoop.Domain.asmdef");

            Assert.That(references, Is.Empty);
        }

        [Test]
        public void PlatformResultTypes_WhenLoaded_CompileUnderDomainAssembly()
        {
            // Tests that extension-facing result values stay outside project implementation assemblies.
            string validationResultAssemblyName = typeof(ValidationResult).Assembly.GetName().Name;
            string serviceResultAssemblyName = typeof(ServiceResult<int>).Assembly.GetName().Name;

            Assert.That(validationResultAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
            Assert.That(serviceResultAssemblyName, Is.EqualTo(DomainAssemblyName));
        }

        [Test]
        public void ProjectRootIdentityValidator_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that project identity safety policy lives in the domain layer.
            string validatorAssemblyName = typeof(ProjectRootIdentityValidator).Assembly.GetName().Name;

            Assert.That(validatorAssemblyName, Is.EqualTo(DomainAssemblyName));
        }

        [Test]
        public void ProjectRootCanonicalizer_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that project-root identity normalization stays with the domain policy.
            string canonicalizerAssemblyName = typeof(ProjectRootCanonicalizer).Assembly.GetName().Name;

            Assert.That(canonicalizerAssemblyName, Is.EqualTo(DomainAssemblyName));
        }

        [Test]
        public void CliVersionComparer_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that CLI compatibility version ordering lives in the domain layer.
            string comparerAssemblyName = typeof(CliVersionComparer).Assembly.GetName().Name;

            Assert.That(comparerAssemblyName, Is.EqualTo(DomainAssemblyName));
        }

        [Test]
        public void CompilationDiagnosticMessageParser_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that first-party dynamic-code parsing stays inside the bundled tool assembly.
            string parserAssemblyName = typeof(CompilationDiagnosticMessageParser).Assembly.GetName().Name;

            Assert.That(parserAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void DynamicCodeConstants_WhenLoaded_CompileUnderDomainAssembly()
        {
            // Tests that dynamic-code defaults are owned by the bundled dynamic-code tool.
            string constantsAssemblyName = typeof(DynamicCodeConstants).Assembly.GetName().Name;

            Assert.That(constantsAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void ScriptChangesDuringPlayOptions_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that play-mode compilation policy values stay with the bundled compile tool.
            string optionsAssemblyName = typeof(ScriptChangesDuringPlayOptions).Assembly.GetName().Name;

            Assert.That(optionsAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void DynamicCodeSecurityValues_WhenLoaded_CompileUnderDomainAssembly()
        {
            // Tests that public dynamic-code security values stay in the tool contract assembly.
            string resultAssemblyName = typeof(SecurityValidationResult).Assembly.GetName().Name;
            string violationAssemblyName = typeof(SecurityViolation).Assembly.GetName().Name;

            Assert.That(resultAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
            Assert.That(violationAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
        }

        [Test]
        public void DangerousApiCatalog_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that public dynamic-code dangerous API policy stays in the tool contract assembly.
            string catalogAssemblyName = typeof(DangerousApiCatalog).Assembly.GetName().Name;

            Assert.That(catalogAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
        }

        [Test]
        public void SourceSecurityScanner_WhenLoaded_CompilesUnderDomainAssembly()
        {
            // Tests that public source-level dynamic-code scanning stays in the tool contract assembly.
            string scannerAssemblyName = typeof(SourceSecurityScanner).Assembly.GetName().Name;

            Assert.That(scannerAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
        }

        [Test]
        public void ToolContractsAsmdef_WhenLoaded_HasNoProjectAssemblyReferences()
        {
            // Tests that the public tool contract assembly stays independent from implementation assemblies.
            string[] references = ReadResolvedReferences("Packages/src/Editor/ToolContracts/UnityCLILoop.ToolContracts.asmdef");

            Assert.That(references, Is.Empty);
        }

        [Test]
        public void ApplicationAsmdef_WhenLoaded_ReferencesInnerContractsButNotOuterLayers()
        {
            // Tests that application code can depend inward without depending on presentation or infrastructure.
            string[] references = ReadResolvedReferences("Packages/src/Editor/Application/UnityCLILoop.Application.asmdef");

            Assert.That(references, Does.Contain(DomainAssemblyName));
            Assert.That(references, Does.Contain(ToolContractsAssemblyName));
            Assert.That(references, Does.Not.Contain(PresentationAssemblyName));
            Assert.That(references, Does.Not.Contain(InfrastructureAssemblyName));
            Assert.That(references, Does.Not.Contain(CompositionRootAssemblyName));
            Assert.That(references, Does.Not.Contain(FirstPartyToolsAssemblyName));
        }

        [Test]
        public void DynamicCompilationPorts_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that dynamic-code compilation ports are owned by the bundled dynamic-code tool.
            string serviceAssemblyName = typeof(IDynamicCompilationService).Assembly.GetName().Name;
            string factoryAssemblyName = typeof(IDynamicCompilationServiceFactory).Assembly.GetName().Name;
            string dynamicServicesAssemblyName = typeof(DynamicCodeServicesRegistry).Assembly.GetName().Name;
            string sourcePreparationAssemblyName = typeof(IDynamicCodeSourcePreparationService).Assembly.GetName().Name;
            string assemblyBuilderAssemblyName = typeof(ICompiledAssemblyBuilder).Assembly.GetName().Name;

            Assert.That(serviceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(factoryAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(dynamicServicesAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(sourcePreparationAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(assemblyBuilderAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void DynamicCompilationDtos_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that dynamic-code compilation DTOs are owned by the bundled dynamic-code tool.
            string requestAssemblyName = typeof(CompilationRequest).Assembly.GetName().Name;
            string resultAssemblyName = typeof(CompilationResult).Assembly.GetName().Name;
            string errorAssemblyName = typeof(CompilationError).Assembly.GetName().Name;
            string backendKindAssemblyName = typeof(DynamicCompilationBackendKind).Assembly.GetName().Name;
            string buildResultAssemblyName = typeof(CompiledAssemblyBuildResult).Assembly.GetName().Name;
            string loadResultAssemblyName = typeof(CompiledAssemblyLoadResult).Assembly.GetName().Name;
            string diagnosticsAssemblyName = typeof(CompilerDiagnostics).Assembly.GetName().Name;
            string planAssemblyName = typeof(DynamicCompilationPlan).Assembly.GetName().Name;
            string preparedCodeAssemblyName = typeof(PreparedDynamicCode).Assembly.GetName().Name;

            Assert.That(requestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(resultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(errorAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(backendKindAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(buildResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(loadResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(diagnosticsAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(planAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(preparedCodeAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void HierarchyTypes_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled hierarchy implementation types stay inside the first-party tool assembly.
            string serviceAssemblyName = typeof(IUnityCliLoopHierarchyService).Assembly.GetName().Name;
            string requestAssemblyName = typeof(UnityCliLoopHierarchyRequest).Assembly.GetName().Name;
            string resultAssemblyName = typeof(UnityCliLoopHierarchyResult).Assembly.GetName().Name;

            Assert.That(serviceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(requestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(resultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void GetHierarchyUseCase_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that the bundled tool owns the hierarchy implementation.
            string useCaseAssemblyName = typeof(GetHierarchyUseCase).Assembly.GetName().Name;

            Assert.That(useCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void TestExecutionTypes_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled test-runner implementation types stay inside the first-party tool assembly.
            string serviceAssemblyName = typeof(IUnityCliLoopTestExecutionService).Assembly.GetName().Name;
            string requestAssemblyName = typeof(UnityCliLoopTestExecutionRequest).Assembly.GetName().Name;
            string resultAssemblyName = typeof(UnityCliLoopTestExecutionResult).Assembly.GetName().Name;

            Assert.That(serviceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(requestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(resultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void RunTestsUseCase_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that the bundled tool owns the test execution implementation.
            string useCaseAssemblyName = typeof(RunTestsUseCase).Assembly.GetName().Name;

            Assert.That(useCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void GameObjectSearchTypes_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled GameObject search implementation types stay inside the first-party tool assembly.
            string serviceAssemblyName = typeof(IUnityCliLoopGameObjectSearchService).Assembly.GetName().Name;
            string requestAssemblyName = typeof(UnityCliLoopGameObjectSearchRequest).Assembly.GetName().Name;
            string resultAssemblyName = typeof(UnityCliLoopGameObjectSearchResult).Assembly.GetName().Name;
            string componentAssemblyName = typeof(ComponentInfo).Assembly.GetName().Name;

            Assert.That(serviceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(requestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(resultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(componentAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void FindGameObjectsUseCase_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that the bundled tool owns the GameObject search implementation.
            string useCaseAssemblyName = typeof(FindGameObjectsUseCase).Assembly.GetName().Name;

            Assert.That(useCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void ScreenshotTypes_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled screenshot implementation types stay inside the first-party tool assembly.
            string serviceAssemblyName = typeof(IUnityCliLoopScreenshotService).Assembly.GetName().Name;
            string requestAssemblyName = typeof(UnityCliLoopScreenshotRequest).Assembly.GetName().Name;
            string resultAssemblyName = typeof(UnityCliLoopScreenshotResult).Assembly.GetName().Name;
            string elementAssemblyName = typeof(UIElementInfo).Assembly.GetName().Name;

            Assert.That(serviceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(requestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(resultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(elementAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void ScreenshotUseCase_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that the bundled tool owns the screenshot implementation.
            string useCaseAssemblyName = typeof(ScreenshotUseCase).Assembly.GetName().Name;

            Assert.That(useCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void InputRecordingTypes_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled input recording implementation types stay inside the first-party tool assembly.
            string recordServiceAssemblyName = typeof(IUnityCliLoopRecordInputService).Assembly.GetName().Name;
            string recordRequestAssemblyName = typeof(UnityCliLoopRecordInputRequest).Assembly.GetName().Name;
            string recordResultAssemblyName = typeof(UnityCliLoopRecordInputResult).Assembly.GetName().Name;
            string replayServiceAssemblyName = typeof(IUnityCliLoopReplayInputService).Assembly.GetName().Name;
            string replayRequestAssemblyName = typeof(UnityCliLoopReplayInputRequest).Assembly.GetName().Name;
            string replayResultAssemblyName = typeof(UnityCliLoopReplayInputResult).Assembly.GetName().Name;
            string recordActionAssemblyName = typeof(RecordInputAction).Assembly.GetName().Name;
            string replayActionAssemblyName = typeof(ReplayInputAction).Assembly.GetName().Name;

            Assert.That(recordServiceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(recordRequestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(recordResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(replayServiceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(replayRequestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(replayResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(recordActionAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(replayActionAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void InputRecordingUseCases_WhenLoaded_CompileUnderApplicationAssembly()
        {
            // Tests that the bundled tools own the record/replay implementations.
            string recordUseCaseAssemblyName = typeof(RecordInputUseCase).Assembly.GetName().Name;
            string replayUseCaseAssemblyName = typeof(ReplayInputUseCase).Assembly.GetName().Name;

            Assert.That(recordUseCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(replayUseCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void InputSimulationTypes_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled input simulation implementation types stay inside the first-party tool assembly.
            string keyboardServiceAssemblyName = typeof(IUnityCliLoopKeyboardSimulationService).Assembly.GetName().Name;
            string keyboardRequestAssemblyName = typeof(UnityCliLoopKeyboardSimulationRequest).Assembly.GetName().Name;
            string keyboardResultAssemblyName = typeof(UnityCliLoopKeyboardSimulationResult).Assembly.GetName().Name;
            string mouseServiceAssemblyName = typeof(IUnityCliLoopMouseInputSimulationService).Assembly.GetName().Name;
            string mouseRequestAssemblyName = typeof(UnityCliLoopMouseInputSimulationRequest).Assembly.GetName().Name;
            string mouseResultAssemblyName = typeof(UnityCliLoopMouseInputSimulationResult).Assembly.GetName().Name;
            string mouseUiServiceAssemblyName = typeof(IUnityCliLoopMouseUiSimulationService).Assembly.GetName().Name;
            string mouseUiRequestAssemblyName = typeof(UnityCliLoopMouseUiSimulationRequest).Assembly.GetName().Name;
            string mouseUiResultAssemblyName = typeof(UnityCliLoopMouseUiSimulationResult).Assembly.GetName().Name;

            Assert.That(keyboardServiceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(keyboardRequestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(keyboardResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseServiceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseRequestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseUiServiceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseUiRequestAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseUiResultAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void InputSimulationUseCases_WhenLoaded_CompileUnderApplicationAssembly()
        {
            // Tests that the bundled tools own the keyboard and mouse input simulation implementations.
            string keyboardUseCaseAssemblyName = typeof(SimulateKeyboardUseCase).Assembly.GetName().Name;
            string mouseUseCaseAssemblyName = typeof(SimulateMouseInputUseCase).Assembly.GetName().Name;
            string mouseUiUseCaseAssemblyName = typeof(SimulateMouseUiUseCase).Assembly.GetName().Name;

            Assert.That(keyboardUseCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseUseCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(mouseUiUseCaseAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void PreloadMetadataValidationPorts_WhenLoaded_CompileUnderMetadataValidationAssembly()
        {
            // Tests that preload metadata validation contracts are owned by the metadata validation module.
            string validatorAssemblyName = typeof(IPreloadAssemblySecurityValidator).Assembly.GetName().Name;
            string overrideAssemblyName = typeof(IOverrideDefaultPreloadAssemblySecurityValidation).Assembly.GetName().Name;
            string registryAssemblyName = typeof(PreloadAssemblySecurityValidatorRegistry).Assembly.GetName().Name;

            Assert.That(validatorAssemblyName, Is.EqualTo(MetadataValidationAssemblyName));
            Assert.That(overrideAssemblyName, Is.EqualTo(MetadataValidationAssemblyName));
            Assert.That(registryAssemblyName, Is.EqualTo(MetadataValidationAssemblyName));
        }

        [Test]
        public void MetadataValidationAsmdef_WhenLoaded_DependsOnlyOnToolContracts()
        {
            // Tests that metadata validation depends on public contracts without reaching into implementation layers.
            string[] references = ReadResolvedReferences(
                "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/MetadataValidation/UnityCLILoop.FirstPartyTools.ExecuteDynamicCode.MetadataValidation.Editor.asmdef");

            Assert.That(references, Is.EquivalentTo(new[] { ToolContractsAssemblyName }));
        }

        [Test]
        public void SharedSupportTypes_WhenLoaded_CompileUnderOwningAssemblies()
        {
            // Tests that support constants and logging are extension-facing while domain reload state stays in application.
            string constantsAssemblyName = typeof(UnityCliLoopConstants).Assembly.GetName().Name;
            string loggerAssemblyName = typeof(VibeLogger).Assembly.GetName().Name;
            string registryAssemblyName = typeof(DomainReloadStateRegistry).Assembly.GetName().Name;
            string providerAssemblyName = typeof(IDomainReloadStateProvider).Assembly.GetName().Name;

            Assert.That(constantsAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
            Assert.That(loggerAssemblyName, Is.EqualTo(ToolContractsAssemblyName));
            Assert.That(registryAssemblyName, Is.EqualTo(ApplicationAssemblyName));
            Assert.That(providerAssemblyName, Is.EqualTo(ApplicationAssemblyName));
        }

        [Test]
        public void PresentationAsmdef_WhenLoaded_DependsOnApplicationAndDoesNotReferenceInfrastructure()
        {
            // Tests that presentation and infrastructure remain sibling outer layers.
            string[] references = ReadResolvedReferences("Packages/src/Editor/Presentation/UnityCLILoop.Presentation.asmdef");

            Assert.That(references, Is.EquivalentTo(new[]
            {
                ApplicationAssemblyName,
                DomainAssemblyName,
                ToolContractsAssemblyName
            }));
        }

        [Test]
        public void InfrastructureAsmdef_WhenLoaded_DependsOnApplicationAndDoesNotReferencePresentation()
        {
            // Tests that infrastructure and presentation remain sibling outer layers.
            string[] references = ReadResolvedReferences("Packages/src/Editor/Infrastructure/UnityCLILoop.Infrastructure.asmdef");

            Assert.That(references, Is.EquivalentTo(new[]
            {
                ApplicationAssemblyName,
                DomainAssemblyName,
                ToolContractsAssemblyName
            }));
        }

        [Test]
        public void FirstPartyToolsAsmdef_WhenLoaded_DoesNotReferenceImplementationLayers()
        {
            // Tests that the first-party startup assembly depends on tool modules, not platform implementation layers.
            string[] references = ReadResolvedReferences("Packages/src/Editor/FirstPartyTools/UnityCLILoop.FirstPartyTools.Editor.asmdef");

            Assert.That(references, Does.Contain(ClearConsoleAssemblyName));
            Assert.That(references, Does.Contain(CompileAssemblyName));
            Assert.That(references, Does.Contain(ControlPlayModeAssemblyName));
            Assert.That(references, Does.Contain(ExecuteDynamicCodeAssemblyName));
            Assert.That(references, Does.Contain(FindGameObjectsAssemblyName));
            Assert.That(references, Does.Contain(GetHierarchyAssemblyName));
            Assert.That(references, Does.Contain(GetLogsAssemblyName));
            Assert.That(references, Does.Contain(RecordInputAssemblyName));
            Assert.That(references, Does.Contain(ReplayInputAssemblyName));
            Assert.That(references, Does.Contain(RunTestsAssemblyName));
            Assert.That(references, Does.Contain(ScreenshotAssemblyName));
            Assert.That(references, Does.Contain(SimulateKeyboardAssemblyName));
            Assert.That(references, Does.Contain(SimulateMouseInputAssemblyName));
            Assert.That(references, Does.Contain(SimulateMouseUiAssemblyName));
            Assert.That(references, Does.Not.Contain(ApplicationAssemblyName));
            Assert.That(references, Does.Not.Contain(DomainAssemblyName));
            Assert.That(references, Does.Not.Contain(InfrastructureAssemblyName));
            Assert.That(references, Does.Not.Contain(PresentationAssemblyName));
        }

        [Test]
        public void ApplicationToolSources_WhenLoaded_DoNotDeclarePublicToolEntryPoints()
        {
            // Tests that bundled tool entry points stay outside the application layer.
            string[] sourcePaths = Directory.GetFiles("Packages/src/Editor/Application/Api/Tools", "*.cs", SearchOption.AllDirectories);
            string[] offendingFiles = sourcePaths
                .Where(path =>
                {
                    string source = File.ReadAllText(path);
                    return source.Contains("[UnityCliLoopTool]") || source.Contains(": UnityCliLoopTool<");
                })
                .Select(path => Path.GetRelativePath(UnityCliLoopPathResolver.GetProjectRoot(), path))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(offendingFiles, Is.Empty);
        }

        [Test]
        public void ApplicationSources_WhenLoaded_DoNotReferenceProjectIpcInfrastructure()
        {
            // Tests that application code depends on server ports instead of project IPC implementation classes.
            string[] forbiddenReferences =
            {
                "UnityCliLoopBridgeServer",
                "BridgeTransportEndpoint",
                "BridgeTransportListener",
                "MessageReassembler",
                "DynamicBufferManager",
                "FrameParser"
            };
            string[] offendingReferences = ReadApplicationSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, forbiddenReferences))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void CompositionRootAsmdef_WhenLoaded_ReferencesAllStartupAssemblies()
        {
            // Tests that the composition root is the assembly allowed to wire every startup assembly together.
            string[] references = ReadResolvedReferences("Packages/src/Editor/CompositionRoot/UnityCLILoop.CompositionRoot.Editor.asmdef");

            Assert.That(references, Is.EquivalentTo(new[]
            {
                ApplicationAssemblyName,
                DomainAssemblyName,
                FirstPartyToolsAssemblyName,
                InfrastructureAssemblyName,
                PresentationAssemblyName,
                ToolContractsAssemblyName
            }));
        }

        [Test]
        public void DynamicCodeServices_WhenLoaded_CompileUnderFirstPartyToolsAssembly()
        {
            // Tests that bundled dynamic-code services are initialized by the first-party tool itself.
            string servicesAssemblyName = typeof(DynamicCodeServicesRegistry).Assembly.GetName().Name;

            Assert.That(servicesAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void DynamicCodeCompilationServiceFactory_WhenLoaded_CompilesUnderFirstPartyToolsAssembly()
        {
            // Tests that concrete dynamic-code compiler construction is owned by the bundled dynamic-code tool.
            string factoryAssemblyName = typeof(DynamicCodeCompilationServiceFactory).Assembly.GetName().Name;

            Assert.That(factoryAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void DynamicCodeCompilationImplementation_WhenLoaded_CompilesUnderFirstPartyToolsAssembly()
        {
            // Tests that concrete dynamic-code compilation implementation stays inside the bundled dynamic-code tool.
            string compilerAssemblyName = typeof(DynamicCodeCompiler).Assembly.GetName().Name;
            string plannerAssemblyName = typeof(DynamicCompilationPlanner).Assembly.GetName().Name;
            string sourcePreparationAssemblyName = typeof(DynamicCodeSourcePreparationService).Assembly.GetName().Name;
            string assemblyBuilderAssemblyName = typeof(CompiledAssemblyBuilder).Assembly.GetName().Name;
            string assemblyLoadServiceAssemblyName = typeof(CompiledAssemblyLoadService).Assembly.GetName().Name;
            string cacheManagerAssemblyName = typeof(CompilationCacheManager).Assembly.GetName().Name;
            string sharedWorkerAssemblyName = typeof(SharedRoslynCompilerWorkerHost).Assembly.GetName().Name;

            Assert.That(compilerAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(plannerAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(sourcePreparationAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(assemblyBuilderAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(assemblyLoadServiceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(cacheManagerAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
            Assert.That(sharedWorkerAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void ApplicationSources_WhenLoaded_DoNotReferenceConcreteDynamicCompilationInfrastructure()
        {
            // Tests that application code depends on dynamic compilation ports instead of concrete compiler collaborators.
            string[] forbiddenReferences =
            {
                "new DynamicCodeCompiler(",
                "new DynamicCodeSourcePreparationService(",
                "new DynamicCompilationPlanner(",
                "new CompiledAssemblyBuilder(",
                "new CompiledAssemblyLoadService(",
                "CompiledAssemblyLoader.",
                "new DynamicCompilationBackend(",
                "new DynamicReferenceSetBuilderService(",
                "new ExternalCompilerPathResolutionService(",
                "ExternalCompilerPathResolver.",
                "SharedRoslynCompilerWorkerHost.",
                "new CompilationCacheManager(",
                "new AutoUsingResolver(",
                "PreUsingResolver.",
                "AssemblyTypeIndex.",
                "DynamicReferenceSetBuilder.",
                "ExternalCompilerMessageParser.",
                "ExternalCompilerPaths ",
                "new IlSecurityValidator(",
                "SourceShaper.",
                "TopLevelReturnDetector.",
                "WrapperTemplate.",
                "DynamicCodeLiteralHoister.",
                "DynamicCodeSourcePreparer.",
                "DynamicCompilationTimingFormatter.",
                "AssemblyBuilderFallbackCompilerBackend.",
                "RoslynCompilerBackend."
            };
            string[] offendingReferences = ReadApplicationSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, forbiddenReferences))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProjectIpcInfrastructure_WhenLoaded_CompilesUnderInfrastructureAssembly()
        {
            // Tests that project IPC transport implementation is owned by infrastructure.
            Type endpointType = Type.GetType(
                "io.github.hatayama.UnityCliLoop.Infrastructure.BridgeTransportEndpoint, " + InfrastructureAssemblyName);
            Type listenerFactoryType = Type.GetType(
                "io.github.hatayama.UnityCliLoop.Infrastructure.BridgeTransportListenerFactory, " + InfrastructureAssemblyName);
            string bridgeAssemblyName = typeof(UnityCliLoopBridgeServer).Assembly.GetName().Name;
            string reassemblerAssemblyName = typeof(MessageReassembler).Assembly.GetName().Name;
            string bufferAssemblyName = typeof(DynamicBufferManager).Assembly.GetName().Name;
            string parserAssemblyName = typeof(FrameParser).Assembly.GetName().Name;

            Assert.That(endpointType, Is.Not.Null);
            Assert.That(listenerFactoryType, Is.Not.Null);
            Assert.That(bridgeAssemblyName, Is.EqualTo(InfrastructureAssemblyName));
            Assert.That(endpointType.Assembly.GetName().Name, Is.EqualTo(InfrastructureAssemblyName));
            Assert.That(listenerFactoryType.Assembly.GetName().Name, Is.EqualTo(InfrastructureAssemblyName));
            Assert.That(reassemblerAssemblyName, Is.EqualTo(InfrastructureAssemblyName));
            Assert.That(bufferAssemblyName, Is.EqualTo(InfrastructureAssemblyName));
            Assert.That(parserAssemblyName, Is.EqualTo(InfrastructureAssemblyName));
        }

        [Test]
        public void ServerApplicationFacade_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that Presentation sees server lifecycle through an application boundary.
            string facadeAssemblyName = typeof(UnityCliLoopServerApplicationFacade).Assembly.GetName().Name;

            Assert.That(facadeAssemblyName, Is.EqualTo(ApplicationAssemblyName));
        }

        [Test]
        public void ServerInstanceHandle_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that application use cases expose a server handle instead of transport internals.
            string handleAssemblyName = typeof(IUnityCliLoopServerInstance).Assembly.GetName().Name;

            Assert.That(handleAssemblyName, Is.EqualTo(ApplicationAssemblyName));
        }

        [Test]
        public void ServerInstanceFactoryPort_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that application code depends on a replaceable server factory port.
            string factoryAssemblyName = typeof(IUnityCliLoopServerInstanceFactory).Assembly.GetName().Name;

            Assert.That(factoryAssemblyName, Is.EqualTo(ApplicationAssemblyName));
        }

        [Test]
        public void ServerLifecycleRegistryService_WhenLoaded_CompilesUnderApplicationAssembly()
        {
            // Tests that lifecycle handler wiring stays in an instance application service.
            string registryAssemblyName = typeof(UnityCliLoopServerLifecycleRegistryService).Assembly.GetName().Name;
            string sourceAssemblyName = typeof(IUnityCliLoopServerLifecycleSource).Assembly.GetName().Name;

            Assert.That(registryAssemblyName, Is.EqualTo(ApplicationAssemblyName));
            Assert.That(sourceAssemblyName, Is.EqualTo(ApplicationAssemblyName));
        }

        [Test]
        public void PresentationSources_WhenLoaded_DoNotReferenceServerInternals()
        {
            // Tests that Presentation does not depend directly on server transport/controller internals.
            string[] sourcePaths = Directory.GetFiles("Packages/src/Editor/Presentation", "*.cs", SearchOption.AllDirectories);
            string[] offendingFiles = sourcePaths
                .Where(path =>
                {
                    string source = File.ReadAllText(path);
                    return source.Contains("UnityCliLoopBridgeServer")
                        || source.Contains("UnityCliLoopServerController");
                })
                .Select(path => Path.GetRelativePath(UnityCliLoopPathResolver.GetProjectRoot(), path))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(offendingFiles, Is.Empty);
        }

        [Test]
        public void ConsoleClearService_WhenLoaded_CompilesUnderFirstPartyToolsAssembly()
        {
            // Tests that Unity Console mutation is owned by the bundled clear-console tool.
            string serviceAssemblyName = typeof(ConsoleClearService).Assembly.GetName().Name;

            Assert.That(serviceAssemblyName, Does.StartWith(FirstPartyToolsAssemblyNamePrefix));
        }

        [Test]
        public void ToolHostServices_WhenLoaded_AreRemoved()
        {
            // Tests that bundled tools no longer require composition-root host service wiring.
            Type hostServicesType = Type.GetType(
                "io.github.hatayama.UnityCliLoop.CompositionRoot.UnityCliLoopToolHostServices, " + CompositionRootAssemblyName);

            Assert.That(hostServicesType, Is.Null);
        }

        [Test]
        public void ToolRegistry_WhenLoaded_DoesNotCreateOrReceiveConcreteHostServices()
        {
            // Tests that the application registry creates tools through their public parameterless contract.
            string registrySource = File.ReadAllText(
                "Packages/src/Editor/Application/Api/Tools/Core/UnityCliLoopToolRegistry.cs");

            Assert.That(registrySource, Does.Not.Contain("new UnityCliLoopToolHostServices"));
            Assert.That(registrySource, Does.Not.Contain("IUnityCliLoopToolHostServices"));
        }

        [Test]
        public void ProductionAsmdefs_WhenLoaded_DoNotReferenceCompositionRootExceptCompositionRootItself()
        {
            // Tests that composition root dependencies do not leak back into production assemblies.
            string[] offendingAssemblyNames = ReadProductionAsmdefPaths()
                .Where(path => ReadAsmdefName(path) != CompositionRootAssemblyName)
                .Where(path => ReadResolvedReferencesFromAbsolutePath(path).Contains(CompositionRootAssemblyName))
                .Select(ReadAsmdefName)
                .OrderBy(assemblyName => assemblyName)
                .ToArray();

            Assert.That(offendingAssemblyNames, Is.Empty);
        }

        [Test]
        public void ProductionEditorStartupHooks_WhenLoaded_AreOwnedOnlyByCompositionRootBootstrap()
        {
            // Tests that production Editor startup order is controlled from one composition-root entrypoint.
            string allowedHookPath = NormalizeRelativePath(
                Path.Combine(
                    "Packages",
                    "src",
                    "Editor",
                    "CompositionRoot",
                    "UnityCliLoopEditorBootstrap.cs"));
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenEditorStartupHookReferences(path, allowedHookPath))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void InfrastructureEditorStartup_WhenLoaded_SchedulesSettingsRecoveryInsteadOfRecoveringSynchronously()
        {
            // Tests that settings file recovery does not block the synchronous Editor startup hook.
            string startupSource = ReadProductionSource(
                "Packages/src/Editor/Infrastructure/InfrastructureEditorStartup.cs");

            Assert.That(
                startupSource,
                Does.Contain("UnityCliLoopEditorSettingsRecoveryScheduler.ScheduleForEditorStartup();"));
            Assert.That(startupSource, Does.Not.Contain("RecoverSettingsFileForEditorStartup"));
        }

        [Test]
        public void DomainReloadStateRegistration_WhenLoaded_DoesNotReadSettingsSynchronously()
        {
            // Tests that provider registration does not force settings JSON load during Editor startup.
            string registrationSource = ReadProductionSource(
                "Packages/src/Editor/Application/Config/UnityCliLoopEditorDomainReloadStateProvider.cs");

            Assert.That(registrationSource, Does.Not.Contain("GetIsDomainReloadInProgress"));
        }

        [Test]
        public void SetupWizardStartup_WhenLoaded_SchedulesVersionCheckInsteadOfReadingSettingsSynchronously()
        {
            // Tests that Setup Wizard settings reads run after the synchronous Editor startup hook.
            string setupWizardSource = ReadProductionSource(
                "Packages/src/Editor/Presentation/Setup/SetupWizardWindow.cs");

            Assert.That(setupWizardSource, Does.Contain("EditorApplication.delayCall += TryShowOnVersionChange;"));
            Assert.That(setupWizardSource, Does.Not.Contain("\n            TryShowOnVersionChange();"));
        }

        [Test]
        public void ProjectAsmdefs_WhenLoaded_DoNotReferenceRemovedSharedAssemblyGuid()
        {
            // Tests that removed module asmdefs do not leave stale GUID references in dependent asmdefs.
            string[] offendingReferences = ReadProjectAsmdefPaths()
                .Where(path => ReadRawReferences(path).Contains(RemovedSharedAssemblyGuidReference))
                .Select(path => $"{ReadAsmdefName(path)} references removed shared assembly")
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void EditorUiFiles_WhenLoaded_DoNotReferenceFacadeInternalsDirectly()
        {
            // Tests that presentation-facing UI code uses Application facades for setup and tool settings workflows.
            string[] forbiddenReferences =
            {
                "CliInstallationDetector",
                "ProjectLocalCliAutoInstaller",
                "ProjectLocalCliInstaller",
                "NativeCliInstaller",
                "CliVersionComparer",
                "ToolSkillSynchronizer",
                "ULoopSettings.",
                "ToolSettings.",
                "UnityCliLoopToolRegistrar",
                "UnityCliLoopToolRegistry",
                "ToolSettingsCatalogItem",
                "InputRecorder",
                "InputReplayer",
                "InputRecordingFileHelper",
                "InputRecordingData",
                "RecordInputConstants",
                "RecordInputOverlayState",
                "OverlayCanvasFactory",
                "RecordReplayOverlayFactory"
            };
            string[] offendingReferences = ReadPresentationSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, forbiddenReferences))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void EditorUiFiles_WhenLoaded_ContainNoEditorWindowImplementations()
        {
            // Tests that EditorWindow implementations compile under the presentation assembly.
            string uiRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), "Packages", "src", "Editor", "UI");
            if (!Directory.Exists(uiRoot))
            {
                Assert.Pass();
            }

            string[] offendingReferences = Directory.GetFiles(uiRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).Contains("EditorWindow"))
                .Select(path => Path.GetRelativePath(UnityCliLoopPathResolver.GetProjectRoot(), path))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void EditorUiFiles_WhenLoaded_DoNotUseLegacyMcpSettingsWindowPath()
        {
            // Tests that the settings window does not return under the legacy MCP UI file path.
            string legacyUiFilePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                "Packages",
                "src",
                "Editor",
                "UI",
                "Mcp" + "EditorWindow.cs");

            Assert.That(File.Exists(legacyUiFilePath), Is.False);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpCommunicationLogTypes()
        {
            // Tests that the removed MCP communication log utility does not return under a legacy public name.
            string[] legacyNames =
            {
                "Mcp" + "CommunicationLog",
                "Mcp" + "CommunicationLogger"
            };
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, legacyNames))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ToolSourceFolders_WhenLoaded_DoNotUseLegacyMcpToolsFolder()
        {
            // Tests that public tool implementations are not grouped under the legacy MCP folder name.
            string editorRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), "Packages", "src", "Editor");
            string legacyToolFolder = Path.Combine(editorRoot, "Api", "Mcp" + "Tools");
            string currentToolFolder = Path.Combine(editorRoot, "FirstPartyTools");

            Assert.That(Directory.Exists(legacyToolFolder), Is.False);
            Assert.That(Directory.Exists(currentToolFolder), Is.True);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpUiConstantsName()
        {
            // Tests that shared UI constants use UnityCLILoop naming instead of legacy MCP naming.
            string legacyUiConstantsName = "Mcp" + "UIConstants";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyUiConstantsName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpEditorSettingsName()
        {
            // Tests that editor settings use UnityCLILoop naming outside protocol-specific code.
            string legacyEditorSettingsName = "Mcp" + "EditorSettings";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyEditorSettingsName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpEditorDomainReloadProviderName()
        {
            // Tests that editor domain reload state code uses UnityCLILoop naming outside protocol-specific code.
            string[] legacyNames =
            {
                "Mcp" + "EditorDomainReloadStateProvider",
                "Mcp" + "EditorDomainReloadStateRegistration"
            };
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, legacyNames))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyUnityMcpPathResolverName()
        {
            // Tests that path resolution code uses UnityCLILoop naming outside protocol-specific code.
            string legacyPathResolverName = "Unity" + "McpPathResolver";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyPathResolverName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpVersionName()
        {
            // Tests that package version code uses UnityCLILoop naming outside protocol-specific code.
            string legacyVersionName = "Mcp" + "Version";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyVersionName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpSecurityNames()
        {
            // Tests that tool execution security code uses UnityCLILoop naming outside protocol-specific code.
            string[] legacyNames =
            {
                "Mcp" + "SecurityChecker",
                "Mcp" + "SecurityException"
            };
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, legacyNames))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpLogTypeName()
        {
            // Tests that GetLogs parameter values use UnityCLILoop naming outside protocol-specific code.
            string legacyLogTypeName = "Mcp" + "LogType";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyLogTypeName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpConstantsName()
        {
            // Tests that shared constants use UnityCLILoop naming outside protocol-specific code.
            string legacyConstantsName = "Mcp" + "Constants";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyConstantsName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpServerConfigName()
        {
            // Tests that bridge server settings use UnityCLILoop naming outside protocol-specific code.
            string legacyConfigName = "Mcp" + "ServerConfig";
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyConfigName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotReferenceRemovedLegacyMcpTypes()
        {
            // Tests that comments and docs in production source do not point to removed legacy types.
            string[] legacyNames =
            {
                "Mcp" + "SessionManager",
                "Mcp" + "Logger",
                "Unity" + "McpServer"
            };
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, legacyNames))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void ProductionSources_WhenLoaded_DoNotUseLegacyMcpServerLifecycleNames()
        {
            // Tests that project IPC server lifecycle types use UnityCLILoop naming outside protocol-specific code.
            string[] legacyNames =
            {
                "Mcp" + "BridgeServer",
                "Mcp" + "ServerController",
                "Mcp" + "ServerStartupService",
                "Mcp" + "ServerInitializationUseCase",
                "Mcp" + "ServerShutdownUseCase"
            };
            string[] offendingReferences = ReadProductionSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, legacyNames))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void EditorUiFiles_WhenLoaded_DoNotUseLegacyMcpSettingsWindowNames()
        {
            // Tests that settings-window UI code uses UnityCLILoop naming instead of legacy MCP naming.
            string legacySettingsWindowName = "Mcp" + "EditorWindow";
            string[] offendingReferences = ReadPresentationSourcePaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacySettingsWindowName }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        [Test]
        public void PresentationAssets_WhenLoaded_DoNotUseLegacyMcpStylePrefix()
        {
            // Tests that presentation USS, UXML, and C# class names do not keep the legacy MCP style prefix.
            string legacyStylePrefix = "mcp" + "-";
            string[] offendingReferences = ReadPresentationAssetPaths()
                .SelectMany(path => FindForbiddenReferences(path, new[] { legacyStylePrefix }))
                .OrderBy(reference => reference)
                .ToArray();

            Assert.That(offendingReferences, Is.Empty);
        }

        private static string[] ReadResolvedReferences(string relativeAsmdefPath)
        {
            string asmdefPath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativeAsmdefPath);
            return ReadResolvedReferencesFromAbsolutePath(asmdefPath);
        }

        private static string[] ReadResolvedReferencesFromAbsolutePath(string asmdefPath)
        {
            Dictionary<string, string> guidToAssemblyNameMap = BuildGuidToAssemblyNameMap();
            string[] rawReferences = ReadRawReferences(asmdefPath);
            List<string> resolvedReferences = new();

            foreach (string rawReference in rawReferences)
            {
                resolvedReferences.Add(ResolveReference(rawReference, guidToAssemblyNameMap));
            }

            return resolvedReferences
                .OrderBy(reference => reference)
                .ToArray();
        }

        private static string[] ReadRawReferences(string asmdefPath)
        {
            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            return asmdef["references"]?.Values<string>().ToArray() ?? new string[0];
        }

        private static string[] ReadProductionAsmdefPaths()
        {
            string editorRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), "Packages", "src", "Editor");
            return Directory.GetFiles(editorRoot, "*.asmdef", SearchOption.AllDirectories);
        }

        private static string[] ReadPresentationSourcePaths()
        {
            string presentationRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), "Packages", "src", "Editor", "Presentation");
            return Directory.GetFiles(presentationRoot, "*.cs", SearchOption.AllDirectories);
        }

        private static string[] ReadPresentationAssetPaths()
        {
            string presentationRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), "Packages", "src", "Editor", "Presentation");
            return Directory.GetFiles(presentationRoot, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                    || path.EndsWith(".uxml", StringComparison.Ordinal)
                    || path.EndsWith(".uss", StringComparison.Ordinal))
                .ToArray();
        }

        private static string[] ReadProductionSourcePaths()
        {
            string editorRoot = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), "Packages", "src", "Editor");
            return Directory.GetFiles(editorRoot, "*.cs", SearchOption.AllDirectories);
        }

        private static string ReadProductionSource(string relativePath)
        {
            string sourcePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativePath);
            return File.ReadAllText(sourcePath);
        }

        private static string[] ReadApplicationSourcePaths()
        {
            string[] excludedDirectories =
            {
                "Packages/src/Editor/CompositionRoot/",
                "Packages/src/Editor/Domain/",
                "Packages/src/Editor/FirstPartyTools/",
                "Packages/src/Editor/Infrastructure/",
                "Packages/src/Editor/InternalAPIBridge/",
                "Packages/src/Editor/Presentation/",
                "Packages/src/Editor/ToolContracts/"
            };

            return ReadProductionSourcePaths()
                .Where(path =>
                {
                    string relativePath = Path.GetRelativePath(UnityCliLoopPathResolver.GetProjectRoot(), path);
                    string normalizedPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                    return !excludedDirectories.Any(normalizedPath.Contains);
                })
                .ToArray();
        }

        private static string[] FindForbiddenReferences(string path, string[] forbiddenReferences)
        {
            string source = File.ReadAllText(path);
            List<string> violations = new();

            foreach (string forbiddenReference in forbiddenReferences)
            {
                if (!source.Contains(forbiddenReference))
                {
                    continue;
                }

                violations.Add($"{Path.GetFileName(path)} references {forbiddenReference}");
            }

            return violations.ToArray();
        }

        private static string[] FindForbiddenEditorStartupHookReferences(string path, string allowedHookPath)
        {
            string source = File.ReadAllText(path);
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(UnityCliLoopPathResolver.GetProjectRoot(), path));
            List<string> violations = new();

            if (source.Contains("[InitializeOnLoad]")
                || source.Contains("[UnityEditor.InitializeOnLoad]"))
            {
                violations.Add($"{relativePath} declares InitializeOnLoad");
            }

            bool isAllowedBootstrap = string.Equals(relativePath, allowedHookPath, StringComparison.Ordinal);
            if (!isAllowedBootstrap
                && (source.Contains("[InitializeOnLoadMethod]")
                    || source.Contains("[UnityEditor.InitializeOnLoadMethod]")))
            {
                violations.Add($"{relativePath} declares InitializeOnLoadMethod");
            }

            return violations.ToArray();
        }

        private static string NormalizeRelativePath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static Dictionary<string, string> BuildGuidToAssemblyNameMap()
        {
            string[] asmdefPaths = ReadProjectAsmdefPaths();
            Dictionary<string, string> guidToAssemblyNameMap = new();

            foreach (string asmdefPath in asmdefPaths)
            {
                string metaPath = asmdefPath + ".meta";
                if (!File.Exists(metaPath))
                {
                    continue;
                }

                string guid = ReadMetaGuid(metaPath);
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                guidToAssemblyNameMap[guid] = ReadAsmdefName(asmdefPath);
            }

            return guidToAssemblyNameMap;
        }

        private static string[] ReadProjectAsmdefPaths()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<string> asmdefPaths = new();
            string assetsPath = Path.Combine(projectRoot, "Assets");
            string packagesSrcPath = Path.Combine(projectRoot, "Packages", "src");

            if (Directory.Exists(assetsPath))
            {
                asmdefPaths.AddRange(Directory.GetFiles(assetsPath, "*.asmdef", SearchOption.AllDirectories));
            }

            if (Directory.Exists(packagesSrcPath))
            {
                asmdefPaths.AddRange(Directory.GetFiles(packagesSrcPath, "*.asmdef", SearchOption.AllDirectories));
            }

            return asmdefPaths.ToArray();
        }

        private static string ResolveReference(string reference, Dictionary<string, string> guidToAssemblyNameMap)
        {
            const string guidReferencePrefix = "GUID:";
            if (!reference.StartsWith(guidReferencePrefix))
            {
                return reference;
            }

            string guid = reference.Substring(guidReferencePrefix.Length);
            if (!guidToAssemblyNameMap.ContainsKey(guid))
            {
                return reference;
            }

            return guidToAssemblyNameMap[guid];
        }

        private static string ReadMetaGuid(string metaPath)
        {
            string[] lines = File.ReadAllLines(metaPath);

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("guid:"))
                {
                    continue;
                }

                return trimmedLine.Substring("guid:".Length).Trim();
            }

            return string.Empty;
        }

        private static string ReadAsmdefName(string asmdefPath)
        {
            JObject asmdef = JObject.Parse(File.ReadAllText(asmdefPath));
            return asmdef["name"]?.Value<string>() ?? string.Empty;
        }
    }
}
