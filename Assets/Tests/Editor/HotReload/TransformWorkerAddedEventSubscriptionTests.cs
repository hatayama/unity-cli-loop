using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for a body that subscribes to an event: a subscription to an event the
    /// same edit adds is skipped with a reason that asks for a compile, because the shim is
    /// compiled against the assembly that has no such event, while a subscription to an event the
    /// compiled assembly already has is left alone.
    /// </summary>
    public class TransformWorkerAddedEventSubscriptionTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string PublisherFileName = "HotReloadAddedEventPublisher.cs";
        private const string SubscriberFileName = "HotReloadAddedEventSubscriber.cs";
        private const string PublisherTypeName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedEventPublisher";
        private const string SubscriberTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedEventSubscriber";
        private const string PublisherEventAnchor = "        public event Action<int> Existing;";
        private const string InnerEventAnchor = "            public event Action<int> InnerExisting;";
        private const string PublisherMethodAnchor =
            "        public void RaiseExisting(int value)\n        {\n            Existing?.Invoke(value);\n        }";
        private const string SubscriberMethodAnchor = "        public int Received => _received;";
        private const string WireBody = "            publisher.Existing += Accept;";
        private const string AddedEvent = "\n        public event Action<int> Changed;";
        private const string AddedInnerEvent = "\n            public event Action<int> InnerChanged;";

        /// <summary>
        /// What: an added method that subscribes a private method group to an event this edit adds
        /// is skipped with the added-event reason, which names the event and offers no lambda.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesMethodGroupToAddedEvent_SkipsNamingTheEvent()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                WithSubscriberMethod(
                    "public void WireChanged(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Changed += OnValue;\n        }"));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "WireChanged");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(
                reason,
                Is.EqualTo(
                    "Subscribes to the event '" + PublisherTypeName + ".Changed', which this edit adds; "
                    + "the compiled assembly has no such event yet, so the subscription cannot bind "
                    + "until 'uloop compile'."));
            Assert.That(reason, Does.Not.Contain("lambda"));
        }

        /// <summary>
        /// What: subscribing a lambda to an event this edit adds is skipped the same way, so
        /// following a lambda hint does not end in a failed shim compile.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesLambdaToAddedEvent_Skips()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                WithSubscriberMethod(
                    "public void WireChanged(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Changed += value => OnValue(value);\n        }"));

            AssertSkippedForAddedEvent(result, "WireChanged");
        }

        /// <summary>
        /// What: an existing method whose edited body subscribes to an event this edit adds is
        /// skipped instead of producing an entry whose shim cannot compile.
        /// </summary>
        [Test]
        public async Task Run_ExistingMethodSubscribesToAddedEvent_Skips()
        {
            string subscriber = ReadOnDisk(SubscriberFileName);
            Assert.That(subscriber, Does.Contain(WireBody), "Precondition: Wire body anchor must exist.");
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                subscriber.Replace(WireBody, "            publisher.Changed += Accept;", StringComparison.Ordinal));

            AssertSkippedForAddedEvent(result, "Wire");
            Assert.That(FindEntry(result, "Wire"), Is.Null, "The subscription must not be emitted as an entry.");
        }

        /// <summary>
        /// What: a method of the publisher itself that subscribes to the event this edit adds to it
        /// is skipped the same way.
        /// </summary>
        [Test]
        public async Task Run_PublisherSubscribesToItsOwnAddedEvent_Skips()
        {
            string publisher = WithAddedEvent();
            Assert.That(publisher, Does.Contain(PublisherMethodAnchor), "Precondition: method anchor must exist.");
            TransformWorkerClientResult result = await RunAsync(
                publisher.Replace(
                    PublisherMethodAnchor,
                    PublisherMethodAnchor
                    + "\n\n        public void SelfWire()\n        {\n            Changed += RaiseExisting;\n        }",
                    StringComparison.Ordinal),
                ReadOnDisk(SubscriberFileName));

            AssertSkippedForAddedEvent(result, "SelfWire");
        }

        /// <summary>
        /// What: a nested type's event this edit adds is recognized as added, because the compiled
        /// nested type is looked up by its reflection name rather than its source spelling.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesToAddedNestedEvent_SkipsNamingTheNestedEvent()
        {
            string publisher = ReadOnDisk(PublisherFileName);
            Assert.That(publisher, Does.Contain(InnerEventAnchor), "Precondition: nested event anchor must exist.");
            TransformWorkerClientResult result = await RunAsync(
                publisher.Replace(InnerEventAnchor, InnerEventAnchor + AddedInnerEvent, StringComparison.Ordinal),
                WithSubscriberMethod(
                    "public void WireInner(HotReloadAddedEventPublisher.Inner inner)\n        {\n"
                    + "            inner.InnerChanged += Accept;\n        }"));

            AssertSkippedForAddedEvent(result, "WireInner");
            Assert.That(
                HotReloadWorkerReasonText.Render(FindSkipped(result, "WireInner").reason),
                Does.Contain("'" + PublisherTypeName + ".Inner.InnerChanged'"));
        }

        /// <summary>
        /// What: an added method that subscribes to an event the compiled assembly already has is
        /// applied, so the added-event check does not refuse every subscription.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesToCompiledEvent_IsApplied()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                WithSubscriberMethod(
                    "public void WireExisting(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Existing += Accept;\n        }"));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, "WireExisting"), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            Assert.That(FindEntry(result, "WireExisting"), Is.Not.Null, "Missing entry.\n" + FormatSkipped(result));
        }

        /// <summary>
        /// What: an added method that subscribes to an event of another assembly is applied, since
        /// that event is never one this edit adds.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesToEventOfAnotherAssembly_IsApplied()
        {
            TransformWorkerClientResult result = await RunAsync(
                ReadOnDisk(PublisherFileName),
                WithSubscriberMethod(
                    "public void WireLowMemory()\n        {\n"
                    + "            UnityEngine.Application.lowMemory += Tick;\n        }"));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, "WireLowMemory"), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            Assert.That(FindEntry(result, "WireLowMemory"), Is.Not.Null, "Missing entry.\n" + FormatSkipped(result));
        }

        private static void AssertSkippedForAddedEvent(TransformWorkerClientResult result, string methodName)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName);
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for " + methodName + ".\n" + FormatSkipped(result));
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent),
                HotReloadWorkerReasonText.Render(skipped.reason));
        }

        private static string WithAddedEvent()
        {
            string publisher = ReadOnDisk(PublisherFileName);
            Assert.That(publisher, Does.Contain(PublisherEventAnchor), "Precondition: event anchor must exist.");
            return publisher.Replace(PublisherEventAnchor, PublisherEventAnchor + AddedEvent, StringComparison.Ordinal);
        }

        private static string WithSubscriberMethod(string method)
        {
            string subscriber = ReadOnDisk(SubscriberFileName);
            Assert.That(subscriber, Does.Contain(SubscriberMethodAnchor), "Precondition: method anchor must exist.");
            return subscriber.Replace(
                SubscriberMethodAnchor,
                SubscriberMethodAnchor + "\n\n        " + method,
                StringComparison.Ordinal);
        }

        private static string ReadOnDisk(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName);
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return File.ReadAllText(path);
        }

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName == SubscriberTypeMetadataName && entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return skipped;
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                lines.Add(skipped.method + " :: " + HotReloadWorkerReasonText.Render(skipped.reason));
            }

            return string.Join("\n", lines);
        }

        // Both files are written edited under the test sources directory and sent under their
        // project-relative paths, with the source on disk as the snapshot the worker diffs against.
        private static async Task<TransformWorkerClientResult> RunAsync(string editedPublisher, string editedSubscriber)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(projectRoot, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    CreateSource(PublisherFileName, editedPublisher),
                    CreateSource(SubscriberFileName, editedSubscriber)
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(projectRoot, compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static TransformWorkerSourceDto CreateSource(string fileName, string editedSource)
        {
            return new TransformWorkerSourceDto
            {
                sourcePath = HotReloadTestSourceWriter.WriteEditedSource("AddedEventSubscription_" + fileName, editedSource),
                projectRelativePath = "Assets/Tests/Editor/HotReload/" + fileName,
                snapshotSource = ReadOnDisk(fileName)
            };
        }

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("CompilationPipeline assembly not found.");
            return null;
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            foreach (string reference in allReferences ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!paths.Contains(fullTarget))
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        private static string[] BuildAbsoluteAssemblySourcePaths(string projectRoot, string[] sourceFiles)
        {
            List<string> paths = new List<string>();
            foreach (string sourceFile in sourceFiles ?? Array.Empty<string>())
            {
                string relative = sourceFile.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
                paths.Add(Path.GetFullPath(Path.Combine(projectRoot, relative)));
            }

            return paths.ToArray();
        }
    }
}
