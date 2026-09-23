using System.Text.Json;

namespace WebMetroSR.Helpers
{
    public static class CommonHelpers
    {
        /// <summary>
        /// Проверяет, является ли строка корректным IPv4-адресом.
        /// </summary>
        public static bool IsValidIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            return !ip.Contains("Ошибка") && !ip.Contains("Не найден");
        }

        /// <summary>
        /// Извлекает JSON-массив или объект из текста PowerShell.
        /// </summary>
        public static string ExtractJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int start = text.IndexOf('[');
            if (start == -1) start = text.IndexOf('{');
            if (start == -1) return "";
            int end = text.LastIndexOf(']');
            if (end == -1) end = text.LastIndexOf('}');
            if (end == -1) return "";
            return text.Substring(start, end - start + 1);
        }

        /// <summary>
        /// Очищает вывод PowerShell от служебных строк и CLIXML.
        /// </summary>
        public static string CleanPowerShellOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return output;
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new List<string>();
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("#< CLIXML")) continue;
                if (line.Contains("System.Management.Automation.PSCustomObject")) continue;
                if (line.Contains("IsSingleton") || line.Contains("IsInstance") || line.Contains("IsClass") ||
                    line.Contains("ClassName") || line.Contains("Server")  ||
                    line.Contains("Path")) continue;
                cleaned.Add(line);
            }
            return string.Join(Environment.NewLine, cleaned);
        }
    }
}