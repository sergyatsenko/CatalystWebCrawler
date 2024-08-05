using Abot2.Core;
using Abot2.Crawler;
using Abot2.Poco;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AzureSearchCrawler
{
	public class SitemapScheduler : IScheduler
	{
		ICrawledUrlRepository _crawledUrlRepo;
		IPagesToCrawlRepository _pagesToCrawlRepo;
		bool _allowUriRecrawling;

		public SitemapScheduler()
			: this(false, null, null)
		{
		}

		public SitemapScheduler(bool allowUriRecrawling, ICrawledUrlRepository crawledUrlRepo, IPagesToCrawlRepository pagesToCrawlRepo)
		{
			_allowUriRecrawling = allowUriRecrawling;
			_crawledUrlRepo = crawledUrlRepo ?? new CompactCrawledUrlRepository();
			_pagesToCrawlRepo = pagesToCrawlRepo ?? new FifoPagesToCrawlRepository();
		}

		public int Count
		{
			get { return _pagesToCrawlRepo.Count(); }
		}

		public void Add(PageToCrawl page)
		{
			Console.WriteLine("adding page:", page);
			//if(page.Uri.LocalPath.)
			if (page == null)
				throw new ArgumentNullException("page");

			if (_allowUriRecrawling || page.IsRetry)
			{
				_pagesToCrawlRepo.Add(page);
			}
			else
			{
				if (_crawledUrlRepo.AddIfNew(page.Uri))
					_pagesToCrawlRepo.Add(page);
			}
		}

		public void Add(IEnumerable<PageToCrawl> pages)
		{
			Console.WriteLine("adding pages");
			if (pages == null)
				throw new ArgumentNullException("pages");

			foreach (var page in pages)
				Add(page);
		}

		public PageToCrawl GetNext()
		{
			Console.WriteLine("getting next");
			return _pagesToCrawlRepo.GetNext();
		}

		public void Clear()
		{
			_pagesToCrawlRepo.Clear();
		}

		public void AddKnownUri(Uri uri)
		{
			Console.WriteLine("AddKnownUri");
			_crawledUrlRepo.AddIfNew(uri);
		}

		public bool IsUriKnown(Uri uri)
		{
			return _crawledUrlRepo.Contains(uri);
		}

		public void Dispose()
		{
			if (_crawledUrlRepo != null)
			{
				_crawledUrlRepo.Dispose();
			}
			if (_pagesToCrawlRepo != null)
			{
				_pagesToCrawlRepo.Dispose();
			}
		}
	}
	/// <summary>
	///  A convenience wrapper for an Abot crawler with a reasonable default configuration and console logging.
	///  The actual action to be performed on the crawled pages is passed in as a CrawlHandler.
	/// </summary>
	class Crawler
	{
		private static int PageCount = 0;

		private readonly CrawlHandler _handler;

		public Crawler(CrawlHandler handler)
		{
			_handler = handler;
		}

		static async Task<List<string>> DownloadAndParseSitemap(string sitemapUrl)
		{
			var urls = new List<string>();

			using (var client = new HttpClient())
			{
				var response = await client.GetStringAsync(sitemapUrl);

				XDocument sitemapDoc = XDocument.Parse(response);
				XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

				var counter = 0;
				foreach (var urlElement in sitemapDoc.Descendants(ns + "url"))
				{
					//if (counter++ > 10)
					//{
					//	break;
					//}
					string loc = urlElement.Element(ns + "loc").Value;
					urls.Add(loc);
				}
			}

			return urls;
		}

		//public async Task Crawl(string rootUri, int maxPages, int maxDepth)
		public async Task Crawl(string rootUri, List<string> urls, int maxPages)
		{
			
			var sitemapScheduler = new SitemapScheduler();
			foreach (var url in urls)
			{
				if (!string.IsNullOrEmpty(url))
				{
					sitemapScheduler.Add(new PageToCrawl(new Uri(url)));
				}
			}

			//if (!string.IsNullOrEmpty(rootUri) && rootUri.ToLower().EndsWith("sitemap.xml"))
			//{
			//	Console.WriteLine("downloading sitemap...");
			//	var sitemapScheduler = new SitemapScheduler();

			//	List<string> urls = await DownloadAndParseSitemap(rootUri);

			//	foreach (var url in urls)
			//	{
			//		if (!string.IsNullOrEmpty(url))
			//		{
			//			sitemapScheduler.Add(new PageToCrawl(new Uri(url)));
			//		}
			//	}
			//	crawler = new(CreateCrawlConfiguration(maxPages, maxDepth), null, null, sitemapScheduler, null, null, null, null, null);

			//}
			//else
			//{
			//	crawler = new(CreateCrawlConfiguration(maxPages, maxDepth), null, null, null, null, null, null, null, null);
			//}
			PoliteWebCrawler crawler = new(CreateCrawlConfiguration(maxPages), null, null, sitemapScheduler, null, null, null, null, null);

			crawler.PageCrawlStarting += crawler_ProcessPageCrawlStarting;
			crawler.PageCrawlCompleted += crawler_ProcessPageCrawlCompleted;

			CrawlResult result = await crawler.CrawlAsync(new Uri(rootUri)); //This is synchronous, it will not go to the next line until the crawl has completed
			if (result.ErrorOccurred)
			{
				Console.WriteLine("Crawl of {0} ({1} pages) completed with error: {2}", result.RootUri.AbsoluteUri, PageCount, result.ErrorException.Message);
			}
			else
			{
				Console.WriteLine("Crawl of {0} ({1} pages) completed without error.", result.RootUri.AbsoluteUri, PageCount);
			}

			await _handler.CrawlFinishedAsync();
		}

		void crawler_ProcessPageCrawlStarting(object sender, PageCrawlStartingArgs e)
		{
			Interlocked.Increment(ref PageCount);

			PageToCrawl pageToCrawl = e.PageToCrawl;
			Console.WriteLine("{0}  found on  {1}, Depth: {2}", pageToCrawl.Uri.AbsoluteUri, pageToCrawl.ParentUri?.AbsoluteUri ?? pageToCrawl.Uri.AbsoluteUri, pageToCrawl.CrawlDepth);
		}

		void  crawler_ProcessPageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
		{
			CrawledPage crawledPage = e.CrawledPage;
			string uri = crawledPage.Uri.AbsoluteUri;

			if (crawledPage.HttpRequestException != null || crawledPage.HttpResponseMessage?.StatusCode != HttpStatusCode.OK)
			{
				Console.WriteLine("Crawl of page failed {0}: exception '{1}', response status {2}", uri, crawledPage.HttpRequestException?.Message, crawledPage.HttpResponseMessage?.StatusCode);
				return ;
			}

			if (string.IsNullOrEmpty(crawledPage.Content.Text))
			{
				Console.WriteLine("Page had no content {0}", uri);
				return ;
			}

			//await _handler.PageCrawledAsync(crawledPage);
			_handler.PageCrawledAsync(crawledPage).Wait();
			return ;
		}

		private CrawlConfiguration CreateCrawlConfiguration(int maxPages)//, int maxDepth)
		{
			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

			CrawlConfiguration crawlConfig = new()
			{
				CrawlTimeoutSeconds = maxPages * 10,
				MaxConcurrentThreads = 5,
				MinCrawlDelayPerDomainMilliSeconds = 100,
				IsSslCertificateValidationEnabled = true,
				MaxPagesToCrawl = maxPages,
				MaxCrawlDepth = 3,
				UserAgentString = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

			};

			return crawlConfig;
		}
	}
}
