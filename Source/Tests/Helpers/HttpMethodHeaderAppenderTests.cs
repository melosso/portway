using Xunit;
using PortwayApi.Classes.Helpers;
using System.Text.Json;

namespace PortwayApi.Tests.Helpers
{
    public class HttpMethodHeaderAppenderTests
    {
        [Fact]
        public void GetAppendHeaders_WithValidMapping_ReturnsCorrectHeaders()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodAppendHeaders", "PUT:X-HTTP-Method={ORIGINAL_METHOD}"}
            };

            // Act
            var result = HttpMethodHeaderAppender.GetAppendHeaders("PUT", "POST", customProperties);

            // Assert
            Assert.Single(result);
            Assert.True(result.ContainsKey("X-HTTP-Method"));
            Assert.Equal("PUT", result["X-HTTP-Method"]);
        }

        [Fact]
        public void GetAppendHeaders_WithConflictingHeaders_SkipsWhenConfigured()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodAppendHeaders", "PUT:X-HTTP-Method={ORIGINAL_METHOD},Content-Type=application/merge-patch+json"}
            };
            var existingHeaders = new[] { "Content-Type", "Authorization" };

            // Act
            var result = HttpMethodHeaderAppender.GetAppendHeaders(
                "PUT", "POST", customProperties, existingHeaders, HeaderConflictResolution.Skip);

            // Assert
            Assert.Single(result); // Only X-HTTP-Method should be added, Content-Type should be skipped
            Assert.True(result.ContainsKey("X-HTTP-Method"));
            Assert.False(result.ContainsKey("Content-Type"));
            Assert.Equal("PUT", result["X-HTTP-Method"]);
        }

        [Fact]
        public void GetAppendHeaders_WithMultipleHeaders_ReturnsAllHeaders()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodAppendHeaders", "PUT:X-HTTP-Method={ORIGINAL_METHOD},Content-Type=application/merge-patch+json"}
            };

            // Act
            var result = HttpMethodHeaderAppender.GetAppendHeaders("PUT", "POST", customProperties);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey("X-HTTP-Method"));
            Assert.Equal("PUT", result["X-HTTP-Method"]);
            Assert.True(result.ContainsKey("Content-Type"));
            Assert.Equal("application/merge-patch+json", result["Content-Type"]);
        }

        [Fact]
        public void GetAppendHeaders_WithTranslatedMethodPlaceholder_ReturnsCorrectValue()
        {
            // Arrange
            var customProperties = new Dictionary<string, object>
            {
                {"HttpMethodAppendHeaders", "PUT:X-Original-Method={ORIGINAL_METHOD},X-Translated-Method={TRANSLATED_METHOD}"}
            };

            // Act
            var result = HttpMethodHeaderAppender.GetAppendHeaders("PUT", "POST", customProperties);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal("PUT", result["X-Original-Method"]);
            Assert.Equal("POST", result["X-Translated-Method"]);
        }

        [Fact]
        public void IsValidHeaderName_WithValidNames_ReturnsTrue()
        {
            // Act & Assert
            Assert.True(HttpMethodHeaderAppender.IsValidHeaderName("X-HTTP-Method"));
            Assert.True(HttpMethodHeaderAppender.IsValidHeaderName("Content-Type"));
            Assert.True(HttpMethodHeaderAppender.IsValidHeaderName("Custom_Header"));
            Assert.True(HttpMethodHeaderAppender.IsValidHeaderName("X123"));
        }

        [Fact]
        public void IsValidHeaderName_WithInvalidNames_ReturnsFalse()
        {
            // Act & Assert
            Assert.False(HttpMethodHeaderAppender.IsValidHeaderName(""));
            Assert.False(HttpMethodHeaderAppender.IsValidHeaderName(null));
            Assert.False(HttpMethodHeaderAppender.IsValidHeaderName("Invalid Header"));
            Assert.False(HttpMethodHeaderAppender.IsValidHeaderName("Header:Value"));
        }
    }
}