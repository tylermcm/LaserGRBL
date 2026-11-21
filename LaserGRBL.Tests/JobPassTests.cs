using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;
using ScanDirection = LaserGRBL.RasterConverter.ImageProcessor.Direction;

namespace LaserGRBL.Tests
{
    [CollectionDefinition("Job passes", DisableParallelization = true)]
    public class JobPassCollection { }

    [Collection("Job passes")]
    public class JobPassTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static JobPassTests()
        {
            // Keep settings/log writes from these hardware-free tests out of the user's profile.
            string data = Path.Combine(Path.GetTempPath(), "LaserGRBL-pass-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(data);
            typeof(GrblCore).GetField("mDataPath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, data);
            Settings.SetObject("DisableSafetyCountdown", true);
            Settings.SetObject("GCode.CustomHeader", "G90");
            Settings.SetObject("GCode.CustomPasses", "; between passes");
            Settings.SetObject("GCode.CustomFooter", "M5");
        }

        [Theory]
        [InlineData("G1X10Y20S1000F600", "G1X10Y20S500F1200")]
        [InlineData("S0 F600", "S0 F1200")]
        [InlineData("X1 S250 F300", "X1 S125 F600")]
        [InlineData("G1 S1000 (S800 F900) F600 ; S700 F200", "G1 S500 (S800 F900) F1200 ; S700 F200")]
        [InlineData("$30=1000", "$30=1000")]
        [InlineData("G0 X0 Y0 M5", "G0 X0 Y0 M5")]
        public void ScalesOnlyPowerAndFeedWords(string input, string expected)
        {
            var source = new GrblCommand(input);
            Assert.Equal(expected, source.CloneForPass(new JobPass(50, 200)).Command);
            Assert.Equal(input, source.Command);
        }

        [Fact]
        public void PreservesGrayscaleTimingAndResultLinkWithoutCompounding()
        {
            var source = new GrblCommand("G1 X1 S250 F600");
            source.SetOffset(TimeSpan.FromSeconds(10));
            source.BuildHelper();
            var first = source.CloneForPass(new JobPass(50));
            first.BuildHelper();
            Assert.Equal(125m, first.S.Number);
            Assert.Equal(250m, source.S.Number);
            Assert.Equal(source.TimeOffset, first.TimeOffset);
            first.SetResult("ok", false);
            Assert.Equal(GrblCommand.CommandStatus.ResponseGood, source.Status);
            Assert.Equal("G1 X1 S250 F600", source.CloneForPass(new JobPass(100)).Command);
            Assert.Equal("G1 X1 S125 F600", source.CloneForPass(new JobPass(50)).Command);
        }

        [Fact]
        public void UsesInvariantDecimalFormatting()
        {
            CultureInfo previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.Equal("S0.25 F0.75", new GrblCommand("S.5 F1.5").CloneForPass(new JobPass(50, 50)).Command);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = previous; }
        }

