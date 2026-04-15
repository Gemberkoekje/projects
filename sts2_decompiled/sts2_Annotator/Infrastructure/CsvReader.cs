using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sts2Extractor.Infrastructure;

internal static class CsvReader
{
    public static List<Dictionary<string, string>> ReadRows(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CSV file not found.", path);
        }

        List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
        using StreamReader reader = new StreamReader(path, Encoding.UTF8);

        string headerLine = reader.ReadLine() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return rows;
        }

        List<string> headers = ParseLine(headerLine);
        while (!reader.EndOfStream)
        {
            string line = reader.ReadLine() ?? string.Empty;
            if (line.Length == 0)
            {
                continue;
            }

            while (NeedsMoreInput(line) && !reader.EndOfStream)
            {
                string next = reader.ReadLine() ?? string.Empty;
                line = line + "\n" + next;
            }

            List<string> values = ParseLine(line);
            Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                string value = i < values.Count ? values[i] : string.Empty;
                row[headers[i]] = value;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static bool NeedsMoreInput(string line)
    {
        int quoteCount = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoteCount++;
            }
        }

        return quoteCount % 2 != 0;
    }

    private static List<string> ParseLine(string line)
    {
        List<string> values = new List<string>();
        StringBuilder current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        values.Add(current.ToString());
        return values;
    }
}
