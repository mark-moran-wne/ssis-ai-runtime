using System;
using Microsoft.SqlServer.Dts.Runtime;
using Newtonsoft.Json.Linq;

namespace SsisAiRuntime.FlowRunner
{
    internal sealed class NativeProbeEvents : DefaultEvents
    {
        private readonly JArray diagnostics;
        private readonly object sync = new object();
        public NativeProbeEvents(JArray diagnostics) { this.diagnostics = diagnostics; }

        public override bool OnError(DtsObject source, int errorCode, string subComponent, string description,
            string helpFile, int helpContext, string idOfInterfaceWithError)
        {
            lock (sync)
            {
                if (diagnostics.Count < 32)
                {
                    diagnostics.Add(new JObject
                    {
                        ["code"] = "flow.native.error", ["stage"] = "execution", ["severity"] = "Error",
                        ["nativeCode"] = "0x" + unchecked((uint)errorCode).ToString("X8"),
                        ["component"] = subComponent, ["message"] = description
                    });
                }
            }
            return false;
        }
    }
}