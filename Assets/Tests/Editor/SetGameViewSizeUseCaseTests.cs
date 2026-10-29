using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the set-game-view-size use case against a fake Editor port, without a Game View.
    /// </summary>
    public sealed class SetGameViewSizeUseCaseTests
    {
        private static SetGameViewSizeSchema Request(int? width, int? height)
        {
            return new SetGameViewSizeSchema { Width = width, Height = height };
        }

        private static SetGameViewSizeSchema ResetRequest()
        {
            return new SetGameViewSizeSchema { Reset = true };
        }

        private static List<(string DisplayText, uint Width, uint Height)> SizesWithOwnedThenCustomB()
        {
            return new List<(string DisplayText, uint Width, uint Height)>
            {
                ("Free Aspect", 800, 600),
                ("Full HD (1920x1080)", 1920, 1080),
                ("uloop (1280x720)", 1280, 720),
                ("Custom B (640x480)", 640, 480)
            };
        }

        private static SetGameViewSizeResponse Execute(FakeSetGameViewSizePort port, SetGameViewSizeSchema schema)
        {
            return new SetGameViewSizeUseCase(port).Execute(schema);
        }

        [Test]
        public void Execute_ReadOnGameView_ReportsResolutionAndSelectionWithoutChanging()
        {
            // Verifies reading on a Game View reports its size and selected entry and touches nothing.
            FakeSetGameViewSizePort port = new();

            SetGameViewSizeResponse response = Execute(port, Request(null, null));

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("GameView"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((1920u, 1080u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((1920u, 1080u)));
            Assert.That(response.Changed, Is.False);
            Assert.That(response.SelectedSize, Is.EqualTo("Full HD (1920x1080)"));
            Assert.That(response.Removed, Is.Null);
            Assert.That(port.Calls, Is.Empty);
            Assert.That(port.Remembered, Is.Null);
        }

        [Test]
        public void Execute_ReadOnSimulator_ReportsSimulatorResolutionWithoutChanging()
        {
            // Verifies reading under the Device Simulator reports the Simulator's size as such and touches nothing.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.Simulator };

            SetGameViewSizeResponse response = Execute(port, Request(null, null));

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("Simulator"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((2360u, 1640u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((2360u, 1640u)));
            Assert.That(response.SelectedSize, Is.Null);
            Assert.That(port.Calls, Is.Empty);
        }

        [Test]
        public void Execute_ReadOnOtherView_ReportsWithoutChanging()
        {
            // Verifies reading under a third-party Play Mode view reports it as Other and touches nothing.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.Other };

            SetGameViewSizeResponse response = Execute(port, Request(null, null));

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("Other"));
            Assert.That(port.Calls, Is.Empty);
        }

        [Test]
        public void Execute_ReadWithoutView_ReportsNoneWithoutOpeningAWindow()
        {
            // Verifies reading with no Play Mode view reports None at 0x0 without asking for the resolution, which would open a window.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.None };

            SetGameViewSizeResponse response = Execute(port, Request(null, null));

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("None"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((0u, 0u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((0u, 0u)));
            Assert.That(response.SelectedSize, Is.Null);
            Assert.That(port.Calls, Is.Empty);
        }

        [Test]
        public void Execute_WidthWithoutHeight_IsRejectedWithoutChanging()
        {
            // Verifies a width without a height is rejected and nothing is applied or remembered.
            FakeSetGameViewSizePort port = new();

            SetGameViewSizeResponse response = Execute(port, Request(1280, null));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Width and Height must be provided together."));
            Assert.That(port.Calls, Is.Empty);
            Assert.That(port.Remembered, Is.Null);
        }

        [Test]
        public void Execute_NonPositiveSize_IsRejectedWithoutChanging()
        {
            // Verifies a non-positive size is rejected and nothing is applied.
            FakeSetGameViewSizePort port = new();

            SetGameViewSizeResponse response = Execute(port, Request(0, 720));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Width and Height must be positive integers."));
            Assert.That(port.Calls, Is.Empty);
        }

        [Test]
        public void Execute_ResetWithSize_IsRejectedWithoutChanging()
        {
            // Verifies --reset combined with a size is rejected before anything is removed.
            FakeSetGameViewSizePort port = new() { OwnedEntriesInOtherGroups = 1 };

            SetGameViewSizeResponse response = Execute(
                port,
                new SetGameViewSizeSchema { Reset = true, Width = 1280, Height = 720 });

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("--reset cannot be combined with --width or --height."));
            Assert.That(port.Calls, Is.Empty);
            Assert.That(response.Removed, Is.Null);
        }

        [Test]
        public void Execute_SetOnGameView_AppliesSelectsOwnedEntryAndRemembersSelection()
        {
            // Verifies a size change on a Game View selects the owned entry, remembers the user's selection, and warns that the entry persists.
            FakeSetGameViewSizePort port = new();

            SetGameViewSizeResponse response = Execute(port, Request(1280, 720));

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("GameView"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((1920u, 1080u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((1280u, 720u)));
            Assert.That(response.Changed, Is.True);
            Assert.That(response.SelectedSize, Is.EqualTo("uloop (1280x720)"));
            Assert.That(port.Remembered, Is.EqualTo("Full HD (1920x1080)"));
            Assert.That(port.CountCalls("Apply"), Is.EqualTo(1));
            Assert.That(response.Warning, Does.Contain("--reset"));
            Assert.That(
                response.Message,
                Is.EqualTo("Game View rendering resolution changed from 1920x1080 to 1280x720."));
        }

        [Test]
        public void Execute_SetSameSizeOnGameView_ReportsAlreadyButStillSelectsOwnedEntry()
        {
            // Verifies setting the size already rendered still reports that the owned entry is now selected.
            FakeSetGameViewSizePort port = new();

            SetGameViewSizeResponse response = Execute(port, Request(1920, 1080));

            Assert.That(response.Success, Is.True);
            Assert.That(response.Changed, Is.False);
            Assert.That(response.Message, Is.EqualTo("Game View rendering resolution is already 1920x1080."));
            Assert.That(response.SelectedSize, Is.EqualTo("uloop (1920x1080)"));
            Assert.That(port.Remembered, Is.EqualTo("Full HD (1920x1080)"));
            Assert.That(response.Warning, Is.Not.Empty);
        }

        [Test]
        public void Execute_SetWhileOwnedEntrySelected_KeepsRememberedSelection()
        {
            // Verifies a second change while the owned entry is selected does not overwrite the remembered user selection.
            FakeSetGameViewSizePort port = new() { Remembered = "Full HD (1920x1080)" };
            port.Sizes.Add(("uloop (1280x720)", 1280, 720));
            port.SelectedIndex = 2;

            SetGameViewSizeResponse response = Execute(port, Request(800, 600));

            Assert.That(port.Remembered, Is.EqualTo("Full HD (1920x1080)"));
            Assert.That(response.SelectedSize, Is.EqualTo("uloop (800x600)"));
            Assert.That(response.Changed, Is.True);
        }

        [Test]
        public void Execute_SetWithUserEntryNamedLikeOurs_LeavesItAloneAndRemembersIt()
        {
            // Verifies a user entry whose name only starts with "uloop" is neither overwritten by a change nor removed by --reset.
            FakeSetGameViewSizePort port = new()
            {
                Sizes = new List<(string DisplayText, uint Width, uint Height)>
                {
                    ("Free Aspect", 800, 600),
                    ("uloop-phone (390x844)", 390, 844),
                    ("Full HD (1920x1080)", 1920, 1080)
                },
                SelectedIndex = 1
            };

            SetGameViewSizeResponse setResponse = Execute(port, Request(1280, 720));

            Assert.That(port.Sizes[1], Is.EqualTo(("uloop-phone (390x844)", 390u, 844u)));
            Assert.That(port.Sizes[^1].DisplayText, Is.EqualTo("uloop (1280x720)"));
            Assert.That(setResponse.SelectedSize, Is.EqualTo("uloop (1280x720)"));
            Assert.That(port.Remembered, Is.EqualTo("uloop-phone (390x844)"));

            SetGameViewSizeResponse resetResponse = Execute(port, ResetRequest());

            Assert.That(resetResponse.Removed, Is.EqualTo(1));
            Assert.That(port.Sizes.Any(size => size.DisplayText == "uloop-phone (390x844)"), Is.True);
            Assert.That(resetResponse.SelectedSize, Is.EqualTo("uloop-phone (390x844)"));
        }

        [Test]
        public void Execute_SetOnSimulator_FailsWithoutChanging()
        {
            // Verifies a size change under the Device Simulator fails, applies nothing, and points to game-view-mode.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.Simulator };

            SetGameViewSizeResponse response = Execute(port, Request(1280, 720));

            Assert.That(response.Success, Is.False);
            Assert.That(response.View, Is.EqualTo("Simulator"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((2360u, 1640u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((2360u, 1640u)));
            Assert.That(response.Changed, Is.False);
            Assert.That(response.Message, Does.Contain("game-view-mode --to GameView"));
            Assert.That(response.Message, Does.Contain("--device"));
            Assert.That(port.Calls, Is.Empty);
            Assert.That(port.Remembered, Is.Null);
        }

        [Test]
        public void Execute_SetOnOtherView_FailsWithoutChanging()
        {
            // Verifies a size change under a third-party Play Mode view fails and applies nothing.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.Other };

            SetGameViewSizeResponse response = Execute(port, Request(1280, 720));

            Assert.That(response.Success, Is.False);
            Assert.That(response.View, Is.EqualTo("Other"));
            Assert.That(port.Calls, Is.Empty);
        }

        [Test]
        public void Execute_SetWithoutView_OpensGameViewAndApplies()
        {
            // Verifies a size change with no Play Mode view opens a Game View, applies the size, and says a view was opened.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.None };

            SetGameViewSizeResponse response = Execute(port, Request(1280, 720));

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("GameView"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((0u, 0u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((1280u, 720u)));
            Assert.That(response.Changed, Is.True);
            Assert.That(response.SelectedSize, Is.EqualTo("uloop (1280x720)"));
            Assert.That(port.Remembered, Is.Null);
            Assert.That(
                response.Message,
                Is.EqualTo("No Play Mode view was open, so a Game View was opened with a rendering resolution of 1280x720."));
            Assert.That(response.Warning, Is.Not.Empty);
        }

        [Test]
        public void Execute_ResetWhileOwnedEntrySelected_RestoresRememberedSelection()
        {
            // Verifies --reset with the owned entry selected removes every owned entry and re-selects the remembered user selection.
            FakeSetGameViewSizePort port = new()
            {
                Sizes = SizesWithOwnedThenCustomB(),
                SelectedIndex = 2,
                Remembered = "Full HD (1920x1080)",
                OwnedEntriesInOtherGroups = 1
            };

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.Success, Is.True);
            Assert.That(response.Removed, Is.EqualTo(2));
            Assert.That(response.SelectedSize, Is.EqualTo("Full HD (1920x1080)"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((1280u, 720u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((1920u, 1080u)));
            Assert.That(response.Changed, Is.True);
            Assert.That(port.CountCalls("SelectFirstSize"), Is.Zero);
            Assert.That(port.Remembered, Is.Null);
            Assert.That(port.Sizes.Any(size => size.DisplayText.StartsWith("uloop", StringComparison.Ordinal)), Is.False);
            Assert.That(response.Message, Does.Contain("Restored the previous selection 'Full HD (1920x1080)'"));
        }

        [Test]
        public void Execute_ResetWithoutRememberedSelection_SelectsFirstSize()
        {
            // Verifies --reset with the owned entry selected and no remembered selection falls back to the first entry.
            FakeSetGameViewSizePort port = new() { Sizes = SizesWithOwnedThenCustomB(), SelectedIndex = 2 };

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.SelectedSize, Is.EqualTo("Free Aspect"));
            Assert.That(port.CountCalls("SelectFirstSize"), Is.EqualTo(1));
            Assert.That(response.Message, Does.Contain("unknown"));
        }

        [Test]
        public void Execute_ResetWhenRememberedSelectionIsGone_SelectsFirstSize()
        {
            // Verifies --reset falls back to the first entry when the remembered selection no longer exists.
            FakeSetGameViewSizePort port = new()
            {
                Sizes = SizesWithOwnedThenCustomB(),
                SelectedIndex = 2,
                Remembered = "Custom A (320x240)"
            };

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.SelectedSize, Is.EqualTo("Free Aspect"));
            Assert.That(port.CountCalls("SelectFirstSize"), Is.EqualTo(1));
            Assert.That(response.Message, Does.Contain("no longer exists"));
            Assert.That(port.Remembered, Is.Null);
        }

        [Test]
        public void Execute_ResetWhileUserSizeSelected_RemovesEntriesAndKeepsSelection()
        {
            // Verifies --reset keeps the user's own selection by re-selecting it by name after the removal shifted its index.
            FakeSetGameViewSizePort port = new()
            {
                Sizes = new List<(string DisplayText, uint Width, uint Height)>
                {
                    ("Free Aspect", 800, 600),
                    ("Full HD (1920x1080)", 1920, 1080),
                    ("uloop (1280x720)", 1280, 720),
                    ("Custom A (640x480)", 640, 480),
                    ("Custom B (320x240)", 320, 240)
                },
                SelectedIndex = 3,
                Remembered = "Free Aspect"
            };

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.Removed, Is.EqualTo(1));
            Assert.That(response.SelectedSize, Is.EqualTo("Custom A (640x480)"));
            Assert.That(response.Changed, Is.False);
            Assert.That(port.CountCalls("TrySelectSize Custom A"), Is.EqualTo(1));
            Assert.That(port.Remembered, Is.Null);
            Assert.That(response.Message, Does.Contain("was kept"));
        }

        [Test]
        public void Execute_ResetWithoutOwnedEntries_ReportsNothingToRemove()
        {
            // Verifies --reset with no owned entry removes nothing, leaves the selection alone, and says so.
            FakeSetGameViewSizePort port = new();

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.Success, Is.True);
            Assert.That(response.Removed, Is.EqualTo(0));
            Assert.That(response.SelectedSize, Is.EqualTo("Full HD (1920x1080)"));
            Assert.That(response.Changed, Is.False);
            Assert.That(port.CountCalls("TrySelectSize"), Is.Zero);
            Assert.That(response.Message, Does.Contain("No 'uloop' size entry"));
        }

        [Test]
        public void Execute_ResetOnSimulator_FailsWithoutRemoving()
        {
            // Verifies --reset under the Device Simulator fails and leaves the entries and the remembered selection untouched.
            FakeSetGameViewSizePort port = new()
            {
                Kind = PlayModeViewKind.Simulator,
                OwnedEntriesInOtherGroups = 1,
                Remembered = "Free Aspect"
            };

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.Success, Is.False);
            Assert.That(response.View, Is.EqualTo("Simulator"));
            Assert.That(response.Removed, Is.Null);
            Assert.That(port.CountCalls("RemoveOwnedSizes"), Is.Zero);
            Assert.That(port.Remembered, Is.EqualTo("Free Aspect"));
            Assert.That(response.Message, Does.Contain("game-view-mode --to GameView"));
        }

        [Test]
        public void Execute_ResetWithoutView_RemovesEntriesWithoutRestoring()
        {
            // Verifies --reset with no Play Mode view removes the entries, clears the memory, and opens nothing.
            FakeSetGameViewSizePort port = new()
            {
                Kind = PlayModeViewKind.None,
                OwnedEntriesInOtherGroups = 2,
                Remembered = "Free Aspect"
            };

            SetGameViewSizeResponse response = Execute(port, ResetRequest());

            Assert.That(response.Success, Is.True);
            Assert.That(response.View, Is.EqualTo("None"));
            Assert.That(response.Removed, Is.EqualTo(2));
            Assert.That(response.SelectedSize, Is.Null);
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((0u, 0u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((0u, 0u)));
            Assert.That(port.Remembered, Is.Null);
            Assert.That(response.Message, Does.Contain("no selection to restore"));
        }

        [Test]
        public void Execute_WidthWithoutHeightWithoutView_IsRejectedWithoutOpeningAWindow()
        {
            // Verifies a rejected request with no Play Mode view reports 0x0 without asking for the resolution, which would open a window.
            FakeSetGameViewSizePort port = new() { Kind = PlayModeViewKind.None };

            SetGameViewSizeResponse response = Execute(port, Request(1280, null));

            Assert.That(response.Success, Is.False);
            Assert.That(response.View, Is.EqualTo("None"));
            Assert.That((response.PreviousWidth, response.PreviousHeight), Is.EqualTo((0u, 0u)));
            Assert.That((response.CurrentWidth, response.CurrentHeight), Is.EqualTo((0u, 0u)));
            Assert.That(response.Message, Is.EqualTo("Width and Height must be provided together."));
            Assert.That(port.Calls, Is.Empty);
        }

        /// <summary>
        /// Minimal stand-in for the Game View size list of the current platform group. Precondition
        /// violations throw so a use case that ignores the view kind fails the test.
        /// </summary>
        private sealed class FakeSetGameViewSizePort : ISetGameViewSizePort
        {
            public PlayModeViewKind Kind { get; set; } = PlayModeViewKind.GameView;

            // Entries of the current platform group in dropdown order.
            public List<(string DisplayText, uint Width, uint Height)> Sizes { get; set; } = new()
            {
                ("Free Aspect", 800, 600),
                ("Full HD (1920x1080)", 1920, 1080)
            };

            public int SelectedIndex { get; set; } = 1;

            // What the Simulator or a third-party view renders at.
            public (uint Width, uint Height) NonGameViewResolution { get; set; } = (2360, 1640);

            // Owned entries in the other platform groups, counted by RemoveOwnedSizes.
            public int OwnedEntriesInOtherGroups { get; set; }

            public string Remembered { get; set; }

            public List<string> Calls { get; } = new();

            public int CountCalls(string prefix)
            {
                return Calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));
            }

            public PlayModeViewKind GetMainViewKind()
            {
                return Kind;
            }

            public (uint Width, uint Height) GetRenderingResolution()
            {
                if (Kind == PlayModeViewKind.None)
                {
                    // Unity opens a Game View here, so a call means the use case would have opened a window.
                    throw new InvalidOperationException("Reading the resolution without a view opens a window.");
                }

                if (Kind != PlayModeViewKind.GameView)
                {
                    return NonGameViewResolution;
                }

                return (Sizes[SelectedIndex].Width, Sizes[SelectedIndex].Height);
            }

            public void ApplyCustomResolution(uint width, uint height)
            {
                Calls.Add($"Apply {width}x{height}");
                if (Kind == PlayModeViewKind.Simulator || Kind == PlayModeViewKind.Other)
                {
                    throw new InvalidOperationException("Custom resolution applies only to a Game View.");
                }

                Kind = PlayModeViewKind.GameView;
                // "uloop (" is the display form of the owned entry: exact base text and a fixed resolution.
                int index = Sizes.FindIndex(size => size.DisplayText.StartsWith("uloop (", StringComparison.Ordinal));
                (string DisplayText, uint Width, uint Height) entry = ($"uloop ({width}x{height})", width, height);
                if (index < 0)
                {
                    Sizes.Add(entry);
                    index = Sizes.Count - 1;
                }
                else
                {
                    Sizes[index] = entry;
                }

                SelectedIndex = index;
            }

            public string GetSelectedSizeDisplayText()
            {
                RequireGameView();
                return Sizes[SelectedIndex].DisplayText;
            }

            public string GetSelectedSizeBaseText()
            {
                RequireGameView();
                string displayText = Sizes[SelectedIndex].DisplayText;
                return displayText.StartsWith("uloop (", StringComparison.Ordinal) ? "uloop" : displayText;
            }

            public int RemoveOwnedSizes()
            {
                Calls.Add("RemoveOwnedSizes");
                int removed = Sizes.RemoveAll(size => size.DisplayText.StartsWith("uloop (", StringComparison.Ordinal))
                    + OwnedEntriesInOtherGroups;
                OwnedEntriesInOtherGroups = 0;
                // Same clamp as Unity's EnsureSelectedSizeAreValid.
                SelectedIndex = Math.Clamp(SelectedIndex, 0, Sizes.Count - 1);
                return removed;
            }

            public bool TrySelectSize(string displayText)
            {
                RequireGameView();
                Calls.Add($"TrySelectSize {displayText}");
                int index = Sizes.FindIndex(size => size.DisplayText == displayText);
                if (index < 0)
                {
                    return false;
                }

                SelectedIndex = index;
                return true;
            }

            public string SelectFirstSize()
            {
                RequireGameView();
                Calls.Add("SelectFirstSize");
                SelectedIndex = 0;
                return Sizes[0].DisplayText;
            }

            public string ReadRememberedSelection()
            {
                return Remembered;
            }

            public void RememberSelection(string displayText)
            {
                Remembered = displayText;
            }

            public void ClearRememberedSelection()
            {
                Remembered = null;
            }

            private void RequireGameView()
            {
                if (Kind != PlayModeViewKind.GameView)
                {
                    throw new InvalidOperationException("The main Play Mode view is not a Game View.");
                }
            }
        }
    }
}
