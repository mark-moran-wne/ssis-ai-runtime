using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class SemanticHandleResolution
    {
        public SemanticHandleResolution(
            SemanticHandleResolutionStatus status,
            SemanticObjectReference resolvedObject,
            IEnumerable<SemanticObjectReference> candidates)
        {
            Status = status;
            ResolvedObject = resolvedObject;
            Candidates = new ReadOnlyCollection<SemanticObjectReference>(new List<SemanticObjectReference>(candidates));
        }

        public SemanticHandleResolutionStatus Status { get; }

        public SemanticObjectReference ResolvedObject { get; }

        public IReadOnlyList<SemanticObjectReference> Candidates { get; }
    }
}