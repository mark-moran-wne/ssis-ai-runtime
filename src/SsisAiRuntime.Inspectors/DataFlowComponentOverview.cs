using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class DataFlowComponentOverview
    {
        public DataFlowComponentOverview(
            string id,
            string name,
            string componentClassId,
            string description,
            int inputCount,
            int outputCount)
            : this(
                id,
                name,
                componentClassId,
                description,
                inputCount,
                outputCount,
                Array.Empty<DataFlowColumnOverview>(),
                Array.Empty<DataFlowColumnOverview>(),
                Array.Empty<DataFlowColumnOverview>(),
                Array.Empty<DataFlowRuntimeConnectionOverview>(),
                Array.Empty<DataFlowSettingOverview>())
        {
        }

        public DataFlowComponentOverview(
            string id,
            string name,
            string componentClassId,
            string description,
            int inputCount,
            int outputCount,
            IEnumerable<DataFlowColumnOverview> inputColumns,
            IEnumerable<DataFlowColumnOverview> outputColumns,
            IEnumerable<DataFlowColumnOverview> externalMetadataColumns,
            IEnumerable<DataFlowRuntimeConnectionOverview> runtimeConnections,
            IEnumerable<DataFlowSettingOverview> settings)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            ComponentClassId = componentClassId ?? string.Empty;
            Description = description ?? string.Empty;
            InputCount = inputCount;
            OutputCount = outputCount;
            InputColumns = new ReadOnlyCollection<DataFlowColumnOverview>(new List<DataFlowColumnOverview>(inputColumns));
            OutputColumns = new ReadOnlyCollection<DataFlowColumnOverview>(new List<DataFlowColumnOverview>(outputColumns));
            ExternalMetadataColumns = new ReadOnlyCollection<DataFlowColumnOverview>(new List<DataFlowColumnOverview>(externalMetadataColumns));
            RuntimeConnections = new ReadOnlyCollection<DataFlowRuntimeConnectionOverview>(new List<DataFlowRuntimeConnectionOverview>(runtimeConnections));
            Settings = new ReadOnlyCollection<DataFlowSettingOverview>(new List<DataFlowSettingOverview>(settings));
        }

        public string Id { get; }

        public string Name { get; }

        public string ComponentClassId { get; }

        public string Description { get; }

        public int InputCount { get; }

        public int OutputCount { get; }

        public IReadOnlyList<DataFlowColumnOverview> InputColumns { get; }

        public IReadOnlyList<DataFlowColumnOverview> OutputColumns { get; }

        public IReadOnlyList<DataFlowColumnOverview> ExternalMetadataColumns { get; }

        public IReadOnlyList<DataFlowRuntimeConnectionOverview> RuntimeConnections { get; }

        public IReadOnlyList<DataFlowSettingOverview> Settings { get; }
    }
}