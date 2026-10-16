using AgreementCleaner.Core.Services;
using Xunit;

namespace AgreementCleaner.Core.Tests;

public class InputValidatorTests
{
    [Fact]
    public void All_fields_empty_is_rejected()
    {
        var result = InputValidator.Validate("", " ", null, "");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one field"));
    }

    [Fact]
    public void Valid_values_are_cleaned_up()
    {
        var result = InputValidator.Validate(" 0713 ", "{f2246ee9-46d9-8560-9e2a-46257acd17b0}", "350142322650009", "+27 (68) 920-9279");

        Assert.True(result.IsValid);
        Assert.Equal("713", result.Search!.AgreementId);
        Assert.Equal(Guid.Parse("F2246EE9-46D9-8560-9E2A-46257ACD17B0"), result.Search.CustomerId);
        Assert.Equal("350142322650009", result.Search.Imei);
        Assert.Equal("27689209279", result.Search.PhoneNumber);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12.5")]
    [InlineData("-5")]
    [InlineData("0")]
    public void Bad_agreement_id_is_rejected(string value)
    {
        Assert.False(InputValidator.Validate(value, null, null, null).IsValid);
    }

    [Fact]
    public void Sim_number_alone_is_enough_and_spaces_are_removed()
    {
        var result = InputValidator.Validate(null, null, null, null, " 8927 0000 1176 0597 297 ");
        Assert.True(result.IsValid);
        Assert.Equal("8927000011760597297", result.Search!.SimNumber);
        Assert.Null(result.Search.AgreementId);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("89270000117605972AB")]
    [InlineData("12345678901234567890123")]
    public void Bad_sim_number_is_rejected(string value)
    {
        var result = InputValidator.Validate(null, null, null, null, value);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SIM Number"));
    }

    [Fact]
    public void Bad_customer_id_is_rejected()
    {
        Assert.False(InputValidator.Validate(null, "not-a-guid", null, null).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("35014232265000A")]
    [InlineData("12345678901234567")]
    public void Bad_imei_is_rejected(string value)
    {
        Assert.False(InputValidator.Validate(null, null, value, null).IsValid);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("068-ABC-9279")]
    public void Bad_phone_is_rejected(string value)
    {
        Assert.False(InputValidator.Validate(null, null, null, value).IsValid);
    }
}
