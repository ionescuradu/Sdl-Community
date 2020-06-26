﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using Sdl.Community.XLIFF.Manager.Common;
using Sdl.Community.XLIFF.Manager.FileTypeSupport.SDLXLIFF;
using Sdl.Community.XLIFF.Manager.FileTypeSupport.XLIFF.Writers;
using Sdl.Community.XLIFF.Manager.Model;
using Sdl.Community.XLIFF.Manager.Model.ProjectSettings;
using Sdl.Community.XLIFF.Manager.Service;
using Sdl.Core.Globalization;
using Sdl.FileTypeSupport.Framework.IntegrationApi;
using Sdl.ProjectAutomation.AutomaticTasks;
using Sdl.ProjectAutomation.Core;
using Sdl.ProjectAutomation.FileBased;
using Sdl.TranslationStudioAutomation.IntegrationApi;
using ProjectFile = Sdl.ProjectAutomation.Core.ProjectFile;

namespace Sdl.Community.XLIFF.Manager.BatchTasks
{
	[AutomaticTask("XLIFF.Manager.BatchTasks.Export",
		"Export to XLIFF",
		"Export to XLIFF",
		GeneratedFileType = AutomaticTaskFileType.BilingualTarget, AllowMultiple = true)]
	[AutomaticTaskSupportedFileType(AutomaticTaskFileType.BilingualTarget)]
	[RequiresSettings(typeof(XliffManagerExportSettings), typeof(ExportSettingsPage))]
	public class ExportBatchTask : AbstractFileContentProcessingAutomaticTask
	{
		private XliffManagerExportSettings _exportSettings;
		private SegmentBuilder _segmentBuilder;
		private ProjectInfo _projectInfo;
		private StringBuilder _logReport;
		private PathInfo _pathInfo;
		private Window _window;
		private string _currentLanguage;
		private Project _project;
		private CustomerProvider _customerProvider;
		private ImageService _imageService;
		private bool _isError;
		private XLIFFManagerViewController _xliffManagerController;
		private WizardContext _wizardContext;

		protected override void OnInitializeTask()
		{
			CloseDocuments();

			_logReport = new StringBuilder();
			_projectInfo = Project.GetProjectInfo();
			_segmentBuilder = new SegmentBuilder();
			_exportSettings = GetSetting<XliffManagerExportSettings>();
			_pathInfo = new PathInfo();
			_customerProvider = new CustomerProvider();
			_imageService = new ImageService();
			_isError = false;
			_xliffManagerController = GetXliffManagerController();
			
			CreateWizardContext();
			WriteLogReportHeader();
			SubscribeToWindowClosing();

			_logReport.AppendLine();
			_logReport.AppendLine("Phase: Export - Started " + FormatDateTime(DateTime.UtcNow));

			base.OnInitializeTask();
		}		

		protected override void ConfigureConverter(ProjectFile projectFile, IMultiFileConverter multiFileConverter)
		{
			var languageName = projectFile.Language.CultureInfo.Name;

			var targetFile = _wizardContext.ProjectFiles.FirstOrDefault(a =>
				a.FileId == projectFile.Id.ToString() && a.TargetLanguage == languageName);

			if (targetFile != null)
			{
				if (!targetFile.Selected)
				{
					return;
				}


				if (string.IsNullOrEmpty(_currentLanguage) || languageName != _currentLanguage)
				{
					_logReport.AppendLine();
					_logReport.AppendLine(string.Format(PluginResources.Label_Language, languageName));
					_currentLanguage = languageName;
				}

				var sdlxliffReader = new SdlxliffReader(_segmentBuilder, _exportSettings.SelectedFilterItemIds, _exportSettings.ExportOptions);
				var xliffWriter = new XliffWriter(_exportSettings.ExportOptions.XliffSupport);

				var dateTimeStampToString = GetDateTimeStampToString(_exportSettings.DateTimeStamp);
				var workingFolder = Path.Combine(_exportSettings.TransactionFolder, dateTimeStampToString);
				var languageFolder = Path.Combine(workingFolder, languageName);

				var xliffFolder = GetXliffFolder(languageFolder, targetFile);
				var xliffFilePath = Path.Combine(xliffFolder, targetFile.Name + ".xliff");

				_logReport.AppendLine(string.Format(PluginResources.label_SdlXliffFile, targetFile.Location));
				_logReport.AppendLine(string.Format(PluginResources.label_XliffFile, xliffFilePath));

				try
				{
					var xliffData = sdlxliffReader.ReadFile(_projectInfo.Id.ToString(), targetFile.Location);
					var exported = xliffWriter.WriteFile(xliffData, xliffFilePath, _exportSettings.ExportOptions.IncludeTranslations);

					if (exported)
					{
						targetFile.Date = _exportSettings.DateTimeStamp;
						targetFile.XliffFilePath = Path.Combine(languageFolder, targetFile.Name + ".xliff");
						targetFile.Action = Enumerators.Action.Export;
						targetFile.Status = Enumerators.Status.Success;
						targetFile.Details = string.Empty;
						targetFile.XliffFilePath = xliffFilePath;
					}

					var activityFile = new ProjectFileActivity
					{
						ProjectFileId = targetFile.FileId,
						ProjectFile = targetFile,
						ActivityId = Guid.NewGuid().ToString(),
						Action = Enumerators.Action.Export,
						Status = exported ? Enumerators.Status.Success : Enumerators.Status.Error,
						Date = targetFile.Date,
						Name = Path.GetFileName(targetFile.XliffFilePath),
						Path = Path.GetDirectoryName(targetFile.XliffFilePath),
						Details = string.Empty
					};

					targetFile.ProjectFileActivities.Add(activityFile);

					if (!exported)
					{
						_isError = true;
					}

					_logReport.AppendLine(string.Format(PluginResources.Label_Success, exported));
					_logReport.AppendLine();
				}
				catch (Exception ex)
				{
					_logReport.AppendLine();
					_logReport.AppendLine(string.Format(PluginResources.label_ExceptionMessage, ex.Message));

					throw;
				}
			}
		}

