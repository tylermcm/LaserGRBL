using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LaserGRBL
{
	public partial class SaveOptionForm : Form
	{
		private JobPassPlan passPlan;
		private Button editPasses;
		private PreviewForm host;
		public SaveOptionForm()
		{
			InitializeComponent();
			editPasses = new UserControls.GrblButton { Text = "Job passes", AutoSize = true };
			tableLayoutPanel3.Controls.Add(editPasses, 0, 0);
			editPasses.Click += (sender, args) =>
			{
				if (host == null) return;
				var editor = new JobPassEditor(passPlan, Math.Min(99, (int)UDCount.Value), true);
				editor.ApplyRequested += (s, e) =>
				{
					passPlan = editor.Plan;
					UpdatePassSummary();
					host.PopJobView();
				};
				editor.CancelRequested += (s, e) => host.PopJobView();
				host.PushJobView(editor, "Job passes");
			};
			UserControls.ThemeMgr.SetTheme(this, true);
		}

		private void UpdatePassSummary()
		{
			UDCount.Enabled = passPlan == null;
			if (passPlan != null) UDCount.Value = passPlan.Count;
			editPasses.Text = passPlan == null ? "Job passes" : string.Format("Job passes ({0})", passPlan.Count);
		}

		internal static void Open(Form parent, GrblCore core, PreviewForm preview)
		{
			if (core.JobEditorOpen) return;
			var editor = new SaveOptionForm { passPlan = core.Passes, host = preview };
			editor.UpdatePassSummary();
			if (core.HasJobOperations)
			{
				editor.UDCount.Enabled = false;
				editor.editPasses.Enabled = false;
			}
			editor.BtnSave.Click += (sender, args) =>
			{
				bool header = editor.CBHeader.Checked, footer = editor.CBFooter.Checked,
					between = editor.CBBetween.Checked, useLf = editor.CBLFLineEndings.Checked;
				int cycles = (int)editor.UDCount.Value;
				var plan = editor.passPlan;
				preview.PopJobView();
				core.SaveProgram(parent, header, footer, between, cycles, useLf, plan);
			};
			preview.ShowJobEditor(editor, editor.Text, showBack: true);
		}

	}
}
