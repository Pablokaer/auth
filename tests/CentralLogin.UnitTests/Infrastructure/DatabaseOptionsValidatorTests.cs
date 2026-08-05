using CentralLogin.Infrastructure.Persistence;
using Xunit;

namespace CentralLogin.UnitTests.Infrastructure;

public sealed class DatabaseOptionsValidatorTests
{
    private static readonly DatabaseOptionsValidator Validator = new();

    [Fact]
    public void Default_sqlite_options_pass()
    {
        var result = Validator.Validate(name: null, new DatabaseOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sqlite_with_blank_data_directory_fails(string dataDirectory)
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Sqlite,
            DataDirectory = dataDirectory,
            FileName = "centrallogin.db",
        };

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Database:{nameof(DatabaseOptions.DataDirectory)}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Sqlite_with_blank_file_name_fails(string fileName)
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Sqlite,
            DataDirectory = "data",
            FileName = fileName,
        };

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Database:{nameof(DatabaseOptions.FileName)}"));
    }

    [Fact]
    public void Postgres_with_a_connection_string_passes()
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Postgres,
            ConnectionString = "Host=db;Database=centrallogin;Username=app;Password=secret;SSL Mode=Require",
        };

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Postgres_without_a_connection_string_fails()
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Postgres,
            ConnectionString = null,
        };

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Database:{nameof(DatabaseOptions.ConnectionString)}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Postgres_with_a_blank_connection_string_fails(string connectionString)
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Postgres,
            ConnectionString = connectionString,
        };

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains($"Database:{nameof(DatabaseOptions.ConnectionString)}"));
    }

    [Fact]
    public void Postgres_options_ignore_sqlite_only_fields()
    {
        // DataDirectory/FileName are SQLite-only and irrelevant once Provider
        // is Postgres, so they must not surface as failures even when blank.
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.Postgres,
            ConnectionString = "Host=db;Database=centrallogin;Username=app;Password=secret;SSL Mode=Require",
            DataDirectory = "",
            FileName = "",
        };

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }
}