		public override void TaskComplete()
		{
			base.TaskComplete();

			_logReport.AppendLine();
			_logReport.AppendLine("Phase: Export - Completed " + FormatDateTime(DateTime.UtcNow));

			SaveLogReport();
			if (!_isError)
			{
				_wizardContext.Completed = true;
				_xliffManagerController?.UpdateProjectData(_wizardContext);
			}
		}

		private void CreateWizardContext()
		{
			var selectedIds = TaskFiles.Select(projectFile => projectFile.Id.ToString()).ToList();
			_project = GetProjectModel(Project as FileBasedProject, selectedIds);

			_wizardContext = new WizardContext
			{
				Action = Enumerators.Action.Export,
				Completed = false,
				Project = _project,
				DateTimeStamp = _exportSettings.DateTimeStamp,
				ExcludeFilterItems = GetSelectedFilterItems(),
				ExportOptions = _exportSettings.ExportOptions,
				LocalProjectFolder = _exportSettings.LocalProjectFolder,
				TransactionFolder = _exportSettings.TransactionFolder,
				ProjectFiles = _project.ProjectFiles
			};
		}

		private List<FilterItem> GetSelectedFilterItems()
		{
			var selectedFilterItems = new List<FilterItem>();

			var allFilterItems = Enumerators.GetFilterItems();
			if (_exportSettings.SelectedFilterItemIds != null)
			{
				foreach (var filterItemId in _exportSettings.SelectedFilterItemIds)
				{
					var filterItem = allFilterItems.FirstOrDefault(a => a.Id == filterItemId);
					if (filterItem != null)
					{
						selectedFilterItems.Add(filterItem);
					}
				}
			}

			return selectedFilterItems;
		}		

