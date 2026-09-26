using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The next steps the Editor appends to a skipped row whose type is built from source while
    /// compiled code still names the compiled copy. Each one holds in Edit Mode and in Play Mode.
    /// </summary>
    internal static class HotReloadSkippedNextStepText
    {
        public static string PassFiles(string files)
        {
            return "Pass " + files + " to this reload as well so both bind to the same type; "
                + "run 'uloop compile' only if it still does not bind.";
        }

        // Why not the holds-patches sentence: a file is in the reload whenever its state is not
        // NotInRun, which includes a file that was only carried in and holds nothing.
        public static string AlreadyInReload(IReadOnlyList<string> files)
        {
            bool single = files.Count == 1;
            return Quote(files) + (single ? " is" : " are") + " already in this reload, so passing "
                + (single ? "it" : "them") + " again does not help; run 'uloop compile'.";
        }

        public static string CompileHoldingPatches(string file)
        {
            return file + " also holds patches this or an earlier reload applied, so it cannot be left out; "
                + "run 'uloop compile'.";
        }

        public static string LeaveOut(string files)
        {
            return "To hot reload it without a compile, leave " + files + " out of --files and rerun (the edit in "
                + files + " waits for the next compile); or run 'uloop compile'.";
        }

        public static string UndoAndLeaveOut(string files)
        {
            return "To hot reload it without a compile, undo the edit in " + files + ", leave " + files
                + " out of --files, and rerun: " + files + " is carried in again while its source matches what "
                + "an earlier reload was given. Or run 'uloop compile'.";
        }

        public static string SameFixAsCarriedInRow(string method, string files)
        {
            return "The step on the Skipped row for " + method + " (leaving " + files + " out) resolves this "
                + "row as well; passing the file that declares the compiled signature rebinds only this member.";
        }

        public static string Quote(IReadOnlyList<string> files)
        {
            List<string> quoted = new List<string>();
            foreach (string file in files)
            {
                quoted.Add("'" + file + "'");
            }

            return string.Join(" and ", quoted);
        }
    }
}
