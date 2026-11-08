using System.Text;

namespace SystemCockpit.Services;

internal static class DesktopScriptText
{
    internal static string Preamble(params string[] keys)
    {
        var script = new StringBuilder("$aetherDesktopText=@{\n");
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
            script.Append('\'').Append(Quote(key)).Append("'='")
                .Append(Quote(DesktopUiText.Get(key))).Append("'\n");
        return script.Append("}\n").ToString();
    }

    private static string Quote(string text) => text.Replace("'", "''", StringComparison.Ordinal);
}
