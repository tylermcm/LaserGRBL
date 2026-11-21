using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LaserGRBL.Tests
{
    [Collection("Job passes")]
    public class WorkflowTests
    {
        static WorkflowTests()
        {
            var field = typeof(GrblCore).GetField("mDataPath", BindingFlags.Static | BindingFlags.NonPublic);
            if (field.GetValue(null) == null)
            {
                string data = Path.Combine(Path.GetTempPath(), "LaserGRBL-workflow-tests-" + Guid.NewGuid());
                Directory.CreateDirectory(data);
                field.SetValue(null, data);
            }
        }

        private static PreflightInput Good() => new PreflightInput
        {
            Lines = 100, HasBounds = true, MinX = 0, MinY = 0, MaxX = 100, MaxY = 50,
            BedWidth = 300, BedHeight = 200, LaserMode = true, MaxPwm = 1000, MaxS = 800
        };

        // ---------- Pre-flight ----------

        [Fact]
        public void PreflightPassesAJobThatFits()
        {
            Assert.Empty(PreflightCheck.Run(Good()));
        }

        [Fact]
        public void PreflightFlagsJobsLargerThanTheBed()
        {
            var input = Good();
            input.MaxX = 400;
            var finding = Assert.Single(PreflightCheck.Run(input));
            Assert.Equal(PreflightSeverity.Error, finding.Severity);
            Assert.Contains("work area", finding.Text);
        }

        [Fact]
        public void PreflightWarnsAboutMachineSettingsAndJobContents()
        {
            var input = Good();
            input.LaserMode = false;
            input.MaxS = 1200;
            input.UsesZ = true;
            input.MinX = -5;
            input.InAlarm = true;
            var text = string.Join("\n", PreflightCheck.Run(input).Select(f => f.Text));
            Assert.Contains("$32", text);
            Assert.Contains("S1200", text);
            Assert.Contains("moves Z", text);
            Assert.Contains("negative", text);
            Assert.Contains("alarm", text);
        }

        [Fact]
        public void PreflightIgnoresNegativeCoordinatesWithAWorkOrigin()
        {
            var input = Good();
            input.MinX = -20;
            input.MaxX = 20;
            input.HasWorkOffset = true;
            Assert.Empty(PreflightCheck.Run(input));
        }

        [Fact]
        public void PreflightScanFindsPowerAndZ()
        {
            decimal maxS;
            bool usesZ;
            PreflightCheck.Scan(new[] { new GrblCommand("G1 X1 S300"), new GrblCommand("G1 X2 S900"), new GrblCommand("G0 Z2") }, out maxS, out usesZ);
            Assert.Equal(900m, maxS);
            Assert.True(usesZ);
        }

        // ---------- Framing ----------

        [Fact]
        public void FramingTracesTheBoxWithTheLaserOff()
        {
            var lines = Framing.Commands(10, 20, 50, 60, 3000, 0, 1000);
            Assert.Equal(new[] { "M5", "G90", "G0 X10 Y20", "M5", "G1 X50 Y20 F3000", "G1 X50 Y60", "G1 X10 Y60", "G1 X10 Y20", "M5 S0" }, lines);
        }

        [Fact]
        public void FramingPowerIsCappedSoItCantBurn()
        {
            Assert.Equal("M3 S50", Framing.Commands(0, 0, 1, 1, 1000, 50, 1000)[3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => Framing.Commands(0, 0, 1, 1, 1000, 150, 1000));
        }

        // ---------- Checkpoint ----------

        [Fact]
        public void CheckpointRoundTripsAndMatchesOnlyTheSameFile()
        {
            var commands = new[] { new GrblCommand("G1 X1"), new GrblCommand("G1 X2") };
            string path = Path.Combine(Path.GetTempPath(), "checkpoint-" + Guid.NewGuid() + ".txt");
            try
            {
                var saved = new JobCheckpoint
                {
                    Name = "box.nc", Signature = JobCheckpoint.Sign(commands), Lines = 500, Executed = 321,
                    PassIndex = 1, LoopCount = 2, WcoX = 10.5f, WcoY = -3, Saved = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local)
                };
                saved.Save(path);
                saved.Executed = 400;
                saved.Save(path); // replaces the earlier one

                var loaded = JobCheckpoint.Load(path);
                Assert.Equal(400, loaded.Executed);
                Assert.Equal(380, loaded.ResumeLine);
                Assert.Equal(10.5f, loaded.WcoX);
                Assert.True(loaded.HasWorkOffset);
                Assert.Equal(saved.Saved, loaded.Saved);
                Assert.True(loaded.Matches(JobCheckpoint.Sign(commands), 500));
                Assert.False(loaded.Matches(JobCheckpoint.Sign(new[] { new GrblCommand("G1 X1"), new GrblCommand("G1 X3") }), 500));
                Assert.False(loaded.Matches(loaded.Signature, 499));

                JobCheckpoint.Clear(path);
                Assert.Null(JobCheckpoint.Load(path));
            }
            finally { JobCheckpoint.Clear(path); }
        }

        [Fact]
        public void CheckpointIgnoresDamagedFilesAndFinishedJobs()
        {
            string path = Path.Combine(Path.GetTempPath(), "checkpoint-" + Guid.NewGuid() + ".txt");
            try
            {
                File.WriteAllText(path, "lines=nope");
                Assert.Null(JobCheckpoint.Load(path));
                var done = new JobCheckpoint { Signature = 5, Lines = 10, Executed = 10 };
                Assert.False(done.Matches(5, 10));
                Assert.Equal(0, new JobCheckpoint { Lines = 10, Executed = 3 }.ResumeLine);
            }
            finally { JobCheckpoint.Clear(path); }
        }

        // ---------- Queue and presets ----------

        [Fact]
        public void JobQueueKeepsOrderAndHandsOutTheNext()
        {
            JobQueue.Clear();
            JobQueue.Add(new[] { "a.nc", "b.nc", "c.nc", " " });
            Assert.Equal(new[] { "a.nc", "b.nc", "c.nc" }, JobQueue.Items);
            Assert.Equal(0, JobQueue.Move(1, -1));
            Assert.Equal(new[] { "b.nc", "a.nc", "c.nc" }, JobQueue.Items);
            Assert.Equal(2, JobQueue.Move(2, 1)); // already last
            JobQueue.RemoveAt(2);
            Assert.Equal("b.nc", JobQueue.TakeNext());
            Assert.Equal("a.nc", JobQueue.TakeNext());
            Assert.Null(JobQueue.TakeNext());
        }

        [Fact]
        public void MachinePresetsSaveAndRestoreSettings()
        {
            Settings.SetObject("Firmware Type", Firmware.Grbl);
            Settings.SetObject("Raster.OverscanMm", 3m);
            Assert.True(MachinePresets.Save("Diode 10W") >= 2);

            Settings.SetObject("Firmware Type", Firmware.Smoothie);
            Settings.SetObject("Raster.OverscanMm", 0m);
            MachinePresets.Apply("Diode 10W");

            Assert.Equal(Firmware.Grbl, Settings.GetObject("Firmware Type", Firmware.Smoothie));
            Assert.Equal(3m, Settings.GetObject("Raster.OverscanMm", 0m));
            Assert.Equal("Diode 10W", MachinePresets.Active);
            Assert.Contains("Diode 10W", MachinePresets.Names);

            MachinePresets.Delete("Diode 10W");
            Assert.DoesNotContain("Diode 10W", MachinePresets.Names);
            Assert.Throws<ArgumentException>(() => MachinePresets.Apply("Diode 10W"));
            Assert.Throws<ArgumentException>(() => MachinePresets.Save(" "));
        }

        // ---------- Undo ----------

        [Fact]
        public void UndoHistoryWalksBackAndForward()
        {
            var history = new UndoHistory<string>(limit: 2);
            history.Record("Place", "v1");
            history.Record("Kerf", "v2");
            history.Record("Fill", "v3"); // v1 falls off the end

            Assert.Equal("Fill", history.UndoName);
            Assert.Equal("v3", history.Undo("v4"));
            Assert.Equal("v2", history.Undo("v3"));
            Assert.False(history.CanUndo);
            Assert.Equal("v3", history.Redo("v2"));
            Assert.Equal("Fill", history.RedoName);

            history.Record("New", "v3");
            Assert.False(history.CanRedo); // a new change drops what was undone
        }
    }
}
