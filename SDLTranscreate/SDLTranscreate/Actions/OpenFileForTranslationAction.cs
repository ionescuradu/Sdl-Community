﻿using System;
using Sdl.Desktop.IntegrationApi.Extensions;
using Sdl.TranslationStudioAutomation.IntegrationApi;

namespace Sdl.Community.Transcreate.Actions
{
	[Action("TranscreateManager_OpenFileForTranslation_Action",
		Name = "TranscreateManager_OpenFileForTranslation_Name",
		Description = "TranscreateManager_OpenFileForTranslation_Description",
		ContextByType = typeof(TranscreateViewController),
		Icon = "OpenForTranslation"
	)]
	[ActionLayout(typeof(TranscreateManagerOpenGroup), 4, DisplayType.Large)]
	public class OpenFileForTranslationAction : AbstractOpenFileInEditorAction
	{
		protected override void Execute()
		{
			OpenFile(EditingMode.Translation);
		}

		public void OpenFile()
		{
			Execute();
		}

		public override void Initialize()
		{
			Setup();
		}
	}
}
