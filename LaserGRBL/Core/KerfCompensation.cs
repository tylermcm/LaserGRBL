namespace LaserGRBL
{
	/// <summary>
	/// Moves closed SVG contours by half the kerf so cut parts keep their drawn size:
	/// outlines grow and holes shrink. Blocks that are open, nested in an unsupported way,
	/// or not plain absolute XY moves are copied unchanged. The work is done by <see cref="ContourToolpath"/>.
	/// </summary>
	internal static class KerfCompensation
	{
		internal sealed class Result
		{
			internal GrblCommand[] Commands;
			internal int[] Contours;
			internal int Compensated;
			internal int Skipped;
		}

		internal static Result Apply(GrblCommand[] source, int[] contours, double kerfMm)
		{
			var result = ContourToolpath.Apply(source, contours, null, kerfMm);
			return new Result { Commands = result.Commands, Contours = result.Contours, Compensated = result.KerfCompensated, Skipped = result.KerfSkipped };
		}
	}
}
