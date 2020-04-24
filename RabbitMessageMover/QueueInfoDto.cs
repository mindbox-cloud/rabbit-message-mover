namespace RabbitMessageMover
{
	public class QueueInfoDto 
	{
		public string Name { get; set; }
		public int MessageCount { get; set; }
		public string DeadLetterExchange { get; set; }
		public string DeadLetterKey { get; set; }
		public int? MessageTtl { get; set; }
		public int? Expires { get; set; }
	}
}