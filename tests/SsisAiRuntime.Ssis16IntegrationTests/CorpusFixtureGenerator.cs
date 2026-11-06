using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Ssis16;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    internal static class CorpusFixtureGenerator
    {
        private sealed class FixtureDefinition
        {
            public FixtureDefinition(string fileName, Action<string> create, Action<string> verify)
            {
                FileName = fileName;
                Create = create;
                Verify = verify;
            }

            public string FileName { get; }
            public Action<string> Create { get; }
            public Action<string> Verify { get; }
        }

        private static readonly FixtureDefinition[] Fixtures =
        {
            new FixtureDefinition("SyntheticScopedControlFlow.dtsx", CreateScopedControlFlow, VerifyScopedControlFlow),
            new FixtureDefinition("SyntheticSqlDependencies.dtsx", CreateSqlDependencies, VerifySqlDependencies)
        };

        public static IReadOnlyList<string> Create(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            { throw new ArgumentException("An output directory is required.", nameof(outputDirectory)); }
            var directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            var paths = Fixtures.Select(fixture => Path.Combine(directory, fixture.FileName)).ToArray();
            if (paths.Any(File.Exists)) { throw new IOException("corpus.fixtures.destination_exists"); }

            var created = new List<string>();
            try
            {
                for (var index = 0; index < Fixtures.Length; index++)
                {
                    created.Add(paths[index]);
                    Fixtures[index].Create(paths[index]);
                    Fixtures[index].Verify(paths[index]);
                }
                return paths;
            }
            catch
            {
                foreach (var path in created)
                {
                    try { File.Delete(path); }
                    catch { }
                }
                throw;
            }
        }

        private static void CreateScopedControlFlow(string path)
        {
            using (var package = new Package
            {
                Name = "SyntheticScopedControlFlow",
                ProtectionLevel = DTSProtectionLevel.DontSaveSensitive
            })
            {
                package.Variables.Add("Enabled", false, "User", true);
                package.Variables.Add("BatchNumber", false, "User", 1);
                var nextBatch = package.Variables.Add("NextBatch", false, "User", 0);
                nextBatch.Expression = "@[User::BatchNumber] + 1";
                nextBatch.EvaluateAsExpression = true;

                var sequence = (Sequence)package.Executables.Add("STOCK:Sequence");
                sequence.Name = "SyntheticScope";
                sequence.Variables.Add("Enabled", false, "User", false);

                var prepare = (TaskHost)sequence.Executables.Add("STOCK:PipelineTask");
                prepare.Name = "Prepare";
                prepare.Variables.Add("LocalFlag", false, "User", true);
                prepare.SetExpression("Disable", "@[User::Enabled]");

                var load = (TaskHost)sequence.Executables.Add("STOCK:PipelineTask");
                load.Name = "Load";
                var constraint = sequence.PrecedenceConstraints.Add(prepare, load);
                constraint.EvalOp = DTSPrecedenceEvalOp.ExpressionAndConstraint;
                constraint.Expression = "@[User::Enabled]";

                new Application().SaveToXml(path, package, null);
            }
        }

        private static void VerifyScopedControlFlow(string path)
        {
            var load = new PackageLoader().Load(path);
            Require(load.Succeeded, "corpus.fixture.scoped.load");
            try
            {
                var sequence = load.Session.Package.Executables.Cast<Executable>().OfType<Sequence>().Single();
                Require(sequence.Name == "SyntheticScope" && sequence.PrecedenceConstraints.Count == 1,
                    "corpus.fixture.scoped.precedence");
                var variables = new PackageVariableInspector().InspectDetailed(load.Session);
                Require(variables.Items.Any(variable => variable.Name == "Enabled" && variable.Scope == "Package") &&
                    variables.Items.Any(variable => variable.Name == "Enabled" && variable.Scope == "SyntheticScope") &&
                    variables.Items.Any(variable => variable.Name == "NextBatch" && variable.HasExpression),
                    "corpus.fixture.scoped.variables");
            }
            finally { load.Session.Package.Dispose(); }
        }

        private static void CreateSqlDependencies(string path)
        {
            using (var package = new Package
            {
                Name = "SyntheticSqlDependencies",
                ProtectionLevel = DTSProtectionLevel.DontSaveSensitive
            })
            {
                var connection = package.Connections.Add("OLEDB");
                connection.Name = "SyntheticWarehouse";
                connection.ConnectionString = "Provider=SQLOLEDB;Data Source=(local);Initial Catalog=CorpusFixture;Integrated Security=SSPI;";
                var task = (TaskHost)package.Executables.Add("STOCK:SQLTask");
                task.Name = "ReadAndLoadStudents";
                var innerTask = task.InnerObject;
                var taskType = innerTask.GetType();
                taskType.GetProperty("Connection")!.SetValue(innerTask, connection.ID, null);
                var sourceType = taskType.GetProperty("SqlStatementSourceType")!;
                sourceType.SetValue(innerTask, Enum.Parse(sourceType.PropertyType, "DirectInput"), null);
                taskType.GetProperty("SqlStatementSource")!.SetValue(innerTask,
                    "SELECT StudentId FROM dbo.Students; EXEC dbo.LoadStudents; SELECT dbo.GetStudentCount();", null);
                new Application().SaveToXml(path, package, null);
            }
        }

        private static void VerifySqlDependencies(string path)
        {
            var load = new PackageLoader().Load(path);
            Require(load.Succeeded, "corpus.fixture.sql.load");
            try
            {
                var sql = new PackageSqlInspector().InspectDetailed(load.Session);
                Require(sql.Items.Count == 1 && sql.Items[0].SqlDependencies != null &&
                    sql.Items[0].SqlDependencies.Parsed && sql.Items[0].SqlDependencies.References.Count == 3,
                    "corpus.fixture.sql.references");
            }
            finally { load.Session.Package.Dispose(); }
        }

        private static void Require(bool condition, string code)
        {
            if (!condition) { throw new InvalidOperationException(code); }
        }
    }
}