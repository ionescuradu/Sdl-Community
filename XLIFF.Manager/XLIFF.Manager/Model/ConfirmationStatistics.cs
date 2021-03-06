﻿using System;
using System.Xml.Serialization;

namespace Sdl.Community.XLIFF.Manager.Model
{
	public class ConfirmationStatistics : ICloneable
	{
		public ConfirmationStatistics()
		{	
			WordCounts = new WordCounts();
		}
		
		[XmlElement]
		public WordCounts WordCounts { get; set; }

		public object Clone()
		{
			var stats = new ConfirmationStatistics
			{
				WordCounts = WordCounts.Clone() as WordCounts
			};

			return stats;
		}
	}
}
