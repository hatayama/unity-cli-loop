
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Constants definition for dynamic code execution
    /// Related Classes: IDynamicCodeExecutor, DynamicCodeExecutor, DynamicCodeExecutorStub
    /// </summary>
    public static class DynamicCodeConstants
    {
        /// <summary>Default name for dynamically generated classes</summary>
        public const string DEFAULT_CLASS_NAME = "DynamicCommand";

        /// <summary>Default namespace for dynamically generated classes</summary>
        public const string DEFAULT_NAMESPACE = "UnityCliLoop.Dynamic";

        // Format: count, space-separated "using Ns; (for 'Ident')" items.
        public const string RetryResolvedUsingHintFormat =
            "Performance hint: Auto-resolved {0} missing using directive(s) after compile errors: {1} "
            + "— Include them in your code to skip auto-resolution retries and improve compilation speed.";

        // Format: count, space-separated "using Ns; (for 'Ident')" items.
        public const string SpeculativeUsingHintFormat =
            "Note: {0} using directive(s) were speculatively pre-injected from an identifier scan: {1} "
            + "— No action needed. An attribution you do not recognize means the namespace was matched "
            + "only by a type's simple name and the directive may be unnecessary.";

        public const string UsingAttributionItemFormat = "using {0}; (for '{1}')";
    }
}