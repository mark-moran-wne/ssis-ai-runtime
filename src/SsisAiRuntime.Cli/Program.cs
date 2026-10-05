using System;
using System.Text;

namespace SsisAiRuntime.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            return new CliRunner(SsisInspectionService.Inspect).Run(args, Console.Out);
        }
    }
}