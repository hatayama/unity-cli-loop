using System;
using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// The checked way to put a value into a field hot reload added, and to read back what is
    /// stored. Meant for a caller outside the reloaded code - an execute-dynamic-code snippet
    /// wiring a scene reference into an added <c>[SerializeField]</c> before a compile exists.
    /// </summary>
    /// <remarks>
    /// Why not call <see cref="HotReloadAddedFieldStore"/> directly: its slots are untyped, so a
    /// misspelled field name, a value of the wrong type, or a static/instance mix-up is accepted
    /// in silence and the reading shim simply overwrites it with the field's initializer on the
    /// next read. Every refusal here happens before anything is written.
    /// Values live in the current domain only: a compile or a domain reload - entering play mode
    /// included - drops them, and the wiring has to run again.
    /// </remarks>
    public static class HotReloadAddedFieldWiring
    {
        /// <summary>
        /// Stores <paramref name="value"/> in the added field <paramref name="fieldName"/> of
        /// <paramref name="instance"/>, which may be declared by any type in its base chain.
        /// Throws without writing when the field is unknown, static, or would not accept the value.
        /// </summary>
        public static void SetInstanceField(object instance, string fieldName, object value)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            RequireLiveInstance(instance);
            HotReloadAddedFieldDeclaration declaration = ResolveDeclaration(instance.GetType(), fieldName, false);
            RequireAssignable(declaration, value);
            RequireInstalledValues().Set(instance, declaration.StoreFieldKey, value);
            // Why only here and not in the store's Set: shim bodies write through the store on
            // every frame, and only a value the developer wired explicitly should outlive the host.
            HotReloadAddedFieldCoordination.WiredValues?.Record(instance, declaration.StoreFieldKey, value);
        }

        /// <summary>
        /// Stores <paramref name="value"/> in the static added field <paramref name="fieldName"/>
        /// of <paramref name="declaringType"/> or one of its base types. Throws without writing
        /// when the field is unknown, an instance field, or would not accept the value.
        /// </summary>
        public static void SetStaticField(Type declaringType, string fieldName, object value)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            HotReloadAddedFieldDeclaration declaration = ResolveDeclaration(declaringType, fieldName, true);
            RequireAssignable(declaration, value);
            RequireInstalledValues().SetStatic(declaration.StoreFieldKey, value);
        }

        /// <summary>
        /// Reports whether the added field currently holds a stored value, and what it is. Reading
        /// leaves an untouched field untouched, so the shim still runs its initializer on the next
        /// read. An unknown or static field is refused the same way <see cref="SetInstanceField"/>
        /// refuses it.
        /// </summary>
        public static bool TryReadInstanceField(object instance, string fieldName, out object value)
        {
            value = null;
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            RequireLiveInstance(instance);
            HotReloadAddedFieldDeclaration declaration = ResolveDeclaration(instance.GetType(), fieldName, false);
            return RequireInstalledValues().TryGet(
                instance, declaration.StoreFieldKey, ResolveDeclaredType(declaration), out value);
        }

        /// <summary>
        /// The static counterpart of <see cref="TryReadInstanceField"/>.
        /// </summary>
        public static bool TryReadStaticField(Type declaringType, string fieldName, out object value)
        {
            value = null;
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            HotReloadAddedFieldDeclaration declaration = ResolveDeclaration(declaringType, fieldName, true);
            return RequireInstalledValues().TryGetStatic(declaration.StoreFieldKey, out value);
        }

        // Why a destroyed object is refused rather than written: a destroyed UnityEngine.Object is
        // only null through Unity's own operator, so `instance == null` above lets it through, and
        // the side table would happily hold a value for a host whose patched methods will never
        // run again. Refusing says so while the caller still remembers which reference it used.
        private static void RequireLiveInstance(object instance)
        {
            if (!(instance is UnityEngine.Object unityObject) || unityObject != null)
            {
                return;
            }

            throw new ArgumentException(
                "The instance is a destroyed UnityEngine.Object, so a value wired into it would "
                + "never be read. Wire the live object instead.",
                nameof(instance));
        }

        // Why the base chain is walked with Type.FullName and no generic-definition step: the
        // transform worker skips every method on a generic type, so a generic type never gains an
        // added field in the first place and a constructed FullName can never be the one to match.
        // Adding generic hosts later means adding that step here.
        private static HotReloadAddedFieldDeclaration ResolveDeclaration(
            Type type,
            string fieldName,
            bool expectStatic)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                throw new ArgumentException("A field name is required.", nameof(fieldName));
            }

            IHotReloadAddedFieldPort port = HotReloadAddedFieldCoordination.ActiveFields;
            if (port == null)
            {
                throw new InvalidOperationException(
                    "hot reload has no domain installed, so it has no added fields to wire. "
                    + "Run a hot reload that adds the field first.");
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                if (!port.TryGetDeclaration(current.FullName, fieldName, out HotReloadAddedFieldDeclaration declaration))
                {
                    continue;
                }

                RequireExpectedStaticness(declaration, expectStatic);
                return declaration;
            }

            throw new InvalidOperationException(FormatUnknownFieldMessage(port, type, fieldName));
        }

        private static void RequireExpectedStaticness(HotReloadAddedFieldDeclaration declaration, bool expectStatic)
        {
            if (declaration.IsStatic == expectStatic)
            {
                return;
            }

            if (declaration.IsStatic)
            {
                throw new InvalidOperationException(
                    Describe(declaration) + " is static; wire it through its type with "
                    + nameof(SetStaticField) + ".");
            }

            throw new InvalidOperationException(
                Describe(declaration) + " is an instance field; wire it through an instance with "
                + nameof(SetInstanceField) + ".");
        }

        private static string FormatUnknownFieldMessage(IHotReloadAddedFieldPort port, Type type, string fieldName)
        {
            // Why the compiled field is checked first: a compiled field is never an added one, so
            // the "run a hot reload first" advice below would loop the caller. Why the message
            // only states facts: this runs for reads and writes alike, and the field may have been
            // compiled all along, so neither a write-only step nor a past compile is assumed.
            FieldInfo compiledField = FindCompiledField(type, fieldName);
            if (compiledField != null)
            {
                return "'" + fieldName + "' is a compiled field of " + compiledField.DeclaringType.FullName
                    + ", not one hot reload added, so this entry point does not serve it. If hot "
                    + "reload added it earlier, a compile has since made it an ordinary field. Read "
                    + "or set it like any other field (directly, by reflection, or through "
                    + "SerializedObject when Unity serializes it); the added-field calls for it are "
                    + "no longer needed.";
            }

            List<string> names = CollectAddedFieldNames(port, type);
            if (names.Count == 0)
            {
                return "'" + fieldName + "' is not an added field of " + type.FullName
                    + ", which has no active added fields at all. Run a hot reload that adds the "
                    + "field first; a compile, a domain reload, or 'uloop hot-reload --revert-all' "
                    + "drops the added fields.";
            }

            names.Sort(StringComparer.Ordinal);
            return "'" + fieldName + "' is not an added field of " + type.FullName
                + ". Active added fields: " + string.Join(", ", names.ToArray()) + ".";
        }

        private static FieldInfo FindCompiledField(Type type, string fieldName)
        {
            const BindingFlags DeclaredFields = BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(fieldName, DeclaredFields);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static List<string> CollectAddedFieldNames(IHotReloadAddedFieldPort port, Type type)
        {
            List<string> names = new List<string>();
            for (Type current = type; current != null; current = current.BaseType)
            {
                IReadOnlyList<string> added = port.GetAddedFieldNames(current.FullName);
                if (added == null)
                {
                    continue;
                }

                for (int index = 0; index < added.Count; index++)
                {
                    if (!names.Contains(added[index]))
                    {
                        names.Add(added[index]);
                    }
                }
            }

            return names;
        }

        private static void RequireAssignable(HotReloadAddedFieldDeclaration declaration, object value)
        {
            Type declaredType = ResolveDeclaredType(declaration);
            if (value == null)
            {
                if (!declaredType.IsValueType || Nullable.GetUnderlyingType(declaredType) != null)
                {
                    return;
                }

                throw new ArgumentException(
                    "null cannot be stored in " + Describe(declaration) + ", declared "
                    + declaredType.FullName + ", which is a non-nullable value type.");
            }

            // Why the same test the reader uses and not a converting assignment: the shim reads the
            // slot with `stored is T`, so a value it would reject has to be rejected here. Stored
            // anyway, it would be silently replaced by the field's initializer on the next read.
            if (declaredType.IsInstanceOfType(value))
            {
                return;
            }

            throw new ArgumentException(
                Describe(declaration) + " is declared " + declaredType.FullName + ", and a "
                + value.GetType().FullName + " is not one." + DescribeMismatchRemedy(declaredType, value));
        }

        // Why a remedy only for these shapes: each names a conversion that always yields a value
        // the field accepts. Any other mismatch has no fix this entry point can know, so naming
        // both types is all it says rather than advice that does not apply.
        private static string DescribeMismatchRemedy(Type declaredType, object value)
        {
            if (IsNumeric(declaredType) && IsNumeric(value.GetType()))
            {
                return " The field is read back with 'is', so even a widening numeric value is "
                    + "refused: cast the value to the declared type before wiring it.";
            }

            if (typeof(UnityEngine.Component).IsAssignableFrom(declaredType) && value is UnityEngine.GameObject)
            {
                return " Pass the component instead: gameObject.GetComponent<"
                    + SourceNameOf(declaredType, declaredType.GetGenericArguments()) + ">().";
            }

            if (declaredType == typeof(UnityEngine.GameObject) && value is UnityEngine.Component)
            {
                return " Pass the GameObject it is on instead: component.gameObject.";
            }

            return string.Empty;
        }

        // The type as C# source writes it without its namespace: nesting joined with '.', and
        // generic arguments in angle brackets instead of the `N arity suffix. A nested type's
        // arguments start with those of its declaring types, which is why the whole list is passed
        // down and each level takes only its own slice.
        private static string SourceNameOf(Type type, Type[] arguments)
        {
            string prefix = type.IsNested ? SourceNameOf(type.DeclaringType, arguments) + "." : string.Empty;
            int arityMark = type.Name.IndexOf('`');
            if (arityMark < 0)
            {
                return prefix + type.Name;
            }

            int inherited = type.IsNested ? type.DeclaringType.GetGenericArguments().Length : 0;
            int own = type.GetGenericArguments().Length - inherited;
            string[] names = new string[own];
            for (int index = 0; index < own; index++)
            {
                Type argument = arguments[inherited + index];
                names[index] = SourceNameOf(argument, argument.GetGenericArguments());
            }

            return prefix + type.Name.Substring(0, arityMark) + "<" + string.Join(", ", names) + ">";
        }

        private static bool IsNumeric(Type candidate)
        {
            Type type = Nullable.GetUnderlyingType(candidate) ?? candidate;
            if (type.IsEnum)
            {
                return false;
            }

            TypeCode code = Type.GetTypeCode(type);
            return code >= TypeCode.SByte && code <= TypeCode.Decimal;
        }

        private static Type ResolveDeclaredType(HotReloadAddedFieldDeclaration declaration)
        {
            if (string.IsNullOrEmpty(declaration.DeclaredTypeAssemblyQualifiedName))
            {
                throw new InvalidOperationException(
                    "hot reload could not name the declared type of " + Describe(declaration)
                    + ", so a value cannot be checked against it.");
            }

            Type declaredType = Type.GetType(declaration.DeclaredTypeAssemblyQualifiedName, false);
            if (declaredType == null)
            {
                throw new InvalidOperationException(
                    "The declared type of " + Describe(declaration) + " does not resolve in this "
                    + "domain: " + declaration.DeclaredTypeAssemblyQualifiedName);
            }

            return declaredType;
        }

        private static HotReloadAddedFieldValues RequireInstalledValues()
        {
            HotReloadAddedFieldValues values = HotReloadAddedFieldStore.Current;
            if (values == null)
            {
                throw new InvalidOperationException(
                    "hot reload has no added field values installed, so a wired value would never "
                    + "be read. Run a hot reload that adds the field first.");
            }

            return values;
        }

        private static string Describe(HotReloadAddedFieldDeclaration declaration)
        {
            return "'" + declaration.DeclaringTypeName + "." + declaration.FieldName + "'";
        }
    }
}
