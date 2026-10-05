using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SsisAiRuntime.Inspectors;

namespace SsisAiRuntime.AI
{
    public enum ImpactCategory
    {
        Direct,
        Indirect,
        Variable,
        Parameter,
        Configuration,
        ControlFlow,
        DataFlow,
        Sql
    }

    public sealed class ClassifiedImpact
    {
        public ClassifiedImpact(DependencyNode node, ImpactCategory category, int distance,
            IEnumerable<DependencyEdge> path)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            Category = category;
            Distance = distance;
            Path = new ReadOnlyCollection<DependencyEdge>(new List<DependencyEdge>(
                path ?? throw new ArgumentNullException(nameof(path))));
        }

        public DependencyNode Node { get; }
        public ImpactCategory Category { get; }
        public int Distance { get; }
        public IReadOnlyList<DependencyEdge> Path { get; }
    }

    public sealed class RichImpactAnalysisResult
    {
        public RichImpactAnalysisResult(DependencyNode root, IEnumerable<ClassifiedImpact> impacts,
            IEnumerable<UnsupportedItem> unsupportedItems)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Impacts = new ReadOnlyCollection<ClassifiedImpact>(new List<ClassifiedImpact>(
                impacts ?? throw new ArgumentNullException(nameof(impacts))));
            UnsupportedItems = new ReadOnlyCollection<UnsupportedItem>(new List<UnsupportedItem>(
                unsupportedItems ?? throw new ArgumentNullException(nameof(unsupportedItems))));
        }

        public DependencyNode Root { get; }
        public IReadOnlyList<ClassifiedImpact> Impacts { get; }
        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }
        public bool IsComplete => UnsupportedItems.Count == 0;
    }

    public sealed class RichImpactAnalysisQuery
    {
        public InspectionResult<RichImpactAnalysisResult> Analyze(PackageDependencyGraph graph, string rootKey)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            var root = graph.Nodes.SingleOrDefault(node => node.Key == rootKey);
            if (root == null)
            {
                return new InspectionResult<RichImpactAnalysisResult>(Array.Empty<RichImpactAnalysisResult>(), new[]
                {
                    new UnsupportedItem(string.Empty, string.Empty, "ImpactAnalysis",
                        "The impact selection was missing or ambiguous.", UnsupportedItem.UnsupportedMetadataCode)
                });
            }

            var impacts = new List<ClassifiedImpact>();
            var visitedDistance = new Dictionary<string, int>(StringComparer.Ordinal) { [root.Key] = 0 };
            var queue = new Queue<PathState>();
            queue.Enqueue(new PathState(root.Key, Array.Empty<DependencyEdge>()));
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                foreach (var edge in graph.Edges.Where(item => item.To == state.NodeKey))
                {
                    var consumer = graph.Nodes.SingleOrDefault(node => node.Key == edge.From);
                    if (consumer == null) { continue; }
                    var path = state.Path.Concat(new[] { edge }).ToArray();
                    var distance = path.Length;
                    if (visitedDistance.TryGetValue(consumer.Key, out var previousDistance) && previousDistance <= distance)
                    {
                        continue;
                    }
                    visitedDistance[consumer.Key] = distance;
                    impacts.RemoveAll(item => item.Node.Key == consumer.Key);
                    impacts.Add(new ClassifiedImpact(consumer, Classify(edge, distance), distance, path));
                    queue.Enqueue(new PathState(consumer.Key, path));
                }
            }

            var ordered = impacts.OrderBy(item => item.Distance).ThenBy(item => item.Category)
                .ThenBy(item => item.Node.Key, StringComparer.Ordinal).ToArray();
            var result = new RichImpactAnalysisResult(root, ordered, graph.UnsupportedItems);
            return new InspectionResult<RichImpactAnalysisResult>(new[] { result }, result.UnsupportedItems);
        }

        private static ImpactCategory Classify(DependencyEdge edge, int distance)
        {
            if (distance > 1) { return ImpactCategory.Indirect; }
            switch (edge.Kind)
            {
                case DependencyKind.ReadsSchemaObject:
                case DependencyKind.WritesSchemaObject:
                case DependencyKind.ExecutesSchemaObject:
                case DependencyKind.ReferencesSqlFunction: return ImpactCategory.Sql;
                case DependencyKind.UsesVariable: return ImpactCategory.Variable;
                case DependencyKind.UsesParameter: return ImpactCategory.Parameter;
                case DependencyKind.UsesConnection: return ImpactCategory.Configuration;
                case DependencyKind.DependsOnTask: return ImpactCategory.ControlFlow;
                case DependencyKind.ContainsDataFlow:
                case DependencyKind.ContainsComponent:
                case DependencyKind.ConnectsComponent:
                case DependencyKind.ReadsColumn:
                case DependencyKind.WritesColumn:
                    return ImpactCategory.DataFlow;
                default: return ImpactCategory.Direct;
            }
        }

        private sealed class PathState
        {
            public PathState(string nodeKey, IReadOnlyList<DependencyEdge> path)
            {
                NodeKey = nodeKey;
                Path = path;
            }

            public string NodeKey { get; }
            public IReadOnlyList<DependencyEdge> Path { get; }
        }
    }
}