using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Security Validation Result
    /// 
    /// Related Class: SecurityValidator
    /// </summary>
    public class SecurityValidationResult
    {
        /// <summary>Validation status</summary>
        public bool IsValid { get; set; }

        /// <summary>List of security violations</summary>
        public List<SecurityViolation> Violations { get; set; } = new();
        
        /// <summary>Compilation errors (extended)</summary>
        public List<string> CompilationErrors { get; set; } = new();
        
        /// <summary>
        /// Retrieve error summary
        /// </summary>
        public string GetErrorSummary()
        {
            if (IsValid) return "No security violations detected.";
            
            StringBuilder sb = new();
            sb.AppendLine($"Security validation failed with {Violations.Count} violation(s):");
            
            foreach (SecurityViolation violation in Violations)
            {
                sb.AppendLine($"  - [{violation.Type}] {violation.Message}");
                if (!string.IsNullOrEmpty(violation.ApiName))
                {
                    sb.AppendLine($"    API: {violation.ApiName}");
                }
            }
            
            if (CompilationErrors?.Any() == true)
            {
                sb.AppendLine($"Compilation errors: {CompilationErrors.Count}");
                foreach (string error in CompilationErrors)
                {
                    sb.AppendLine($"  - {error}");
                }
            }
            
            return sb.ToString();
        }
    }
}