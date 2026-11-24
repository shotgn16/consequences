using Microsoft.AspNetCore.SignalR;

namespace Consequences.Hubs;

/// <summary>
/// Game rule errors ("not your turn") are normal and go back to the player as a message.
/// Anything else is a real bug: log it, and give the player a friendly message.
/// </summary>
public class ErrorFilter(ILogger<ErrorFilter> log) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try { return await next(ctx); }
        catch (HubException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Hub method {Method} failed", ctx.HubMethodName);
            throw new HubException("Oops, something went wrong on the server. Please try again.");
        }
    }
}
