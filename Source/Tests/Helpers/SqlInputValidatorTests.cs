using System.Text.Json;
using PortwayApi.Classes;
using PortwayApi.Helpers;
using Xunit;

namespace PortwayApi.Tests.Helpers;

public class SqlInputValidatorTests
{
    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json).RootElement;

    private static EndpointDefinition EmptyEndpoint() => new EndpointDefinition();

    // ── null/empty body ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullBody_ReturnsFalse()
    {
        var data = Parse("null");
        var (isValid, msg, _) = SqlInputValidator.Validate(data, EmptyEndpoint(), "POST");
        Assert.False(isValid);
        Assert.Equal("Request body cannot be empty", msg);
    }

    // ── required columns ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingRequiredColumn_ReturnsError()
    {
        var ep = new EndpointDefinition { RequiredColumns = ["Name"] };
        var (isValid, msg, errors) = SqlInputValidator.Validate(Parse("""{"Age":30}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Field == "Name");
    }

    [Fact]
    public void Validate_RequiredColumnPresent_Passes()
    {
        var ep = new EndpointDefinition { RequiredColumns = ["Name"] };
        var (isValid, _, _) = SqlInputValidator.Validate(Parse("""{"Name":"Alice"}"""), ep, "POST");
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_RequiredColumnNull_ReturnsError()
    {
        var ep = new EndpointDefinition { RequiredColumns = ["Name"] };
        var (isValid, _, errors) = SqlInputValidator.Validate(Parse("""{"Name":null}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Field == "Name");
    }

    [Fact]
    public void Validate_RequiredColumnWhitespace_ReturnsError()
    {
        var ep = new EndpointDefinition { RequiredColumns = ["Name"] };
        var (isValid, _, errors) = SqlInputValidator.Validate(Parse("""{"Name":"   "}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Field == "Name");
    }

    [Fact]
    public void Validate_RequiredColumnNotCheckedForPut()
    {
        var ep = new EndpointDefinition { RequiredColumns = ["Name"] };
        var (isValid, _, _) = SqlInputValidator.Validate(Parse("""{"Age":5}"""), ep, "PUT");
        Assert.True(isValid);
    }

    // ── allowed columns ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_DisallowedColumn_ReturnsError()
    {
        var ep = new EndpointDefinition { AllowedColumns = ["Name", "Age"] };
        var (isValid, _, errors) = SqlInputValidator.Validate(Parse("""{"Name":"Bob","Secret":"x"}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Field == "Secret");
    }

    [Fact]
    public void Validate_AliasedColumn_IsAllowed()
    {
        var ep = new EndpointDefinition { AllowedColumns = ["EmployeeId;ID"] };
        var (isValid, _, _) = SqlInputValidator.Validate(Parse("""{"ID":42}"""), ep, "POST");
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_AllColumnsAllowed_NoAllowedListConfigured_Passes()
    {
        var ep = EmptyEndpoint();
        var (isValid, _, _) = SqlInputValidator.Validate(Parse("""{"Anything":"goes"}"""), ep, "POST");
        Assert.True(isValid);
    }

    // ── regex validation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_RegexMatch_Passes()
    {
        var ep = new EndpointDefinition
        {
            ColumnValidation = new Dictionary<string, ColumnValidationRule>
            {
                ["Email"] = new ColumnValidationRule { Regex = @"^[^@]+@[^@]+\.[^@]+$" }
            }
        };
        var (isValid, _, _) = SqlInputValidator.Validate(Parse("""{"Email":"user@example.com"}"""), ep, "POST");
        Assert.True(isValid);
    }

    [Fact]
    public void Validate_RegexNoMatch_ReturnsError()
    {
        var ep = new EndpointDefinition
        {
            ColumnValidation = new Dictionary<string, ColumnValidationRule>
            {
                ["Email"] = new ColumnValidationRule
                {
                    Regex = @"^[^@]+@[^@]+\.[^@]+$",
                    ValidationMessage = "Invalid email"
                }
            }
        };
        var (isValid, _, errors) = SqlInputValidator.Validate(Parse("""{"Email":"notanemail"}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        var err = Assert.Single(errors);
        Assert.Equal("Invalid email", err.Message);
    }

    [Fact]
    public void Validate_RegexNoMatch_DefaultMessage_Used()
    {
        var ep = new EndpointDefinition
        {
            ColumnValidation = new Dictionary<string, ColumnValidationRule>
            {
                ["Code"] = new ColumnValidationRule { Regex = @"^\d{4}$" }
            }
        };
        var (isValid, _, errors) = SqlInputValidator.Validate(Parse("""{"Code":"abc"}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        Assert.Contains(errors, e => e.Message.Contains("does not match the required format"));
    }

    [Fact]
    public void Validate_MultipleErrors_AllReturned()
    {
        var ep = new EndpointDefinition
        {
            RequiredColumns = ["Name", "Age"],
            AllowedColumns = ["Name", "Age"]
        };
        var (isValid, _, errors) = SqlInputValidator.Validate(Parse("""{"Extra":"x"}"""), ep, "POST");
        Assert.False(isValid);
        Assert.NotNull(errors);
        Assert.True(errors.Count >= 2); // Missing Name, Missing Age, disallowed Extra
    }
}
