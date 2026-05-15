using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FlowBoard.Core.Data;

public class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration config)
    {
        // Prefer DATABASE_URL (Railway / Neon style URI), fall back to ConnectionStrings:Neon.
        var url = Environment.GetEnvironmentVariable("DATABASE_URL");
        _connectionString = !string.IsNullOrWhiteSpace(url)
            ? ParseUri(url)
            : config.GetConnectionString("Neon")
              ?? throw new InvalidOperationException("No Neon connection string configured.");
    }

    public NpgsqlConnection Create() => new NpgsqlConnection(_connectionString);

    // Convert a postgresql:// URI to an Npgsql key=value connection string.
    private static string ParseUri(string uri)
    {
        var u = new Uri(uri);
        var userInfo = u.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = u.Host,
            Port = u.Port > 0 ? u.Port : 5432,
            Database = u.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
            SslMode = SslMode.Require,
        };
        return builder.ConnectionString;
    }
}
