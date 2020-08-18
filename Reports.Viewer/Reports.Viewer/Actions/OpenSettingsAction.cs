﻿using System.IO;
using System.Windows;
using Newtonsoft.Json;
using Sdl.Community.Reports.Viewer.Model;
using Sdl.Community.Reports.Viewer.View;
using Sdl.Community.Reports.Viewer.ViewModel;
using Sdl.Desktop.IntegrationApi;
using Sdl.Desktop.IntegrationApi.Extensions;
using Sdl.TranslationStudioAutomation.IntegrationApi;

namespace Sdl.Community.Reports.Viewer.Actions
{
	[Action("ReportsViewer_OpenSettings_Action",
		Name = "ReportsViewer_OpenSettings_Name",
		Description = "ReportsViewer_OpenSettings_Description",
		ContextByType = typeof(ReportsViewerController),
		Icon = "Settings"
	)]
	[ActionLayout(typeof(ReportsViewerSettingsGroups), 10, DisplayType.Large)]
	public class OpenSettingsAction : AbstractViewControllerAction<ReportsViewerController>
	{
		private PathInfo _pathInfo;
		private ReportsViewerController _reportsViewerController;

		protected override void Execute()
		{
			var settings = GetSettings();
			var view = new SettingsWindow();
			var viewModel = new SettingsViewModel(view, settings, _pathInfo);
			view.DataContext = viewModel;
			var result = view.ShowDialog();
			if (result != null && (bool)result)
			{
				_reportsViewerController.RefreshView();
			}
		}

		private Settings GetSettings()
		{
			if (File.Exists(_pathInfo.SettingsFilePath))
			{
				var json = File.ReadAllText(_pathInfo.SettingsFilePath);
				return JsonConvert.DeserializeObject<Settings>(json);
			}

			return new Settings();
		}

		public override void Initialize()
		{
			Enabled = true;
			_pathInfo = new PathInfo();
			_reportsViewerController = SdlTradosStudio.Application.GetController<ReportsViewerController>();
		}
	}
}