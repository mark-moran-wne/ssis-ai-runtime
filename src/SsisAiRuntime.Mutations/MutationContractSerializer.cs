using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace SsisAiRuntime.Mutations
{
    public static class MutationContractSchema
    {
        public const string CurrentVersion = "2.0";

        public static bool IsCompatible(string version) =>
            string.Equals(version, CurrentVersion, StringComparison.Ordinal);
    }

    public static class MutationContractSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.None
        };

        public static string Serialize<TTarget, TRequest, TPreview, TMutation>(
            MutationEnvelope<TTarget, TRequest, TPreview, TMutation> envelope)
        {
            if (envelope == null) { throw new ArgumentNullException(nameof(envelope)); }
            return JsonConvert.SerializeObject(envelope, Settings);
        }
    }
}