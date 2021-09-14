using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;

namespace RabbitMessageMover
{
	class Program
	{
		private const int ThreadCount = 32;

		private static async Task<int> Main(string[] args)
		{
			if (args.Length != 3)
			{
				Console.WriteLine("Parameters:");
				Console.WriteLine("1) Source server URI with AMQP protocol");
				Console.WriteLine("2) Destination server URI with AMQP protocol");
				Console.WriteLine("3) Queue list uri");
				return 1;
			}

			var sourceUri = args[0];
			var destinationUri = args[1];
			var queuesUri = args[2];

			Console.WriteLine("Preparing list of queues...");
			
			var queues = await GetQueues(queuesUri);

			for (var i = 0; i < queues.Count; i++)
			{
				Console.WriteLine("{0} - {1}", i + 1, queues[i]);
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

		private static async Task<List<string>> GetQueues(string queuesUri)
		{
			using (var client = new HttpClient())
			{
				var uri = new Uri(queuesUri, UriKind.Absolute);
				var content = await client.GetStringAsync(uri);
				return content.Split(Environment.NewLine).Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s))
					.ToList();
			}
		}

		private static void Move(string queue, IConnection sourceConnection, IConnection destinationConnection)
		{
			try
			{
				using var sourceModel = sourceConnection.CreateModel();
				using var destinationModel = destinationConnection.CreateModel();

				destinationModel.ConfirmSelect();
				sourceModel.QueueDeclarePassive(queue);
				destinationModel.QueueDeclarePassive(queue);

				while (true)
				{
					var result = sourceModel.BasicGet(queue, false);
					if (result == null)
						return;

					var properties = result.BasicProperties;

					destinationModel.BasicPublish(
						exchange: string.Empty,
						routingKey: queue,
						mandatory: true,
						basicProperties: properties,
						body: result.Body);
					destinationModel.WaitForConfirmsOrDie();

					sourceModel.BasicAck(result.DeliveryTag, false);
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to move {queue}, error: {e.Message}");
			}
		}
	}
}