		private void WriteLogReportHeader()
		{
			var dateTimeStampToString = GetDateTimeStampToString(_exportSettings.DateTimeStamp);
			var workingFolder = Path.Combine(_exportSettings.TransactionFolder, dateTimeStampToString);

			_logReport = new StringBuilder();
			_logReport.AppendLine("Start Process: Export " + FormatDateTime(DateTime.UtcNow));
			_logReport.AppendLine();

			var indent = "   ";
			_logReport.AppendLine(PluginResources.Label_Project);
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_Id, _project.Id));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_Name, _project.Name));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_Location, _project.Path));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_Created, _project.Created.ToString(CultureInfo.InvariantCulture)));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_DueDate, _project.DueDate.ToString(CultureInfo.InvariantCulture)));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_SourceLanguage, _project.SourceLanguage.CultureInfo.DisplayName));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_TargetLanguages, GetProjectTargetLanguagesString(_project)));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_ProjectType, _project.ProjectType));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_Customer, _project.Customer?.Name));

			_logReport.AppendLine();
			_logReport.AppendLine(PluginResources.Label_Options);
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_XliffSupport, _exportSettings.ExportOptions.XliffSupport));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_WorkingFolder, workingFolder));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_IncludeTranslations, _exportSettings.ExportOptions.IncludeTranslations));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_CopySourceToTarget, _exportSettings.ExportOptions.CopySourceToTarget));
			if (_exportSettings.SelectedFilterItemIds?.Count > 0)
			{
				_logReport.AppendLine(indent + string.Format(PluginResources.Label_ExcludeFilters, GetFitlerItemsString(_exportSettings.SelectedFilterItemIds)));
			}

			_logReport.AppendLine();
			_logReport.AppendLine(PluginResources.Label_Files);
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_TotalFiles, _wizardContext.ProjectFiles.Count));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_ExportFiles, _wizardContext.ProjectFiles.Count(a => a.Selected)));
			_logReport.AppendLine(indent + string.Format(PluginResources.Label_Languages, GetSelectedLanguagesString()));
			_logReport.AppendLine();
		}

		private void SaveLogReport()
		{
			var dateTimeStampToString = GetDateTimeStampToString(_exportSettings.DateTimeStamp);
			var workingFolder = Path.Combine(_exportSettings.TransactionFolder, dateTimeStampToString);
			if (!Directory.Exists(workingFolder))
			{
				Directory.CreateDirectory(workingFolder);
			}

			var logFileName = "log." + dateTimeStampToString + ".txt";
			var outputFile = Path.Combine(workingFolder, logFileName);
			using (var writer = new StreamWriter(outputFile, false, Encoding.UTF8))
			{
				writer.Write(_logReport);
				writer.Flush();
			}

			File.Copy(outputFile, Path.Combine(_pathInfo.ApplicationLogsFolderPath, logFileName));
		}

		private List<LanguageInfo> GetLanguageInfos(IEnumerable<Language> languages)
		{
			var targetLanguages = new List<LanguageInfo>();
			foreach (var targetLanguage in languages)
			{
				targetLanguages.Add(GetLanguageInfo(targetLanguage.CultureInfo));
			}

			return targetLanguages;
		}

		private string GetProjectType(FileBasedProject project)
		{
			var type = project.GetType();
			var internalProjectField = type.GetField("_project", BindingFlags.NonPublic | BindingFlags.Instance);
			if (internalProjectField != null)
			{
				dynamic internalDynamicProject = internalProjectField.GetValue(project);
				return internalDynamicProject.ProjectType.ToString();
			}

			return null;
		}

		private LanguageInfo GetLanguageInfo(CultureInfo cultureInfo)
		{
			var languageInfo = new LanguageInfo
			{
				CultureInfo = cultureInfo,
				Image = _imageService.GetImage(cultureInfo.Name)
			};

			return languageInfo;
		}

		private string GetProjectTargetLanguagesString(Project project)
		{
			var targetLanguages = string.Empty;
			foreach (var languageInfo in project.TargetLanguages)
			{
				targetLanguages += (string.IsNullOrEmpty(targetLanguages) ? string.Empty : ", ") +
								   languageInfo.CultureInfo.DisplayName;
			}

			return targetLanguages;
		}

		private string GetSelectedLanguagesString()
		{
			var selected = _wizardContext.ProjectFiles.Where(a => a.Selected);

			var selectedLanguages = string.Empty;
			foreach (var name in selected.Select(a => a.TargetLanguage).Distinct())
			{
				selectedLanguages += (string.IsNullOrEmpty(selectedLanguages) ? string.Empty : ", ") + name;
			}

			return selectedLanguages;
		}

		private string GetFitlerItemsString(IEnumerable<string> filterItems)
		{
			var items = string.Empty;
			foreach (var filterItem in filterItems)
			{
				items += (string.IsNullOrEmpty(items) ? string.Empty : ", ") + filterItem;
			}

			return items;
		}

		public string GetDateTimeStampToString(DateTime dateTime)
		{
			var value = dateTime.Year
						+ "" + dateTime.Month.ToString().PadLeft(2, '0')
						+ "" + dateTime.Day.ToString().PadLeft(2, '0')
						+ "" + dateTime.Hour.ToString().PadLeft(2, '0')
						+ "" + dateTime.Minute.ToString().PadLeft(2, '0')
						+ "" + dateTime.Second.ToString().PadLeft(2, '0');

			return value;

		}

		private string FormatDateTime(DateTime dateTime)
		{
			var value = dateTime.Year
						+ "-" + dateTime.Month.ToString().PadLeft(2, '0')
						+ "-" + dateTime.Day.ToString().PadLeft(2, '0')
						+ "T" + dateTime.Hour.ToString().PadLeft(2, '0')
						+ ":" + dateTime.Minute.ToString().PadLeft(2, '0')
						+ ":" + dateTime.Second.ToString().PadLeft(2, '0')
						+ "." + dateTime.Millisecond.ToString().PadLeft(2, '0');

			return value;
		}

		private static string GetXliffFolder(string languageFolder, Model.ProjectFile targetFile)
		{
			var xliffFolder = Path.Combine(languageFolder, targetFile.Path.TrimStart('\\'));
			if (!Directory.Exists(xliffFolder))
			{
				Directory.CreateDirectory(xliffFolder);
			}

			return xliffFolder;
		}

		private Project GetProjectModel(FileBasedProject selectedProject, IReadOnlyCollection<string> selectedFileIds)
		{
			if (selectedProject == null)
			{
				return null;
			}

			var projectInfo = selectedProject.GetProjectInfo();

			var project = new Project
			{
				Id = projectInfo.Id.ToString(),
				Name = projectInfo.Name,
				AbsoluteUri = projectInfo.Uri.AbsoluteUri,
				Customer = _customerProvider.GetProjectCustomer(selectedProject),
				Created = projectInfo.CreatedAt,
				DueDate = projectInfo.DueDate ?? DateTime.MaxValue,
				Path = projectInfo.LocalProjectFolder,
				SourceLanguage = GetLanguageInfo(projectInfo.SourceLanguage.CultureInfo),
				TargetLanguages = GetLanguageInfos(projectInfo.TargetLanguages),
				ProjectType = GetProjectType(selectedProject)
			};

			var existingProject = _xliffManagerController.GetProjects().FirstOrDefault(a => a.Id == projectInfo.Id.ToString());

			if (existingProject != null)
			{
				foreach (var projectFile in existingProject.ProjectFiles)
				{
					if (projectFile.Clone() is Model.ProjectFile clonedProjectFile)
					{
						clonedProjectFile.Project = project;
						clonedProjectFile.Selected = selectedFileIds != null && selectedFileIds.Any(a => a == projectFile.FileId.ToString());

						foreach (var clonedFileActivity in clonedProjectFile.ProjectFileActivities)
						{
							clonedFileActivity.ProjectFile = clonedProjectFile;
						}

						project.ProjectFiles.Add(clonedProjectFile);
					}
				}
			}
			else
			{
				project.ProjectFiles = GetProjectFiles(selectedProject, project, selectedFileIds);
			}

			return project;
		}

		private static List<Model.ProjectFile> GetProjectFiles(IProject project, Project projectModel, IReadOnlyCollection<string> selectedFileIds)
		{
			var projectInfo = project.GetProjectInfo();
			var projectFiles = new List<Model.ProjectFile>();

			foreach (var targetLanguage in projectInfo.TargetLanguages)
			{
				var languageFiles = project.GetTargetLanguageFiles(targetLanguage);
				foreach (var projectFile in languageFiles)
				{
					if (projectFile.Role != FileRole.Translatable)
					{
						continue;
					}

					var projectFileModel = GetProjectFile(projectModel, projectFile, targetLanguage, selectedFileIds);
					projectFiles.Add(projectFileModel);
				}
			}

			return projectFiles;
		}

		private static Model.ProjectFile GetProjectFile(Project project, ProjectFile projectFile,
			Language targetLanguage, IReadOnlyCollection<string> selectedFileIds)
		{
			var projectFileModel = new Model.ProjectFile
			{
				ProjectId = project.Id,
				FileId = projectFile.Id.ToString(),
				Name = projectFile.Name,
				Path = projectFile.Folder,
				Location = projectFile.LocalFilePath,
				Action = Enumerators.Action.None,
				Status = Enumerators.Status.Ready,
				Date = DateTime.MinValue,
				TargetLanguage = targetLanguage.CultureInfo.Name,
				Selected = selectedFileIds != null && selectedFileIds.Any(a => a == projectFile.Id.ToString()),
				FileType = projectFile.FileTypeId,
				Project = project
			};

			return projectFileModel;
		}

		private void SubscribeToWindowClosing()
		{
			try
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					if (_window != null)
					{
						_window.Closing -= Window_Closing;
					}

					foreach (Window window in Application.Current?.Windows)
					{
						if (!window.Title.Equals("Batch Processing"))
						{
							continue;
						}

						_window = window;
						_window.Closing += Window_Closing;
						break;
					}
				});
			}
			catch
			{
				// catch all; ignore
			}
		}

		private static XLIFFManagerViewController GetXliffManagerController()
		{
			try
			{
				return SdlTradosStudio.Application.GetController<XLIFFManagerViewController>();
			}
			catch
			{
				// catch all; ignore
			}

			return null;
		}

		private static EditorController GetEditorController()
		{
			try
			{
				return SdlTradosStudio.Application.GetController<EditorController>();
			}
			catch
			{
				// catch all; ignore
			}

			return null;
		}

		private static void CloseDocuments()
		{
			var editor = GetEditorController();
			if (editor == null)
			{
				return;
			}

			var activeDocs = editor.GetDocuments().ToList();

			try
			{
				foreach (var activeDoc in activeDocs)
				{
					Application.Current.Dispatcher.Invoke(() => { editor.Close(activeDoc); });
				}
			}
			catch
			{
				// catch all; ignore
			}
		}

		private void RemoveSettingsFromProject(string id)
		{
			var projectSettings = Project.GetSettings();
			projectSettings.RemoveSettingsGroup(id);
			Project.UpdateSettings(projectSettings);
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			RemoveSettingsFromProject(_exportSettings.Id);
		}
	}
}
