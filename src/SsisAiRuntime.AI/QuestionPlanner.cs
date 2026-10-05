using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsisAiRuntime.AI
{
    public enum QuestionIntent
    {
        Unknown,
        PackageSummary,
        MetadataSearch,
        TaskDependencies,
        ColumnTrace,
        DependencyQuery,
        ImpactAnalysis
    }

    public sealed class QuestionPlan
    {
        public QuestionPlan(QuestionIntent intent, string toolName, double confidence, IEnumerable<string> requiredSelectors)
        {
            Intent = intent;
            ToolName = toolName ?? string.Empty;
            Confidence = confidence;
            RequiredSelectors = new ReadOnlyCollection<string>(new List<string>(requiredSelectors ?? throw new ArgumentNullException(nameof(requiredSelectors))));
        }

        public QuestionIntent Intent { get; }
        public string ToolName { get; }
        public double Confidence { get; }
        public IReadOnlyList<string> RequiredSelectors { get; }
    }

    public sealed class QuestionPlanner
    {
        public QuestionPlan Plan(string question)
        {
            if (string.IsNullOrWhiteSpace(question) || question.Length > 1024)
            {
                return Create(QuestionIntent.Unknown, string.Empty, 0);
            }

            var normalized = question.ToLowerInvariant();
            if (ContainsAny(normalized, "impact", "affected", "what breaks"))
                return Create(QuestionIntent.ImpactAnalysis, "impact.analysis", 0.85, "nodeKey");
            if (ContainsAny(normalized, "what uses", "used by", "what depends", "dependency"))
                return Create(QuestionIntent.DependencyQuery, "dependency.query", 0.8, "nodeKey");
            if (ContainsAny(normalized, "where does", "come from", "lineage", "upstream", "downstream"))
                return Create(QuestionIntent.ColumnTrace, "column.trace", 0.8, "flowId", "componentId", "columnId");
            if (ContainsAny(normalized, "predecessor", "successor", "task dependencies"))
                return Create(QuestionIntent.TaskDependencies, "task.dependencies", 0.8, "taskId");
            if (ContainsAny(normalized, "find", "search", "locate"))
                return Create(QuestionIntent.MetadataSearch, "metadata.search", 0.7, "query");
            if (ContainsAny(normalized, "summary", "summarize", "overview", "inventory"))
                return Create(QuestionIntent.PackageSummary, AiToolNames.PackageSummary, 0.75);
            return Create(QuestionIntent.Unknown, string.Empty, 0);
        }

        private static bool ContainsAny(string value, params string[] phrases) => phrases.Any(value.Contains);

        private static QuestionPlan Create(QuestionIntent intent, string toolName, double confidence, params string[] selectors) =>
            new QuestionPlan(intent, toolName, confidence, selectors);
    }
}