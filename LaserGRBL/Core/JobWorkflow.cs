using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserGRBL
{
	/// <summary>Files to run one after another. The next one is only loaded, never started, so material can be swapped first.</summary>
	internal static class JobQueue
	{
		internal const string SettingsKey = "JobQueue.Items";

		internal static List<string> Items
		{
			get { return new List<string>(Settings.GetObject<string[]>(SettingsKey, null) ?? new string[0]); }
			private set { Settings.SetObject(SettingsKey, value.ToArray(), true); }
		}

		internal static void Add(IEnumerable<string> files)
		{
			var items = Items;
			items.AddRange(files.Where(f => !string.IsNullOrWhiteSpace(f)));
			Items = items;
		}

		internal static void RemoveAt(int index)
		{
			var items = Items;
			if (index < 0 || index >= items.Count) return;
			items.RemoveAt(index);
			Items = items;
		}

		/// <summary>Moves an entry and returns its new index.</summary>
		internal static int Move(int index, int delta)
		{
			var items = Items;
			int destination = index + delta;
			if (index < 0 || index >= items.Count || destination < 0 || destination >= items.Count) return index;
			string item = items[index];
			items.RemoveAt(index);
			items.Insert(destination, item);
			Items = items;
			return destination;
		}

		internal static void Clear() { Items = new List<string>(); }

		/// <summary>Removes and returns the first entry, or null when the queue is empty.</summary>
		internal static string TakeNext()
		{
			var items = Items;
			if (items.Count == 0) return null;
			string next = items[0];
			items.RemoveAt(0);
			Items = items;
			return next;
		}
	}

	/// <summary>
	/// Named sets of LaserGRBL settings for different machines: connection, firmware, laser commands, custom G-code and
	/// defaults. GRBL's own $ settings stay on the machine and aren't changed.
	/// </summary>
	internal static class MachinePresets
	{
		internal const string SettingsKey = "MachinePresets";

		internal static readonly string[] Keys =
		{
			"ComWrapper Protocol", "Serial Speed", "Telnet Address", "Websocket URL",
			"Firmware Type", "Support Hardware PWM", "Threading Mode", "Streaming Mode",
			"Reset Grbl On Connect", "HardReset Grbl On Connect", "Query MachineInfo ($I) at connect",
			"GCode.CustomHeader", "GCode.CustomFooter", "GCode.CustomPasses",
			"GrayScaleConversion.Gcode.LaserOptions.LaserOn", "GrayScaleConversion.Gcode.LaserOptions.LaserOff",
			"GrayScaleConversion.Gcode.LaserOptions.PowerMin", "GrayScaleConversion.Gcode.LaserOptions.PowerMax",
			"GrayScaleConversion.Gcode.Speed.Mark", "GrayScaleConversion.VectorizeOptions.BorderSpeed",
			"Disable G0 fast skip", "Unidirectional Engraving", "Raster Hi-Res", "Raster.OverscanMm",
			"Enale Z Jog Control", "Enable Continuous Jog", "Click N Jog", "Jog Speed", "Jog Step",
			"AutoCooling", "AutoCooling TOn", "AutoCooling TOff",
			"Safety.HomeBeforeFirstRun", "Safety.Preflight", "Framing.Feed", "Framing.Power", "Materials.LastModel"
		};

		private static Dictionary<string, Dictionary<string, object>> All
		{
			get { return Settings.GetObject<Dictionary<string, Dictionary<string, object>>>(SettingsKey, null) ?? new Dictionary<string, Dictionary<string, object>>(); }
			set { Settings.SetObject(SettingsKey, value, true); }
		}

		internal static List<string> Names { get { return All.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase).ToList(); } }

		internal static string Active
		{
			get { return Settings.GetObject<string>("MachinePresets.Active", null); }
			private set { Settings.SetObject("MachinePresets.Active", value); }
		}

		/// <summary>Saves the current values of <see cref="Keys"/> under <paramref name="name"/>, replacing a preset with that name.</summary>
		internal static int Save(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a name for the preset.");
			name = name.Trim();
			var values = new Dictionary<string, object>();
			foreach (var key in Keys)
			{
				object value = Settings.GetRaw(key);
				if (value != null) values[key] = value;
			}
			var all = All;
			all[name] = values;
			All = all;
			Active = name;
			return values.Count;
		}

		/// <summary>Writes a preset's values back into the settings. Returns how many were applied.</summary>
		internal static int Apply(string name)
		{
			Dictionary<string, object> values;
			if (name == null || !All.TryGetValue(name, out values)) throw new ArgumentException("That preset no longer exists.");
			foreach (var entry in values)
				if (Keys.Contains(entry.Key)) Settings.SetObject(entry.Key, entry.Value, true);
			Active = name;
			return values.Count;
		}

		internal static void Delete(string name)
		{
			var all = All;
			if (name != null && all.Remove(name)) All = all;
			if (Active == name) Active = null;
		}
	}

	/// <summary>Bounded undo and redo over immutable snapshots. The owner applies a snapshot; this only keeps the order.</summary>
	internal sealed class UndoHistory<T> where T : class
	{
		private readonly int limit;
		private readonly LinkedList<KeyValuePair<string, T>> undo = new LinkedList<KeyValuePair<string, T>>();
		private readonly Stack<KeyValuePair<string, T>> redo = new Stack<KeyValuePair<string, T>>();

		internal UndoHistory(int limit = 50)
		{
			if (limit < 1) throw new ArgumentOutOfRangeException("limit");
			this.limit = limit;
		}

		internal bool CanUndo { get { return undo.Count > 0; } }
		internal bool CanRedo { get { return redo.Count > 0; } }
		internal string UndoName { get { return CanUndo ? undo.Last.Value.Key : null; } }
		internal string RedoName { get { return CanRedo ? redo.Peek().Key : null; } }

		/// <summary>Records the state before a change named <paramref name="change"/>. A new change clears redo.</summary>
		internal void Record(string change, T before)
		{
			if (before == null) return;
			undo.AddLast(new KeyValuePair<string, T>(change, before));
			while (undo.Count > limit) undo.RemoveFirst();
			redo.Clear();
		}

		/// <summary>Returns the state to go back to, given the current one, or null when there is nothing to undo.</summary>
		internal T Undo(T current)
		{
			if (!CanUndo) return null;
			var entry = undo.Last.Value;
			undo.RemoveLast();
			redo.Push(new KeyValuePair<string, T>(entry.Key, current));
			return entry.Value;
		}

		internal T Redo(T current)
		{
			if (!CanRedo) return null;
			var entry = redo.Pop();
			undo.AddLast(new KeyValuePair<string, T>(entry.Key, current));
			return entry.Value;
		}

		internal void Clear()
		{
			undo.Clear();
			redo.Clear();
		}
	}
}
