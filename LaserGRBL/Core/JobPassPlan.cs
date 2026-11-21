using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserGRBL
{
	[Serializable]
	public sealed class JobPass
	{
		public decimal PowerPercent { get; private set; }
		public decimal SpeedPercent { get; private set; }
		public RasterConverter.ImageProcessor.Direction Direction { get; private set; }
		public decimal LinesPerMm { get; private set; }

		public JobPass(decimal powerPercent, decimal speedPercent = 100,
			RasterConverter.ImageProcessor.Direction direction = RasterConverter.ImageProcessor.Direction.None, decimal linesPerMm = 0)
		{
			if (powerPercent < 0 || powerPercent > 100) throw new ArgumentOutOfRangeException("powerPercent");
			if (speedPercent < 1 || speedPercent > 1000) throw new ArgumentOutOfRangeException("speedPercent");
			PowerPercent = powerPercent;
			SpeedPercent = speedPercent;
			Direction = direction;
			LinesPerMm = linesPerMm;
		}

		public override string ToString()
		{
			return string.Format("{0:0.#}% power / {1:0.#}% speed", PowerPercent, SpeedPercent);
		}
	}

	/// <summary>An immutable sequence of percentages of the file's programmed S and F values.</summary>
	[Serializable]
	public sealed class JobPassPlan
	{
		private readonly JobPass[] powers;
		public int Count { get { return powers.Length; } }
		public JobPass this[int index] { get { return powers[index]; } }

		public JobPassPlan(IEnumerable<JobPass> passes)
		{
			if (passes == null) throw new ArgumentNullException("passes");
			powers = passes.ToArray();
			if (powers.Length < 1 || powers.Length > 99 || powers.Any(p => p == null))
				throw new ArgumentException("Use 1 to 99 passes.", "passes");
		}
	}

	internal sealed class JobPassProfiles
	{
		private readonly string key;
		private readonly SortedDictionary<string, JobPassPlan> profiles;

		internal JobPassProfiles(bool raster)
		{
			key = raster ? "JobPassProfiles.Raster" : "JobPassProfiles.Toolpath";
			var saved = Settings.GetObject<Dictionary<string, JobPassPlan>>(key, null);
			profiles = new SortedDictionary<string, JobPassPlan>(StringComparer.OrdinalIgnoreCase);
			if (saved != null)
				foreach (var entry in saved)
					profiles[entry.Key] = entry.Value;
		}

		internal IEnumerable<string> Names { get { return profiles.Keys; } }

		internal bool TryGet(string name, out JobPassPlan plan)
		{
			return profiles.TryGetValue(name, out plan);
		}

		internal void Save(string name, JobPassPlan plan)
		{
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a profile name.", "name");
			if (plan == null) throw new ArgumentNullException("plan");
			profiles[name.Trim()] = plan;
			Persist();
		}

		internal void Remove(string name)
		{
			if (profiles.Remove(name)) Persist();
		}

		private void Persist()
		{
			Settings.SetObject(key, new Dictionary<string, JobPassPlan>(profiles, StringComparer.OrdinalIgnoreCase));
		}
	}
}
