using System;
using System.Collections.Generic;
using System.Text;

namespace BuildForge.Editor.Core
{
    internal static class UnityYamlParser
    {
        public static Dictionary<string, string> ParseToPropertyMap(List<string> yamlLines)
        {
            var yaml = string.Join("\n", yamlLines);
            return ParseToPropertyMap(yaml);
        }

        public static Dictionary<string, string> ParseToPropertyMap(string yaml)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(yaml))
                return result;

            var lines = yaml.Split(new[] { '\n', '\r' }, StringSplitOptions.None);

            int baseIndent = 0;
            int startLine = 0;

            // Detect PlayerSettings root
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith("%") || trimmed.StartsWith("---"))
                    continue;

                if (trimmed == "PlayerSettings:" || trimmed.StartsWith("PlayerSettings:"))
                {
                    baseIndent = 2;
                    startLine = i + 1;
                }
                break;
            }

            string currentKey = null;
            var valueBuilder = new StringBuilder();

            for (int i = startLine; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int indent = 0;
                while (indent < line.Length && line[indent] == ' ')
                    indent++;

                // Unity writes block-sequence items at the SAME indent as the parent
                // key ("  m_BuildTargetIcons:" followed by "  - m_BuildTarget: iPhone"),
                // so a dash line at base indent belongs to the current key's value.
                bool isSequenceItem = indent < line.Length && line[indent] == '-';

                if (indent == baseIndent && !isSequenceItem)
                {
                    if (currentKey != null)
                        result[currentKey] = valueBuilder.ToString().TrimEnd('\n');

                    var colonIdx = line.IndexOf(':', baseIndent);
                    if (colonIdx < 0)
                        continue;

                    currentKey = line.Substring(baseIndent, colonIdx - baseIndent).Trim();
                    var afterColon = line.Substring(colonIdx + 1).Trim();

                    valueBuilder.Clear();
                    valueBuilder.Append(afterColon);
                }
                else if (indent >= baseIndent && currentKey != null)
                {
                    if (valueBuilder.Length > 0)
                        valueBuilder.Append('\n');
                    valueBuilder.Append(line);
                }
            }

            if (currentKey != null)
                result[currentKey] = valueBuilder.ToString().TrimEnd('\n');

            return result;
        }
    }
}
