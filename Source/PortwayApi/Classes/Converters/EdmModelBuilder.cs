using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using PortwayApi.Interfaces;
using Serilog;
using System.Xml;

namespace PortwayApi.Classes;

/// <summary>
/// Implements the IEdmModelBuilder interface to provide EDM model building for OData to SQL queries
/// </summary>
public class EdmModelBuilder : IEdmModelBuilder
{
    private readonly Dictionary<string, IEdmModel> _modelCache 
        = new Dictionary<string, IEdmModel>(StringComparer.OrdinalIgnoreCase);

    public IEdmModel GetEdmModel(string entityName)
    {
        Log.Debug("🔧 Building EDM model for entity: {EntityName}", entityName);
        
        // Check if we already have the model in cache
        if (_modelCache.TryGetValue(entityName, out var cachedModel))
        {
            Log.Debug("✅ Using cached EDM model for entity: {EntityName}", entityName);
            return cachedModel;
        }

        // Create a new model
        var model = BuildModel(entityName);
        
        // Cache the model
        _modelCache[entityName] = model;
        
        Log.Debug("✅ Successfully built and cached EDM model for: {EntityName}", entityName);
        return model;
    }
    
    public EdmModel BuildModel(string tableName)
    {
        // Create a new model - all properties will be handled dynamically at query time
        var model = new EdmModel();
        
        // Extract schema and table name
        var parts = tableName.Split('.');
        string schema = "dbo";
        string table = tableName;
        
        if (parts.Length > 1)
        {
            schema = parts[0].Replace("[", "").Replace("]", "");
            table = parts[1].Replace("[", "").Replace("]", "");
        }
        else
        {
            table = table.Replace("[", "").Replace("]", "");
        }
        
        // Create a namespace for the EDM model
        var edmNamespace = $"PortwayApi.Data.{schema}";
        
        // Create a dynamic entity type with no predefined properties
        var type = new EdmEntityType(edmNamespace, table);
        model.AddElement(type);
        
        // Add a default key property (ID) for OData functionality - actual keys are determined at query time
        var idProperty = type.AddStructuralProperty(
            "ID", 
            EdmPrimitiveTypeKind.Int32, 
            false);
        type.AddKeys(idProperty);
        
        // Create a dynamic entity container
        var container = new EdmEntityContainer(edmNamespace, "DefaultContainer");
        model.AddElement(container);
        
        // Create an entity set
        var set = container.AddEntitySet(table, type);
        
        return model;
    }
    
    /// <summary>
    /// Parses an EDM model from CSDL 
    /// </summary>
    public IEdmModel? ParseMetadata(string csdl)
    {
        try
        {
            // Parse CSDL XML to EDM model
            IEnumerable<EdmError> errors;
            IEdmModel? edmModel;
            
            if (CsdlReader.TryParse(XmlReader.Create(new StringReader(csdl)), out edmModel, out errors))
            {
                return edmModel;
            }
            
            // Log parsing errors
            foreach (var error in errors)
            {
                Log.Error("❌ EDM parsing error: {ErrorMessage}", error.ErrorMessage);
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Failed to parse CSDL metadata");
            return null;
        }
    }
}