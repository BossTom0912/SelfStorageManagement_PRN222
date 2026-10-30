using SelfStorageManagementSystem.WpfClient.Views;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class WpfCatalogFilterParsingTests
{
    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("1500000", true, 1500000.0)]
    [InlineData("1.500.000", true, 1500000.0)]
    [InlineData("1,500,000", true, 1500000.0)]
    [InlineData("50000", true, 50000.0)]
    [InlineData("0", true, 0.0)]
    [InlineData("-1000", false, null)]
    [InlineData("abc", false, null)]
    [InlineData("1.50", false, null)]
    [InlineData("1.500.00", false, null)]
    public void ParseVndPrice_ShouldHandleEmptyValidAndInvalidStrictly(string? input, bool expectedValid, double? expectedValue)
    {
        var (isValid, value, errorMessage) = FacilityCatalogWindow.ParseVndPrice(input);

        Assert.Equal(expectedValid, isValid);
        if (expectedValid)
        {
            Assert.Null(errorMessage);
            if (expectedValue.HasValue)
            {
                Assert.Equal((decimal)expectedValue.Value, value);
            }
            else
            {
                Assert.Null(value);
            }
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
            Assert.Null(value);
        }
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("2.5", true, 2.5)]
    [InlineData("2,5", true, 2.5)]
    [InlineData("10", true, 10.0)]
    [InlineData("0", true, 0.0)]
    [InlineData("-1", false, null)]
    [InlineData("abc", false, null)]
    [InlineData("2.5.5", false, null)]
    public void ParseArea_ShouldHandleEmptyValidAndInvalidStrictly(string? input, bool expectedValid, double? expectedValue)
    {
        var (isValid, value, errorMessage) = FacilityCatalogWindow.ParseArea(input, "Diện tích");

        Assert.Equal(expectedValid, isValid);
        if (expectedValid)
        {
            Assert.Null(errorMessage);
            if (expectedValue.HasValue)
            {
                Assert.Equal((decimal)expectedValue.Value, value);
            }
            else
            {
                Assert.Null(value);
            }
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
            Assert.Null(value);
        }
    }
}
