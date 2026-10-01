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
    /// EditMode coverage for how the transform worker classifies and rewrites a field-like event
    /// the edit adds to a compiled class: the uses that can live in the added-field store are
    /// rewritten to it under the declaring type's key, and the shapes that cannot stay refused.
    /// </summary>
    public class TransformWorkerAddedEventTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string PublisherFileName = "HotReloadAddedEventPublisher.cs";
        private const string SubscriberFileName = "HotReloadAddedEventSubscriber.cs";
        private const string PublisherTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedEventPublisher";
        private const string SubscriberTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedEventSubscriber";
        private const string ChangedKey = PublisherTypeMetadataName + "::Changed";
        private const string PublisherEventAnchor = "        public event Action<int> Existing;";
        private const string RaiseBodyAnchor = "            Existing?.Invoke(value);";
        private const string InnerRaiseBodyAnchor = "                InnerExisting?.Invoke(value);";
        private const string CountAnchor = "        public int Count => 0;";
        private const string PayloadAnchor = "            public int Value;";
        private const string ClashAnchor = "        public int Clash;";
        private const string GenericMarkerAnchor = "        public static int Marker;";
        private const string SubscriberMethodAnchor = "        public int Received => _received;";
        private const string WireBody = "            publisher.Existing += Accept;";
        private const string AddedEvent = "\n        public event Action<int> Changed;";

        /// <summary>
        /// What: each way the declaring type reads or writes the added event is rewritten to the
        /// store under the declaring type's key, and the method is emitted instead of skipped.
        /// </summary>
        [TestCase("Changed?.Invoke(value);")]
        [TestCase("if (Changed != null)\n            {\n                Changed(value);\n            }")]
        [TestCase("Changed = null;")]
        [TestCase("Action<int> handler = Changed;\n            handler?.Invoke(value);")]
        [TestCase("this.Changed?.Invoke(value);")]
        public async Task Rewrite_EachReadAndWriteForm_UsesTheStore(string raiseBody)
        {
            TransformWorkerClientResult result = await RunAsync(
                WithRaiseBody(WithAddedEvent(AddedEvent), raiseBody),
                ReadOnDisk(SubscriberFileName));

            AssertEmittedThroughStore(result, PublisherTypeMetadataName, "RaiseExisting", ChangedKey);
        }

        /// <summary>
        /// What: an added property's getter that calls a private member, so its body is planned
        /// through accessor delegates, raises the added event through the store and the plan
        /// holds no backing-field accessor for the event, which the compiled class does not have.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedGetterWithPrivateAccess_PlansNoBackingFieldForTheEvent()
        {
            string publisher = WithAddedEvent(
                AddedEvent + "\n\n        public int Fire\n        {\n            get\n            {\n"
                + "                Changed?.Invoke(Secret());\n                return 1;\n            }\n        }");
            TransformWorkerClientResult result = await RunAsync(publisher, ReadOnDisk(SubscriberFileName));

            AssertEmittedThroughStore(result, PublisherTypeMetadataName, "get_Fire", ChangedKey);
            Assert.That(result.Output.shimSource, Does.Not.Contain("__EV_Changed"));
        }

        /// <summary>
        /// What: a static event added to a generic class keeps today's refusal when another type
        /// subscribes through a closed instantiation, because one store slot would serve every
        /// instantiation the CLR keeps apart.
        /// </summary>
        [Test]
        public async Task Classify_EventOnGenericHost_StaysRefused()
        {
            string publisher = ReplaceInSource(
                ReadOnDisk(PublisherFileName),
                GenericMarkerAnchor,
                GenericMarkerAnchor + "\n\n        public static event Action<int> GenericChanged;");
            TransformWorkerClientResult result = await RunAsync(
                publisher,
                WithSubscriberMethod(
                    "public void WireGeneric()\n        {\n"
                    + "            HotReloadAddedEventGenericHost<int>.GenericChanged += Accept;\n        }"));

            AssertSkippedWith(result, "WireGeneric", HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent);
        }

        /// <summary>
        /// What: an event added to a struct is skipped with the reason an added struct field gets,
        /// because a boxed struct has no identity the store could key a value by.
        /// </summary>
        [Test]
        public async Task Classify_EventOnStruct_IsSkippedLikeAStructField()
        {
            string publisher = ReplaceInSource(
                ReadOnDisk(PublisherFileName),
                PayloadAnchor,
                PayloadAnchor + "\n\n            public event Action<int> PayloadChanged;");
            TransformWorkerClientResult result = await RunAsync(
                publisher,
                WithSubscriberMethod(
                    "public void WirePayload(HotReloadAddedEventPublisher.Payload payload)\n        {\n"
                    + "            payload.PayloadChanged += Accept;\n        }"));

            AssertSkippedWith(result, "WirePayload", HotReloadWorkerReasonCode.AddedFieldStructHost);
        }

        /// <summary>
        /// What: an added event whose delegate type code outside the assembly cannot name stays
        /// skipped for that reason, whether it is raised or subscribed to.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Classify_EventWithNonVisibleDelegate_StaysSkipped(bool raise)
        {
            string publisher = WithAddedEvent("\n        internal event HotReloadAddedEventHiddenHandler Hidden;");
            string subscriber = ReadOnDisk(SubscriberFileName);
            if (raise)
            {
                publisher = WithRaiseBody(publisher, "Hidden?.Invoke(value);");
            }
            else
            {
                subscriber = WithSubscriberMethod(
                    "public void WireHidden(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Hidden += Accept;\n        }");
            }

            TransformWorkerClientResult result = await RunAsync(publisher, subscriber);

            AssertSkippedWith(
                result,
                raise ? "RaiseExisting" : "WireHidden",
                HotReloadWorkerReasonCode.EventDelegateTypeNotVisible);
        }

        /// <summary>
        /// What: an event whose name the compiled class already uses for a field is not put in the
        /// store, so subscribing and raising keep today's refusals.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Classify_EventNameClashesWithCompiledField_StaysRefused(bool raise)
        {
            string publisher = ReplaceInSource(
                ReadOnDisk(PublisherFileName),
                ClashAnchor,
                "        public event Action<int> Clash;");
            string subscriber = ReadOnDisk(SubscriberFileName);
            if (raise)
            {
                publisher = WithRaiseBody(publisher, "Clash?.Invoke(value);");
            }
            else
            {
                subscriber = WithSubscriberMethod(
                    "public void WireClash(HotReloadAddedEventPublisher publisher)\n        {\n"
                    + "            publisher.Clash += Accept;\n        }");
            }

            TransformWorkerClientResult result = await RunAsync(publisher, subscriber);

            AssertSkippedWith(
                result,
                raise ? "RaiseExisting" : "WireClash",
                raise
                    ? HotReloadWorkerReasonCode.EventAddedInThisEdit
                    : HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent);
        }

        /// <summary>
        /// What: a compiled event re-declared static is not an added event, so raising it keeps
        /// the reason that the compiled backing field does not match.
        /// </summary>
        [Test]
        public async Task Classify_EventWithChangedStaticness_KeepsTodaysReasons()
        {
            string publisher = ReplaceInSource(
                ReadOnDisk(PublisherFileName),
                PublisherEventAnchor,
                "        public static event Action<int> Existing;");
            TransformWorkerClientResult result = await RunAsync(
                WithRaiseBody(publisher, "Existing?.Invoke(value + 1);"),
                ReadOnDisk(SubscriberFileName));

            AssertSkippedWith(result, "RaiseExisting", HotReloadWorkerReasonCode.EventAddedInThisEdit);
        }

        /// <summary>
        /// What: an initializer the store can run is emitted with the event, and one it cannot run
        /// skips the using body with the added-field initializer reason instead of failing. The
        /// lambda that calls a private static of the host pins that a lambda's body is still
        /// checked name by name, not let through because it is an anonymous function.
        /// </summary>
        [TestCase("delegate { }", "", true)]
        [TestCase("new Action<int>(RaiseExisting)", "", false)]
        [TestCase(
            "v => HostPrivateStatic(v)",
            "\n        private static void HostPrivateStatic(int value)\n        {\n        }\n",
            false)]
        public async Task Classify_EventInitializer_FollowsTheFieldInitializerRules(
            string initializer,
            string extraMember,
            bool emittable)
        {
            string publisher = WithRaiseBody(
                WithAddedEvent(extraMember + "\n        public event Action<int> Changed = " + initializer + ";"),
                "Changed?.Invoke(value);");
            TransformWorkerClientResult result = await RunAsync(publisher, ReadOnDisk(SubscriberFileName));

            if (emittable)
            {
                AssertEmittedThroughStore(result, PublisherTypeMetadataName, "RaiseExisting", ChangedKey);
                return;
            }

            AssertSkippedWith(
                result,
                "RaiseExisting",
                HotReloadWorkerReasonCode.AddedFieldInitializerNotLiteralOrExternalStatic);
        }

        /// <summary>
        /// What: each declarator of a multi-declarator event gets its own store key.
        /// </summary>
        [Test]
        public async Task Classify_MultipleDeclarators_EachGetsAKey()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithRaiseBody(
                    WithAddedEvent("\n        public event Action<int> First, Second;"),
                    "First?.Invoke(value);\n            Second?.Invoke(value);"),
                ReadOnDisk(SubscriberFileName));

            AssertEmittedThroughStore(result, PublisherTypeMetadataName, "RaiseExisting", PublisherTypeMetadataName + "::First");
            Assert.That(result.Output.shimSource, Does.Contain("\"" + PublisherTypeMetadataName + "::Second\""));
        }

        /// <summary>
        /// What: subscribing to an added event through a null-conditional receiver is skipped,
        /// because the store call has no receiver name to put there.
        /// </summary>
        [Test]
        public async Task Classify_ConditionalReceiverSubscription_IsSkipped()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(AddedEvent),
                ReadOnDisk(SubscriberFileName).Replace(WireBody, "            publisher?.Changed += Accept;", StringComparison.Ordinal));

            AssertSkippedWith(result, "Wire", HotReloadWorkerReasonCode.EventConditionalReceiver);
        }

        /// <summary>
        /// What: '??=' on an added event is skipped with the added-field '??=' reason before the
        /// rewrite is reached.
        /// </summary>
        [Test]
        public async Task Classify_NullCoalescingAssignment_IsSkippedBeforeRewrite()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithRaiseBody(WithAddedEvent(AddedEvent), "Changed ??= RaiseExisting;"),
                ReadOnDisk(SubscriberFileName));

            AssertSkippedWith(result, "RaiseExisting", HotReloadWorkerReasonCode.AddedFieldCoalesceAssignment);
        }

        /// <summary>
        /// What: a parenthesized event on the left of '+=' is a subscription like the bare one.
        /// </summary>
        [Test]
        public async Task Rewrite_ParenthesizedSubscription_IsTreatedAsSubscription()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithRaiseBody(WithAddedEvent(AddedEvent), "(Changed) += RaiseExisting;"),
                ReadOnDisk(SubscriberFileName));

            AssertEmittedThroughStore(result, PublisherTypeMetadataName, "RaiseExisting", ChangedKey);
            Assert.That(result.Output.shimSource, Does.Contain("Delegate.Combine"));
        }

        /// <summary>
        /// What: a nested type's body that raises and subscribes to its outer type's added event
        /// uses the outer type's key, not the nested type it is emitted from.
        /// </summary>
        [Test]
        public async Task Rewrite_NestedTypeUsesOuterAddedEvent_UsesTheOuterKey()
        {
            string publisher = ReplaceInSource(
                WithAddedEvent(AddedEvent),
                InnerRaiseBodyAnchor,
                "                HotReloadAddedEventPublisher outer = new HotReloadAddedEventPublisher();\n"
                + "                outer.Changed += InnerExisting;\n"
                + "                outer.Changed?.Invoke(value);");
            TransformWorkerClientResult result = await RunAsync(publisher, ReadOnDisk(SubscriberFileName));

            AssertEmittedThroughStore(result, PublisherTypeMetadataName + "/Inner", "RaiseInnerExisting", ChangedKey);
        }

        /// <summary>
        /// What: raising and subscribing inside a lambda and a local function use the store too.
        /// </summary>
        [Test]
        public async Task Rewrite_UseInsideLambda_UsesTheStore()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithRaiseBody(
                    WithAddedEvent(AddedEvent),
                    "Action raise = () => Changed?.Invoke(value);\n            raise();\n"
                    + "            void Wire()\n            {\n                Changed += RaiseExisting;\n            }\n\n"
                    + "            Wire();"),
                ReadOnDisk(SubscriberFileName));

            AssertEmittedThroughStore(result, PublisherTypeMetadataName, "RaiseExisting", ChangedKey);
        }

        /// <summary>
        /// What: a compiled property's getter and an added property's getter that raise the added
        /// event are emitted through the store, since the getter paths ask the same question.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Rewrite_RaiseFromPropertyGetter_UsesTheStore(bool compiledProperty)
        {
            string property = compiledProperty
                ? "        public int Count\n        {\n            get\n            {\n"
                  + "                Changed?.Invoke(1);\n                return 1;\n            }\n        }"
                : CountAnchor + "\n\n        public int AddedCount\n        {\n            get\n            {\n"
                  + "                Changed?.Invoke(2);\n                return 2;\n            }\n        }";
            TransformWorkerClientResult result = await RunAsync(
                ReplaceInSource(WithAddedEvent(AddedEvent), CountAnchor, property),
                ReadOnDisk(SubscriberFileName));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string getterName = compiledProperty ? "get_Count" : "get_AddedCount";
            Assert.That(FindSkipped(result, getterName), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            Assert.That(result.Output.shimSource, Does.Contain("\"" + ChangedKey + "\""), FormatSkipped(result));
        }

        /// <summary>
        /// What: subscribing through a receiver that may have side effects is skipped, because the
        /// store write reads and writes the receiver twice.
        /// </summary>
        [Test]
        public async Task Classify_SubscriptionThroughAnInvocationReceiver_IsSkippedForDoubleEvaluation()
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(AddedEvent),
                ReadOnDisk(SubscriberFileName).Replace(WireBody, "            publisher.Self().Changed += Accept;", StringComparison.Ordinal));

            AssertSkippedWith(result, "Wire", HotReloadWorkerReasonCode.AddedFieldDoubleEvalReceiver);
        }

        /// <summary>
        /// What: subscribing through a field or a local is applied, since reading either twice has
        /// no side effect; this is the shape most subscriptions take.
        /// </summary>
        [TestCase("_publisher.Changed += Accept;")]
        [TestCase("HotReloadAddedEventPublisher local = publisher;\n            local.Changed += Accept;")]
        public async Task Rewrite_SubscriptionThroughAFieldOrLocal_IsApplied(string body)
        {
            TransformWorkerClientResult result = await RunAsync(
                WithAddedEvent(AddedEvent),
                ReadOnDisk(SubscriberFileName).Replace(WireBody, "            " + body, StringComparison.Ordinal));

            AssertEmittedThroughStore(result, SubscriberTypeMetadataName, "Wire", ChangedKey);
        }

        private static void AssertEmittedThroughStore(
            TransformWorkerClientResult result,
            string typeMetadataName,
            string methodName,
            string storeKey)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipped(result, methodName), Is.Null, "Unexpected skip.\n" + FormatSkipped(result));
            Assert.That(
                FindEntry(result, typeMetadataName, methodName),
                Is.Not.Null,
                "Missing entry.\n" + FormatSkipped(result));
            Assert.That(result.Output.shimSource, Does.Contain("\"" + storeKey + "\""));
            Assert.That(result.Output.shimSource, Does.Contain("HotReloadAddedFieldStore"));
        }

        private static void AssertSkippedWith(
            TransformWorkerClientResult result,
            string methodName,
            HotReloadWorkerReasonCode code)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName);
            Assert.That(skipped, Is.Not.Null, "Missing skipped row for " + methodName + ".\n" + FormatSkipped(result));
            Assert.That(skipped.reason.code, Is.EqualTo(code), HotReloadWorkerReasonText.Render(skipped.reason));
        }

        private static string WithAddedEvent(string addedEvent)
        {
            return ReplaceInSource(ReadOnDisk(PublisherFileName), PublisherEventAnchor, PublisherEventAnchor + addedEvent);
        }

        private static string WithRaiseBody(string publisher, string body)
        {
            return ReplaceInSource(publisher, RaiseBodyAnchor, "            " + body);
        }

        private static string WithSubscriberMethod(string method)
        {
            return ReplaceInSource(
                ReadOnDisk(SubscriberFileName),
                SubscriberMethodAnchor,
                SubscriberMethodAnchor + "\n\n        " + method);
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string ReadOnDisk(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName);
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return File.ReadAllText(path);
        }

        private static TransformWorkerEntryDto FindEntry(
            TransformWorkerClientResult result,
            string typeMetadataName,
            string methodName)
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
                sourcePath = HotReloadTestSourceWriter.WriteEditedSource("AddedEvent_" + fileName, editedSource),
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
