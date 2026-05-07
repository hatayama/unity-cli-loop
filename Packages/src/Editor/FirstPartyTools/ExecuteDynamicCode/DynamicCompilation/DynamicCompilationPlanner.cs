using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Compilation Planner behavior for Unity CLI Loop.
    /// </summary>
    internal sealed class DynamicCompilationPlanner : IDynamicCompilationPlanner
    {
        private readonly IDynamicCodeSourcePreparationService _sourcePreparationService;

        public DynamicCompilationPlanner(IDynamicCodeSourcePreparationService sourcePreparationService)
        {
            _sourcePreparationService = sourcePreparationService;
        }

        public DynamicCompilationPlan CreatePlan(CompilationRequest request)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(request.Code), "request.Code must not be empty");

            string namespaceName = request.Namespace ?? DynamicCodeConstants.DEFAULT_NAMESPACE;
            string className = request.ClassName ?? DynamicCodeConstants.DEFAULT_CLASS_NAME;
            PreparedDynamicCode preparedCode = _sourcePreparationService.Prepare(
                request.Code,
                namespaceName,
                className);

            CompilationRequest normalizedRequest = new()            {
                Code = preparedCode.PreparedSource ?? request.Code,
                ClassName = className,
                Namespace = namespaceName,
                AdditionalReferences = request.AdditionalReferences != null
                    ? new List<string>(request.AdditionalReferences)
                    : new List<string>(),
                AssemblyMode = request.AssemblyMode
            };

            return new DynamicCompilationPlan(
                request,
                normalizedRequest,
                preparedCode,
                className,
                namespaceName);
        }
    }
}
