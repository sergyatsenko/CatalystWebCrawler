using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using HtmlAgilityPack;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;

using System.Text.Json;


public class PageCrawlerHttp : PageCrawlerBase
{
	public PageCrawlerHttp(IConfiguration configuration, ILoggerFactory loggerFactory): base(configuration, loggerFactory)
	{

	}

	[Function("PageCrawlerHttp")]
	public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
	{
		_logger.LogInformation("C# HTTP trigger function processed a request.");

		string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
		var crawlRequest = JsonSerializer.Deserialize<CrawlRequest>(requestBody);

		if (crawlRequest?.urls == null || crawlRequest.urls.Length == 0)
		{
			var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
			await badResponse.WriteStringAsync("Please provide an array of URLs in the request body.");
			return badResponse;
		}

		if (crawlRequest?.source == null || crawlRequest.source.Length == 0)
		{
			var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
			await badResponse.WriteStringAsync("Please provide an array of URLs in the request body.");
			return badResponse;
		}

		return await CrawlPages(req, crawlRequest);
	}
}

