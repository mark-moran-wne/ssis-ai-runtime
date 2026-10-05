namespace SsisAiRuntime.Inspectors
{
    public sealed class ColumnLineageLink
    {
        public ColumnLineageLink(DataFlowColumnOverview source, DataFlowColumnOverview target, string pathId)
            : this(source, target, pathId, string.Empty)
        {
        }

        public ColumnLineageLink(DataFlowColumnOverview source, DataFlowColumnOverview target, string pathId, string synchronousOutputId)
        {
            Source = source;
            Target = target;
            PathId = pathId;
            SynchronousOutputId = synchronousOutputId;
        }

        public DataFlowColumnOverview Source { get; }
        public DataFlowColumnOverview Target { get; }
        public string PathId { get; }
        public string SynchronousOutputId { get; }
        public string Kind => SynchronousOutputId.Length > 0 ? "SynchronousPassThrough" : PathId.Length > 0 ? "Path" :
            Target.ExpressionDependencies != null ? "ExpressionResolved" :
            Target.SourceInputLineageId.HasValue && Target.SourceInputLineageId.Value == Source.LineageId ? "ExplicitMapping" : "ProjectedIdentity";
    }
}