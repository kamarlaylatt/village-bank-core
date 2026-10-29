using VillageBank.Api.Auth;
using VillageBank.Api.Domain;

namespace VillageBank.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public void Passwords_shorter_than_eight_characters_are_rejected(string password)
    {
        Assert.Throws<BusinessRuleException>(() => PasswordPolicy.Validate(password, "teller01"));
    }

    [Fact]
    public void Passwords_longer_than_the_maximum_are_rejected()
    {
        Assert.Throws<BusinessRuleException>(
            () => PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength + 1), "teller01"));
    }

    [Fact]
    public void A_password_equal_to_the_username_is_rejected_ignoring_case()
    {
        Assert.Throws<BusinessRuleException>(() => PasswordPolicy.Validate("Teller-User", "teller-user"));
    }

    [Fact]
    public void A_reasonable_password_is_accepted()
    {
        var ex = Record.Exception(() => PasswordPolicy.Validate("correct-horse-battery", "teller01"));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("teller01", "teller01")]
    [InlineData("  Teller01  ", "teller01")]
    [InlineData("a.b-c_d", "a.b-c_d")]
    public void Valid_usernames_are_normalised_to_lowercase(string input, string expected)
    {
        Assert.Equal(expected, UsernamePolicy.NormalizeAndValidate(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("this-username-is-definitely-longer-than-fifty-characters-in-total")]
    public void Invalid_usernames_are_rejected(string input)
    {
        Assert.Throws<BusinessRuleException>(() => UsernamePolicy.NormalizeAndValidate(input));
    }
}
