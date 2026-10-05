using System;
using System.Text;

namespace SsisAiRuntime.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args != null && args.Length > 0 && args[0] == "ai")
            {
                return new AiCommandRunner().Run(args, Console.Out);
            }
            return new CliRunner(SsisInspectionService.Inspect).Run(args, Console.Out);
        }
    }
}