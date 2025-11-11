using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using Serilog;

namespace PortwayApi.Helpers;

/// <summary>
/// Helper class for XML operations to support OData metadata processing
/// </summary>
public static class XmlHelper
{
    /// <summary>
    /// Creates an XmlReader from a string containing XML
    /// </summary>
    public static XmlReader CreateReader(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Prohibit // For security
            };

            return XmlReader.Create(new StringReader(xml), settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating XML reader from string");
            throw;
        }
    }

    /// <summary>
    /// Parses a string into an XDocument
    /// </summary>
    public static XDocument ParseXml(string xml)
    {
        try
        {
            return XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing XML string to XDocument");
            throw;
        }
    }

    /// <summary>
    /// Extracts namespace information from XML document
    /// </summary>
    public static Dictionary<string, string> ExtractNamespaces(XDocument doc)
    {
        try
        {
            var result = new Dictionary<string, string>();

            // Get all namespace declarations
            var namespaces = doc.Root?
                .Attributes()
                .Where(a => a.IsNamespaceDeclaration)
                .GroupBy(a => a.Name.LocalName)
                .ToDictionary(g => g.Key, g => g.First().Value);

            if (namespaces != null)
            {
                foreach (var ns in namespaces)
                {
                    string prefix = string.IsNullOrEmpty(ns.Key) ? "xmlns" : ns.Key;
                    result[prefix] = ns.Value;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error extracting namespaces from XML document");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Creates an XmlNamespaceManager with the namespaces from an XDocument
    /// </summary>
    public static XmlNamespaceManager CreateNamespaceManager(XDocument doc)
    {
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        var namespaces = ExtractNamespaces(doc);

        foreach (var ns in namespaces)
        {
            namespaceManager.AddNamespace(ns.Key, ns.Value);
        }

        return namespaceManager;
    }
}