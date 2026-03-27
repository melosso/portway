namespace PortwayApi.Services.Mcp;

public static class EndpointExplorerHtml
{
    public static string Generate(IEnumerable<EndpointMcpInfo> endpoints)
    {
        var endpointsList = endpoints.Select(e =>
            $"{{name:\"{EscapeJs(e.Name)}\",ns:\"{EscapeJs(e.Namespace ?? "")}\",url:\"{EscapeJs(e.Url)}\"," +
            $"methods:[{string.Join(",", e.Methods.Select(m => $"\"{EscapeJs(m)}\""))}]}}"
        ).ToList();
        var endpointJson = string.Join(",", endpointsList);

        return string.Format(HtmlTemplate, endpointJson);
    }

    private static string EscapeJs(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private const string HtmlTemplate = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Portway Endpoint Explorer</title>
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; padding: 20px; line-height: 1.5; }
                .container { max-width: 800px; margin: 0 auto; }
                h1 { color: #1a1a2e; margin-bottom: 8px; }
                .subtitle { color: #666; margin-bottom: 24px; }
                .endpoint-list { display: flex; flex-direction: column; gap: 12px; }
                .endpoint-card { background: white; border-radius: 8px; padding: 16px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); cursor: pointer; transition: box-shadow 0.2s; }
                .endpoint-card:hover { box-shadow: 0 4px 12px rgba(0,0,0,0.15); }
                .endpoint-header { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; flex-wrap: wrap; }
                .method { padding: 2px 8px; border-radius: 4px; font-size: 12px; font-weight: 600; }
                .method.GET { background: #e3f2fd; color: #1565c0; }
                .method.POST { background: #f3e5f5; color: #7b1fa2; }
                .method.PUT { background: #fff3e0; color: #e65100; }
                .method.DELETE { background: #ffebee; color: #c62828; }
                .endpoint-name { font-weight: 600; color: #333; }
                .endpoint-ns { color: #888; font-size: 14px; }
                .endpoint-url { color: #666; font-size: 13px; word-break: break-all; }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>Portway Endpoint Explorer</h1>
                <p class="subtitle">Browse and interact with available endpoints</p>
                <div class="endpoint-list" id="endpoints"></div>
            </div>
            <script>
                var endpoints = [{0}];
                var container = document.getElementById('endpoints');
                for (var i = 0; i < endpoints.length; i++) {{
                    var ep = endpoints[i];
                    var card = document.createElement('div');
                    card.className = 'endpoint-card';
                    var methodsHtml = '';
                    for (var j = 0; j < ep.methods.length; j++) {{
                        methodsHtml += '<span class="method ' + ep.methods[j] + '">' + ep.methods[j] + '</span>';
                    }}
                    var nsHtml = ep.ns ? '<span class="endpoint-ns">(' + ep.ns + ')</span>' : '';
                    card.innerHTML = '<div class="endpoint-header">' + methodsHtml + '<span class="endpoint-name">' + ep.name + '</span>' + nsHtml + '</div><div class="endpoint-url">' + ep.url + '</div>';
                    var epData = ep;
                    card.onclick = (function(e) {{
                        return function() {{ window.parent.postMessage({{ type: 'mcp-call-tool', tool: e.name, namespace: e.ns }}, '*'); }};
                    }})(epData);
                    container.appendChild(card);
                }}
            </script>
        </body>
        </html>
        """;
}
