using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the record of open modal dialogs that uloop status reads off the main thread.
    /// </summary>
    [TestFixture]
    public sealed class EditorDialogTrackerTests
    {
        /// <summary>
        /// What: one shown dialog is reported with its title, message, and buttons unchanged.
        /// </summary>
        [Test]
        public void GetOpenDialogs_WhenOneDialogIsShown_ReportsItsContent()
        {
            EditorDialogTracker tracker = new EditorDialogTracker();

            tracker.RecordShown(7, "Save scene?", "Your changes will be lost.", new[] { "Save", "Don't Save", "Cancel" });

            EditorDialogInfo[] dialogs = tracker.GetOpenDialogs();
            Assert.That(dialogs, Has.Length.EqualTo(1));
            Assert.That(dialogs[0].Title, Is.EqualTo("Save scene?"));
            Assert.That(dialogs[0].Message, Is.EqualTo("Your changes will be lost."));
            Assert.That(dialogs[0].Buttons, Is.EqualTo(new[] { "Save", "Don't Save", "Cancel" }));
        }

        /// <summary>
        /// What: dialogs are reported in the order they were shown.
        /// </summary>
        [Test]
        public void GetOpenDialogs_WhenTwoDialogsAreShown_ReportsThemInShowOrder()
        {
            EditorDialogTracker tracker = new EditorDialogTracker();

            tracker.RecordShown(9, "First", "", new[] { "OK" });
            tracker.RecordShown(3, "Second", "", new[] { "OK" });

            EditorDialogInfo[] dialogs = tracker.GetOpenDialogs();
            Assert.That(dialogs, Has.Length.EqualTo(2));
            Assert.That(dialogs[0].Title, Is.EqualTo("First"));
            Assert.That(dialogs[1].Title, Is.EqualTo("Second"));
        }

        /// <summary>
        /// What: dismissing one dialog removes only that dialog.
        /// </summary>
        [Test]
        public void RecordDismissed_WhenOneOfTwoDialogsCloses_KeepsTheOther()
        {
            EditorDialogTracker tracker = new EditorDialogTracker();
            tracker.RecordShown(1, "First", "", new[] { "OK" });
            tracker.RecordShown(2, "Second", "", new[] { "OK" });

            tracker.RecordDismissed(1);

            EditorDialogInfo[] dialogs = tracker.GetOpenDialogs();
            Assert.That(dialogs, Has.Length.EqualTo(1));
            Assert.That(dialogs[0].Title, Is.EqualTo("Second"));
        }

        /// <summary>
        /// What: dismissing an id that was never shown changes nothing and does not throw.
        /// </summary>
        [Test]
        public void RecordDismissed_WhenIdIsUnknown_KeepsOpenDialogs()
        {
            EditorDialogTracker tracker = new EditorDialogTracker();
            tracker.RecordShown(1, "Open", "", new[] { "OK" });

            tracker.RecordDismissed(42);

            EditorDialogInfo[] dialogs = tracker.GetOpenDialogs();
            Assert.That(dialogs, Has.Length.EqualTo(1));
            Assert.That(dialogs[0].Title, Is.EqualTo("Open"));
        }

        /// <summary>
        /// What: a modal Editor window, which Unity reports without a message or buttons, is reported
        /// with an empty message and an empty button list.
        /// </summary>
        [Test]
        public void RecordShown_WhenMessageAndButtonsAreNull_ReportsEmptyValues()
        {
            EditorDialogTracker tracker = new EditorDialogTracker();

            tracker.RecordShown(5, "Modal window", null, null);

            EditorDialogInfo[] dialogs = tracker.GetOpenDialogs();
            Assert.That(dialogs, Has.Length.EqualTo(1));
            Assert.That(dialogs[0].Message, Is.EqualTo(string.Empty));
            Assert.That(dialogs[0].Buttons, Is.Empty);
        }

        /// <summary>
        /// What: showing the same id again replaces the earlier record instead of adding a second one.
        /// </summary>
        [Test]
        public void RecordShown_WhenIdIsShownAgain_ReplacesTheRecord()
        {
            EditorDialogTracker tracker = new EditorDialogTracker();
            tracker.RecordShown(4, "Before", "", new[] { "OK" });

            tracker.RecordShown(4, "After", "Updated", new List<string> { "Yes", "No" });

            EditorDialogInfo[] dialogs = tracker.GetOpenDialogs();
            Assert.That(dialogs, Has.Length.EqualTo(1));
            Assert.That(dialogs[0].Title, Is.EqualTo("After"));
            Assert.That(dialogs[0].Buttons, Is.EqualTo(new[] { "Yes", "No" }));
        }
    }
}
