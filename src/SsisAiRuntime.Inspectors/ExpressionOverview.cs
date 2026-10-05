namespace SsisAiRuntime.Inspectors
{
    public sealed class ExpressionOverview
    {
        public ExpressionOverview(string objectType, string objectId, string objectName, string creationName)
        {
            ObjectType = objectType ?? string.Empty;
            ObjectId = objectId ?? string.Empty;
            ObjectName = objectName ?? string.Empty;
            CreationName = creationName ?? string.Empty;
        }

        public string ObjectType { get; }

        public string ObjectId { get; }

        public string ObjectName { get; }

        public string CreationName { get; }

        public bool HasExpression => true;

        public bool ExpressionTextOmitted => true;

        public bool PropertyAssociationOmitted => true;
    }
}