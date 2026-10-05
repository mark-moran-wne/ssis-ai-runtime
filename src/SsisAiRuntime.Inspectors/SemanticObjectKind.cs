namespace SsisAiRuntime.Inspectors
{
    public enum SemanticObjectKind
    {
        Package,
        Connection,
        Variable,
        Parameter,
        Executable,
        DataFlow,
        DataFlowComponent,
        InputColumn,
        OutputColumn,
        ExternalMetadataColumn,
        DataFlowPath,
        DataFlowRuntimeConnection,
        ExpressionOwner
    }
}