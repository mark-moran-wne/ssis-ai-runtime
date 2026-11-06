namespace SsisAiRuntime.Inspectors
{
    public sealed class ExecutePackageParameterBindingOverview
    {
        public ExecutePackageParameterBindingOverview(string childParameterName, string parentSourceReference)
        {
            ChildParameterName = childParameterName ?? string.Empty;
            ParentSourceReference = parentSourceReference ?? string.Empty;
        }

        public string ChildParameterName { get; }
        public string ParentSourceReference { get; }
    }
}