using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowExpressionDependencies
    {
        public DataFlowExpressionDependencies(bool isResolved, IEnumerable<int> inputLineageIds)
        {
            if (inputLineageIds == null) { throw new ArgumentNullException(nameof(inputLineageIds)); }
            var ids = inputLineageIds.Distinct().OrderBy(id => id).ToList();
            if (ids.Any(id => id <= 0) || (!isResolved && ids.Count > 0))
            {
                throw new ArgumentException("Only resolved positive column references may be projected.", nameof(inputLineageIds));
            }
            IsResolved = isResolved;
            InputLineageIds = new ReadOnlyCollection<int>(ids);
        }

        public bool IsResolved { get; }
        public string Resolution => IsResolved ? "NativeParser" : "Unresolved";
        public IReadOnlyList<int> InputLineageIds { get; }
        public bool ExpressionTextOmitted => true;
    }
}