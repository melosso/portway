using Xunit;
using PortwayApi.Classes.Helpers;
using System.Collections.Generic;

namespace PortwayApi.Tests.Helpers;

public class ColumnMappingHelperTests
{
    [Fact]
    public void ParseColumnMappings_ValidMappings_ReturnsCorrectDictionaries()
    {
        // Arrange
        var allowedColumns = new List<string>
        {
            "ItemCode;ProductNumber",
            "Description;Description", 
            "Assortment;AssortmentID",
            "sysguid;InternalID"
        };

        // Act
        var (aliasToDatabase, databaseToAlias) = ColumnMappingHelper.ParseColumnMappings(allowedColumns);

        // Assert
        Assert.Equal(4, aliasToDatabase.Count);
        Assert.Equal(4, databaseToAlias.Count);
        
        Assert.Equal("ItemCode", aliasToDatabase["ProductNumber"]);
        Assert.Equal("Description", aliasToDatabase["Description"]);
        Assert.Equal("Assortment", aliasToDatabase["AssortmentID"]);
        Assert.Equal("sysguid", aliasToDatabase["InternalID"]);
        
        Assert.Equal("ProductNumber", databaseToAlias["ItemCode"]);
        Assert.Equal("Description", databaseToAlias["Description"]);
        Assert.Equal("AssortmentID", databaseToAlias["Assortment"]);
        Assert.Equal("InternalID", databaseToAlias["sysguid"]);
    }

    [Fact]
    public void ParseColumnMappings_NoSemicolon_UsesSameNameForBoth()
    {
        // Arrange
        var allowedColumns = new List<string> { "ItemCode", "Description" };

        // Act
        var (aliasToDatabase, databaseToAlias) = ColumnMappingHelper.ParseColumnMappings(allowedColumns);

        // Assert
        Assert.Equal(2, aliasToDatabase.Count);
        Assert.Equal("ItemCode", aliasToDatabase["ItemCode"]);
        Assert.Equal("Description", aliasToDatabase["Description"]);
        Assert.Equal("ItemCode", databaseToAlias["ItemCode"]);
        Assert.Equal("Description", databaseToAlias["Description"]);
    }

    [Fact]
    public void ParseColumnMappings_MalformedEntries_HandlesGracefully()
    {
        // Arrange
        var allowedColumns = new List<string>
        {
            "ItemCode;ProductNumber",    // Valid
            "Description",               // No semicolon - fallback
            "Assortment;",              // Empty alias - fallback
            ";InternalID",              // Empty database column - fallback
            "Field1;Field2;Field3",     // Multiple semicolons - use first part
            "",                         // Empty string - skip
            "   ",                      // Whitespace only - skip
            ";",                        // Only semicolon - skip (this was causing IndexOutOfRangeException)
            ";;;",                      // Multiple semicolons only - skip
        };

        // Act
        var (aliasToDatabase, databaseToAlias) = ColumnMappingHelper.ParseColumnMappings(allowedColumns);

        // Assert
        Assert.Equal(5, aliasToDatabase.Count); // Should have 5 valid mappings
        
        // Valid mapping
        Assert.Equal("ItemCode", aliasToDatabase["ProductNumber"]);
        
        // Fallback cases
        Assert.Equal("Description", aliasToDatabase["Description"]);
        Assert.Equal("Assortment", aliasToDatabase["Assortment"]);
        Assert.Equal("InternalID", aliasToDatabase["InternalID"]);
        Assert.Equal("Field1", aliasToDatabase["Field1"]);
    }

    [Fact]
    public void ParseColumnMappings_EdgeCases_HandlesGracefully()
    {
        // Arrange - Test specific edge cases that could cause IndexOutOfRangeException
        var allowedColumns = new List<string>
        {
            ";",                        // Just semicolon
            ";;",                       // Double semicolon
            ";;;",                      // Triple semicolon
            " ; ",                      // Semicolon with spaces
            "ValidColumn",              // Valid single column
            "Database;Alias"            // Valid mapping
        };

        // Act & Assert - Should not throw any exceptions
        var (aliasToDatabase, databaseToAlias) = ColumnMappingHelper.ParseColumnMappings(allowedColumns);
        
        // Should only have the valid entries
        Assert.Equal(2, aliasToDatabase.Count);
        Assert.Equal("ValidColumn", aliasToDatabase["ValidColumn"]);
        Assert.Equal("Database", aliasToDatabase["Alias"]);
    }

