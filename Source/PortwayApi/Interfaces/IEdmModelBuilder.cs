namespace PortwayApi.Interfaces;

using Microsoft.OData.Edm;
using DynamicODataToSQL;
using PortwayApi.Services.Providers;

/// <summary>Interface for building EDM models for OData queries</summary>
public interface IEdmModelBuilder
{
    IEdmModel GetEdmModel(string entityName);
    EdmModel BuildModel(string tableName);
    IEdmModel? ParseMetadata(string csdl);
}
