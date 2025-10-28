namespace PortwayApi.Classes;

/// <summary>
/// Execution context to maintain state between composite step executions
/// </summary>
public class ExecutionContext
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, object> Variables { get; set; } = new();
    
    public void SetVariable(string name, object value)
    {
        Variables[name] = value;
    }
    
    public T? GetVariable<T>(string name)
    {
        if (Variables.TryGetValue(name, out var value))
        {
            if (value is T typedValue)
            {
                return typedValue;
            }
            
            try
            {
                // Try to convert if direct cast fails
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        
        return default;
    }
}