using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sts2Extractor.Infrastructure;

internal static class CsvWriter
{
    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(JoinCsv(headers));

        foreach (IReadOnlyList<string> row in rows)
        {
            writer.WriteLine(JoinCsv(row));
        }
    }

    private static string JoinCsv(IReadOnlyList<string> values)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(values[i]));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        string safe = value;
        if (!safe.Contains('"') && !safe.Contains(',') && !safe.Contains('\n') && !safe.Contains('\r'))
        {
            return safe;
        }

        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
