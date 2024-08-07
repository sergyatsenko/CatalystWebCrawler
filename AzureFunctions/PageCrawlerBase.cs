using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;


public class PageCrawlerBase
{
	private readonly HttpClient _httpClient;
	internal readonly ILogger _logger;
	private readonly int _maxConcurrency;
	private readonly int _maxRetries;
	private readonly SearchClient _searchClient;

	public PageCrawlerBase(IConfiguration configuration, ILoggerFactory loggerFactory)
	{
		_logger = loggerFactory.CreateLogger<PageCrawlerHttp>();

		// Initialize HttpClient with User-Agent from configuration
		_httpClient = new HttpClient();
		string userAgent = configuration["UserAgent"] ?? "DefaultCrawlerBot/1.0";
		_httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);

		// Initialize Azure Search Client
		string searchServiceEndpoint = configuration["SearchServiceEndpoint"];
		string searchIndexName = configuration["SearchIndexName"];
		string searchApiKey = configuration["SearchApiKey"];

		_searchClient = new SearchClient(
			new Uri(searchServiceEndpoint),
			searchIndexName,
			new AzureKeyCredential(searchApiKey));

		// Get configuration for concurrency and retries
		_maxConcurrency = int.Parse(configuration["CrawlerMaxConcurrency"] ?? "3");
		_maxRetries = int.Parse(configuration["CrawlerMaxRetries"] ?? "3");

		_logger.LogInformation($"Page Crawler initialized with User-Agent: {userAgent}, MaxConcurrency: {_maxConcurrency}, MaxRetries: {_maxRetries}");

	}

	internal async Task<PageInfo> CrawlPageAsync(string url, string source)
	{
		_logger.LogInformation($"Crawling {url} from source {source}");

		var response = await _httpClient.GetAsync(url);
		response.EnsureSuccessStatusCode();

		var html = await response.Content.ReadAsStringAsync();
		var doc = new HtmlDocument();
		doc.LoadHtml(html);

		var mainElement = doc.DocumentNode.SelectSingleNode("//main");
		string mainContentHtml = mainElement?.InnerHtml ?? string.Empty;
		string mainContentText = mainElement?.InnerText ?? string.Empty;

		var pageInfo = new PageInfo
		{
			Url = url,
			Title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim(),
			MetaTags = JsonSerializer.Serialize(ExtractMetaTags(doc)),
			HtmlContent = html,
			MainContentHtml = mainContentHtml,
			MainContentText = mainContentText.Trim(),
			Source = source
		};

		return pageInfo;
	}

	internal async Task<HttpResponseData> CrawlPages(HttpRequestData req, CrawlRequest? crawlRequest)
	{
		var results = new ConcurrentBag<PageInfo>();

		var parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = _maxConcurrency
		};

		await Parallel.ForEachAsync(crawlRequest.urls, parallelOptions, async (url, ct) =>
		{
			var pageInfo = await ProcessUrlWithRetryAsync(url, crawlRequest.source);
			results.Add(pageInfo);
		});

		var response = req.CreateResponse(HttpStatusCode.OK);
		await response.WriteAsJsonAsync(results);
		return response;
	}

	private Dictionary<string, string> ExtractMetaTags(HtmlDocument doc)
	{
		var metaTags = new Dictionary<string, string>();
		var nodes = doc.DocumentNode.SelectNodes("//meta");

		if (nodes != null)
		{
			foreach (var node in nodes)
			{
				var name = node.GetAttributeValue("name", null) ??
						   node.GetAttributeValue("property", null);
				var content = node.GetAttributeValue("content", null);

				if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(content))
				{
					metaTags[name] = content;
				}
			}
		}

		return metaTags;
	}

	private string GenerateShortUniqueId(string url)
	{
		using (var sha256 = SHA256.Create())
		{
			byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
			return Convert.ToBase64String(hashBytes)
				.Replace("/", "_")
				.Replace("+", "-");
		}
	}

	private async Task IndexPageInfoAsync(PageInfo pageInfo)
	{
		string uniqueId = GenerateShortUniqueId(pageInfo.Url);

		var document = new SearchDocument
		{
			["id"] = uniqueId,
			["url"] = pageInfo.Url,
			["title"] = pageInfo.Title,
			["metaTags"] = pageInfo.MetaTags,
			["htmlContent"] = pageInfo.HtmlContent,
			["mainContentHtml"] = pageInfo.MainContentHtml,
			["mainContentText"] = pageInfo.MainContentText,
			["source"] = pageInfo.Source
		};

		try
		{
			await _searchClient.MergeOrUploadDocumentsAsync(new[] { document });
			_logger.LogInformation($"Indexed document for {pageInfo.Url} with ID: {uniqueId} from source: {pageInfo.Source}");
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error indexing document for {pageInfo.Url}: {ex.Message}");
			throw;
		}
	}

	private async Task<PageInfo> ProcessUrlWithRetryAsync(string url, string source)
	{
		for (int attempt = 1; attempt <= _maxRetries; attempt++)
		{
			try
			{
				var pageInfo = await CrawlPageAsync(url, source);
				await IndexPageInfoAsync(pageInfo);
				return pageInfo;
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"Error processing {url} (Attempt {attempt}/{_maxRetries}): {ex.Message}");
				if (attempt == _maxRetries)
				{
					_logger.LogError($"Failed to process {url} after {_maxRetries} attempts");
					return new PageInfo { Url = url, Error = ex.Message, Source = source };
				}
				await Task.Delay(1000 * attempt); // Exponential backoff
			}
		}

		// This line should never be reached due to the return in the catch block, but it's here to satisfy the compiler
		return new PageInfo { Url = url, Error = "Unexpected error", Source = source };
	}
}

public class CrawlRequest
{
	public string[] urls { get; set; }
	public string source { get; set; }
}

public class PageInfo
{
	public string Url { get; set; }
	public string Title { get; set; }
	public string MetaTags { get; set; }
	public string HtmlContent { get; set; }
	public string MainContentHtml { get; set; }
	public string MainContentText { get; set; }
	public string Source { get; set; }
	public string Error { get; set; }
}