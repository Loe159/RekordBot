using CueGen.Workflow;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace CueGen.Test
{
    [TestFixture]
    public class WorkflowEnvironmentConfigurationTests
    {
        [Test]
        public void CompleteWorkflowEnvironmentEnablesArgumentFreeGeneration()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [WorkflowEnvironmentConfiguration.DatabasePathVariable] = " D:\\PIONEER\\Master\\master.db ",
                [WorkflowEnvironmentConfiguration.FileGlobVariable] = " D:/Music/*.flac ",
                [WorkflowEnvironmentConfiguration.DryRunVariable] = "1",
                [WorkflowEnvironmentConfiguration.TaxonomyPathVariable] = " D:/taxonomy.json "
            };
            var environment = WorkflowEnvironmentConfiguration.Load(name =>
                values.TryGetValue(name, out var value) ? value : null);
            var config = new Config();

            environment.Apply(config);

            Assert.That(environment.WorkflowHotCuesEnabled, Is.True);
            Assert.That(config.DatabasePath, Is.EqualTo("D:\\PIONEER\\Master\\master.db"));
            Assert.That(config.FileGlob, Is.EqualTo("D:/Music/*.flac"));
            Assert.That(config.GenerateWorkflowHotCues, Is.True);
            Assert.That(config.DryRun, Is.True);
            Assert.That(environment.TaxonomyPath, Is.EqualTo("D:/taxonomy.json"));
        }

        [TestCase(WorkflowEnvironmentConfiguration.DatabasePathVariable)]
        [TestCase(WorkflowEnvironmentConfiguration.FileGlobVariable)]
        public void PartialWorkflowEnvironmentIsRejected(string variableName)
        {
            Assert.Throws<InvalidOperationException>(() =>
                WorkflowEnvironmentConfiguration.Load(name =>
                    name == variableName ? "configured" : null));
        }

        [Test]
        public void InvalidDryRunValueIsRejected()
        {
            Assert.Throws<InvalidOperationException>(() =>
                WorkflowEnvironmentConfiguration.Load(name =>
                    name == WorkflowEnvironmentConfiguration.DryRunVariable ? "sometimes" : null));
        }

        [Test]
        public void EmptyEnvironmentLeavesExistingConfigurationUntouched()
        {
            var config = new Config
            {
                DatabasePath = "existing.db",
                FileGlob = "existing/*.flac",
                DryRun = true
            };

            WorkflowEnvironmentConfiguration.Load(_ => null).Apply(config);

            Assert.That(config.DatabasePath, Is.EqualTo("existing.db"));
            Assert.That(config.FileGlob, Is.EqualTo("existing/*.flac"));
            Assert.That(config.DryRun, Is.True);
            Assert.That(config.GenerateWorkflowHotCues, Is.False);
        }
    }
}
