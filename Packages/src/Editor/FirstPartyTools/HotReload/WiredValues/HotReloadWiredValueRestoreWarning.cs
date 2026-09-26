using System;
using System.Collections.Generic;
using System.Globalization;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns wired values that a scene reload did not restore into response rows and the warning
    /// line that names them with the reason each did not come back.
    /// </summary>
    internal static class HotReloadWiredValueRestoreWarning
    {
        internal static void Append(List<string> warnings, IReadOnlyList<HotReloadWiredValueRestoreFailure> failures)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (failures == null)
            {
                throw new ArgumentNullException(nameof(failures));
            }

            if (failures.Count == 0)
            {
                return;
            }

            List<string> items = new List<string>(failures.Count);
            foreach (HotReloadWiredValueRestoreFailure failure in failures)
            {
                items.Add(DescribeField(failure.StoreFieldKey) + " on " + failure.HostIdentity + ": " + failure.Reason);
            }

            warnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.WiredValueNotRestoredWarningFormat,
                    string.Join("; ", items)));
        }

        internal static List<HotReloadUnrestoredWiredValue> BuildRows(
            IReadOnlyList<HotReloadWiredValueRestoreFailure> failures)
        {
            List<HotReloadUnrestoredWiredValue> rows = new List<HotReloadUnrestoredWiredValue>(failures.Count);
            foreach (HotReloadWiredValueRestoreFailure failure in failures)
            {
                rows.Add(
                    new HotReloadUnrestoredWiredValue
                    {
                        Host = failure.HostIdentity,
                        Field = DescribeField(failure.StoreFieldKey),
                        Reason = failure.Reason
                    });
            }

            return rows;
        }

        // Why the reflection form: the store key nests types with '/', while the added-field rows
        // of the same response name types the way reflection and the reader's code do.
        private static string DescribeField(string storeFieldKey)
        {
            int separator = storeFieldKey.LastIndexOf(HotReloadAddedFieldStore.FieldKeySeparator, StringComparison.Ordinal);
            if (separator < 0)
            {
                return storeFieldKey;
            }

            string typeName = storeFieldKey.Substring(0, separator).Replace('/', '+');
            string fieldName = storeFieldKey.Substring(separator + HotReloadAddedFieldStore.FieldKeySeparator.Length);
            return typeName + "." + fieldName;
        }
    }
}
