using System.Reflection;

namespace Microsoft.DurableTask;

public static class DurableTaskUserAgentUtil
{
    static string SdkName => "durabletask-dotnet";

    public static string GetUserAgent()
    {
        var version = typeof(DurableTaskUserAgentUtil).Assembly.GetName().Version;
        return $"{SdkName}/{version?.ToString() ?? "unknown"}";
    }
}