using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace LaserGRBL.Tests
{
    [Collection("Job passes")]
    public class AnalysisTests
    {
        static AnalysisTests()
        {
            var field = typeof(GrblCore).GetField("mDataPath", BindingFlags.Static | BindingFlags.NonPublic);
            if (field.GetValue(null) == null)
            {
                string data = Path.Combine(Path.GetTempPath(), "LaserGRBL-analysis-tests-" + Guid.NewGuid());
                Directory.CreateDirectory(data);
                field.SetValue(null, data);
            }
        }

        private static GrblCommand[] C(params string[] lines) => lines.Select(l => new GrblCommand(l)).ToArray();

        // ---------- Stats, heatmap, cost ----------

        [Fact]
        public void StatsSplitBurnAndTravel()
        {
            var stats = JobStats.Measure(C("G90", "M4 S0", "G0 X10 Y0", "G1 X20 Y0 S500 F600", "G1 X20 Y10", "G0 X0 Y0 S0"), 3000);
            Assert.Equal(20, stats.BurnMm, 6);
            Assert.Equal(10 + Math.Sqrt(500), stats.TravelMm, 6);
            Assert.Equal(TimeSpan.FromSeconds(2), stats.BurnTime); // 20 mm at 600 mm/min
            Assert.Equal(500, stats.MaxPower);
            Assert.Equal(10, stats.Width, 6);
            Assert.Equal(10, stats.Height, 6);
        }

        [Fact]
        public void StatsFollowArcsAndRelativeMoves()
        {
            // Half circle of radius 5, then a relative 10 mm line.
            var stats = JobStats.Measure(C("G0 X0 Y0", "G2 X10 Y0 I5 J0 S100 F1000", "G91", "G1 X0 Y10"), 3000);
            Assert.Equal(Math.PI * 5 + 10, stats.BurnMm, 1);
            Assert.Equal(10, stats.MaxX, 6);
        }

        [Fact]
        public void HeatmapShowsOverlapAsHotter()
        {
            var map = EnergyMap.Build(C("G0 X0 Y0", "G1 X10 S1000 F600", "G0 X0 Y5", "G1 X10", "G0 X0 Y5", "G1 X10"), 1);
            double single = map.Cells[2, 0], triple = map.Cells[2, map.Rows - 1];
            Assert.True(single > 0);
            Assert.Equal(2 * single, triple, 6); // the line at Y5 runs twice
            Assert.Equal(triple, map.Max, 6);
            Assert.Throws<InvalidOperationException>(() => EnergyMap.Build(C("G0 X10"), 1));
        }

        [Fact]
        public void CostAddsMachineEnergyMaterialAndSetup()
        {
            var rates = new CostRates { MachinePerHour = 60, LaserWatts = 1000, EnergyPerKwh = 0.5m, MaterialPerM2 = 100, MarginMm = 0, SetupCost = 5, Quantity = 2 };
            var cost = CostEstimate.For(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(12), 100, 100, rates);
            Assert.Equal(60m, Math.Round(cost.Machine, 6));   // 0.5 h × 60 × 2
            Assert.Equal(0.2m, Math.Round(cost.Energy, 6));   // 0.2 h × 1 kW × 0.5 × 2
            Assert.Equal(2m, Math.Round(cost.Material, 6));   // 0.01 m² × 100 × 2
            Assert.Equal(67.2m, Math.Round(cost.Total, 6));
            Assert.Equal(33.6m, Math.Round(cost.PerPiece, 6));
            Assert.Contains("Total: €67.20", cost.Format("€").Replace(',', '.'));
        }

        // ---------- Nesting ----------

        [Fact]
        public void NestingFillsRowsWithoutOverlap()
        {
            var sizes = Enumerable.Repeat(new SizeF(40, 20), 6).ToList();
            var placed = Nesting.Pack(sizes, 130, 100, 5, 0, false);
            Assert.Equal(6, placed.Count);
            var boxes = placed.Select(p => new RectangleF((float)p.CenterX - 20, (float)p.CenterY - 10, 40, 20)).ToList();
            for (int i = 0; i < boxes.Count; i++)
            {
                Assert.True(boxes[i].Left >= 0 && boxes[i].Right <= 130 && boxes[i].Bottom <= 100);
                for (int j = i + 1; j < boxes.Count; j++) Assert.False(boxes[i].IntersectsWith(boxes[j]));
            }
            Assert.Equal(3, placed.Count(p => Math.Abs(p.CenterY - 10) < 1e-6)); // three per row
        }

        [Fact]
        public void NestingTurnsTallPartsAndReportsWhatDoesntFit()
        {
            var placed = Nesting.Pack(new[] { new SizeF(10, 80) }, 100, 30, 0, 0, true);
            Assert.True(placed[0].Rotated);
            Assert.Throws<InvalidOperationException>(() => Nesting.Pack(new[] { new SizeF(10, 80) }, 100, 30, 0, 0, false));
            var ex = Assert.Throws<InvalidOperationException>(() => Nesting.Pack(Enumerable.Repeat(new SizeF(50, 50), 5).ToList(), 100, 100, 0, 0, false));
            Assert.Contains("Only 4 of 5", ex.Message);
        }

        [Fact]
        public void NestPlacementMovesTheBoxCentre()
        {
            var operation = new JobOperation("Part", C("G0 X100 Y100", "G1 X120 Y100 S500 F600", "G1 X120 Y110"));
            var bounds = Nesting.Bounds(operation);
            var placement = Nesting.PlacementFor(bounds, new NestPlacement { CenterX = 15, CenterY = 5 }, 1);
            var moved = JobStats.Measure(placement.Transform(operation.Source), 1000);
            Assert.Equal(5, moved.MinX, 3);
            Assert.Equal(0, moved.MinY, 3);
        }

        // ---------- Rotary ----------

        [Fact]
        public void RotaryScaleDependsOnRollerOrObject()
        {
            Assert.Equal(1, Rotary.Scale(RotaryKind.Roller, Math.PI * 30, 30, 999), 9);
            Assert.Equal(0.5, Rotary.Scale(RotaryKind.Chuck, Math.PI * 30, 1, 60), 9);
            Assert.Throws<ArgumentOutOfRangeException>(() => Rotary.Scale(RotaryKind.Chuck, 100, 30, 0));
        }

        [Fact]
        public void RotaryScalesYAndFlattensArcs()
        {
            var output = Rotary.Apply(C("G90", "G0 X0 Y10", "G1 X5 Y20 S300 F900", "G91", "G1 Y-4", "G90", "G2 X10 Y16 I2.5 J0"), 0.5)
                .Select(c => c.Command).ToList();
            Assert.Equal("G0 X0 Y5", output[1]);
            Assert.Equal("G1 X5 Y10 S300 F900", output[2]);
            Assert.Equal("G1 Y-2", output[4]);
            var arc = output.Skip(6).ToList();
            Assert.True(arc.Count > 4);
            Assert.All(arc, l => Assert.StartsWith("G1 ", l));
            Assert.Equal("G1 X10 Y8", arc.Last()); // ends at (10, 16) scaled by 0.5
            Assert.Throws<InvalidOperationException>(() => Rotary.Apply(C("G20", "G1 X1"), 2));
        }

        // ---------- Serial and QR ----------

        [Fact]
        public void SerialTemplateFillsCounterAndDate()
        {
            var now = new DateTime(2026, 3, 4, 5, 6, 0);
            Assert.Equal("SN 0042 · 2026-03-04 05:06", SerialTemplate.Render("SN {n:0000} · {date} {time}", 42, now));
            Assert.Equal("04/03/26 #7", SerialTemplate.Render("{DATE:dd/MM/yy} #{n}", 7, now));
            Assert.Throws<FormatException>(() => SerialTemplate.Render("{date:%}", 1, now));
        }

        [Fact]
        public void ReedSolomonMatchesTheStandardExample()
        {
            // "HELLO WORLD" at 1-M, from the QR specification's worked example.
            byte[] data = { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17 };
            var ecc = QrCode.ReedSolomonRemainder(data, QrCode.ReedSolomonDivisor(10));
            Assert.Equal(new byte[] { 196, 35, 39, 119, 235, 215, 231, 226, 93, 23 }, ecc);
        }

        [Fact]
        public void FormatWordMatchesTheStandardTable()
        {
            Assert.Equal(Convert.ToInt32("101010000010010", 2), QrCode.FormatWord(QrErrorCorrection.M, 0));
            Assert.Equal(Convert.ToInt32("111011111000100", 2), QrCode.FormatWord(QrErrorCorrection.L, 0));
            Assert.Equal(Convert.ToInt32("001011010001001", 2), QrCode.FormatWord(QrErrorCorrection.H, 0));
        }

        [Theory]
        [InlineData("https://example.com/item/123", "M")]
        [InlineData("SN 0001", "H")]
        [InlineData("A much longer serial label that needs a bigger version to hold all of its bytes, so it uses several blocks.", "Q")]
        public void QrCodeReadsBackToTheSameText(string text, string levelName)
        {
            var level = (QrErrorCorrection)Enum.Parse(typeof(QrErrorCorrection), levelName);
            var code = QrCode.Encode(text, level);
            Assert.Equal(code.Version * 4 + 17, code.Size);
            AssertFinder(code, 0, 0);
            AssertFinder(code, code.Size - 7, 0);
            AssertFinder(code, 0, code.Size - 7);
            Assert.Equal(text, QrReader.Read(code, level));
        }

        [Fact]
        public void QrCodeRejectsTextThatIsTooLong()
        {
            Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('x', 400), QrErrorCorrection.L));
        }

        private static void AssertFinder(QrCode code, int left, int top)
        {
            for (int dy = 0; dy < 7; dy++)
                for (int dx = 0; dx < 7; dx++)
                {
                    int d = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    Assert.Equal(d != 2, code[left + dx, top + dy]);
                }
        }

        [Fact]
        public void QrMarkingBurnsOnlyDarkModules()
        {
            var code = QrCode.Encode("SN 1", QrErrorCorrection.L);
            var commands = MarkingGenerator.Qr(code, 10, 20, 1, 2, 1200, 600, "M4");
            var stats = JobStats.Measure(commands, 3000);
            Assert.Equal(10, stats.MinX, 6);
            Assert.Equal(10 + code.Size, stats.MaxX, 6);
            int dark = 0;
            for (int y = 0; y < code.Size; y++) for (int x = 0; x < code.Size; x++) if (code[x, y]) dark++;
            Assert.Equal(dark * 2, stats.BurnMm, 6); // two lines per module, 1 mm each
        }

        [Fact]
        public void TextMarkingIsPlacedAndSized()
        {
            var stats = JobStats.Measure(MarkingGenerator.Text("SN 12", 5, 7, 4, 1000, 500, "M4"), 3000);
            Assert.Equal(5, stats.MinX, 2);
            Assert.Equal(7, stats.MinY, 2);
            Assert.InRange(stats.Height, 2, 4.01);
        }

        /// <summary>Independent reader for the tests: format, unmask, de-interleave, check ECC, decode byte mode.</summary>
        private static class QrReader
        {
            internal static string Read(QrCode code, QrErrorCorrection level)
            {
                int size = code.Size;
                // Format bits around the top-left finder, row 8 then column 8.
                int format = 0;
                int[][] spots =
                {
                    new[] { 8, 0 }, new[] { 8, 1 }, new[] { 8, 2 }, new[] { 8, 3 }, new[] { 8, 4 }, new[] { 8, 5 }, new[] { 8, 7 }, new[] { 8, 8 },
                    new[] { 7, 8 }, new[] { 5, 8 }, new[] { 4, 8 }, new[] { 3, 8 }, new[] { 2, 8 }, new[] { 1, 8 }, new[] { 0, 8 }
                };
                for (int i = 0; i < 15; i++) if (code[spots[i][0], spots[i][1]]) format |= 1 << i;
                int mask = Enumerable.Range(0, 8).Single(m => QrCode.FormatWord(level, m) == format);

                var reserved = new bool[size, size];
                Action<int, int, int, int> box = (x0, y0, w, h) => { for (int x = x0; x < x0 + w; x++) for (int y = y0; y < y0 + h; y++) if (x >= 0 && y >= 0 && x < size && y < size) reserved[x, y] = true; };
                box(0, 0, 9, 9); box(size - 8, 0, 8, 9); box(0, size - 8, 9, 8);
                box(6, 0, 1, size); box(0, 6, size, 1);
                int[] align = code.Version == 1 ? new int[0] : code.Version >= 7 ? AlignmentFor(code.Version) : new[] { 6, size - 7 };
                foreach (int ax in align) foreach (int ay in align)
                    {
                        if (ax == 6 && ay == 6 || ax == 6 && ay == size - 7 || ax == size - 7 && ay == 6) continue;
                        box(ax - 2, ay - 2, 5, 5);
                    }
                if (code.Version >= 7) { box(size - 11, 0, 3, 6); box(0, size - 11, 6, 3); }

                var bits = new List<bool>();
                for (int right = size - 1; right >= 1; right -= 2)
                {
                    if (right == 6) right = 5;
                    bool up = ((size - 1 - right) / 2) % 2 == 0;
                    for (int v = 0; v < size; v++)
                    {
                        int y = up ? size - 1 - v : v;
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            if (reserved[x, y]) continue;
                            bits.Add(code[x, y] ^ Masked(mask, x, y));
                        }
                    }
                }
                var bytes = new List<byte>();
                for (int i = 0; i + 8 <= bits.Count; i += 8)
                    bytes.Add((byte)Enumerable.Range(0, 8).Aggregate(0, (acc, k) => acc << 1 | (bits[i + k] ? 1 : 0)));

                // De-interleave: data codewords column by column across blocks, then ECC.
                var (blocks, ecc, total) = Layout(code.Version, level);
                int shortLength = total / blocks, shortBlocks = blocks - total % blocks;
                var dataBlocks = Enumerable.Range(0, blocks).Select(b => new List<byte>()).ToList();
                var eccBlocks = Enumerable.Range(0, blocks).Select(b => new List<byte>()).ToList();
                int k2 = 0;
                int longestData = shortLength - ecc + (shortBlocks < blocks ? 1 : 0);
                for (int i = 0; i < longestData; i++)
                    for (int b = 0; b < blocks; b++)
                        if (i < shortLength - ecc + (b < shortBlocks ? 0 : 1)) dataBlocks[b].Add(bytes[k2++]);
                for (int i = 0; i < ecc; i++)
                    for (int b = 0; b < blocks; b++) eccBlocks[b].Add(bytes[k2++]);
                var divisor = QrCode.ReedSolomonDivisor(ecc);
                for (int b = 0; b < blocks; b++) Assert.Equal(eccBlocks[b].ToArray(), QrCode.ReedSolomonRemainder(dataBlocks[b], divisor));

                var data = dataBlocks.SelectMany(b => b).ToList();
                var stream = data.SelectMany(d => Enumerable.Range(0, 8).Select(i => (d >> (7 - i) & 1) == 1)).ToList();
                Func<int, int, int> read = (start, length) => Enumerable.Range(start, length).Aggregate(0, (acc, i) => acc << 1 | (stream[i] ? 1 : 0));
                Assert.Equal(4, read(0, 4)); // byte mode
                int countBits = code.Version <= 9 ? 8 : 16;
                int count = read(4, countBits);
                var text = Enumerable.Range(0, count).Select(i => (byte)read(4 + countBits + i * 8, 8)).ToArray();
                return Encoding.UTF8.GetString(text);
            }

            private static int[] AlignmentFor(int version)
            {
                switch (version) { case 7: return new[] { 6, 22, 38 }; case 8: return new[] { 6, 24, 42 }; case 9: return new[] { 6, 26, 46 }; default: return new[] { 6, 28, 50 }; }
            }

            private static (int blocks, int ecc, int total) Layout(int version, QrErrorCorrection level)
            {
                int[] totals = { 26, 44, 70, 100, 134, 172, 196, 242, 292, 346 };
                int[,] ecc = { { 7, 10, 15, 20, 26, 18, 20, 24, 30, 18 }, { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 }, { 13, 22, 18, 26, 18, 24, 18, 22, 20, 24 }, { 17, 28, 22, 16, 22, 28, 26, 26, 24, 28 } };
                int[,] blocks = { { 1, 1, 1, 1, 1, 2, 2, 2, 2, 4 }, { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 }, { 1, 1, 2, 2, 4, 4, 6, 6, 8, 8 }, { 1, 1, 2, 4, 4, 4, 5, 6, 8, 8 } };
                return (blocks[(int)level, version - 1], ecc[(int)level, version - 1], totals[version - 1]);
            }

            private static bool Masked(int mask, int x, int y)
            {
                switch (mask)
                {
                    case 0: return (x + y) % 2 == 0;
                    case 1: return y % 2 == 0;
                    case 2: return x % 3 == 0;
                    case 3: return (x + y) % 3 == 0;
                    case 4: return (x / 3 + y / 2) % 2 == 0;
                    case 5: return x * y % 2 + x * y % 3 == 0;
                    case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                    default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
                }
            }
        }
    }
}
