using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.InternalAPIBridge
{
    /// <summary>
    /// Reads and edits the Game View size list and the Game View's selected entry. GameViewSizes,
    /// GameViewSizeGroup, and GameView are internal Editor types reached directly through the
    /// friend-assembly grant; no reflection.
    /// </summary>
    public static class GameViewSizeListBridge
    {
        /// <summary>Name of the platform group of the active build target, e.g. "Standalone".</summary>
        public static string GetCurrentGroupName()
        {
            return GameViewSizes.instance.currentGroupType.ToString();
        }

        /// <summary>Display text of the selected entry. Precondition: the main view is a Game View.</summary>
        public static string GetSelectedSizeDisplayText()
        {
            return GetSelectedSize(RequireGameView()).displayText;
        }

        /// <summary>Base text of the selected entry. Precondition: the main view is a Game View.</summary>
        public static string GetSelectedSizeBaseText()
        {
            return GetSelectedSize(RequireGameView()).baseText;
        }

        /// <summary>
        /// Whether a size entry is one the tool owns: a fixed resolution whose base text equals the owned
        /// base text exactly. A prefix match would also claim user entries such as "uloop-phone".
        /// </summary>
        public static bool IsOwnedSize(string candidateBaseText, bool isFixedResolution, string ownedBaseText)
        {
            return isFixedResolution && string.Equals(candidateBaseText, ownedBaseText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Sets the owned entry of the current group to width x height, adding it when missing, saves the
        /// list, and selects the entry, opening a Game View when no Play Mode view exists.
        /// Precondition: the main view is a Game View or none exists.
        /// </summary>
        public static void ApplyCustomSize(uint width, uint height, string baseText)
        {
            Debug.Assert(!string.IsNullOrEmpty(baseText), "baseText must not be empty");
            PlayModeView mainView = PlayModeView.GetMainPlayModeView();
            Debug.Assert(mainView == null || mainView is GameView, "the main Play Mode view must be a Game View or absent");
            // Same fallback as PlayModeWindow.SetCustomRenderingResolution when no Play Mode view exists.
            GameView gameView = mainView as GameView ?? EditorWindow.CreateWindow<GameView>();

            // Mirrors GameView.SetCustomResolution, except that Unity matches the first entry whose display
            // text starts with the base name and would overwrite a user entry such as "uloop-phone".
            GameViewSizeGroup group = GameViewSizes.instance.currentGroup;
            int index = FindOwnedCustomSize(group, baseText);
            if (index >= 0)
            {
                GameViewSize size = group.GetGameViewSize(index);
                size.width = (int)width;
                size.height = (int)height;
            }
            else
            {
                group.AddCustomSize(new GameViewSize(GameViewSizeType.FixedResolution, (int)width, (int)height, baseText));
                index = group.GetTotalCount() - 1;
            }

            GameViewSizes.instance.SaveToHDD();
            SelectIndex(gameView, index);
        }

        /// <summary>
        /// Removes every fixed-resolution custom entry whose base text equals the argument from every
        /// platform group, saves the list when any was removed, and returns the count.
        /// </summary>
        public static int RemoveCustomSizes(string baseText)
        {
            Debug.Assert(!string.IsNullOrEmpty(baseText), "baseText must not be empty");

            int removed = 0;
            foreach (GameViewSizeGroup group in DistinctGroups())
            {
                // Walk backwards so removing an entry does not shift the indices still to be visited.
                for (int index = group.GetTotalCount() - 1; index >= group.GetBuiltinCount(); index--)
                {
                    GameViewSize size = group.GetGameViewSize(index);
                    if (!IsOwnedSize(size.baseText, size.sizeType == GameViewSizeType.FixedResolution, baseText))
                    {
                        continue;
                    }

                    group.RemoveCustomSize(index);
                    removed++;
                }
            }

            if (removed > 0)
            {
                GameViewSizes.instance.SaveToHDD();
            }

            return removed;
        }

        /// <summary>
        /// Selects the entry of the current group whose display text matches. Precondition: the main
        /// view is a Game View. Returns false, changing nothing, when no entry matches.
        /// </summary>
        public static bool TrySelectSize(string displayText)
        {
            GameView gameView = RequireGameView();
            GameViewSizeGroup group = GameViewSizes.instance.currentGroup;
            for (int index = 0; index < group.GetTotalCount(); index++)
            {
                if (group.GetGameViewSize(index).displayText != displayText)
                {
                    continue;
                }

                SelectIndex(gameView, index);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Selects the first entry of the current group and returns its display text. Precondition:
        /// the main view is a Game View.
        /// </summary>
        public static string SelectFirstSize()
        {
            GameView gameView = RequireGameView();
            SelectIndex(gameView, 0);
            return GameViewSizes.instance.currentGroup.GetGameViewSize(0).displayText;
        }

        private static int FindOwnedCustomSize(GameViewSizeGroup group, string baseText)
        {
            for (int index = group.GetBuiltinCount(); index < group.GetTotalCount(); index++)
            {
                GameViewSize size = group.GetGameViewSize(index);
                if (IsOwnedSize(size.baseText, size.sizeType == GameViewSizeType.FixedResolution, baseText))
                {
                    return index;
                }
            }

            return -1;
        }

        private static GameView RequireGameView()
        {
            GameView gameView = PlayModeView.GetMainPlayModeView() as GameView;
            if (gameView == null)
            {
                throw new InvalidOperationException("The main Play Mode view is not a Game View.");
            }

            return gameView;
        }

        private static GameViewSize GetSelectedSize(GameView gameView)
        {
            return GameViewSizes.instance.currentGroup.GetGameViewSize(gameView.selectedSizeIndex);
        }

        private static List<GameViewSizeGroup> DistinctGroups()
        {
            List<GameViewSizeGroup> groups = new();
            foreach (GameViewSizeGroupType type in Enum.GetValues(typeof(GameViewSizeGroupType)))
            {
                GameViewSizeGroup group = GameViewSizes.instance.GetGroup(type);
                // Obsolete members map to the Standalone group, so the same group comes back more than once.
                if (!groups.Contains(group))
                {
                    groups.Add(group);
                }
            }

            return groups;
        }

        private static void SelectIndex(GameView gameView, int index)
        {
            gameView.SizeSelectionCallback(index, null);
            // SizeSelectionCallback returns early when the index is unchanged, which happens after a removal
            // shifted the list, so the cached rendering size is refreshed here unconditionally.
            gameView.targetSize = gameView.targetRenderSize;
            gameView.Repaint();
        }
    }
}
