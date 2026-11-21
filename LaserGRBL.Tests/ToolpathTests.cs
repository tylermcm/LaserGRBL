using ClipperLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LaserGRBL.Tests
{
    [Collection("Job passes")]
    public class ToolpathTests
    {
        static ToolpathTests()
        {
            var field = typeof(GrblCore).GetField("mDataPath", BindingFlags.Static | BindingFlags.NonPublic);
            if (field.GetValue(null) == null)
            {
                string data = Path.Combine(Path.GetTempPath(), "LaserGRBL-toolpath-tests-" + Guid.NewGuid());
                Directory.CreateDirectory(data);
                field.SetValue(null, data);
            }
        }

        private static string F(double v) => v.ToString(CultureInfo.InvariantCulture);

        // Shaped like the SVG import: pen up, travel, pen down, F on the first burn move.
        private static List<string> Square(double x, double y, double size, bool feed = true) => new List<string>
        {
            "S0", $"G0 X{F(x)} Y{F(y)}", "S1000",
            $"G1 X{F(x + size)} Y{F(y)}" + (feed ? " F1000" : ""), $"G1 X{F(x + size)} Y{F(y + size)}", $"G1 X{F(x)} Y{F(y + size)}", $"G1 X{F(x)} Y{F(y)}"
        };

        private static List<string> Line(double x1, double y1, double x2, double y2) => new List<string>
        {
            "S0", $"G0 X{F(x1)} Y{F(y1)}", "S1000", $"G1 X{F(x2)} Y{F(y2)} F1000"
        };

        private static (GrblCommand[] commands, int[] contours) Job(params List<string>[] blocks)
        {
            var lines = new List<string> { "G90", "M4 S0" };
            var contours = new List<int>();
            foreach (var block in blocks) { contours.Add(lines.Count); lines.AddRange(block); }
            return (lines.Select(l => new GrblCommand(l)).ToArray(), contours.ToArray());
        }

        private static GrblCommand H(GrblCommand command)
        {
            var c = new GrblCommand(command.Command);
            c.BuildHelper();
            return c;
        }

        private static IEnumerable<string> Range(ContourToolpath.Result r, int block)
        {
            int end = block + 1 < r.Contours.Length ? r.Contours[block + 1] : r.Commands.Length;
            return r.Commands.Skip(r.Contours[block]).Take(end - r.Contours[block]).Select(c => c.Command);
        }

        private static DoublePoint Rapid(ContourToolpath.Result r, int block)
        {
            var g0 = Range(r, block).Select(l => H(new GrblCommand(l))).First(c => c.IsMovement);
            return new DoublePoint((double)g0.X.Number, (double)g0.Y.Number);
        }

        private static List<DoublePoint> Burns(IEnumerable<string> lines)
        {
            return lines.Select(l => H(new GrblCommand(l))).Where(c => c.IsMovement && c.G != null && c.G.Number == 1)
                .Select(c => new DoublePoint((double)c.X.Number, (double)c.Y.Number)).ToList();
        }

        // ---------- Cut order ----------

        [Fact]
        public void InsideFirstCutsHolesBeforeTheOutlineAroundThem()
        {
            var (source, contours) = Job(Square(0, 0, 20), Square(30, 0, 10), Square(5, 5, 5));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Order = CutOrder.InsideFirst }, 0);

            Assert.True(r.Reordered, string.Join("|", r.Notes));
            Assert.Equal(new[] { 5.0, 0, 30 }, Enumerable.Range(0, 3).Select(i => Rapid(r, i).X));
        }

        [Fact]
        public void InsideFirstPicksTheNearestNextPath()
        {
            var (source, contours) = Job(Square(0, 0, 5), Square(50, 0, 5), Square(10, 0, 5));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Order = CutOrder.InsideFirst }, 0);
            Assert.Equal(new[] { 0.0, 10, 50 }, Enumerable.Range(0, 3).Select(i => Rapid(r, i).X));
        }

        [Fact]
        public void ReorderSpellsOutTravelAndFeedSoAnyOrderWorks()
        {
            // The second path's travel leaves out Y (unchanged) and its burn relies on the earlier F.
            var second = new List<string> { "S0", "G0 X40", "S1000", "G1 X45 Y0", "G1 X45 Y5" };
            var (source, contours) = Job(Square(40, 0, 5), Square(0, 0, 5), second);
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Order = CutOrder.InsideFirst }, 0);

            var moved = Range(r, 2).ToList();
            Assert.Contains("G0 X40 Y0", moved);
            Assert.Contains("G1 X45 Y0 F1000", moved);
        }

        [Fact]
        public void ReorderAddsPenUpForPathsThatRelyOnThePreviousOne()
        {
            var noPenUp = Square(30, 0, 5);
            noPenUp.RemoveAt(0);
            var first = Square(0, 0, 5);
            first.Add("S0");
            var (source, contours) = Job(first, noPenUp, Square(10, 0, 5));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Order = CutOrder.InsideFirst }, 0);

            Assert.True(r.Reordered, string.Join("|", r.Notes));
            var last = Range(r, 2).ToList();
            Assert.Equal("S0", last[0]);
            Assert.StartsWith("G0 X30", last[1]);
        }

        [Fact]
        public void ReorderIsSkippedForRelativeMoves()
        {
            var relative = new List<string> { "S0", "G0 X0 Y0", "S1000", "G91", "G1 X5 F1000", "G90" };
            var (source, contours) = Job(Square(50, 0, 5), relative);
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Order = CutOrder.InsideFirst }, 0);

            Assert.False(r.Reordered);
            Assert.Contains(r.Notes, n => n.Contains("left as drawn"));
            Assert.Equal(source.Select(c => c.Command), r.Commands.Select(c => c.Command));
        }

        [Fact]
        public void ClosingCommandsStayAtTheEnd()
        {
            var last = Square(0, 0, 5);
            last.AddRange(new[] { "S0", "M5 S0" });
            // The square nearest the origin is drawn last and carries the footer, so reordering moves it first.
            var (source, contours) = Job(Square(10, 0, 5), last);
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Order = CutOrder.InsideFirst }, 0);

            Assert.Equal(0, Rapid(r, 0).X);
            Assert.Equal("M5 S0", r.Commands.Last().Command);
            Assert.Equal(1, r.Commands.Count(c => c.Command == "M5 S0"));
        }

        // ---------- Lead-in, overcut, tabs ----------

        [Fact]
        public void LeadInStartsOnTheWasteSide()
        {
            var (source, contours) = Job(Square(0, 0, 10), Square(3, 3, 4));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { LeadInMm = 1 }, 0);

            Assert.Equal(2, r.LeadIns);
            var outer = Rapid(r, 0);
            Assert.True(outer.X < 0 && outer.Y < 0, "outline lead-in should start outside the corner");
            Assert.Equal(1, Math.Sqrt(outer.X * outer.X + outer.Y * outer.Y), 2); // 0.001 rounding in the G-code
            var hole = Rapid(r, 1);
            Assert.True(hole.X > 3 && hole.X < 7 && hole.Y > 3 && hole.Y < 7, "hole lead-in should start inside the hole");
            Assert.StartsWith("G1 X0 Y0 F1000", Range(r, 0).First(l => l.StartsWith("G1")));
        }

        [Fact]
        public void OvercutRunsPastTheStart()
        {
            var (source, contours) = Job(Square(0, 0, 10));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { OvercutMm = 2 }, 0);
            var burns = Burns(Range(r, 0));
            Assert.Equal(2, burns.Last().X, 3);
            Assert.Equal(0, burns.Last().Y, 3);
        }

        [Fact]
        public void TabsLeaveGapsWithTheLaserAtZero()
        {
            var (source, contours) = Job(Square(0, 0, 10), Square(3, 3, 4));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { TabCount = 2, TabWidthMm = 2 }, 0);

            Assert.Equal(1, r.Tabbed);
            var outline = Range(r, 0).Where(l => l.StartsWith("G1")).ToList();
            Assert.Equal(new[]
            {
                "G1 X9 Y0 F1000", "G1 X10 Y0 S0", "G1 X10 Y1", "G1 X10 Y10 S1000",
                "G1 X1 Y10", "G1 X0 Y10 S0", "G1 X0 Y9", "G1 X0 Y0 S1000"
            }, outline);
            Assert.DoesNotContain(Range(r, 1), l => l.EndsWith("S0") && l.StartsWith("G1"));
        }

        // ---------- Fill ----------

        [Fact]
        public void HatchFillsInsideOutlinesAndSkipsHoles()
        {
            var (source, contours) = Job(Square(0, 0, 10), Square(3, 3, 4));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Fill = FillPattern.Hatch, FillSpacingMm = 1 }, 0);

            Assert.Equal(3, r.Contours.Length); // fill, outline, hole
            Assert.Equal(14, r.FillPaths); // ten lines, four of them split by the hole
            var fill = Range(r, 0).ToList();
            var burns = Burns(fill);
            Assert.All(burns, p => Assert.False(p.X > 3.001 && p.X < 6.999 && p.Y > 3 && p.Y < 7));
            Assert.All(fill.Where(l => l.StartsWith("G0")), l => Assert.EndsWith(" S0", l));
            Assert.Equal("G1 X10 Y0.5 S1000 F1000", fill[1]);
            Assert.Equal("S0", fill.Last());
        }

        [Fact]
        public void CrossHatchAndFillOnly()
        {
            var (source, contours) = Job(Square(0, 0, 10), Line(20, 0, 30, 0));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Fill = FillPattern.CrossHatch, FillSpacingMm = 1, FillOnly = true }, 0);

            Assert.Equal(20, r.FillPaths);
            Assert.Equal(2, r.Contours.Length); // fill and the open line; the closed outline is dropped
            Assert.Contains("G1 X30 Y0 F1000", Range(r, 1));
        }

        [Fact]
        public void OffsetFillMakesConcentricLoops()
        {
            var (source, contours) = Job(Square(0, 0, 10));
            var r = ContourToolpath.Apply(source, contours, new ToolpathOptions { Fill = FillPattern.Offset, FillSpacingMm = 1 }, 0);
            Assert.Equal(5, r.FillPaths);
        }

        [Fact]
        public void FillRejectsSpacingThatIsTooFine()
        {
            var (source, contours) = Job(Square(0, 0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ContourToolpath.Apply(source, contours, new ToolpathOptions { Fill = FillPattern.Hatch, FillSpacingMm = 0.001m }, 0));
        }

        // ---------- Operations ----------

        [Fact]
        public void ZStepLowersEachRepeatAndReturns()
        {
            var operation = new JobOperation("Cut", new[] { new GrblCommand("G1 X10 S1000 F500") }, 3)
            {
                Toolpath = new ToolpathOptions { ZStepMm = 0.5m }
            };
            var commands = new List<GrblCommand>();
            operation.AppendTo(commands);
            var lines = commands.Select(c => c.Command).ToList();

            Assert.Equal(2, lines.Count(l => l == "G0 Z-0.5"));
            Assert.Equal("G0 Z1", lines[lines.Count - 2]);
            Assert.Equal("G90", lines.Last());
            Assert.True(lines.IndexOf("G0 Z-0.5") > lines.IndexOf("M5"), "Z moves only after the laser is off");
        }

        [Fact]
        public void ToolpathOptionsSurviveCopyAndSave()
        {
            var (source, contours) = Job(Square(0, 0, 10));
            var options = new ToolpathOptions { Order = CutOrder.InsideFirst, TabCount = 3, Fill = FillPattern.Offset, ZStepMm = 1 };
            var operation = new JobOperation("Cut", source) { Contours = contours, Toolpath = options };

            var copy = operation.WithSettings("Cut", 1, null, true);
            Assert.Equal(3, copy.Toolpath.TabCount);
            Assert.NotSame(options, copy.Toolpath);
            var restored = new JobOperation.Saved(operation).Restore();
            Assert.Equal(FillPattern.Offset, restored.Toolpath.Fill);

            var commands = new List<GrblCommand>();
            operation.AppendTo(commands);
            Assert.Contains("fill", operation.ToolpathReport);
        }

        [Fact]
        public void MaterialZStepAndLinesPerMmReachTheToolpath()
        {
            var setting = new MaterialSetting { Name = "Ply", Power = 800, Speed = 600, Extras = new MaterialExtras(linesPerMm: 10, zStepMm: 0.4m) };
            var applied = MaterialApplication.For(setting, new[] { new GrblCommand("M3 S1000 F600"), new GrblCommand("G1 X10") }, true);
            Assert.Equal(0.4m, applied.ZStepMm);
            Assert.Equal(0.1m, applied.FillSpacingMm);
            Assert.DoesNotContain("Z step", applied.Warning);
        }

        // ---------- Raster overscan ----------

        private static (decimal min, decimal max, decimal burnMin, decimal burnMax) RasterX(double overscan)
        {
            var file = new GrblFile();
            using (var image = new Bitmap(4, 3))
            {
                using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Black);
                file.AppendRasterPass(image, new GrblFile.L2LConf
                {
                    oX = 10, oY = 20, markSpeed = 1000, maxPower = 1000, pwm = true, lOn = "M4", lOff = "M5",
                    dir = RasterConverter.ImageProcessor.Direction.Horizontal, overscanMm = overscan
                }, new SizeF(4, 3), new JobPass(100, 100));
            }
            decimal x = 0, min = decimal.MaxValue, max = decimal.MinValue, burnMin = decimal.MaxValue, burnMax = decimal.MinValue, s = 0;
            foreach (var command in file.Commands)
            {
                command.BuildHelper();
                if (command.S != null) s = command.S.Number;
                if (command.X == null) continue;
                decimal next = command.X.Number;
                min = Math.Min(min, next); max = Math.Max(max, next);
                if (s > 0 && command.G?.Number != 0) { burnMin = Math.Min(burnMin, Math.Min(x, next)); burnMax = Math.Max(burnMax, Math.Max(x, next)); }
                x = next;
            }
            return (min, max, burnMin, burnMax);
        }

        [Fact]
        public void RasterOverscanRunsPastTheImageWithTheLaserOff()
        {
            var plain = RasterX(0);
            Assert.Equal(10m, plain.min);
            Assert.Equal(14m, plain.max);

            var over = RasterX(2);
            Assert.Equal(8m, over.min);
            Assert.Equal(16m, over.max);
            Assert.True(over.burnMin >= 10m && over.burnMax <= 14m, $"burned from {over.burnMin} to {over.burnMax}");
        }
    }
}
