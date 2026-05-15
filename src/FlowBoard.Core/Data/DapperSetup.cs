using System.Data;
using Dapper;

namespace FlowBoard.Core.Data;

/// <summary>
/// Configures Dapper to use snake_case column names so we can use Dapper's
/// default mapping with our PascalCase C# records.
/// </summary>
public static class DapperSetup
{
    private static bool _initialized;
    private static readonly object _lock = new();

    public static void Initialize()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new DateOnlyHandler());
            SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
            _initialized = true;
        }
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateTime dt => DateOnly.FromDateTime(dt),
            DateOnly d  => d,
            string s    => DateOnly.Parse(s),
            _           => DateOnly.FromDateTime(Convert.ToDateTime(value)),
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }

    private sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override DateOnly? Parse(object value) => value switch
        {
            null        => null,
            DateTime dt => DateOnly.FromDateTime(dt),
            DateOnly d  => d,
            string s    => DateOnly.Parse(s),
            _           => DateOnly.FromDateTime(Convert.ToDateTime(value)),
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.HasValue
                ? value.Value.ToDateTime(TimeOnly.MinValue)
                : (object)DBNull.Value;
        }
    }
}
