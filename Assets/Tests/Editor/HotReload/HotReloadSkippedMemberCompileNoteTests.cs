using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies unresolved-member extraction and skipped-member notes for shim compile failures.
    /// </summary>
    public sealed class HotReloadSkippedMemberCompileNoteTests
    {
        private const string Surface11Cs1061 =
            "CS1061: 'TetrisGameController' does not contain a definition for 'DescribeValue' and no accessible extension method 'DescribeValue' accepting a first argument of type 'TetrisGameController' could be found (are you missing a using directive or an assembly reference?) (line 95)";

        private const string Surface11SkippedMethod =
            "Tetris.Presentation.TetrisGameController.DescribeValue`1(T)";

        private const string Surface11SkippedReason =
            "Added generic methods are skipped; hot reload cannot emit a typed shim for them. Run 'uloop compile'.";

        private const string ExpectedSkippedMemberNote =
            "'DescribeValue' was skipped by this hot reload run, which is why this compile failed: "
            + "Added generic methods are skipped; hot reload cannot emit a typed shim for them. Run 'uloop compile'.";

        /// <summary>
        /// What: the surface-11 CS1061 line yields the unresolved member name DescribeValue.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_Surface11Cs1061_ReturnsDescribeValue()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[] { Surface11Cs1061 });

            Assert.That(names, Is.EqualTo(new[] { "DescribeValue" }));
        }

        /// <summary>
        /// What: CS0117 uses the same definition-for quote as CS1061.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_Cs0117_ReturnsQuotedMemberName()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    "CS0117: 'Host' does not contain a definition for 'MissingHelper'"
                });

            Assert.That(names, Is.EqualTo(new[] { "MissingHelper" }));
        }

        /// <summary>
        /// What: CS0103 yields the quoted name from The name 'X' does not exist.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_Cs0103_ReturnsQuotedName()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    "CS0103: The name 'MissingHelperAddedByEdit' does not exist in the current context"
                });

            Assert.That(names, Is.EqualTo(new[] { "MissingHelperAddedByEdit" }));
        }

        /// <summary>
        /// What: duplicate unresolved names from several diagnostics are returned once, in first-seen order.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_DuplicateNames_ReturnsEachNameOnce()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    Surface11Cs1061,
                    "CS0103: The name 'DescribeValue' does not exist in the current context",
                    "CS0117: 'Host' does not contain a definition for 'OtherMissing'"
                });

            Assert.That(names, Is.EqualTo(new[] { "DescribeValue", "OtherMissing" }));
        }

        /// <summary>
        /// What: lines that are not CS1061/CS0117/CS0103 prefixes are ignored.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_UnrelatedDiagnostic_ReturnsEmpty()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    "CS0229: Ambiguity between 'A.DescribeValue' and 'B.DescribeValue'",
                    "hint: CS1061: 'Host' does not contain a definition for 'DescribeValue'"
                });

            Assert.That(names, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: the surface-11 generic skip row matches DescribeValue and returns that reason.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_Surface11GenericSkip_ReturnsReason()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedMethodGeneric
                    }
                }
            };

            string note = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("DescribeValue", skipped);

            Assert.That(note, Is.EqualTo(Surface11SkippedReason));
        }

        /// <summary>
        /// What: a skipped method whose simple name does not match returns null.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_WhenNoSimpleNameMatches_ReturnsNull()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedMethodGeneric
                    }
                }
            };

            string note = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("BuildDiagnosticLine", skipped);

            Assert.That(note, Is.Null);
        }

        /// <summary>
        /// What: a skipped get_X or set_X label matches unresolved property name X.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_WhenAccessorPrefixMatchesPropertyName_ReturnsReason()
        {
            const string reason =
                "Added properties with only a setter are skipped; the shim requires a getter identity. "
                + "Run 'uloop compile' to add the property.";
            TransformWorkerSkippedDto[] getterSkipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Ns.Type.get_HasTarget",
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedPropertySetOnly
                    }
                }
            };
            TransformWorkerSkippedDto[] setterSkipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Ns.Type.set_HasTarget",
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedPropertySetOnly
                    }
                }
            };

            Assert.That(
                HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("HasTarget", getterSkipped),
                Is.EqualTo(reason));
            Assert.That(
                HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("HasTarget", setterSkipped),
                Is.EqualTo(reason));
        }

        /// <summary>
        /// What: Get_X and getX (not the get_/set_ accessor prefixes) do not match X.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_WhenAccessorPrefixIsNotExact_ReturnsNull()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Ns.Type.Get_HasTarget",
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.MethodTransformNoBody
                    }
                },
                new TransformWorkerSkippedDto
                {
                    method = "Ns.Type.getHasTarget",
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.MethodTransformNoBody
                    }
                }
            };

            Assert.That(
                HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("HasTarget", skipped),
                Is.Null);
        }

        /// <summary>
        /// What: a skipped label whose parameter type contains dots still matches the simple name.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_WhenParameterTypeIsQualified_UsesMethodSimpleName()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Ns.Type.DescribeValue(System.Int32)",
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedMethodGeneric
                    }
                }
            };

            string note = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("DescribeValue", skipped);

            Assert.That(note, Is.EqualTo(Surface11SkippedReason));
        }

        /// <summary>
        /// What: the skipped-member note format plus the surface-11 reason is an exact full line.
        /// </summary>
        [Test]
        public void SkippedMemberCompileFailureNote_Surface11_MatchesFullText()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedMethodGeneric
                    }
                }
            };
            string reason = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("DescribeValue", skipped);
            string note = string.Format(
                HotReloadConstants.SkippedMemberCompileFailureNoteFormat,
                "DescribeValue",
                reason);

            Assert.That(note, Is.EqualTo(ExpectedSkippedMemberNote));
        }

        /// <summary>
        /// What: AppendNotes appends the skipped-member note after the composed shim-compile hints.
        /// </summary>
        [Test]
        public void AppendNotes_Surface11Cs1061_AppendsFullNoteAfterComposeHints()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedMethodGeneric
                    }
                }
            };
            string composed = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[] { Surface11Cs1061 });
            string expected = composed + "\n" + ExpectedSkippedMemberNote;

            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                composed,
                new[] { Surface11Cs1061 },
                new HotReloadCompileFailureNoteSources(skipped, Array.Empty<HotReloadRefusedIntroducedType>()));

            Assert.That(message, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: a CS0246 error naming a type this run refused gets a note that quotes the refusal.
        /// </summary>
        [Test]
        public void AppendNotes_Cs0246NamingRefusedType_AppendsRefusedTypeNote()
        {
            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                "composed",
                new[] { RefusedTypeCs0246 },
                CreateRefusedTypeSources("Game.Units.Spawner"));

            Assert.That(message, Is.EqualTo("composed\n" + ExpectedRefusedTypeNote));
        }

        /// <summary>
        /// What: a CS0234 error naming a type this run refused gets the same note as CS0246.
        /// </summary>
        [Test]
        public void AppendNotes_Cs0234NamingRefusedType_AppendsRefusedTypeNote()
        {
            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                "composed",
                new[] { RefusedTypeCs0234 },
                CreateRefusedTypeSources("Game.Units.Spawner"));

            Assert.That(message, Is.EqualTo("composed\n" + ExpectedRefusedTypeNote));
        }

        /// <summary>
        /// What: a CS0426 error naming a nested type this run refused inside a compiled type gets
        /// the note, matched through the '/' the worker's metadata name nests with.
        /// </summary>
        [Test]
        public void AppendNotes_Cs0426NamingRefusedNestedType_AppendsRefusedTypeNote()
        {
            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                "composed",
                new[] { RefusedTypeCs0426 },
                CreateRefusedTypeSources("Game.Units.Barracks/Spawner"));

            Assert.That(message, Is.EqualTo("composed\n" + ExpectedRefusedTypeNote));
        }

        /// <summary>
        /// What: a CS0103 error, which a static member access on a refused type fails with, gets
        /// the note.
        /// </summary>
        [Test]
        public void AppendNotes_Cs0103NamingRefusedType_AppendsRefusedTypeNote()
        {
            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                "composed",
                new[] { RefusedTypeCs0103 },
                CreateRefusedTypeSources("Game.Units.Spawner"));

            Assert.That(message, Is.EqualTo("composed\n" + ExpectedRefusedTypeNote));
        }

        /// <summary>
        /// What: a CS0117 error, which a static member access on a refused type nested in a
        /// compiled type fails with, gets the note.
        /// </summary>
        [Test]
        public void AppendNotes_Cs0117NamingRefusedNestedType_AppendsRefusedTypeNote()
        {
            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                "composed",
                new[] { RefusedTypeCs0117 },
                CreateRefusedTypeSources("Game.Units.Barracks/Spawner"));

            Assert.That(message, Is.EqualTo("composed\n" + ExpectedRefusedTypeNote));
        }

        /// <summary>
        /// What: a CS0246 error whose name matches no refused type adds no note.
        /// </summary>
        [Test]
        public void AppendNotes_WhenNoRefusedTypeMatches_AppendsNothing()
        {
            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                "composed",
                new[] { RefusedTypeCs0246 },
                CreateRefusedTypeSources("Game.Units.Launcher"));

            Assert.That(message, Is.EqualTo("composed"));
        }

        /// <summary>
        /// What: the simple name of a refused type drops the namespace, the enclosing type in any
        /// separator form, and the generic arity.
        /// </summary>
        [TestCase("Game.Units.Spawner", "Spawner")]
        [TestCase("Game.Outer/Inner", "Inner")]
        [TestCase("Game.Outer+Inner", "Inner")]
        [TestCase("Game.Pool`1", "Pool")]
        [TestCase("Spawner", "Spawner")]
        public void ExtractSimpleTypeName_StripsQualifierAndArity(string metadataName, string expected)
        {
            Assert.That(HotReloadSkippedMemberCompileNote.ExtractSimpleTypeName(metadataName), Is.EqualTo(expected));
        }

        private const string RefusedTypeCs0246 =
            "CS0246: The type or namespace name 'Spawner' could not be found "
            + "(are you missing a using directive or an assembly reference?) (line 12)";

        private const string RefusedTypeCs0234 =
            "CS0234: The type or namespace name 'Spawner' does not exist in the namespace 'Game.Units' "
            + "(are you missing an assembly reference?) (line 12)";

        private const string RefusedTypeCs0426 =
            "CS0426: The type name 'Spawner' does not exist in the type 'Barracks' (line 12)";

        private const string RefusedTypeCs0103 =
            "CS0103: The name 'Spawner' does not exist in the current context (line 12)";

        private const string RefusedTypeCs0117 =
            "CS0117: 'Barracks' does not contain a definition for 'Spawner' (line 12)";

        private const string RefusedTypeNotice =
            "Unity object introduced type requires a compile: Game.Units.Spawner";

        private const string ExpectedRefusedTypeNote =
            "'Spawner' was refused by this hot reload run (" + RefusedTypeNotice
            + "), which is why this compile failed; run 'uloop compile'.";

        private static HotReloadCompileFailureNoteSources CreateRefusedTypeSources(string refusedMetadataName)
        {
            return new HotReloadCompileFailureNoteSources(
                Array.Empty<TransformWorkerSkippedDto>(),
                new[] { new HotReloadRefusedIntroducedType(refusedMetadataName, RefusedTypeNotice) });
        }
    }
}
