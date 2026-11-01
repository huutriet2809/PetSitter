using System.Threading.Channels;

namespace PetSitter.Services
{
    public interface IEmailQueue
    {
        ValueTask EnqueueAsync(EmailMessage message);
        IAsyncEnumerable<EmailMessage> ReadAllAsync(System.Threading.CancellationToken cancellationToken);
    }

    public class EmailQueue : IEmailQueue
    {
        private readonly Channel<EmailMessage> _channel;

        public EmailQueue()
        {
            // BoundedChannel để chống cạn kiệt bộ nhớ nếu có lượng phát sinh đột biến
            var options = new BoundedChannelOptions(2000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _channel = Channel.CreateBounded<EmailMessage>(options);
        }

        public ValueTask EnqueueAsync(EmailMessage message)
        {
            return _channel.Writer.WriteAsync(message);
        }

        public IAsyncEnumerable<EmailMessage> ReadAllAsync(System.Threading.CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}

