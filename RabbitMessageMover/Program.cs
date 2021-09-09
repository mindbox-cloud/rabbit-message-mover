using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;

namespace RabbitMessageMover
{
	class Program
	{
		private const int ThreadCount = 16;
		private const int RabbitMqAdministrationPort = 15672;
		private const string RabbitMqAdministrationLogin = "guest";
		private const string RabbitMqAdministrationPassword = "guest";

		private const string DeadLetterExchangeHeaderName = "x-dead-letter-exchange";
		private const string DeadLetterRoutingKeyHeaderName = "x-dead-letter-routing-key";
		private const string MessageTtlHeaderName = "x-message-ttl";
		private const string ExpiresHeaderName = "x-expires";

		private static int Main(string[] args)
		{
			if (args.Length < 2 || args.Length > 3)
			{
				Console.WriteLine("Parameters:");
				Console.WriteLine("1) Source server URI with AMQP protocol");
				Console.WriteLine("2) Destination server URI with AMQP protocol");
				Console.WriteLine("3) Queue prefix (optional)");
				return 1;
			}

			var sourceUri = args[0];
			var destinationUri = args[1];
			var queuePrefix = args[2];

			Console.WriteLine("Preparing list of queues...");
			
			var queues = GetNonEmptyQueues(sourceUri, queuePrefix);

			for (var i = 0; i < queues.Count; i++)
			{
				Console.WriteLine("{0}) {1} ({2} m.)", i + 1, queues[i].Name, queues[i].MessageCount);
			}

			using(var sourceConnection = new ConnectionFactory { Uri = new Uri(sourceUri) }.CreateConnection())
			using(var destinationConnection = new ConnectionFactory {Uri = new Uri(destinationUri)}.CreateConnection())
			{
				var threads = Enumerable
					.Range(0, ThreadCount)
					.Select(threadIndex => new Thread(() =>
					{
						foreach (var queue in queues)
							Move(queue, sourceConnection, destinationConnection);
					}))
					.ToList();
				foreach (var thread in threads)
					thread.Start();
				foreach (var thread in threads)
					thread.Join();
			}


			Console.WriteLine("Done");
			Console.ReadLine();
			return 0;
		}

		private static List<QueueInfoDto> GetNonEmptyQueues(string sourceUri, string prefix)
		{
			var apiUriBuilder = new UriBuilder(sourceUri)
			{
				Scheme = "http",
				Port = RabbitMqAdministrationPort,
				Path = "/api/queues/"
			};
			var resultUri = apiUriBuilder.Uri;

			var request = WebRequest.CreateHttp(resultUri);
			request.Timeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
			request.Credentials = new NetworkCredential(RabbitMqAdministrationLogin, RabbitMqAdministrationPassword);
			string responseText;
			using (var response = request.GetResponse())
			using (var responseStream = response.GetResponseStream())
			using (var responseReader = new StreamReader(responseStream, Encoding.UTF8))
				responseText = responseReader.ReadToEnd();

			var queuesJsonData = JArray.Parse(responseText);
			var queues = queuesJsonData
				.Select(q => new QueueInfoDto
				{
					Name = q["name"]?.Value<string>(),
					MessageCount = q["messages"]?.Value<int>() ?? 0,
					DeadLetterExchange = q["arguments"]?[DeadLetterExchangeHeaderName]?.Value<string>(),
					DeadLetterKey = q["arguments"]?[DeadLetterRoutingKeyHeaderName]?.Value<string>(),
					MessageTtl = q["arguments"]?[MessageTtlHeaderName]?.Value<int>(),
					Expires = q["arguments"]?[ExpiresHeaderName]?.Value<int>()

				})
				.Where(x => x.Name != null && x.MessageCount > 0)
				.Where(x => prefix == null || x.Name.StartsWith(prefix))
				.OrderByDescending(x => x.MessageCount)
				.ToList();

			return queues;
		}

		private static void Move(QueueInfoDto queue, IConnection sourceConnection, IConnection destinationConnection)
		{
			using var sourceModel = sourceConnection.CreateModel();
			using var destinationModel = destinationConnection.CreateModel();
			
			destinationModel.ConfirmSelect();
			sourceModel.QueueDeclarePassive(queue.Name);
			destinationModel.QueueDeclarePassive(queue.Name);
			
			while (true)
			{
				var result = sourceModel.BasicGet(queue.Name, false);
				if (result == null)
					return;
				
				var properties = result.BasicProperties;
				
				destinationModel.BasicPublish(
					exchange: string.Empty,
					routingKey: queue.Name,
					mandatory: true,
					basicProperties: properties,
					body: result.Body);
				destinationModel.WaitForConfirmsOrDie();
				
				sourceModel.BasicAck(result.DeliveryTag, false);
			}
		}
	}
}
