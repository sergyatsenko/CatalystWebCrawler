using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace AzureSearchCrawler
{
	public class CrawlIndexRunner
	{
		private readonly ILogger _logger;
		private readonly string _serviceBusConnectionString;
		private readonly string _queueName;
		private readonly string _schedule;

		public CrawlIndexRunner(ILoggerFactory loggerFactory, IConfiguration configuration)
		{
			_logger = loggerFactory.CreateLogger<CrawlIndexRunner>();
			_serviceBusConnectionString = configuration["ServiceBusConnection"];
			_queueName = configuration["ServiceBusQueueName"];
			_schedule = configuration["CrawlIndexSchedule"];

			if (string.IsNullOrEmpty(_serviceBusConnectionString))
			{
				throw new ArgumentNullException(nameof(_serviceBusConnectionString), "ServiceBusConnection configuration is missing");
			}

			if (string.IsNullOrEmpty(_queueName))
			{
				throw new ArgumentNullException(nameof(_queueName), "ServiceBusQueueName configuration is missing");
			}

			if (string.IsNullOrEmpty(_schedule))
			{
				throw new ArgumentNullException(nameof(_schedule), "UrlPosterSchedule configuration is missing");
			}
		}

		[Function("CrawlIndexRunner")]
		public async Task RunAsync([TimerTrigger("%CrawlIndexSchedule%")] TimerInfo myTimer)
		{
			_logger.LogInformation($"CrawlIndexRunner function executed at: {DateTime.Now}");

			
			var urls = new[]
			{
				 "https://www.catalyst.org/2020/08/10/racism-gender-pay-gap-women/",
				"https://www.catalyst.org/2021/04/27/future-of-work-summit-europe-2021-takeaways/"
			};

			var message = new { urls = urls };
			var messageBody = JsonSerializer.Serialize(message);

			await using var client = new ServiceBusClient(_serviceBusConnectionString);
			var sender = client.CreateSender(_queueName);

			try
			{
				await sender.SendMessageAsync(new ServiceBusMessage(messageBody));
				_logger.LogInformation($"Message sent to queue: {messageBody}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error sending message to Service Bus: {ex.Message}");
			}
		}
	}
}