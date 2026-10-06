using System;
using System.Collections.Generic;
using System.Linq;
using SsisAiRuntime.AI;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.Mutations
{
    public sealed class MutationPreviewer : IMutationPreviewer
    {
        public MutationPreview Preview(PackageAnalysisSnapshot snapshot, MutationRequest request)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            if (request == null) { throw new ArgumentNullException(nameof(request)); }

            if (request.Kind != MutationKind.RenameTask || request.Target.Kind != SemanticObjectKind.Executable)
            {
                return Invalid(MutationPreviewStatus.UnsupportedTarget, request);
            }
            if (string.IsNullOrWhiteSpace(request.ProposedName) || request.ProposedName.Length > 128 ||
                request.ProposedName.Any(char.IsControl))
            {
                return Invalid(MutationPreviewStatus.InvalidValue, request);
            }

            var targets = snapshot.Executables.Where(item =>
                string.Equals(item.Id, request.Target.NativeId, StringComparison.Ordinal)).ToArray();
            if (targets.Length == 0)
            {
                return Invalid(MutationPreviewStatus.TargetNotFound, request);
            }
            if (targets.Length != 1)
            {
                return Invalid(MutationPreviewStatus.AmbiguousTarget, request);
            }

            var task = targets[0];
            if (task.IsContainer)
            {
                return Invalid(MutationPreviewStatus.UnsupportedTarget, request);
            }
            var nodes = snapshot.Dependencies.Nodes.Where(node =>
                node.Kind == SemanticObjectKind.Executable &&
                string.Equals(node.NativeId, task.Id, StringComparison.Ordinal)).ToArray();
            if (nodes.Length == 0)
            {
                return Invalid(MutationPreviewStatus.UnsupportedTarget, request);
            }
            if (nodes.Length != 1)
            {
                return Invalid(MutationPreviewStatus.AmbiguousTarget, request);
            }

            var impactResult = new RichImpactAnalysisQuery().Analyze(snapshot.Dependencies, nodes[0].Key);
            if (impactResult.Items.Count != 1)
            {
                return Invalid(MutationPreviewStatus.IncompleteCoverage, request, impactResult.UnsupportedItems);
            }

            var impactAnalysis = impactResult.Items[0];
            var coverage = MergeCoverage(snapshot.UnsupportedItems, impactAnalysis.UnsupportedItems);
            var impact = new MutationImpact(MutationRisk.Low,
                "Task rename; " + impactAnalysis.Impacts.Count + " dependent node(s) identified.",
                impactAnalysis.Impacts.Select(item => item.Node.Key));
            if (!snapshot.IsComplete || !snapshot.Dependencies.IsComplete || !impactAnalysis.IsComplete)
            {
                return new MutationPreview(MutationPreviewStatus.IncompleteCoverage, request,
                    task.Name, request.ProposedName, impact, null, coverage);
            }

            var plan = new MutationExecutionPlan(request, task.Name, request.ProposedName, impact,
                MutationExecutionPolicies.RenameTask());
            return new MutationPreview(MutationPreviewStatus.Valid, request,
                task.Name, request.ProposedName, impact, plan, coverage);
        }

        private static MutationPreview Invalid(MutationPreviewStatus status, MutationRequest request,
            IEnumerable<UnsupportedItem> coverage = null)
        {
            return new MutationPreview(status, request, string.Empty, string.Empty,
                null, null, coverage ?? Array.Empty<UnsupportedItem>());
        }

        private static IReadOnlyList<UnsupportedItem> MergeCoverage(
            IEnumerable<UnsupportedItem> first, IEnumerable<UnsupportedItem> second) =>
            first.Concat(second)
                .GroupBy(item => item.Id + "\u001f" + item.Name + "\u001f" + item.CreationName + "\u001f" + item.ReasonCode,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
    }
}