using System;

namespace CueGen.Workflow
{
    public sealed class WorkflowEnvironmentConfiguration
    {
        public const string DatabasePathVariable = "REKORDBOT_DATABASE_PATH";
        public const string FileGlobVariable = "REKORDBOT_FILE_GLOB";
        public const string DryRunVariable = "REKORDBOT_DRY_RUN";
        public const string TaxonomyPathVariable = "REKORDBOT_TAXONOMY_PATH";

        public string DatabasePath { get; private set; }
        public string FileGlob { get; private set; }
        public bool? DryRun { get; private set; }
        public string TaxonomyPath { get; private set; }

        public bool WorkflowHotCuesEnabled =>
            DatabasePath != null && FileGlob != null;

        public static WorkflowEnvironmentConfiguration Load(Func<string, string> readVariable)
        {
            if (readVariable == null)
                throw new ArgumentNullException(nameof(readVariable));

            var result = new WorkflowEnvironmentConfiguration
            {
                DatabasePath = Normalize(readVariable(DatabasePathVariable)),
                FileGlob = Normalize(readVariable(FileGlobVariable)),
                TaxonomyPath = Normalize(readVariable(TaxonomyPathVariable))
            };

            if ((result.DatabasePath == null) != (result.FileGlob == null))
            {
                throw new InvalidOperationException(
                    $"{DatabasePathVariable} and {FileGlobVariable} must be set together");
            }

            var dryRun = Normalize(readVariable(DryRunVariable));
            if (dryRun != null)
                result.DryRun = ParseBoolean(DryRunVariable, dryRun);

            return result;
        }

        public void Apply(Config config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (WorkflowHotCuesEnabled)
            {
                config.DatabasePath = DatabasePath;
                config.FileGlob = FileGlob;
                config.GenerateWorkflowHotCues = true;
            }
            if (DryRun.HasValue)
                config.DryRun = DryRun.Value;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool ParseBoolean(string variableName, string value)
        {
            if (bool.TryParse(value, out var parsed))
                return parsed;
            if (value == "1")
                return true;
            if (value == "0")
                return false;
            throw new InvalidOperationException(
                $"{variableName} must be true, false, 1, or 0");
        }
    }
}
