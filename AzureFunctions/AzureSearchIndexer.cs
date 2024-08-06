using Abot2.Poco;
using AngleSharp.Html;
using Azure;
using Azure.Core.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
//using Azure;
//using Azure.AI.TextAnalytics;
using System.Linq;

namespace AzureSearchCrawler
{
	/// <summary>
	/// A CrawlHandler that indexes crawled pages into Azure Search. Pages are represented by the nested WebPage class.
	/// <para/>To customize what text is extracted and indexed from each page, you implement a custom TextExtractor
	/// and pass it in.
	/// </summary>
	partial class AzureSearchIndexer : CrawlHandler
	{
		private const int IndexingBatchSize = 25;

		private readonly TextExtractor _textExtractor;
		private readonly SearchClient _indexClient;
		private readonly bool _extractText;

		private readonly BlockingCollection<WebPage> _queue = [];
		private readonly SemaphoreSlim indexingLock = new(1, 1);
		//TextAnalyticsClient _aiClient;

		private readonly SemaphoreSlim _rateLimiter;
		private const int MaxConcurrentRequests = 10; // Adjust based on your rate limit
		private const int MaxRetries = 10;

		public AzureSearchIndexer(string serviceEndPoint, string indexName, string adminApiKey, bool extractText, TextExtractor textExtractor)
		{
			_textExtractor = textExtractor;
			// Create client using endpoint and key

			// Create serializer options to convert to camelCase
			JsonSerializerOptions serializerOptions = new()
			{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			};

			SearchClientOptions clientOptions = new()
			{
				Serializer = new JsonObjectSerializer(serializerOptions)
			};

			// Create a client
			Uri endpoint = new(serviceEndPoint);
			AzureKeyCredential credential = new(adminApiKey);
			_indexClient = new SearchClient(endpoint, indexName, credential, clientOptions);
			_extractText = extractText;

			_rateLimiter = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);

		}

	

		public async Task PageCrawledAsync(CrawledPage crawledPage)
		{
			var page = _textExtractor.ExtractText(_extractText, crawledPage.Content.Text);
			string text = page["content"];
			string title = page["title"];

			if (text == null)
			{
				Console.WriteLine("No content for page {0}", crawledPage?.Uri.AbsoluteUri);
				return;
			}

			var webPage = new WebPage(crawledPage.Uri.AbsoluteUri, title, text);
			webPage.PageHtml = crawledPage?.Content?.Text;
			webPage.HeadHtml = crawledPage?.AngleSharpHtmlDocument?.Head?.InnerHtml;

			const int maxLanguageServiceLength = 5120;
			if (!string.IsNullOrEmpty(text))
			{
				var trimmedText = text.Substring(0, Math.Min(text.Length, maxLanguageServiceLength));
				await ProcessTextAnalyticsWithRetryAsync(webPage, trimmedText);
			}

			_queue.Add(webPage);

			if (_queue.Count > IndexingBatchSize)
			{
				await IndexBatchIfNecessary();
			}
		}

		private async Task ProcessTextAnalyticsWithRetryAsync(WebPage webPage, string text)
		{
			for (int attempt = 0; attempt < MaxRetries; attempt++)
			{
				try
				{
					await _rateLimiter.WaitAsync();
					//await ProcessTextAnalyticsAsync(webPage, text);
					await ProcessPageContent(webPage, text);
					break; // Success, exit the retry loop
				}
				catch (RequestFailedException ex) when (ex.Status == 429) // Too Many Requests
				{
					if (attempt == MaxRetries - 1) throw; // Rethrow on last attempt
					int delay = (int)Math.Pow(2, attempt) * 1000; // Exponential backoff
					await Task.Delay(delay);
				}
				finally
				{
					_rateLimiter.Release();
				}
			}
		}


		private async Task ProcessPageContent(WebPage webPage, string text)
		{
			// Detect language
			webPage.LanguageCode = "en"; // detectedLanguage.Value.Iso6391Name;
			webPage.LanguageName = "English"; // detectedLanguage.Value.Name;

			// Analyze sentiment
			//webPage.SentimentValue = ""; // sentimentAnalysis.Value.Sentiment.ToString();

			//// Recognize entities
			//webPage.EntitiesList = [""]; // entities.Value.Select(p => p.Text).Distinct().ToList();
			//webPage.EntitiesJson = JsonSerializer.Serialize(new List<object>());//JsonSerializer.Serialize(entities.Value);

			//// Recognize linked entities
			//webPage.LinkedEntitiesList = [""]; ; // linkedEntities.Value.Select(p => p.Name).Distinct().ToList();
			//webPage.LinkedEntitiesJson = JsonSerializer.Serialize(new List<object>());//JsonSerializer.Serialize(linkedEntities.Value);
		}

		public async Task CrawlFinishedAsync()
		{
			await IndexBatchIfNecessary();

			// sanity check
			if (_queue.Count > 0)
			{
				Console.WriteLine($"Error: indexing queue is still not empty at the end. {_queue.Count} items still left in the queue.");
			}
		}

		private async Task<IndexDocumentsResult> IndexBatchIfNecessary()
		{
			await indexingLock.WaitAsync();

			if (_queue.Count == 0)
			{
				return null;
			}

			int batchSize = Math.Min(_queue.Count, IndexingBatchSize);
			Console.WriteLine("Indexing batch of {0}", batchSize);

			try
			{
				var pages = new List<WebPage>(batchSize);
				for (int i = 0; i < batchSize; i++)
				{
					pages.Add(_queue.Take());
				}
				return await _indexClient.MergeOrUploadDocumentsAsync(pages);
			}
			finally
			{
				indexingLock.Release();
			}
		}
	}
}
