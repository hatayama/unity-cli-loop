namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Security Violation
    /// Related Classes: SecurityValidator, SecuritySyntaxWalker
    /// </summary>
    public class SecurityViolation
    {
        /// <summary>Violation Type</summary>
        public SecurityViolationType Type { get; set; }

        /// <summary>Description of the Security Violation</summary>
        public string Description { get; set; }

        /// <summary>Line Number where the Violation Occurred</summary>
        public int LineNumber { get; set; }

        /// <summary>Code Snippet Containing the Violation</summary>
        public string CodeSnippet { get; set; }
        
        /// <summary>Violation Message</summary>
        public string Message { get; set; }
        
        /// <summary>API Name Involved in the Violation</summary>
        public string ApiName { get; set; }
        
        /// <summary>Location of the Violation</summary>
        public string Location { get; set; }
    }

    /// <summary>
    /// Security Violation Types
    /// </summary>
    public enum SecurityViolationType
    {
        /// <summary>Using Declaration of a Forbidden Namespace</summary>
        ForbiddenNamespace,

        /// <summary>Dangerous API Call (Method, Property, etc.)</summary>
        DangerousApiCall,

        /// <summary>Inheritance from a Dangerous Class</summary>
        DangerousInheritance,

        /// <summary>Instantiation of a Dangerous Type</summary>
        DangerousTypeCreation,

        /// <summary>Unauthorized Reflection Usage</summary>
        UnauthorizedReflection
    }
}
