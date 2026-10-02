using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    /// same edit adds goes to the added-field store, and one to an event the compiled assembly
    /// already has stays on the event's accessors. Either way the method is applied unless its
    /// handler is a shape the shim cannot build.
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
        private const string ChangedKey = PublisherTypeName + "::Changed";
        private const string AddedInnerEvent = "\n            public event Action<int> InnerChanged;";

        /// <summary>
        /// What: an added method that subscribes a compiled private method group to an event this
        /// edit adds is no longer refused for the event; it is refused for the private method
        /// group, whose reason offers the lambda that does apply.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesMethodGroupToAddedEvent_SkipsNamingTheMethodGroup()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                WithSubscriberMethod(
                    "public void WireChanged(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Changed += OnValue;\n        }"));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "WireChanged");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row.\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.Not.EqualTo(HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(reason, Does.Contain("OnValue").And.Contain("lambda"), reason);
        }

        /// <summary>
        /// What: a method group of an added method is skipped with the reason that fits where it is
        /// used: a '+=' handler is told to subscribe a lambda, a '-=' handler is told to remove a
        /// kept delegate instead of a lambda, and any other use is told to wrap it in a lambda.
        /// </summary>
        [TestCase(
            "publisher.Existing += AddedHandler;",
            nameof(HotReloadWorkerReasonCode.AddedMethodMethodGroupSubscription),
            "a => AddedHandler(a)")]
        [TestCase(
            "publisher.Existing -= AddedHandler;",
            nameof(HotReloadWorkerReasonCode.AddedMethodMethodGroupUnsubscription),
            "added field")]
        [TestCase(
            "System.Action<int> handler = AddedHandler;\n            handler(1);",
            nameof(HotReloadWorkerReasonCode.AddedMethodMethodGroupReference),
            "a => AddedHandler(a)")]
        public async Task Skip_AddedMethodGroup_ReasonFitsWhereItIsUsed(
            string body,
            string expectedCode,
            string expectedAdvice)
        {
            TransformWorkerClientResult result = await RunAsync(
                ReadOnDisk(PublisherFileName),
                WithSubscriberMethod(
                    "public void AddedHandler(int value)\n        {\n            Accept(value);\n        }\n\n"
                    + "        public void WireAdded(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            " + body + "\n        }"));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "WireAdded");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row.\n" + FormatSkipped(result));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(skipped.reason.code.ToString(), Is.EqualTo(expectedCode), reason);
            Assert.That(reason, Does.Contain(expectedAdvice), reason);
        }

        /// <summary>
        /// What: an edited compiled method that keeps a compiled private method group beside a
        /// closure calling a private member stays a transplant: only the private call inside the
        /// closure goes through an accessor, and the method group outside it is left as written so
        /// a compiled '-=' still removes it. Each closure form is covered because the in-closure
        /// check must see every one of them.
        /// </summary>
        [TestCase(
            "            publisher.Existing += OnValue;\n"
            + "            publisher.Existing += value => OnValue(value + 1);",
            "+=",
            TestName = "Lambda beside a '+=' method group")]
        [TestCase(
            "            publisher.Existing -= OnValue;\n"
            + "            publisher.Existing += value => OnValue(value + 1);",
            "-=",
            TestName = "Lambda beside a '-=' method group")]
        [TestCase(
            "            publisher.Existing += OnValue;\n"
            + "            publisher.Existing += delegate (int value) { OnValue(value + 1); };",
            "+=",
            TestName = "Anonymous method beside a method group")]
        [TestCase(
            "            publisher.Existing += OnValue;\n"
            + "            void Local(int value)\n            {\n                OnValue(value + 1);\n            }\n"
            + "            publisher.Existing += Local;",
            "+=",
            TestName = "Block-bodied local function beside a method group")]
        [TestCase(
            "            publisher.Existing += OnValue;\n"
            + "            void Local(int value) => OnValue(value + 1);\n"
            + "            publisher.Existing += Local;",
            "+=",
            TestName = "Expression-bodied local function beside a method group")]
        [TestCase(
            "            publisher.Existing += OnValue;\n"
            + "            void Wire()\n            {\n                publisher.Existing += value => OnValue(value + 1);\n            }\n"
            + "            Wire();",
            "+=",
            TestName = "Lambda inside a local function beside a method group")]
        [TestCase(
            "            publisher.Existing += OnValue;\n"
            + "            System.Action wire = () => publisher.Existing += value => OnValue(value + 1);\n"
            + "            wire();",
            "+=",
            TestName = "Lambda inside a lambda beside a method group")]
        public async Task Run_PrivateHandlerKeptBesideClosure_RewritesOnlyInsideTheClosure(
            string body,
            string outerOperator)
        {
            string subscriber = ReadOnDisk(SubscriberFileName);
            Assert.That(subscriber, Does.Contain(WireBody), "Precondition: Wire body must exist.");
            subscriber = subscriber.Replace(WireBody, body, StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunAsync(ReadOnDisk(PublisherFileName), subscriber);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, "Wire"), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            TransformWorkerEntryDto entry = FindEntry(result, "Wire");
            Assert.That(entry, Is.Not.Null, "Missing entry.\n" + FormatSkipped(result));
            Assert.That(entry.patchKind, Is.EqualTo("transplant"));
            string slice = SliceShimMethod(result.Output.shimSource, entry.shimMethodName);
            Assert.That(slice, Does.Contain("__M_OnValue"), "The closure's private call must use an accessor.\n" + slice);
            Assert.That(
                Regex.IsMatch(slice, "(?<!__M_)OnValue\\("),
                Is.False,
                "No direct private call may remain inside the closure.\n" + slice);
            Assert.That(
                Regex.IsMatch(slice, Regex.Escape(outerOperator) + " (__uloopInstance\\.)?OnValue;"),
                Is.True,
                "The method group outside the closure must stay as written.\n" + slice);
        }

        /// <summary>
        /// What: a closure that uses a compiled private method as a method group has no accessor
        /// shape, so the method is still skipped for that closure.
        /// </summary>
        [Test]
        public async Task Skip_PrivateMethodGroupInsideLambda_KeepsTheMethodGroupReason()
        {
            string subscriber = ReadOnDisk(SubscriberFileName);
            Assert.That(subscriber, Does.Contain(WireBody), "Precondition: Wire body must exist.");
            subscriber = subscriber.Replace(
                WireBody,
                "            publisher.Existing += value => { System.Action<int> handler = OnValue; handler(value); };",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunAsync(ReadOnDisk(PublisherFileName), subscriber);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "Wire");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row.\n" + FormatSkipped(result));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformClosureInaccessibleAccess),
                reason);
            Assert.That(skipped.reason.detail, Is.Not.Null, reason);
            Assert.That(
                skipped.reason.detail.code,
                Is.EqualTo(HotReloadWorkerReasonCode.AccessorMethodGroupNoShape),
                reason);
        }

        /// <summary>
        /// What: an added method runs JIT-compiled as a whole, so a compiled private method group
        /// beside a closure still refuses it, now with the added-method reason.
        /// </summary>
        [Test]
        public async Task Skip_AddedMethodWithPrivateHandlerBesideLambda_UsesTheAddedMethodReason()
        {
            TransformWorkerClientResult result = await RunAsync(
                ReadOnDisk(PublisherFileName),
                WithSubscriberMethod(
                    "public void WireBoth(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Existing += OnValue;\n"
                    + "            publisher.Existing += value => OnValue(value + 1);\n        }"));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "WireBoth");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row.\n" + FormatSkipped(result));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.AddedMethodInaccessibleAccessNoRewrite),
                reason);
            Assert.That(skipped.reason.detail, Is.Not.Null, reason);
            Assert.That(
                skipped.reason.detail.code,
                Is.EqualTo(HotReloadWorkerReasonCode.AccessorMethodGroupSubscribeNoShape),
                reason);
        }

        /// <summary>
        /// What: in an iterator, whose whole body is rewritten when it touches a private member, a
        /// compiled private method group on the right of '+=' is skipped with the '+=' reason,
        /// which offers the lambda unless compiled code removes the handler.
        /// </summary>
        [Test]
        public async Task Skip_PrivateHandlerInIterator_UsesTheSubscribeReason()
        {
            TransformWorkerSkippedDto skipped = await RunWireLaterAsync(
                "            publisher.Existing += OnValue;\n");

            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformAsyncIteratorInaccessibleAccess),
                reason);
            Assert.That(skipped.reason.detail, Is.Not.Null, reason);
            Assert.That(
                skipped.reason.detail.code,
                Is.EqualTo(HotReloadWorkerReasonCode.AccessorMethodGroupSubscribeNoShape),
                reason);
        }

        /// <summary>
        /// What: an iterator with both a compiled private method group on the right of '+=' and a
        /// lambda calling a private member is still rewritten whole, so it is skipped for the
        /// closure with the '+=' reason.
        /// </summary>
        [Test]
        public async Task Skip_PrivateHandlerBesideLambdaInIterator_UsesTheSubscribeReason()
        {
            TransformWorkerSkippedDto skipped = await RunWireLaterAsync(
                "            publisher.Existing += OnValue;\n"
                + "            publisher.Existing += value => OnValue(value + 1);\n");

            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(
                skipped.reason.code,
                Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformClosureInaccessibleAccess),
                reason);
            Assert.That(skipped.reason.detail, Is.Not.Null, reason);
            Assert.That(
                skipped.reason.detail.code,
                Is.EqualTo(HotReloadWorkerReasonCode.AccessorMethodGroupSubscribeNoShape),
                reason);
        }

        private static async Task<TransformWorkerSkippedDto> RunWireLaterAsync(string addedLines)
        {
            string subscriber = ReadOnDisk(SubscriberFileName);
            const string iteratorBody = "            yield return null;\n        }\n\n        public void Accept";
            Assert.That(subscriber, Does.Contain(iteratorBody), "Precondition: WireLater body must exist.");
            subscriber = subscriber.Replace(iteratorBody, addedLines + iteratorBody, StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunAsync(ReadOnDisk(PublisherFileName), subscriber);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, "WireLater");
            Assert.That(skipped, Is.Not.Null, "Missing skipped row.\n" + FormatSkipped(result));
            return skipped;
        }

        /// <summary>
        /// What: an added method that subscribes a lambda to an event this edit adds is applied.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesLambdaToAddedEvent_IsApplied()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                WithSubscriberMethod(
                    "public void WireChanged(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Changed += value => OnValue(value);\n        }"));

            AssertAppliedThroughStore(result, "WireChanged", ChangedKey);
        }

        /// <summary>
        /// What: an existing method whose edited body subscribes a compiled public method group to
        /// an event this edit adds is applied through the store.
        /// </summary>
        [Test]
        public async Task Run_ExistingMethodSubscribesToAddedEvent_IsApplied()
        {
            string subscriber = ReadOnDisk(SubscriberFileName);
            Assert.That(subscriber, Does.Contain(WireBody), "Precondition: Wire body anchor must exist.");
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(),
                subscriber.Replace(WireBody, "            publisher.Changed += Accept;", StringComparison.Ordinal));

            AssertAppliedThroughStore(result, "Wire", ChangedKey);
        }

        /// <summary>
        /// What: a method of the publisher itself that subscribes a compiled method group to the
        /// event this edit adds to it is applied through the store.
        /// </summary>
        [Test]
        public async Task Run_PublisherSubscribesToItsOwnAddedEvent_IsApplied()
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

            AssertAppliedThroughStore(result, "SelfWire", ChangedKey, PublisherTypeName);
        }

        /// <summary>
        /// What: a nested type's event this edit adds is recognized as added and keyed by the
        /// nested type's metadata name, because the compiled nested type is looked up by its
        /// reflection name rather than its source spelling.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodSubscribesToAddedNestedEvent_UsesTheNestedKey()
        {
            string publisher = ReadOnDisk(PublisherFileName);
            Assert.That(publisher, Does.Contain(InnerEventAnchor), "Precondition: nested event anchor must exist.");
            TransformWorkerClientResult result = await RunAsync(
                publisher.Replace(InnerEventAnchor, InnerEventAnchor + AddedInnerEvent, StringComparison.Ordinal),
                WithSubscriberMethod(
                    "public void WireInner(HotReloadAddedEventPublisher.Inner inner)\n        {\n"
                    + "            inner.InnerChanged += Accept;\n        }"));

            AssertAppliedThroughStore(result, "WireInner", PublisherTypeName + "/Inner::InnerChanged");
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

        private static void AssertAppliedThroughStore(
            TransformWorkerClientResult result,
            string methodName,
            string storeKey,
            string typeMetadataName = SubscriberTypeMetadataName)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, methodName), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            Assert.That(
                FindEntry(result, methodName, typeMetadataName),
                Is.Not.Null,
                "Missing entry for " + methodName + ".\n" + FormatSkipped(result));
            Assert.That(result.Output.shimSource, Does.Contain("\"" + storeKey + "\""));
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

        private static TransformWorkerEntryDto FindEntry(
            TransformWorkerClientResult result,
            string methodName,
            string typeMetadataName = SubscriberTypeMetadataName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName == typeMetadataName && entry.methodName == methodName)
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

        private static string SliceShimMethod(string shimSource, string shimMethodName)
        {
            // Why the '(': an added member's shim type also declares its invocation counter, whose
            // name starts with the shim method name.
            int nameIndex = shimSource.IndexOf(shimMethodName + "(", StringComparison.Ordinal);
            Assert.That(nameIndex, Is.GreaterThanOrEqualTo(0), "Shim method missing: " + shimMethodName);
            int declarationStart = shimSource.LastIndexOf("public static", nameIndex, StringComparison.Ordinal);
            int openBrace = shimSource.IndexOf('{', nameIndex);
            Assert.That(openBrace, Is.GreaterThan(0));
            int depth = 0;
            for (int index = openBrace; index < shimSource.Length; index++)
            {
                if (shimSource[index] == '{')
                {
                    depth++;
                }
                else if (shimSource[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return shimSource.Substring(declarationStart, index - declarationStart + 1);
                    }
                }
            }

            Assert.Fail("Unbalanced shim method: " + shimMethodName);
            return string.Empty;
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
