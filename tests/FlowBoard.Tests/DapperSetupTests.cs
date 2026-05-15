using System.Data;
using Dapper;
using FlowBoard.Core.Data;

namespace FlowBoard.Tests;

/// <summary>
/// Locks down the Dapper customisations in <see cref="DapperSetup"/>. These
/// rules underpin every query in the app — silent regressions show up as
/// runtime mapping failures, so we want fast unit-level coverage.
/// </summary>
public class DapperSetupTests
{
    public DapperSetupTests() => DapperSetup.Initialize();

    [Fact]
    public void Initialize_EnablesSnakeCaseColumnMatching()
    {
        Assert.True(DefaultTypeMap.MatchNamesWithUnderscores);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        // Re-init must not throw or reset state — Program.cs calls it on every cold start.
        DapperSetup.Initialize();
        DapperSetup.Initialize();
        Assert.True(DefaultTypeMap.MatchNamesWithUnderscores);
    }

    [Theory]
    [InlineData("2024-01-15")]
    [InlineData("2024-12-31")]
    public void DateOnlyHandler_ParsesStringFromDb(string value)
    {
        var handler = ResolveHandler<DateOnly>();
        var result = (DateOnly)handler.Parse(typeof(DateOnly), value)!;
        Assert.Equal(DateOnly.Parse(value), result);
    }

    [Fact]
    public void DateOnlyHandler_ParsesDateTime()
    {
        var dt = new DateTime(2024, 5, 20);
        var handler = ResolveHandler<DateOnly>();
        Assert.Equal(new DateOnly(2024, 5, 20), (DateOnly)handler.Parse(typeof(DateOnly), dt)!);
    }

    [Fact]
    public void NullableDateOnlyHandler_ParsesNullAsNull()
    {
        var handler = ResolveHandler<DateOnly?>();
        Assert.Null(handler.Parse(typeof(DateOnly?), null!));
    }

    [Fact]
    public void DateOnlyHandler_SetsParameterValueAsDate()
    {
        var handler = ResolveHandler<DateOnly>();
        var p = new FakeDbParameter();
        handler.SetValue(p, new DateOnly(2024, 7, 4));
        Assert.Equal(DbType.Date, p.DbType);
        Assert.Equal(new DateTime(2024, 7, 4), p.Value);
    }

    [Fact]
    public void NullableDateOnlyHandler_SetsDbNullForNull()
    {
        var handler = ResolveHandler<DateOnly?>();
        var p = new FakeDbParameter();
        handler.SetValue(p, null!);
        Assert.Equal(DBNull.Value, p.Value);
    }

    private static SqlMapper.ITypeHandler ResolveHandler<T>()
    {
        var queryHandlersField = typeof(SqlMapper).GetField(
            "typeHandlers",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Dapper internals changed");
        var dict = (System.Collections.IDictionary)queryHandlersField.GetValue(null)!;
        var handler = dict[typeof(T)]
            ?? throw new InvalidOperationException($"No handler registered for {typeof(T)}");
        return (SqlMapper.ITypeHandler)handler;
    }

    private sealed class FakeDbParameter : IDbDataParameter
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; }
        public bool IsNullable { get; set; }
        public string ParameterName { get; set; } = string.Empty;
        public string SourceColumn { get; set; } = string.Empty;
        public DataRowVersion SourceVersion { get; set; }
        public object? Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
    }
}
