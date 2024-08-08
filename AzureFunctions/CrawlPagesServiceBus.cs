//using System;
//using System.Collections.Generic;
//using System.Text.Json;
//using System.Threading.Tasks;
//using Microsoft.Azure.Functions.Worker;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Configuration;
//using Azure.Messaging.ServiceBus;

//namespace AzureSearchCrawler
//{
//	public class CrawlPagesServiceBus
//	{
//		private readonly ILogger _logger;
//		private readonly string _queueName;
//		private readonly ServiceBusClient _serviceBusClient;

//		public CrawlPagesServiceBus(ILoggerFactory loggerFactory, IConfiguration configuration)
//		{
//			_logger = loggerFactory.CreateLogger<CrawlPagesServiceBus>();
//			_queueName = configuration["ServiceBusQueueName"];
//			var serviceBusConnection = configuration["ServiceBusConnection"];

//			if (string.IsNullOrEmpty(_queueName))
//			{
//				throw new ArgumentNullException(nameof(_queueName), "ServiceBusQueueName configuration is missing");
//			}
//			if (string.IsNullOrEmpty(serviceBusConnection))
//			{
//				throw new ArgumentNullException("ServiceBusConnection", "ServiceBusConnection configuration is missing");
//			}

//			_serviceBusClient = new ServiceBusClient(serviceBusConnection);
//		}

//		[Function("CrawlPagesServiceBus")]
//		public async Task Run(
//			[ServiceBusTrigger("%ServiceBusQueueName%", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage triggerMessage,
//			ServiceBusMessageActions triggerMessageActions)
//		{
//			_logger.LogInformation("CrawlPagesServiceBus function triggered. Processing messages...");

//			//await using var receiver = _serviceBusClient.CreateReceiver(_queueName);

//			//while (true)
//			//{
//			//	try
//			//	{
//			//		// Try to receive a message with a 5-second timeout
//			//		var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));

//			//		if (message == null)
//			//		{
//			//			_logger.LogInformation("No more messages in the queue. Exiting.");
//			//			break;
//			//		}

//			//		await ProcessMessage(message, receiver);
//			//	}
//			//	catch (Exception ex)
//			//	{
//			//		_logger.LogError($"Error in message processing loop: {ex.Message}");
//			//		// Consider implementing a circuit breaker or backoff strategy here
//			//	}
//			//}

//			//// Complete the trigger message
//			//await triggerMessageActions.CompleteMessageAsync(triggerMessage);
//		}

//		private async Task ProcessMessage(ServiceBusReceivedMessage message, ServiceBusReceiver receiver)
//		{
//			_logger.LogInformation("Processing message: {MessageId}", message.MessageId);

//			List<string> urls = new List<string>();
//			try
//			{
//				if (!string.IsNullOrEmpty(message.Body.ToString()))
//				{
//					var jsonDocument = JsonDocument.Parse(message.Body.ToString());
//					if (jsonDocument.RootElement.TryGetProperty("urls", out JsonElement urlsElement) && urlsElement.ValueKind == JsonValueKind.Array)
//					{
//						foreach (JsonElement urlElement in urlsElement.EnumerateArray())
//						{
//							string url = urlElement.GetString();
//							if (!string.IsNullOrEmpty(url))
//							{
//								urls.Add(url);
//							}
//						}
//					}
//				}

//				string logMessage = urls.Count > 0
//					? $"Received message with {urls.Count} URL(s): {string.Join(", ", urls)}"
//					: "Received message, but no valid URLs were provided.";
//				_logger.LogInformation(logMessage);

//				var maxPagesToIndex = 500;
//				var crawler = Crawler.Instance;
//				await crawler.Crawl(urls, maxPagesToIndex);

//				// Explicitly complete the message
//				await receiver.CompleteMessageAsync(message);
//				_logger.LogInformation("Message {MessageId} processed and removed from the queue", message.MessageId);
//			}
//			catch (JsonException ex)
//			{
//				_logger.LogError($"Error parsing JSON: {ex.Message}");
//				// Abandon the message so it can be processed again
//				await receiver.AbandonMessageAsync(message);
//				_logger.LogWarning("Message {MessageId} abandoned due to JSON parsing error", message.MessageId);
//			}
//			catch (Exception ex)
//			{
//				_logger.LogError($"Error processing message: {ex.Message}");
//				// Abandon the message so it can be processed again
//				await receiver.AbandonMessageAsync(message);
//				_logger.LogWarning("Message {MessageId} abandoned due to processing error", message.MessageId);
//			}
//		}
//	}
//}