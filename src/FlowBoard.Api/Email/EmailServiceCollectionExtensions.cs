namespace FlowBoard.Api.Email;

/// <summary>
/// DI wiring for the auth-flow email senders. Provider is chosen by the
/// <c>Email:Provider</c> config key ("log" | "resend", default "log").
/// "log" stays the default so a fresh checkout / CI runs without external
/// dependencies; production deploys override the key to "resend".
/// </summary>
public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddFlowBoardEmail(this IServiceCollection services, IConfiguration config)
    {
        var provider = (config["Email:Provider"] ?? "log").Trim().ToLowerInvariant();

        switch (provider)
        {
            case "resend":
                services.AddHttpClient<IEmailSender, ResendEmailSender>(c =>
                {
                    c.Timeout = TimeSpan.FromSeconds(10);
                });
                break;

            case "log":
            default:
                services.AddSingleton<IEmailSender, LogEmailSender>();
                break;
        }

        return services;
    }
}