        [Fact]
        public void ValidatesAndSnapshotsThePlan()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JobPass(101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new JobPass(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new JobPass(50, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new JobPass(50, 1001));
            Assert.Throws<ArgumentException>(() => new JobPassPlan(new JobPass[0]));
            Assert.Throws<ArgumentException>(() => new JobPassPlan(Enumerable.Repeat(new JobPass(50), 100)));
            Assert.Throws<ArgumentException>(() => new JobPassPlan(new JobPass[] { null }));
            var input = new[] { new JobPass(50), new JobPass(100), new JobPass(50, 200) };
            var plan = new JobPassPlan(input);
            input[0] = new JobPass(0);
            Assert.Equal(50m, plan[0].PowerPercent);
            Assert.Equal(200m, plan[2].SpeedPercent);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExportContainsEveryPassWithIndependentPowerAndSpeed(bool lfOnly)
        {
            var file = SampleFile();
            string path = Path.GetTempFileName();
            try
            {
                file.SaveGCODE(path, false, false, false, 1, lfOnly, null, SamplePlan());
                string[] lines = File.ReadAllLines(path).Where(l => !l.StartsWith(";")).ToArray();
                Assert.Equal(15, lines.Length);
                Assert.Equal("G1 X10 S500 F600", lines[1]);
                Assert.Equal("G1 X10 S1000 F600", lines[6]);
                Assert.Equal("G1 X10 S500 F1200", lines[11]);
                Assert.Equal("G1 X10 S1000 F600", file.Commands[1].Command);
                if (lfOnly) Assert.DoesNotContain("\r", File.ReadAllText(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void OrdinaryExportStillRepeatsUnchangedCommands()
        {
            string path = Path.GetTempFileName();
            try
            {
                SampleFile().SaveGCODE(path, false, false, false, 2, false, null);
                Assert.Equal(2, File.ReadAllLines(path).Count(l => l == "G1 X10 S1000 F600"));
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData(ScanDirection.Horizontal, 4, 3, 1, 0)]
        [InlineData(ScanDirection.Vertical, 8, 6, 0, 1)]
        [InlineData(ScanDirection.Diagonal, 4, 3, 1, -1)]
        [InlineData(ScanDirection.NewReverseDiagonal, 8, 6, 1, 1)]
        public void RasterPassChangesScanGeometryWithinTheSamePhysicalArea(
            ScanDirection direction, int width, int height, int xKind, int yKind)
        {
            var source = new GrblFile();
            using (var image = new Bitmap(width, height))
            {
                // Mark the left half only, so a reflected design would be detected.
                using (var graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.White);
                    graphics.FillRectangle(Brushes.Black, 0, 0, width / 2, height);
                }
                var config = new GrblFile.L2LConf
                {
                    oX = 10, oY = 20, markSpeed = 6000, maxPower = 1000,
                    pwm = true, lOn = "M4", lOff = "M5", dir = direction
                };
                source.AppendRasterPass(image, config, new SizeF(4, 3), new JobPass(50, 75));
            }

            Assert.Equal("M5", source.Commands.First().Command);
            Assert.Equal("M5", source.Commands.Last().Command);
            decimal x = 0, y = 0;
            int burns = 0;
            foreach (var command in source.Commands)
            {
                command.BuildHelper();
                decimal nextX = command.X == null ? x : command.X.Number;
                decimal nextY = command.Y == null ? y : command.Y.Number;
                if (command.F != null) Assert.Equal(4500m, command.F.Number);
                if (command.S != null && command.S.Number > 0)
                {
                    burns++;
                    Assert.Equal(500m, command.S.Number);
                    Assert.InRange(x, 10m, 12m);
                    Assert.InRange(nextX, 10m, 12m);
                    Assert.InRange(y, 20m, 23m);
                    Assert.InRange(nextY, 20m, 23m);
                    Assert.Equal(xKind == 0, nextX == x);
                    Assert.Equal(yKind == 0, nextY == y);
                    if (xKind != 0 && yKind != 0)
                        Assert.Equal(yKind, Math.Sign((nextX - x) * (nextY - y)));
                }
                x = nextX;
                y = nextY;
            }
            Assert.True(burns > 0);
            if (direction == ScanDirection.Horizontal) Assert.Equal(height, burns);
            if (direction == ScanDirection.Vertical) Assert.Equal(width / 2, burns);
        }

        [Fact]
        public void QueueAdvancesPassesAndFreshRunRestartsTheSequence()
        {
            var core = CoreWithoutHardware();
            core.ConfigurePasses(SamplePlan());
            Call(core, "RunProgramFromStart", false, true, false);
            AssertQueued(core, "G1 X10 S500 F600");
            Assert.Throws<InvalidOperationException>(() => core.ConfigurePasses(null));
            CompletePass(core);
            Assert.Equal(2, core.CurrentPassNumber);
            AssertQueued(core, "G1 X10 S1000 F600");
            CompletePass(core);
            Assert.Equal(3, core.CurrentPassNumber);
            AssertQueued(core, "G1 X10 S500 F1200");
            CompletePass(core);
            Assert.False(core.InProgram);
            Queue(core).Clear();
            Call(core, "RunProgramFromStart", false, true, false);
            Assert.Equal(1, core.CurrentPassNumber);
            Assert.Equal(3m, core.LoopCount);
            AssertQueued(core, "G1 X10 S500 F600");
        }

        [Fact]
        public void ResumeAndRestartRetainInterruptedPassSettings()
        {
            var core = CoreWithoutHardware();
            core.ConfigurePasses(SamplePlan());
            Call(core, "RunProgramFromStart", false, true, false);
            CompletePass(core);
            CompletePass(core);
            var time = (TimeProjection)Get(core, "mTP");
            time.JobExecuted(TimeSpan.Zero);
            time.JobExecuted(TimeSpan.Zero);
            time.JobEnd(true);
            Queue(core).Clear();
            Call(core, "ContinueProgramFromKnown", 2, false, false);
            Assert.Equal(3, core.CurrentPassNumber);
            Assert.Contains(Queue(core), cmd => cmd.Command.StartsWith("M5 G0") && cmd.Command.Contains("S500") && cmd.Command.Contains("F1200"));
            AssertQueued(core, "G1 X20 S125");
            time.JobEnd(true);
            Queue(core).Clear();
            Call(core, "RunProgramFromStart", false, false, false);
            Assert.Equal(3, core.CurrentPassNumber);
            AssertQueued(core, "G1 X10 S500 F1200");
        }

        [Fact]
        public void LoadingAnotherFileClearsCustomPasses()
        {
            var core = CoreWithoutHardware();
            core.ConfigurePasses(SamplePlan());
            Call(core, "RiseOnFileLoaded", 0L, "new.nc");
            Assert.Null(core.Passes);
            Assert.Equal(1m, core.LoopCount);
        }

        [Fact]
        public void ResumeBeforeFirstAcknowledgementKeepsCurrentPass()
        {
            var core = CoreWithoutHardware();
            core.ConfigurePasses(SamplePlan());
            Call(core, "RunProgramFromStart", false, true, false);
            CompletePass(core);
            CompletePass(core);
            ((TimeProjection)Get(core, "mTP")).JobEnd(true);
            Queue(core).Clear();
            Call(core, "ContinueProgramFromKnown", 1, false, false);
            Assert.Equal(3, core.CurrentPassNumber);
            AssertQueued(core, "G1 X10 S500 F1200");
        }

        [Fact]
        public void CheckModeDoesNotAdvanceToAnotherPass()
        {
            var core = CoreWithoutHardware();
            core.ConfigurePasses(SamplePlan());
            Set(core, "mMachineStatus", GrblCore.MacStatus.Check);
            Call(core, "RunProgramFromStart", false, true, false);
            CompletePass(core);
            Assert.False(core.InProgram);
            Assert.Equal(1, core.CurrentPassNumber);
            Assert.DoesNotContain(Queue(core), c => c.Command.Contains("X10"));
        }

        [Fact]
        public void ExportScalesCustomCodeForTheCorrespondingPass()
        {
            string path = Path.GetTempFileName();
            Settings.SetObject("GCode.CustomHeader", "M5 S800 F400");
            Settings.SetObject("GCode.CustomPasses", "M5 S600 F300");
            Settings.SetObject("GCode.CustomFooter", "M5 S400 F200");
            try
            {
                SampleFile().SaveGCODE(path, true, true, true, 1, true, CoreWithoutHardware(), SamplePlan());
                string[] lines = File.ReadAllLines(path);
                Assert.Equal("M5 S400 F400", lines.First());
                Assert.Contains("M5 S600 F300", lines);
                Assert.Contains("M5 S300 F600", lines);
                Assert.Equal("M5 S200 F400", lines.Last());
            }
            finally
            {
                File.Delete(path);
                Settings.SetObject("GCode.CustomHeader", "G90");
                Settings.SetObject("GCode.CustomPasses", "; between passes");
                Settings.SetObject("GCode.CustomFooter", "M5");
            }
        }

        [Fact]
        public void LoadedRasterCanBeRebuiltAndRestoredAfterImportCloses()
        {
            var core = CoreWithoutHardware();
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            using (var bitmap = new Bitmap(16, 12))
            {
                using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.DimGray);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            try
            {
                var importer = new LaserGRBL.RasterConverter.ImageProcessor(core, path, new Size(16, 12), false)
                {
                    TargetSize = new SizeF(4, 3), TargetOffset = new PointF(10, 20),
                    MarkSpeed = 6000, MaxPower = 1000, LaserOn = "M4", LaserOff = "M5",
                    Brightness = 100, Contrast = 100, Red = 100, Green = 100, Blue = 100,
                    LineDirection = ScanDirection.Horizontal, Quality = 10
                };
                try { importer.GetType().GetMethod("LoadRasterJob", PrivateInstance).Invoke(importer, null); }
                finally { importer.Dispose(); }
                File.Delete(path);

                var file = core.LoadedFile;
                var source = file.RasterSource;
                Assert.NotNull(source);
                string[] original = file.Commands.Select(c => c.Command).ToArray();
                var plan = new JobPassPlan(new[] {
                    new JobPass(50, 75, ScanDirection.Vertical, 8),
                    new JobPass(100, 100, ScanDirection.NewReverseDiagonal, 10)
                });
                Assert.Null(RebuildRasterJob(core, plan));
                Assert.Same(plan, file.RasterSource.RasterPasses);
                Assert.Equal(1m, core.LoopCount);
                Assert.Contains(file.Commands, c => c.Command.Contains("F4500"));
                string[] rebuilt = file.Commands.Select(c => c.Command).ToArray();

                var tooLarge = new JobPassPlan(new[] { new JobPass(50, 100, ScanDirection.Horizontal, 5000000) });
                Assert.IsType<InvalidOperationException>(RebuildRasterJob(core, tooLarge));
                Assert.Equal(rebuilt, file.Commands.Select(c => c.Command).ToArray());
                Assert.Same(plan, file.RasterSource.RasterPasses);
                Assert.True(file.CanUndoRasterEdit);
                Assert.False(file.CanRedoRasterEdit);

                Assert.Null(ApplyHistory(core, GrblFile.RasterEdit.Undo));
                Assert.Same(source, file.RasterSource);
                Assert.Equal(original, file.Commands.Select(c => c.Command).ToArray());
                Assert.False(file.CanUndoRasterEdit);
                Assert.True(file.CanRedoRasterEdit);
                Assert.Null(ApplyHistory(core, GrblFile.RasterEdit.Redo));
                Assert.Equal(rebuilt, file.Commands.Select(c => c.Command).ToArray());

                var draft = file.RasterSource.CopyRasterSource();
                draft.CropImage(new Rectangle(0, 0, 8, 12), new Size(16, 12));
                draft.Contrast = 140;
                draft.TargetSize = new SizeF(2, 3);
                Assert.Null(WaitForRasterEdit(done => core.ApplyRasterEdit(file.RasterSource, draft, GrblFile.RasterEdit.Apply, done)));
                string[] edited = file.Commands.Select(c => c.Command).ToArray();
                Assert.NotEqual(rebuilt, edited);
                Assert.Equal(8, file.RasterSource.TrueOriginal.Width);
                Assert.Equal(140, file.RasterSource.Contrast);
                Assert.Equal(new SizeF(2, 3), file.RasterSource.TargetSize);
                AssertSavedRasterProject(file.RasterSource, plan);
                Assert.Null(ApplyHistory(core, GrblFile.RasterEdit.Undo));
                Assert.Equal(16, file.RasterSource.TrueOriginal.Width);
                Assert.Equal(100, file.RasterSource.Contrast);
                Assert.Equal(new SizeF(4, 3), file.RasterSource.TargetSize);
                Assert.Equal(rebuilt, file.Commands.Select(c => c.Command).ToArray());
                Assert.Null(ApplyHistory(core, GrblFile.RasterEdit.Redo));
                Assert.Equal(edited, file.Commands.Select(c => c.Command).ToArray());

                Assert.Null(ApplyHistory(core, GrblFile.RasterEdit.Undo));
                Assert.Null(RebuildRasterJob(core, null));
                Assert.Equal(original, file.Commands.Select(c => c.Command).ToArray());
                Assert.Null(file.RasterSource.RasterPasses);
                Assert.False(file.CanRedoRasterEdit);
                typeof(GrblFile).GetMethod("RiseOnFileLoading", PrivateInstance).Invoke(file, new object[] { "another.nc", null });
                Assert.Null(file.RasterSource);
                Assert.False(file.CanUndoRasterEdit);
            }
            finally { File.Delete(path); }
        }

        private static Exception RebuildRasterJob(GrblCore core, JobPassPlan plan)
        {
            return WaitForRasterEdit(done => core.RebuildRasterPasses(core.LoadedFile.RasterSource, plan, done));
        }

        private static void AssertSavedRasterProject(LaserGRBL.RasterConverter.ImageProcessor source, JobPassPlan plan)
        {
            string projectPath = Path.GetTempFileName();
            source.RasterSettings = new Dictionary<string, object>
            {
                { "GrayScaleConversion.Parameters.Contrast", source.Contrast },
                { "GrayScaleConversion.Gcode.ImageSize.W", source.TargetSize.Width }
            };
            try
            {
                Tools.Project.StoreSettings(projectPath, source);
                var saved = Assert.Single(Tools.Project.LoadProject(projectPath));
                Assert.Equal(140, saved["GrayScaleConversion.Parameters.Contrast"]);
                Assert.Equal(2F, saved["GrayScaleConversion.Gcode.ImageSize.W"]);
                Assert.Equal(true, saved[GrblCore.ProjectRasterSizeKey]);
                Assert.Equal(plan[0].LinesPerMm, ((JobPassPlan)saved[GrblCore.ProjectRasterPassesKey])[0].LinesPerMm);
                using (var stream = new MemoryStream(Convert.FromBase64String((string)saved["ImageBase64"])))
                using (var crop = Image.FromStream(stream))
                    Assert.Equal(new Size(8, 12), crop.Size);
            }
            finally { File.Delete(projectPath); }
        }

        private static Exception ApplyHistory(GrblCore core, GrblFile.RasterEdit edit)
        {
            var file = core.LoadedFile;
            return WaitForRasterEdit(done => core.ApplyRasterEdit(file.RasterSource, file.GetRasterHistory(edit), edit, done));
        }

        private static Exception WaitForRasterEdit(Action<Action<Exception>> start)
        {
            Exception error = null;
            using (var finished = new System.Threading.ManualResetEventSlim())
            {
                var context = System.Threading.SynchronizationContext.Current;
                try
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    start(result =>
                    {
                        error = result;
                        finished.Set();
                    });
                }
                finally { System.Threading.SynchronizationContext.SetSynchronizationContext(context); }
                Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
            }
            return error;
        }

        [Fact]
        public void ThreeRasterPassesAndSvgOutlinesCompileAsIndependentOperations()
        {
            var core = CoreWithoutHardware();
            var raster = new GrblFile();
            for (int i = 0; i < 3; i++)
            {
                using (var image = new Bitmap(4, 3))
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Black);
                    raster.AppendRasterPass(image, new GrblFile.L2LConf
                    {
                        oX = 1, oY = 2, markSpeed = 6000, maxPower = 1000,
                        pwm = true, lOn = "M4", lOff = "M5", dir = ScanDirection.Horizontal
                    }, new SizeF(4, 3), new JobPass(50, 100, ScanDirection.Horizontal, 10));
                }
            }
            raster.Analyze();
            var file = core.LoadedFile;
            file.LoadRasterPasses(raster, "Tile designs.png", false);

            string svg = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".svg");
            string project = Path.GetTempFileName();
            try
            {
                File.WriteAllText(svg, "<svg xmlns='http://www.w3.org/2000/svg' width='24mm' height='24mm' viewBox='0 0 24 24'><rect x='2' y='2' width='6' height='6'/><rect x='12' y='2' width='6' height='6'/></svg>");
                using (var finished = new System.Threading.ManualResetEventSlim())
                {
                    file.OnFileLoaded += (elapsed, name) => { if (name == svg) finished.Set(); };
                    file.LoadImportedSVG(svg, true, core, LaserGRBL.SvgConverter.ColorFilter.All);
                    Assert.True(finished.Wait(TimeSpan.FromSeconds(10)));
                }

                Assert.Equal(2, file.Operations.Count);
                var design = file.Operations[0];
                var outline = file.Operations[1];
                Assert.Equal(3, design.CompiledPasses.Count);
                Assert.Equal(1, design.Repeats);
                Assert.Equal(1, outline.Repeats);
                Assert.Equal(2, outline.Contours.Length);
                Assert.Equal(design.End, outline.Start);
                Assert.Equal(file.Count, outline.End);
                Assert.Equal("M5", file[outline.Start].Command);

                var originalOutline = outline.Source;
                var draft = outline.WithSettings("Outlines", 2, new JobPass(80, 50), true);
                draft.RepeatContours = true;
                var compiled = file.BuildOperations(new[] { design, draft });
                var cut = compiled.Operations[1];
                var starts = new[] { outline.Contours[0], outline.Contours[1] }
                    .Select(index =>
                    {
                        var rapid = new GrblCommand(originalOutline.Skip(index).First(command => command.Command.StartsWith("G0 ")).Command);
                        rapid.BuildHelper();
                        return rapid.Command;
                    }).ToArray();
                Assert.Equal(new[] { starts[0], starts[0], starts[1], starts[1] },
                    compiled.Commands.Skip(cut.Start).Where(command => starts.Contains(command.Command)).Select(command => command.Command).ToArray());

                Tools.Project.StoreOperations(project, compiled.Operations);
                var saved = (JobOperation.Saved[])Assert.Single(Tools.Project.LoadProject(project))[Tools.Project.OperationsKey];
                Assert.Equal(compiled.Commands.Select(c => c.Command).ToArray(),
                    file.BuildOperations(saved.Select(entry => entry.Restore())).Commands.Select(c => c.Command).ToArray());
            }
            finally
            {
                File.Delete(svg);
                File.Delete(project);
            }
        }

        [Fact]
        public void TimelineSeeksAndAccountsForBurnTravelAndDwell()
        {
            var file = new GrblFile();
            file.Commands.AddRange(new[] { "G90", "M4 S500", "G1 X10 Y0 F600", "M5", "G4 P2", "G0 X20 Y0" }
                .Select(line => new GrblCommand(line)));
            file.Analyze();
            var timeline = new JobTimeline(file, 0, file.Count);
            var duration = timeline.Measure();
            Assert.Equal(1, duration.Engraving, 5);
            Assert.Equal(2, duration.Waits, 5);
            Assert.True(duration.Travel > 0);
            Assert.Equal(timeline.Duration, duration.Engraving + duration.Waits + duration.Travel, 5);
            Assert.Equal(new PointF(5, 0), timeline.Moves[timeline.Seek(0.5)].At(0.5));
            Assert.True(timeline.Moves[timeline.Seek(1.5)].Waiting);
            Assert.Equal(new PointF(10, 0), timeline.Moves[timeline.Seek(1.5)].At(0.25));
        }

        [Fact]
        public void PlacementTransformsModalCoordinatesAndArcCentersTogether()
        {
            var source = new[] { "G0 X1 Y2", "M4 S500", "G1 X3 F600", "G3 Y4 I0 J1", "M5" }
                .Select(line => new GrblCommand(line)).ToArray();
            var placement = new JobPlacement(new PointF(1, 2), new PointF(10, 20), 2, 90);
            var transformed = placement.Transform(source);
            Assert.Equal(new[] { "G0 X10 Y20", "M4 S500", "G1 X10 Y24 F600", "G3 X6 Y24 I-2 J0", "M5" },
                transformed.Select(command => command.Command).ToArray());
            Assert.Equal("G1 X3 F600", source[2].Command);
        }

        [Fact]
        public void PassProfilesSaveAllRasterSettingsAndKeepToolpathProfilesSeparate()
        {
            string name = "Profile " + Guid.NewGuid();
            var raster = new JobPassProfiles(true);
            var toolpath = new JobPassProfiles(false);
            var plan = new JobPassPlan(new[]
            {
                new JobPass(50, 95, ScanDirection.Horizontal, 10),
                new JobPass(100, 75, ScanDirection.Diagonal, 8),
                new JobPass(50, 100, ScanDirection.NewReverseDiagonal, 10)
            });

            try
            {
                raster.Save(" " + name + " ", plan);
                toolpath.Save(name, new JobPassPlan(new[] { new JobPass(70) }));
                Assert.True(new JobPassProfiles(true).TryGet(name.ToUpperInvariant(), out var saved));
                Assert.Equal(3, saved.Count);
                Assert.Equal(50m, saved[0].PowerPercent);
                Assert.Equal(75m, saved[1].SpeedPercent);
                Assert.Equal(8m, saved[1].LinesPerMm);
                Assert.Equal(ScanDirection.NewReverseDiagonal, saved[2].Direction);
                Assert.True(new JobPassProfiles(false).TryGet(name, out var existingPath));
                Assert.Equal(70m, existingPath[0].PowerPercent);
                Settings.Exiting();
                var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                using (var stream = File.OpenRead(Path.Combine(GrblCore.DataPath, "LaserGRBL.Settings.bin")))
                {
                    var onDisk = (Dictionary<string, object>)formatter.Deserialize(stream);
                    var savedProfiles = (Dictionary<string, JobPassPlan>)onDisk["JobPassProfiles.Raster"];
                    Assert.Equal(8m, savedProfiles[name][1].LinesPerMm);
                    Assert.Equal(ScanDirection.NewReverseDiagonal, savedProfiles[name][2].Direction);
                }
            }
            finally
            {
                raster.Remove(name);
                toolpath.Remove(name);
            }
            Assert.False(new JobPassProfiles(true).TryGet(name, out _));
        }

        [Fact]
        public void InlineEditorKeepsTheMachineFromStartingAnUnappliedJob()
        {
            var core = CoreWithoutHardware();
            Assert.True(core.CanSendFile);

            core.JobEditorOpen = true;
            Assert.False(core.CanSendFile);
            Assert.True(core.CanConfigurePasses);
            core.ConfigurePasses(SamplePlan());
            Assert.False(core.CanSendFile);

            core.JobEditorOpen = false;
            Assert.True(core.CanSendFile);
            Assert.Equal(3, core.Passes.Count);
        }

        private static JobPassPlan SamplePlan()
        {
            return new JobPassPlan(new[] { new JobPass(50), new JobPass(100), new JobPass(50, 200) });
        }

        private static GrblFile SampleFile()
        {
            var file = new GrblFile();
            file.Commands.AddRange(new[] { "G90 M4", "G1 X10 S1000 F600", "X20 S250", "M5", "G0 X0" }.Select(l => new GrblCommand(l)));
            return file;
        }

        private static GrblCore CoreWithoutHardware()
        {
            var core = (GrblCore)FormatterServices.GetUninitializedObject(typeof(GrblCore));
            var queue = new Queue<GrblCommand>();
            var sent = new List<IGrblRow>();
            Set(core, "file", SampleFile());
            Set(core, "mTP", new TimeProjection());
            Set(core, "mQueue", queue);
            Set(core, "mQueuePtr", queue);
            Set(core, "mPending", new Queue<GrblCommand>());
            Set(core, "mSent", sent);
            Set(core, "mSentPtr", sent);
            Set(core, "mMachineStatus", GrblCore.MacStatus.Idle);
            core.LoopCount = 1;
            return core;
        }

        private static void CompletePass(GrblCore core)
        {
            var time = (TimeProjection)Get(core, "mTP");
            foreach (var command in Queue(core)) time.JobExecuted(command.TimeOffset);
            Queue(core).Clear();
            Call(core, "OnProgramEnd");
        }

        private static Queue<GrblCommand> Queue(GrblCore core) { return (Queue<GrblCommand>)Get(core, "mQueue"); }
        private static void AssertQueued(GrblCore core, string command) { Assert.Contains(Queue(core), c => c.Command == command); }
        private static object Get(GrblCore core, string name) { return typeof(GrblCore).GetField(name, PrivateInstance).GetValue(core); }
        private static void Set(GrblCore core, string name, object value) { typeof(GrblCore).GetField(name, PrivateInstance).SetValue(core, value); }
        private static void Call(GrblCore core, string name, params object[] args) { typeof(GrblCore).GetMethod(name, PrivateInstance).Invoke(core, args); }
    }
}
