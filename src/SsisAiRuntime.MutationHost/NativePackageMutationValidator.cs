using System;
using Microsoft.SqlServer.Dts.Runtime;
using SsisAiRuntime.Core;

namespace SsisAiRuntime.MutationHost
{
    public sealed class NativePackageMutationValidator : INativeMutationValidator
    {
        public NativeMutationValidationResult Validate(PackageSession<Package> session)
        {
            if (session?.Package == null)
            {
                return Failure("mutation.validation.session_missing");
            }

            var package = session.Package;
            var previousOfflineMode = package.OfflineMode;
            try
            {
                var previousErrorCount = package.Errors.Count;
                var previousWarningCount = package.Warnings.Count;
                package.OfflineMode = true;
                var result = package.Validate(package.Connections, package.Variables, null, null);
                var validationErrorCount = Math.Max(0, package.Errors.Count - previousErrorCount);
                var validationWarningCount = Math.Max(0, package.Warnings.Count - previousWarningCount);
                if (result == DTSExecResult.Success && validationErrorCount == 0)
                {
                    return new NativeMutationValidationResult(true, validationWarningCount > 0,
                        validationWarningCount == 0 ? Array.Empty<string>() :
                            new[] { "ssis.validation.warnings_" + Math.Min(validationWarningCount, 999) });
                }

                var code = validationErrorCount > 0
                    ? "ssis.validation.native_errors_" + Math.Min(validationErrorCount, 999)
                    : result == DTSExecResult.Canceled
                        ? "ssis.validation.cancelled"
                        : result == DTSExecResult.Failure
                            ? "ssis.validation.failed"
                            : "ssis.validation.incomplete";
                var diagnostics = validationWarningCount == 0
                    ? new[] { code }
                    : new[] { code, "ssis.validation.warnings_" + Math.Min(validationWarningCount, 999) };
                return new NativeMutationValidationResult(false, validationWarningCount > 0, diagnostics);
            }
            catch (Exception)
            {
                return Failure("ssis.validation.failed");
            }
            finally
            {
                package.OfflineMode = previousOfflineMode;
            }
        }

        private static NativeMutationValidationResult Failure(string code) =>
            new NativeMutationValidationResult(false, false, new[] { code });
    }
}