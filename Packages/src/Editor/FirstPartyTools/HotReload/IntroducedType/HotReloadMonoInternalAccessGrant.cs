using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Validates Mono's native method layout before granting access to a generated assembly.
    /// </summary>
    internal sealed class HotReloadMonoInternalAccessGrant : IHotReloadInternalAccessGrant
    {
        private const BindingFlags AllDeclared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private readonly HotReloadMonoMethodLayout layout;

        public bool IsAvailable { get; }
        public string UnavailableReason { get; }

        internal HotReloadMonoInternalAccessGrant(HotReloadMonoMethodLayout layout)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
            UnavailableReason = Probe();
            IsAvailable = string.IsNullOrEmpty(UnavailableReason);
        }

        internal static HotReloadMonoInternalAccessGrant ForThisProcess()
        {
            return new HotReloadMonoInternalAccessGrant(new HotReloadMonoMethodLayout());
        }

        public HotReloadInternalAccessGrantResult Grant(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (!IsAvailable)
            {
                throw new InvalidOperationException(UnavailableReason);
            }

            List<MethodBase> methods;
            try
            {
                methods = CollectMethods(assembly);
            }
            catch (ReflectionTypeLoadException exception)
            {
                return HotReloadInternalAccessGrantResult.Refused(
                    "Artifact types could not be enumerated: " + exception.Message);
            }

            List<IntPtr> records = new List<IntPtr>(methods.Count);
            foreach (MethodBase method in methods)
            {
                if (!TryValidateRecord(method, out IntPtr record, out string reason))
                {
                    return HotReloadInternalAccessGrantResult.Refused(reason);
                }

                records.Add(record);
            }

            // No artifact code or await belongs between validation and these writes. A later
            // invalid record must never leave earlier methods partially granted.
            foreach (IntPtr record in records)
            {
                int word = Marshal.ReadInt32(record, layout.BitfieldOffset);
                ushort implementationFlags = (ushort)Marshal.ReadInt16(record, layout.ImplementationFlagsOffset);
                Marshal.WriteInt32(record, layout.BitfieldOffset, word | HotReloadMonoMethodLayout.SkipVisibilityBit);
                // Inlining into an ungranted caller would re-check access in the caller's context.
                Marshal.WriteInt16(record, layout.ImplementationFlagsOffset,
                    unchecked((short)(implementationFlags | (ushort)MethodImplAttributes.NoInlining)));
            }

            return HotReloadInternalAccessGrantResult.Granted(records.Count);
        }

        private string Probe()
        {
            if (Type.GetType("Mono.Runtime") == null || IntPtr.Size != 8)
            {
                return "Internal access grants require the supported 64-bit Mono method layout.";
            }

            MethodInfo plain = typeof(ProbeFixture).GetMethod(nameof(ProbeFixture.Plain), AllDeclared);
            MethodInfo definition = typeof(ProbeFixture).GetMethod(nameof(ProbeFixture.Echo), AllDeclared);
            MethodInfo constructed = definition.MakeGenericMethod(typeof(int));
            string reason = ValidateProbeMethod(plain, false, false);
            if (!string.IsNullOrEmpty(reason))
            {
                return reason;
            }

            reason = ValidateProbeMethod(definition, true, false);
            return string.IsNullOrEmpty(reason)
                ? ValidateProbeMethod(constructed, null, true)
                : reason;
        }

        private string ValidateProbeMethod(MethodInfo method, bool? generic, bool inflated)
        {
            if (!TryValidateRecord(method, out IntPtr record, out string reason))
            {
                return reason;
            }

            int word = Marshal.ReadInt32(record, layout.BitfieldOffset);
            if (generic.HasValue && ((word & HotReloadMonoMethodLayout.GenericBit) != 0) != generic.Value)
            {
                return DescribeMismatch(method, "generic bit");
            }

            if (((word & HotReloadMonoMethodLayout.InflatedBit) != 0) != inflated)
            {
                return DescribeMismatch(method, "inflated bit");
            }

            return (word & HotReloadMonoMethodLayout.SkipVisibilityBit) == 0
                ? string.Empty
                : DescribeMismatch(method, "skip-visibility bit");
        }

        private bool TryValidateRecord(MethodBase method, out IntPtr record, out string reason)
        {
            record = method.MethodHandle.Value;
            if (record == IntPtr.Zero)
            {
                reason = DescribeMismatch(method, "method handle");
                return false;
            }

            // Header checks precede the pointer read so a changed header stops the probe before
            // dereferencing the expected name field of an unknown layout.
            if ((ushort)Marshal.ReadInt16(record, layout.FlagsOffset) != (ushort)method.Attributes)
            {
                reason = DescribeMismatch(method, "flags");
                return false;
            }

            if ((ushort)Marshal.ReadInt16(record, layout.ImplementationFlagsOffset)
                != (ushort)method.MethodImplementationFlags)
            {
                reason = DescribeMismatch(method, "implementation flags");
                return false;
            }

            if (Marshal.ReadInt32(record, layout.TokenOffset) != method.MetadataToken)
            {
                reason = DescribeMismatch(method, "token");
                return false;
            }

            IntPtr namePointer = Marshal.ReadIntPtr(record, layout.NameOffset);
            if (!TryReadName(namePointer, out string name) || !string.Equals(name, method.Name, StringComparison.Ordinal))
            {
                reason = DescribeMismatch(method, "UTF-8 name");
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool TryReadName(IntPtr pointer, out string name)
        {
            name = string.Empty;
            if (pointer == IntPtr.Zero)
            {
                return false;
            }

            byte[] bytes = new byte[layout.MaxNameBytes];
            for (int index = 0; index < bytes.Length; index++)
            {
                byte value = Marshal.ReadByte(pointer, index);
                if (value == 0)
                {
                    // Mono stores UTF-8 even when Windows' ANSI code page is not UTF-8.
                    name = Encoding.UTF8.GetString(bytes, 0, index);
                    return true;
                }

                bytes[index] = value;
            }

            return false;
        }

        private static List<MethodBase> CollectMethods(Assembly assembly)
        {
            List<MethodBase> methods = new List<MethodBase>();
            foreach (Type type in assembly.GetTypes())
            {
                methods.AddRange(type.GetMethods(AllDeclared));
                // Static constructors and compiler-generated types must not escape the grant.
                methods.AddRange(type.GetConstructors(AllDeclared));
            }

            return methods;
        }

        private static string DescribeMismatch(MethodBase method, string field)
        {
            return "Mono method layout mismatch for " + method.Name + ": " + field + ".";
        }

        // The short names allow a bounded-name refusal test to pass the probe but reject an
        // artifact's longer name. Reflection only reads these methods; the probe never calls them.
        private static class ProbeFixture
        {
            internal static int Plain() => 1;
            internal static T Echo<T>(T value) => value;
        }
    }
}
