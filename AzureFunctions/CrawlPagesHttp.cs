using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AzureSearchCrawler
{
	public class CrawlPagesHttp
	{
		private readonly ILogger _logger;

		public CrawlPagesHttp(ILoggerFactory loggerFactory)
		{
			_logger = loggerFactory.CreateLogger<CrawlPagesHttp>();
		}

		[Function("CrawlPagesHttp")]
		public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
		{
			_logger.LogInformation("C# HTTP trigger function processed a request.");

			List<string> urls = new List<string>();

			try
			{
				using (StreamReader reader = new StreamReader(req.Body))
				{
					string requestBody = await reader.ReadToEndAsync();
					if (!string.IsNullOrEmpty(requestBody))
					{
						var jsonDocument = JsonDocument.Parse(requestBody);
						if (jsonDocument.RootElement.TryGetProperty("urls", out JsonElement urlsElement) && urlsElement.ValueKind == JsonValueKind.Array)
						{
							foreach (JsonElement urlElement in urlsElement.EnumerateArray())
							{
								string url = urlElement.GetString();
								if (!string.IsNullOrEmpty(url))
								{
									urls.Add(url);
								}
							}
						}
					}
				}
			}
			catch (JsonException ex)
			{
				_logger.LogError($"Error parsing JSON: {ex.Message}");
			}

			var response = req.CreateResponse(HttpStatusCode.OK);
			response.Headers.Add("Content-Type", "application/json; charset=utf-8");

			string responseMessage = urls.Count > 0
				? $"Received request with {urls.Count} URL(s): {string.Join(", ", urls)}"
				: "Received request, but no valid URLs were provided.";

			var serviceEndpoint = "https://xccomaisearch.search.windows.net";
			var indexName = "catalyst-az-poc";
			var key = "yrQnhlo42h8Nq5tK7alHcucdUskKb2FDtnx32ssLlsAzSeBOWnoA";
			var maxPagesToIndex = 500;
			var rootUrl = ""; // "https://www.catalyst.org";
			

			var indexer = new AzureSearchIndexer(serviceEndpoint, indexName, key, true, new TextExtractor());
			var crawler = new Crawler(indexer);
			crawler.Crawl(rootUrl, urls, maxPagesToIndex).Wait();


			await response.WriteStringAsync(JsonSerializer.Serialize(new { message = responseMessage, urls = urls }));

			return response;
		}
	}
}