    [Fact]
    public void ConvertAliasesToDatabaseColumns_ValidAliases_ReturnsCorrectColumns()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode",
            ["Description"] = "Description",
            ["AssortmentID"] = "Assortment"
        };
        var aliasColumns = "ProductNumber,Description,AssortmentID";

        // Act
        var result = ColumnMappingHelper.ConvertAliasesToDatabaseColumns(aliasColumns, aliasToDatabase);

        // Assert
        Assert.Equal("ItemCode,Description,Assortment", result);
    }

    [Fact]
    public void ConvertAliasesToDatabaseColumns_UnknownAlias_UsesAsIs()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode"
        };
        var aliasColumns = "ProductNumber,UnknownColumn";

        // Act
        var result = ColumnMappingHelper.ConvertAliasesToDatabaseColumns(aliasColumns, aliasToDatabase);

        // Assert
        Assert.Equal("ItemCode,UnknownColumn", result);
    }

    [Fact]
    public void ValidateAliasColumns_ValidAliases_ReturnsTrue()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode",
            ["Description"] = "Description"
        };
        var requestedAliases = "ProductNumber,Description";

        // Act
        var (isValid, invalidAliases) = ColumnMappingHelper.ValidateAliasColumns(requestedAliases, aliasToDatabase);

        // Assert
        Assert.True(isValid);
        Assert.Empty(invalidAliases);
    }

    [Fact]
    public void ValidateAliasColumns_InvalidAliases_ReturnsFalse()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode"
        };
        var requestedAliases = "ProductNumber,InvalidColumn,AnotherInvalid";

        // Act
        var (isValid, invalidAliases) = ColumnMappingHelper.ValidateAliasColumns(requestedAliases, aliasToDatabase);

        // Assert
        Assert.False(isValid);
        Assert.Equal(2, invalidAliases.Count);
        Assert.Contains("InvalidColumn", invalidAliases);
        Assert.Contains("AnotherInvalid", invalidAliases);
    }

    [Fact]
    public void ConvertODataFilterAliases_SimpleFilter_ConvertsCorrectly()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode",
            ["AssortmentID"] = "Assortment"
        };
        var filter = "ProductNumber eq 'PROD001' and AssortmentID eq 'Electronics'";

        // Act
        var result = ColumnMappingHelper.ConvertODataFilterAliases(filter, aliasToDatabase);

        // Assert
        Assert.Equal("ItemCode eq 'PROD001' and Assortment eq 'Electronics'", result);
    }

    [Fact]
    public void ConvertODataFilterAliases_ComplexFilter_ConvertsCorrectly()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode",
            ["AssortmentID"] = "Assortment"
        };
        var filter = "contains(ProductNumber,'PROD') and (AssortmentID eq 'Electronics' or AssortmentID eq 'Books')";

        // Act
        var result = ColumnMappingHelper.ConvertODataFilterAliases(filter, aliasToDatabase);

        // Assert
        Assert.Equal("contains(ItemCode,'PROD') and (Assortment eq 'Electronics' or Assortment eq 'Books')", result);
    }

    [Fact]
    public void ConvertODataOrderByAliases_SimpleOrderBy_ConvertsCorrectly()
    {
        // Arrange
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["ProductNumber"] = "ItemCode",
            ["AssortmentID"] = "Assortment"
        };
        var orderBy = "ProductNumber desc, AssortmentID asc";

        // Act
        var result = ColumnMappingHelper.ConvertODataOrderByAliases(orderBy, aliasToDatabase);

        // Assert
        Assert.Equal("ItemCode desc, Assortment asc", result);
    }

    [Fact]
    public void ConvertODataFilterAliases_PartialMatch_DoesNotConvert()
    {
        // Arrange - test that partial matches are not converted (word boundary protection)
        var aliasToDatabase = new Dictionary<string, string>
        {
            ["Code"] = "ItemCode"
        };
        var filter = "ProductCode eq 'PROD001'"; // Should NOT convert "Code" in "ProductCode"

        // Act
        var result = ColumnMappingHelper.ConvertODataFilterAliases(filter, aliasToDatabase);

        // Assert - Should remain unchanged because "Code" is part of "ProductCode"
        Assert.Equal("ProductCode eq 'PROD001'", result);
    }
}
