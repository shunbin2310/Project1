namespace Project1.Api.Email;

public interface IEmailOutboxProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}
