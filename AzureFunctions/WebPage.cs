using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AzureSearchCrawler
{
	partial class AzureSearchIndexer
	{
		private static string CreateSHA512(string strData)
		{
			var message = Encoding.UTF8.GetBytes(strData);
			var hashValue = SHA512.HashData(message);
			return hashValue.Aggregate("", (current, x) => current + $"{x:x2}");
		}



		/// <summary>
		/// Web page that is defined in the Azure AI Search Index
		/// </summary>
		/// <param name="url">Url</param>
		/// <param name="title">Title</param>
		/// <param name="content">Content</param>
		public class WebPage(string url, string title, string content) //, string pageHtml, string imagesJson, string headHtml)
		{
			public string Id { get; } = CreateSHA512(url);

			public string Url { get; } = url;
			public string Title { get; } = title;

			public string Content { get; } = content;
			public string LanguageCode { get; set; }
			public string LanguageName { get; set; }

			public string ImagesJson { get; set; }
			public string PageHtml { get; set; }
			public string HeadHtml { get; set; }
			public string SentimentValue { get; set; }
			public List<string> PhrasesList { get; set; }
			public List<string> EntitiesList { get; set; }
			public string EntitiesJson { get; set; }
			public List<string> LinkedEntitiesList { get; set; }
			public string LinkedEntitiesJson { get; set; }


			//public string PageHtml { get; } = pageHtml;
			//public string HeadHtml { get; } = headHtml;
			//public string ImagesJson { get; } = imagesJson;

		}
	}
}
