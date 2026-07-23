namespace PortwayApi.Helpers;

using System.ComponentModel.DataAnnotations;

/// <summary>Runs attribute and async object validation for web UI payloads, preserving the UI's error contract</summary>
public static class UiValidationHelper
{
    /// <summary>Returns the first validation error message, or null when the instance is valid</summary>
    public static async Task<string?> FirstErrorAsync(object instance, IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = new ValidationContext(instance, services, items: null);
        var results = new List<ValidationResult>();
        await Validator.TryValidateObjectAsync(instance, context, results, validateAllProperties: true, cancellationToken);
        return results.Count > 0 ? results[0].ErrorMessage : null;
    }
}
