using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LaserGRBL
{
	/// <summary>
	/// Fills {n}, {n:000}, {date}, {date:yyyy-MM-dd} and {time} in a marking template.
	/// {n} is the serial counter, which moves on each time a marking is added to a job.
	/// </summary>
	internal static class SerialTemplate
	{
		internal const string CounterKey = "Marking.Counter";
		private static readonly Regex Token = new Regex(@"\{(n|date|time)(?::([^}]*))?\}", RegexOptions.IgnoreCase);

		internal static int Counter
		{
			get { return Settings.GetObject(CounterKey, 1); }
			set { Settings.SetObject(CounterKey, value); }
		}

		internal static string Render(string template, int counter, DateTime now)
		{
			if (template == null) return "";
			return Token.Replace(template, match =>
			{
				string format = match.Groups[2].Success ? match.Groups[2].Value : null;
				try
				{
					switch (match.Groups[1].Value.ToLowerInvariant())
					{
						case "n": return counter.ToString(format ?? "0", CultureInfo.InvariantCulture);
						case "date": return now.ToString(format ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
						default: return now.ToString(format ?? "HH:mm", CultureInfo.InvariantCulture);
					}
				}
				catch (FormatException) { throw new FormatException("The format in " + match.Value + " isn't valid."); }
			});
		}
	}

	internal enum QrErrorCorrection { L, M, Q, H }

	/// <summary>
	/// QR code encoder (byte mode, versions 1-10, up to 271 bytes at level L), following ISO/IEC 18004.
	/// Picks the smallest version that fits and the mask with the lowest penalty.
	/// </summary>
	internal sealed class QrCode
	{
		private static readonly int[,] EccPerBlock = { { 7, 10, 15, 20, 26, 18, 20, 24, 30, 18 }, { 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 }, { 13, 22, 18, 26, 18, 24, 18, 22, 20, 24 }, { 17, 28, 22, 16, 22, 28, 26, 26, 24, 28 } };
		private static readonly int[,] Blocks = { { 1, 1, 1, 1, 1, 2, 2, 2, 2, 4 }, { 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 }, { 1, 1, 2, 2, 4, 4, 6, 6, 8, 8 }, { 1, 1, 2, 4, 4, 4, 5, 6, 8, 8 } };
		private static readonly int[] FormatBits = { 1, 0, 3, 2 }; // L, M, Q, H

		internal readonly int Version, Size, Mask;
		internal readonly QrErrorCorrection Level;
		private readonly bool[,] modules; // [x, y]
		private readonly bool[,] function;

		internal bool this[int x, int y] { get { return modules[x, y]; } }

		private QrCode(int version, QrErrorCorrection level, byte[] data, int mask)
		{
			Version = version;
			Level = level;
			Size = version * 4 + 17;
			modules = new bool[Size, Size];
			function = new bool[Size, Size];
			DrawFunctionPatterns();
			DrawCodewords(AddEccAndInterleave(data));
			if (mask < 0)
			{
				int best = int.MaxValue;
				for (int m = 0; m < 8; m++)
				{
					ApplyMask(m);
					DrawFormat(m);
					int penalty = Penalty();
					if (penalty < best) { best = penalty; mask = m; }
					ApplyMask(m); // XOR again to undo
				}
			}
			Mask = mask;
			ApplyMask(mask);
			DrawFormat(mask);
		}

		internal static QrCode Encode(string text, QrErrorCorrection level = QrErrorCorrection.M, int mask = -1)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
			if (mask < -1 || mask > 7) throw new ArgumentOutOfRangeException("mask");
			for (int version = 1; version <= 10; version++)
			{
				int capacityBits = DataCodewords(version, level) * 8;
				int countBits = version <= 9 ? 8 : 16;
				int used = 4 + countBits + bytes.Length * 8;
				if (used > capacityBits) continue;
				var bits = new List<bool>();
				Append(bits, 4, 4);
				Append(bits, bytes.Length, countBits);
				foreach (byte b in bytes) Append(bits, b, 8);
				Append(bits, 0, Math.Min(4, capacityBits - bits.Count));
				Append(bits, 0, (8 - bits.Count % 8) % 8);
				var data = new List<byte>();
				for (int i = 0; i < bits.Count; i += 8)
				{
					int value = 0;
					for (int j = 0; j < 8; j++) value = value << 1 | (bits[i + j] ? 1 : 0);
					data.Add((byte)value);
				}
				for (byte pad = 0xEC; data.Count < capacityBits / 8; pad ^= 0xEC ^ 0x11) data.Add(pad);
				return new QrCode(version, level, data.ToArray(), mask);
			}
			throw new ArgumentException("That text is too long for a QR code here. Shorten it or use a lower error correction level.");
		}

		private static void Append(List<bool> bits, int value, int length)
		{
			for (int i = length - 1; i >= 0; i--) bits.Add((value >> i & 1) != 0);
		}

		private static int RawDataModules(int version)
		{
			int result = (16 * version + 128) * version + 64;
			if (version >= 2)
			{
				int align = version / 7 + 2;
				result -= (25 * align - 10) * align - 55;
				if (version >= 7) result -= 36;
			}
			return result;
		}

		internal static int DataCodewords(int version, QrErrorCorrection level)
		{
			int l = (int)level, v = version - 1;
			return RawDataModules(version) / 8 - EccPerBlock[l, v] * Blocks[l, v];
		}

		private byte[] AddEccAndInterleave(byte[] data)
		{
			int l = (int)Level, v = Version - 1;
			int blocks = Blocks[l, v], ecc = EccPerBlock[l, v];
			int raw = RawDataModules(Version) / 8;
			int shortBlocks = blocks - raw % blocks, shortLength = raw / blocks;
			var divisor = ReedSolomonDivisor(ecc);
			var list = new List<byte[]>();
			for (int i = 0, k = 0; i < blocks; i++)
			{
				int length = shortLength - ecc + (i < shortBlocks ? 0 : 1);
				var block = data.Skip(k).Take(length).ToList();
				k += length;
				var remainder = ReedSolomonRemainder(block, divisor);
				if (i < shortBlocks) block.Add(0);
				block.AddRange(remainder);
				list.Add(block.ToArray());
			}
			var result = new List<byte>();
			for (int i = 0; i < list[0].Length; i++)
				for (int j = 0; j < list.Count; j++)
					if (i != shortLength - ecc || j >= shortBlocks) result.Add(list[j][i]);
			return result.ToArray();
		}

		internal static byte[] ReedSolomonDivisor(int degree)
		{
			var result = new byte[degree];
			result[degree - 1] = 1;
			int root = 1;
			for (int i = 0; i < degree; i++)
			{
				for (int j = 0; j < degree; j++)
				{
					result[j] = Multiply(result[j], root);
					if (j + 1 < degree) result[j] ^= result[j + 1];
				}
				root = Multiply(root, 2);
			}
			return result;
		}

		internal static byte[] ReedSolomonRemainder(IList<byte> data, byte[] divisor)
		{
			var result = new byte[divisor.Length];
			foreach (byte b in data)
			{
				int factor = b ^ result[0];
				Array.Copy(result, 1, result, 0, result.Length - 1);
				result[result.Length - 1] = 0;
				for (int i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
			}
			return result;
		}

		private static byte Multiply(int x, int y)
		{
			int z = 0;
			for (int i = 7; i >= 0; i--)
			{
				z = z << 1 ^ (z >> 7) * 0x11D;
				z ^= (y >> i & 1) * x;
			}
			return (byte)z;
		}

		private void Set(int x, int y, bool dark)
		{
			modules[x, y] = dark;
			function[x, y] = true;
		}

		private void DrawFunctionPatterns()
		{
			for (int i = 0; i < Size; i++)
			{
				Set(6, i, i % 2 == 0);
				Set(i, 6, i % 2 == 0);
			}
			DrawFinder(3, 3);
			DrawFinder(Size - 4, 3);
			DrawFinder(3, Size - 4);
			var positions = AlignmentPositions();
			int last = positions.Length - 1;
			for (int i = 0; i <= last; i++)
				for (int j = 0; j <= last; j++)
					if (!(i == 0 && j == 0 || i == 0 && j == last || i == last && j == 0))
						for (int dy = -2; dy <= 2; dy++)
							for (int dx = -2; dx <= 2; dx++)
								Set(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
			DrawFormat(0); // reserve the format area; the real bits are written after masking
			if (Version >= 7)
			{
				int rem = Version;
				for (int i = 0; i < 12; i++) rem = rem << 1 ^ (rem >> 11) * 0x1F25;
				long bits = (long)Version << 12 | (long)rem;
				for (int i = 0; i < 18; i++)
				{
					bool bit = (bits >> i & 1) != 0;
					int a = Size - 11 + i % 3, b = i / 3;
					Set(a, b, bit);
					Set(b, a, bit);
				}
			}
		}

		private int[] AlignmentPositions()
		{
			switch (Version)
			{
				case 1: return new int[0];
				case 7: return new[] { 6, 22, 38 };
				case 8: return new[] { 6, 24, 42 };
				case 9: return new[] { 6, 26, 46 };
				case 10: return new[] { 6, 28, 50 };
				default: return new[] { 6, Size - 7 };
			}
		}

		private void DrawFinder(int x, int y)
		{
			for (int dy = -4; dy <= 4; dy++)
				for (int dx = -4; dx <= 4; dx++)
				{
					int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
					if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) Set(xx, yy, d != 2 && d != 4);
				}
		}

		internal static int FormatWord(QrErrorCorrection level, int mask)
		{
			int data = FormatBits[(int)level] << 3 | mask;
			int rem = data;
			for (int i = 0; i < 10; i++) rem = rem << 1 ^ (rem >> 9) * 0x537;
			return (data << 10 | rem) ^ 0x5412;
		}

		private void DrawFormat(int mask)
		{
			int bits = FormatWord(Level, mask);
			Func<int, bool> bit = i => (bits >> i & 1) != 0;
			for (int i = 0; i <= 5; i++) Set(8, i, bit(i));
			Set(8, 7, bit(6));
			Set(8, 8, bit(7));
			Set(7, 8, bit(8));
			for (int i = 9; i < 15; i++) Set(14 - i, 8, bit(i));
			for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, bit(i));
			for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, bit(i));
			Set(8, Size - 8, true); // always dark
		}

		private void DrawCodewords(byte[] data)
		{
			int i = 0;
			for (int right = Size - 1; right >= 1; right -= 2)
			{
				if (right == 6) right = 5;
				for (int vert = 0; vert < Size; vert++)
					for (int j = 0; j < 2; j++)
					{
						int x = right - j;
						bool upward = (right + 1 & 2) == 0;
						int y = upward ? Size - 1 - vert : vert;
						if (!function[x, y] && i < data.Length * 8)
						{
							modules[x, y] = (data[i >> 3] >> 7 - (i & 7) & 1) != 0;
							i++;
						}
					}
			}
		}

		private void ApplyMask(int mask)
		{
			for (int y = 0; y < Size; y++)
				for (int x = 0; x < Size; x++)
				{
					bool invert;
					switch (mask)
					{
						case 0: invert = (x + y) % 2 == 0; break;
						case 1: invert = y % 2 == 0; break;
						case 2: invert = x % 3 == 0; break;
						case 3: invert = (x + y) % 3 == 0; break;
						case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
						case 5: invert = x * y % 2 + x * y % 3 == 0; break;
						case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
						default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
					}
					if (!function[x, y] && invert) modules[x, y] = !modules[x, y];
				}
		}

		private int Penalty()
		{
			int penalty = 0, dark = 0;
			for (int pass = 0; pass < 2; pass++)
				for (int a = 0; a < Size; a++)
				{
					int run = 0;
					bool previous = false;
					var line = new bool[Size];
					for (int b = 0; b < Size; b++)
					{
						bool m = pass == 0 ? modules[b, a] : modules[a, b];
						line[b] = m;
						if (b > 0 && m == previous) run++;
						else
						{
							if (run >= 5) penalty += run - 2;
							run = 1;
						}
						previous = m;
					}
					if (run >= 5) penalty += run - 2;
					for (int b = 0; b + 7 <= Size; b++)
						if (line[b] && !line[b + 1] && line[b + 2] && line[b + 3] && line[b + 4] && !line[b + 5] && line[b + 6])
						{
							bool before = Enumerable.Range(b - 4, 4).All(k => k < 0 || !line[k]);
							bool after = Enumerable.Range(b + 7, 4).All(k => k >= Size || !line[k]);
							if (before || after) penalty += 40;
						}
				}
			for (int y = 0; y < Size; y++)
				for (int x = 0; x < Size; x++)
				{
					if (modules[x, y]) dark++;
					if (x + 1 < Size && y + 1 < Size && modules[x, y] == modules[x + 1, y] && modules[x, y] == modules[x, y + 1] && modules[x, y] == modules[x + 1, y + 1])
						penalty += 3;
				}
			int total = Size * Size;
			penalty += Math.Abs(dark * 20 - total * 10) / total * 10;
			return penalty;
		}
	}

	/// <summary>Turns marking text or a QR code into a G-code operation.</summary>
	internal static class MarkingGenerator
	{
		/// <summary>Single-line text, <paramref name="heightMm"/> tall, with its lower-left corner at (x, y).</summary>
		internal static GrblCommand[] Text(string text, double x, double y, double heightMm, int speed, int power, string mode)
		{
			if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter the text to mark.");
			if (heightMm < 0.5 || heightMm > 20) throw new ArgumentOutOfRangeException("heightMm", "Text height must be between 0.5 and 20 mm.");
			var raw = Hershey.Hershey.CreateString(text, 0, 0, speed, power, true, mode).ToArray();
			var stats = JobStats.Measure(raw, 1000);
			double scale = heightMm / 2; // the Hershey font is 2 mm tall
			var anchor = new PointF((float)stats.MinX, (float)stats.MinY);
			var placed = new JobPlacement(anchor, new PointF((float)x, (float)y), scale, 0).Transform(raw);
			return new[] { new GrblCommand("M5"), new GrblCommand("G90") }.Concat(placed).Concat(new[] { new GrblCommand("M5") }).ToArray();
		}

		/// <summary>
		/// QR code with its lower-left corner at (x, y). Each dark run in a module row is filled with horizontal lines,
		/// <paramref name="linesPerModule"/> per module, plus the four-module quiet zone left blank around it.
		/// </summary>
		internal static GrblCommand[] Qr(QrCode code, double x, double y, double moduleMm, int linesPerModule, int speed, int power, string mode)
		{
			if (moduleMm < 0.1 || moduleMm > 10) throw new ArgumentOutOfRangeException("moduleMm", "Module size must be between 0.1 and 10 mm.");
			if (linesPerModule < 1 || linesPerModule > 50) throw new ArgumentOutOfRangeException("linesPerModule", "Lines per module must be between 1 and 50.");
			Func<double, string> f = v => v.ToString("0.###", CultureInfo.InvariantCulture);
			var lines = new List<string> { "M5", "G90", mode + " S0", "F" + speed.ToString(CultureInfo.InvariantCulture) };
			double pitch = moduleMm / linesPerModule;
			bool forward = true;
			for (int row = 0; row < code.Size; row++)
			{
				double top = y + (code.Size - row) * moduleMm; // row 0 is the top of the code
				for (int k = 0; k < linesPerModule; k++)
				{
					double ly = top - (k + 0.5) * pitch;
					var runs = new List<int[]>();
					for (int col = 0; col < code.Size; col++)
					{
						if (!code[col, row]) continue;
						int start = col;
						while (col + 1 < code.Size && code[col + 1, row]) col++;
						runs.Add(new[] { start, col + 1 });
					}
					if (!forward) runs.Reverse();
					foreach (var run in runs)
					{
						double a = x + run[0] * moduleMm, b = x + run[1] * moduleMm;
						if (!forward) { double t = a; a = b; b = t; }
						lines.Add("G0 X" + f(a) + " Y" + f(ly) + " S0");
						lines.Add("G1 X" + f(b) + " S" + power.ToString(CultureInfo.InvariantCulture));
					}
					if (runs.Count > 0) forward = !forward;
				}
			}
			lines.Add("M5 S0");
			return lines.Select(l => new GrblCommand(l)).ToArray();
		}
	}
}
