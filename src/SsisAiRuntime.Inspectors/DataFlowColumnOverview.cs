namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowColumnOverview
    {
        public DataFlowColumnOverview(
            string componentId,
            string portId,
            string portName,
            string direction,
            string id,
            string name,
            string dataType,
            int length,
            int precision,
            int scale,
            int codePage,
            int lineageId,
            int externalMetadataColumnId,
            string usageType)
        {
            ComponentId = componentId ?? string.Empty;
            PortId = portId ?? string.Empty;
            PortName = portName ?? string.Empty;
            Direction = direction ?? string.Empty;
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            DataType = dataType ?? string.Empty;
            Length = length;
            Precision = precision;
            Scale = scale;
            CodePage = codePage;
            LineageId = lineageId;
            ExternalMetadataColumnId = externalMetadataColumnId;
            UsageType = usageType ?? string.Empty;
        }

        public string ComponentId { get; }

        public string PortId { get; }

        public string PortName { get; }

        public string Direction { get; }

        public string Id { get; }

        public string Name { get; }

        public string DataType { get; }

        public int Length { get; }

        public int Precision { get; }

        public int Scale { get; }

        public int CodePage { get; }

        public int LineageId { get; }

        public int ExternalMetadataColumnId { get; }

        public string UsageType { get; }
    }
}