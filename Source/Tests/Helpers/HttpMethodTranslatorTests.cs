using Xunit;
using PortwayApi.Classes.Helpers;
using System.Collections.Generic;
using System.Text.Json;

namespace PortwayApi.Tests.Helpers
{
    public class HttpMethodTranslatorTests
    {
        [Fact]
        public void TranslateMethod_WithValidTranslation_ReturnsTranslatedMethod()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", "PUT;MERGE"}
            };

            // Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", customProperties);

            // Assert
            Assert.Equal("MERGE", result);
        }

        [Fact]
        public void TranslateMethod_WithColonFormat_ReturnsTranslatedMethod()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", "PUT:MERGE"}
            };

            // Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", customProperties);

            // Assert
            Assert.Equal("MERGE", result);
        }

        [Fact]
        public void TranslateMethod_WithMultipleTranslations_ReturnsCorrectTranslation()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", "PUT;MERGE,POST;CREATE"}
            };

            // Act
            var putResult = HttpMethodTranslator.TranslateMethod("PUT", customProperties);
            var postResult = HttpMethodTranslator.TranslateMethod("POST", customProperties);
            var getResult = HttpMethodTranslator.TranslateMethod("GET", customProperties);

            // Assert
            Assert.Equal("MERGE", putResult);
            Assert.Equal("CREATE", postResult);
            Assert.Equal("GET", getResult); // No translation configured for GET
        }

        [Fact]
        public void TranslateMethod_WithColonFormatMultipleTranslations_ReturnsCorrectTranslation()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", "PUT:MERGE,POST:CREATE"}
            };

            // Act
            var putResult = HttpMethodTranslator.TranslateMethod("PUT", customProperties);
            var postResult = HttpMethodTranslator.TranslateMethod("POST", customProperties);
            var getResult = HttpMethodTranslator.TranslateMethod("GET", customProperties);

            // Assert
            Assert.Equal("MERGE", putResult);
            Assert.Equal("CREATE", postResult);
            Assert.Equal("GET", getResult); // No translation configured for GET
        }

        [Fact]
        public void TranslateMethod_WithJsonElement_ReturnsTranslatedMethod()
        {
            // Arrange
            var jsonString = "PUT;MERGE";
            var jsonElement = JsonSerializer.Deserialize<JsonElement>($"\"{jsonString}\"");
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", jsonElement}
            };

            // Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", customProperties);

            // Assert
            Assert.Equal("MERGE", result);
        }

        [Fact]
        public void TranslateMethod_WithNoCustomProperties_ReturnsOriginalMethod()
        {
            // Arrange & Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", null);

            // Assert
            Assert.Equal("PUT", result);
        }

        [Fact]
        public void TranslateMethod_WithNoTranslationProperty_ReturnsOriginalMethod()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"SomeOtherProperty", "value"}
            };

            // Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", customProperties);

            // Assert
            Assert.Equal("PUT", result);
        }

        [Fact]
        public void TranslateMethod_WithEmptyTranslationString_ReturnsOriginalMethod()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", ""}
            };

            // Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", customProperties);

            // Assert
            Assert.Equal("PUT", result);
        }

        [Fact]
        public void TranslateMethod_CaseInsensitive_ReturnsTranslatedMethod()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodTranslation", "put;merge"}
            };

            // Act
            var result = HttpMethodTranslator.TranslateMethod("PUT", customProperties);

            // Assert
            Assert.Equal("MERGE", result);
        }

        [Theory]
        [InlineData("GET", true)]
        [InlineData("POST", true)]
        [InlineData("PUT", true)]
        [InlineData("DELETE", true)]
        [InlineData("PATCH", true)]
        [InlineData("MERGE", true)]
        [InlineData("HEAD", true)]
        [InlineData("OPTIONS", true)]
        [InlineData("INVALID", false)]
        [InlineData("", false)]
        public void IsValidHttpMethod_ValidatesCorrectly(string method, bool expected)
        {
            // Act
            var result = HttpMethodTranslator.IsValidHttpMethod(method);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}