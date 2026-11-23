namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.@event
{
    /// <summary>
    /// Host types declared in a namespace whose last segment is a C# keyword. Tests edit their
    /// bodies to check that hot reload patches them: the escaped spelling (@event) appears only in
    /// source, while the compiled metadata names the namespace "event".
    /// </summary>
    public sealed class HotReloadKeywordNamespaceHost
    {
        public const int KeywordTuning = 4;

        public int MethodValue()
        {
            return 35990;
        }

        public int GetterValue
        {
            get
            {
                return 35993;
            }
        }

        public int CallsAddedMethod()
        {
            return 35996;
        }

        public int CallsIntroducedType()
        {
            return 36001;
        }

        public int KindChangeValue => 36002;

        public int @checked => 36007;

        public int ReadsAddedField()
        {
            return 36008;
        }

        /// <summary>
        /// Nested host, so the conversion of a nested type name in that namespace is covered too.
        /// </summary>
        public sealed class Nested
        {
            public int NestedValue()
            {
                return 35999;
            }
        }
    }

    /// <summary>
    /// Host whose own name is a C# keyword, so a key built from its identifier must drop the @
    /// as well as the namespace's.
    /// </summary>
    public sealed class @class
    {
        public int ClassKindChangeValue => 36005;
    }
}
