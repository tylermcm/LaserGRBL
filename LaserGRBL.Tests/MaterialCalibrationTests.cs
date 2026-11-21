using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LaserGRBL.Tests
{
    [Collection("Job passes")]
    public class MaterialCalibrationTests
    {
        static MaterialCalibrationTests()
        {
            // Same isolation as JobPassTests: settings and Material DB writes go to a temp folder.
            var field = typeof(GrblCore).GetField("mDataPath", BindingFlags.Static | BindingFlags.NonPublic);
            if (field.GetValue(null) == null)
            {
                string data = Path.Combine(Path.GetTempPath(), "LaserGRBL-material-tests-" + Guid.NewGuid());
                Directory.CreateDirectory(data);
                field.SetValue(null, data);
            }
        }

        private static GrblCommand[] Commands(params string[] lines)
        {
            return lines.Select(line => new GrblCommand(line)).ToArray();
        }

        // ---------- Material conversion ----------

        [Fact]
        public void StatsIgnoreLabelsAndRapidsAndPickTheMostUsedFeed()
        {
            var stats = ToolpathStats.Measure(Commands(
                "M5 S900 F5000", "G0 X1 Y1", "G1 X2",          // label setup with the laser off
                "M4 S400 F1200", "G1 X3", "G1 X4", "G1 X5",     // engraving
                "G1 X6 S600 F800",                               // one faster, stronger line
                "G0 X0 Y0 S1000"));                              // rapid with S doesn't count
            Assert.Equal(600, stats.MaxPower);
            Assert.Equal(1200, stats.MainFeed);
        }

        [Fact]
        public void MaterialBecomesPercentagesOfTheToolpath()
        {
            var setting = new MaterialSetting { Name = "Birch", Power = 300, Speed = 600, Cycles = 3, Extras = new MaterialExtras(kerfMm: 0.15m) };
            var applied = MaterialApplication.For(setting, Commands("M3 S600 F1200", "G1 X10"), supportsKerf: true);
            Assert.Equal(50m, applied.Pass.PowerPercent);
            Assert.Equal(50m, applied.Pass.SpeedPercent);
            Assert.Equal(3, applied.Repeats);
            Assert.Equal(0.15m, applied.KerfMm);
            Assert.Equal("", applied.Warning);
        }

        [Fact]
        public void MaterialAboveToolpathPeakClampsAndExplains()
        {
            var setting = new MaterialSetting { Power = 900, Speed = 600, Extras = new MaterialExtras(kerfMm: 0.2m) };
            var applied = MaterialApplication.For(setting, Commands("M3 S300 F600", "G1 X10"), supportsKerf: false);
            Assert.Equal(100m, applied.Pass.PowerPercent);
            Assert.Equal(0m, applied.KerfMm);
            Assert.Contains("S300", applied.Warning);
            Assert.Contains("Kerf", applied.Warning);
        }

        [Theory]
        [InlineData(500, 1000, 50)]
        [InlineData(0, 1000, 1)]       // the DB rejects 0%
        [InlineData(1200, 1000, 100)]
        [InlineData(127, 255, 50)]
        public void PowerPercentFitsTheMaterialDbRange(int power, int max, int expected)
        {
            Assert.Equal(expected, MaterialSetting.PowerPercent(power, max));
        }

        // ---------- Test records ----------

        [Fact]
        public void PowerSpeedRecordMatchesGridLabels()
        {
            var record = MaterialTestRecord.ForPowerSpeed(7, 10, 1000, 4000, 100, 1000, 8, "Birch", "M4");
            Assert.Equal(new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 }, record.Powers);
            Assert.Equal(new[] { 1000, 1500, 2000, 2500, 3000, 3500, 4000 }, record.Speeds);
            Assert.Equal(new[] { 8m }, record.LinesPerMm);
            Assert.Equal(new[] { 333 }, MaterialTestRecord.Steps(333, 999, 1));
        }

        [Fact]
        public void CuttingRecordListsEveryPassCount()
        {
            var record = MaterialTestRecord.ForCutting(5, 200, 1000, 2, 4, 1000, "", "M3");
            Assert.Equal(new[] { 2, 3, 4 }, record.Passes);
            Assert.Equal(new[] { 1000 }, record.Powers);
            Assert.Equal("M3", record.LaserMode);
        }

        [Fact]
        public void TestResultIsAddedThenReplacedInMaterialDb()
        {
            var db = new PSHelper.MaterialDB();
            var record = MaterialTestRecord.ForPowerSpeed(3, 3, 1000, 3000, 200, 600, 10, "Birch", "M4");
            var row = record.AddTo(db, "My laser", "Birch", "3mm", "Engrave", 400, 2000, 1, 10, 0, "nice brown", 1000);
            Assert.Equal(40, row.Power);
            Assert.Equal(2000, row.Speed);
            Assert.Contains("S400 F2000", row.Remarks);
            Assert.Equal(10m, MaterialExtrasStore.Get(row.id).LinesPerMm);
            Assert.Equal("M4", MaterialExtrasStore.Get(row.id).LaserMode);

            var again = record.AddTo(db, "My laser", "Birch", "3mm", "Engrave", 600, 3000, 1, 10, 0, "", 1000);
            Assert.Same(row, again);
            Assert.Equal(1, db.Materials.Count);
            Assert.Equal(60, again.Power);

            var setting = MaterialSetting.FromRow(again, 1000);
            Assert.Equal(600, setting.Power);
            Assert.Equal("Birch 3mm · Engrave", setting.Name);
        }

        [Fact]
        public void TestResultRejectsValuesThatWereNotBurned()
        {
            var record = MaterialTestRecord.ForPowerSpeed(2, 2, 1000, 2000, 100, 200, 10, "", "M4");
            Assert.Throws<ArgumentException>(() => record.AddTo(new PSHelper.MaterialDB(), "m", "x", "-", "Engrave", 150, 1000, 1, 10, 0, "", 1000));
        }

        // ---------- Calibration patterns ----------

        [Fact]
        public void FocusOffsetsAreCentredOnCurrentHeight()
        {
            Assert.Equal(new[] { -1.0, -0.5, 0, 0.5, 1.0 }, CalibrationPatterns.FocusOffsets(5, 0.5));
            Assert.Equal(new[] { -0.25, 0.25 }, CalibrationPatterns.FocusOffsets(2, 0.5));
        }

        [Fact]
        public void FocusTestReturnsZToWhereItStarted()
        {
            var commands = CalibrationPatterns.FocusTest(new CalibrationPatterns.FocusOptions { Steps = 7, ZStepMm = 0.3 });
            decimal z = 0;
            bool relative = false;
            foreach (var command in commands)
            {
                command.BuildHelper();
                if (command.G != null && command.G.Number == 91) relative = true;
                if (command.G != null && command.G.Number == 90) relative = false;
                if (command.Z != null)
                {
                    Assert.True(relative, "Z moves must be relative: " + command.Command);
                    z += command.Z.Number;
                }
            }
            Assert.Equal(0m, z);
            Assert.Equal(7, commands.Count(c => c.Command.StartsWith("G1 X") && c.Command.Contains("S1000")));
        }

        [Fact]
        public void RampFocusTestHasNoZ()
        {
            var commands = CalibrationPatterns.FocusTest(new CalibrationPatterns.FocusOptions { UseZAxis = false, RampLengthMm = 60 });
            foreach (var command in commands) command.BuildHelper();
            Assert.DoesNotContain(commands, c => c.Z != null);
            Assert.Contains(commands, c => c.Command == "G1 X63 M3 S1000 F1000");
        }

        [Fact]
        public void IntervalPatchesUseTheirLineSpacing()
        {
            var options = new CalibrationPatterns.IntervalOptions { LinesPerMm = new[] { 5m, 10m }, PatchHeightMm = 2 };
            var commands = CalibrationPatterns.IntervalTest(options).Select(c => c.Command).ToList();
            // 2 mm at 5 lines/mm = 11 lines; at 10 lines/mm = 21 lines.
            int burned = commands.Count(c => c.StartsWith("G1 X") && c.EndsWith(" S600"));
            Assert.Equal(11 + 21, burned);
            Assert.Contains("G1 Y9.1 S0", commands);
            var record = CalibrationPatterns.IntervalRecord(options, "Slate");
            Assert.Equal(new[] { 5m, 10m }, record.LinesPerMm);
        }

        // ---------- Kerf ----------

        private static readonly string[] Prefix = { "G90", "M4 S0" };

        private static List<string> Square(double x, double y, double size)
        {
            string f(double v) => v.ToString(CultureInfo.InvariantCulture);
            return new List<string>
            {
                "S0", $"G0 X{f(x)} Y{f(y)}", "S1000",
                $"G1 X{f(x + size)} Y{f(y)} F1000", $"G1 X{f(x + size)} Y{f(y + size)}", $"G1 X{f(x)} Y{f(y + size)}", $"G1 X{f(x)} Y{f(y)}"
            };
        }

        private static (GrblCommand[] commands, int[] contours) Job(params List<string>[] blocks)
        {
            var lines = new List<string>(Prefix);
            var contours = new List<int>();
            foreach (var block in blocks) { contours.Add(lines.Count); lines.AddRange(block); }
            return (Commands(lines.ToArray()), contours.ToArray());
        }

        private static (double minX, double minY, double maxX, double maxY) Bounds(GrblCommand[] commands, int start, int end)
        {
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = start; i < end; i++)
            {
                var c = new GrblCommand(commands[i].Command);
                c.BuildHelper();
                if (c.G != null && c.G.Number == 1) { xs.Add((double)c.X.Number); ys.Add((double)c.Y.Number); }
            }
            return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        [Fact]
        public void KerfGrowsOutlinesAndShrinksHoles()
        {
            var (source, contours) = Job(Square(0, 0, 10), Square(3, 3, 4));
            var result = KerfCompensation.Apply(source, contours, 0.2);
            Assert.Equal(2, result.Compensated);
            Assert.Equal(0, result.Skipped);
            var outer = Bounds(result.Commands, result.Contours[0], result.Contours[1]);
            var hole = Bounds(result.Commands, result.Contours[1], result.Commands.Length);
            Assert.Equal(-0.1, outer.minX, 2); Assert.Equal(10.1, outer.maxX, 2);
            Assert.Equal(-0.1, outer.minY, 2); Assert.Equal(10.1, outer.maxY, 2);
            Assert.Equal(3.1, hole.minX, 2); Assert.Equal(6.9, hole.maxX, 2);
            // Prefix, pen-up/down commands and feed are kept.
            Assert.Equal("G90", result.Commands[0].Command);
            Assert.Equal("S0", result.Commands[result.Contours[0]].Command);
            Assert.StartsWith("G0 ", result.Commands[result.Contours[0] + 1].Command);
            Assert.Equal("S1000", result.Commands[result.Contours[0] + 2].Command);
            Assert.EndsWith(" F1000", result.Commands[result.Contours[0] + 3].Command);
        }

        [Fact]
        public void KerfPreservesDirectionAndClosesThePath()
        {
            var (source, contours) = Job(Square(0, 0, 10));
            var result = KerfCompensation.Apply(source, contours, 0.4);
            var moves = result.Commands.Skip(contours[0] + 3).Select(c => { var g = new GrblCommand(c.Command); g.BuildHelper(); return g; }).ToList();
            var rapid = new GrblCommand(result.Commands[contours[0] + 1].Command); rapid.BuildHelper();
            Assert.Equal(rapid.X.Number, moves.Last().X.Number);
            Assert.Equal(rapid.Y.Number, moves.Last().Y.Number);
            // Original runs counter-clockwise (positive area); so must the offset.
            double area = 0;
            var pts = new[] { rapid }.Concat(moves).ToList();
            for (int i = 0; i + 1 < pts.Count; i++)
                area += (double)(pts[i].X.Number * pts[i + 1].Y.Number - pts[i + 1].X.Number * pts[i].Y.Number);
            Assert.True(area > 0);
        }

        [Fact]
        public void KerfFlattensFullCircleArcs()
        {
            var circle = new List<string> { "S0", "G0 X15 Y10", "S1000", "G2 X15 Y10 I-5 J0 F900" };
            var (source, contours) = Job(circle);
            var result = KerfCompensation.Apply(source, contours, 0.2);
            Assert.Equal(1, result.Compensated);
            var b = Bounds(result.Commands, result.Contours[0], result.Commands.Length);
            Assert.Equal(4.9, b.minX, 1); Assert.Equal(15.1, b.maxX, 1);
            Assert.Equal(4.9, b.minY, 1); Assert.Equal(15.1, b.maxY, 1);
        }

        [Fact]
        public void KerfLeavesOpenPathsAndTinyHolesAlone()
        {
            var open = new List<string> { "S0", "G0 X0 Y0", "S1000", "G1 X10 Y0 F1000", "G1 X10 Y10" };
            var (source, contours) = Job(Square(0, 0, 10), Square(4, 4, 0.1), open);
            var result = KerfCompensation.Apply(source, contours, 0.5);
            Assert.Equal(1, result.Compensated);
            Assert.Equal(2, result.Skipped);
            var tail = result.Commands.Skip(result.Contours[1]).Select(c => c.Command);
            var expected = source.Skip(contours[1]).Select(c => c.Command);
            Assert.Equal(expected, tail);
        }

        [Fact]
        public void KerfOnOperationKeepsPerContourRepeats()
        {
            var (source, contours) = Job(Square(0, 0, 10), Square(20, 0, 10));
            var operation = new JobOperation("Cut", source, 2) { Contours = contours, RepeatContours = true, KerfMm = 0.2m };
            var output = new List<GrblCommand>();
            operation.AppendTo(output);
            Assert.Equal(2, operation.KerfCompensated);
            // Each contour is burned twice in a row; the first contour's G0 lands just outside its corner.
            var rapids = output.Where(c => c.Command.StartsWith("G0 ")).Select(c => c.Command).ToList();
            Assert.Equal(4, rapids.Count);
            Assert.Equal(rapids[0], rapids[1]);
            Assert.NotEqual(rapids[1], rapids[2]);
            Assert.Equal(0.2m, operation.WithSettings("Cut", 1, null, true).KerfMm);
        }

        // ---------- Home before first run ----------

        [Theory]
        [InlineData(true, true, false, false, "HomeThenRun")]
        [InlineData(true, true, false, true, "Ask")]
        [InlineData(true, true, true, false, "Run")]
        [InlineData(true, true, true, true, "Run")]
        [InlineData(true, false, false, false, "Run")]   // $22=0: $H would error
        [InlineData(false, true, false, false, "Run")]   // turned off
        public void HomeBeforeFirstRunDecision(bool enabled, bool homing, bool homed, bool offset, string expected)
        {
            Assert.Equal(expected, GrblCore.DecideHomeBeforeRun(enabled, homing, homed, offset).ToString());
        }
    }
}
