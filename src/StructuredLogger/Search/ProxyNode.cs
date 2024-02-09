﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StructuredLogViewer;

namespace Microsoft.Build.Logging.StructuredLogger
{
    public class ProxyNode : TextNode
    {
        private BaseNode original;
        public BaseNode Original
        {
            get => original;
            set
            {
                if (original == value)
                {
                    return;
                }

                original = value;
                if (original != null)
                {
                    if (Text == null)
                    {
                        Text = GetNodeText(original);
                    }
                }
            }
        }

        public SearchResult SearchResult { get; set; }

        private List<object> highlights;
        public List<object> Highlights
        {
            get
            {
                if (highlights == null)
                {
                    highlights = new List<object>();
                    Populate(SearchResult);
                }

                return highlights;
            }
        }

        public static string GetNodeText(BaseNode node)
        {
            if (node == null)
            {
                return null;
            }

            if (node is NamedNode namedNode)
            {
                if (node is Project project)
                {
                    return $"{project.Name} {project.AdornmentString} {project.TargetsDisplayText}";
                }
                else if (node is ProjectEvaluation evaluation)
                {
                    return $"{evaluation.Name} {evaluation.AdornmentString} {evaluation.EvaluationText}";
                }

                return namedNode.Name;
            }
            else if (node is TextNode textNode)
            {
                return textNode.Text;
            }

            return node.Title;
        }

        public void Populate(SearchResult result)
        {
            if (result == null)
            {
                Highlights.Add(Text);
                return;
            }

            var node = result.Node;

            if (result.WordsInFields.Count == 0)
            {
                if (result.MatchedByType)
                {
                    Highlights.Add(new HighlightedText { Text = OriginalType });
                }

                Highlights.Add((Highlights.Count > 0 ? " " : "") + TextUtilities.ShortenValue(GetNodeText(node), "..."));

                AddDuration(result);

                return;
            }

            string typePrefix = OriginalType;
            bool addedTypePrefix = false;
            if (typePrefix != Strings.Folder &&
                typePrefix != Strings.Item &&
                typePrefix != Strings.Metadata &&
                typePrefix != Strings.Property &&
                typePrefix != "Package")
            {
                Highlights.Add(typePrefix);
                addedTypePrefix = true;
            }

            // NameValueNode is special case: have to show name=value when searched only in one (name or value)
            var nameValueNode = node as NameValueNode;
            var namedNode = node as NamedNode;

            bool nameFound = false;
            bool valueFound = false;
            bool namedNodeNameFound = false;

            foreach (var fieldText in result.WordsInFields)
            {
                if (nameValueNode != null)
                {
                    if (!nameFound && fieldText.field.Equals(nameValueNode.Name))
                    {
                        nameFound = true;
                    }

                    if (!valueFound && fieldText.field.Equals(nameValueNode.Value))
                    {
                        valueFound = true;
                    }
                }
                else if (namedNode != null && !namedNodeNameFound)
                {
                    if (fieldText.field.Equals(namedNode.Name))
                    {
                        namedNodeNameFound = true;
                    }
                }
            }

            if (namedNode != null && !namedNodeNameFound)
            {
                Highlights.Add((Highlights.Count > 0 ? " " : "") + namedNode.Name);
                if (GetNodeDifferentiator(node) is object differentiator)
                {
                    Highlights.Add(differentiator);
                }
            }

            IEnumerable<(string Key, IEnumerable<string> Occurrences)> fieldsWithMatches = null;
            if (result.FieldsToDisplay != null)
            {
                fieldsWithMatches = result.FieldsToDisplay.Select(f =>
                {
                    List<string> matches = null;

                    foreach (var kvp in result.WordsInFields)
                    {
                        if (kvp.field == f)
                        {
                            matches ??= new();
                            matches.Add(kvp.match);
                        }
                    }

                    return (f, (IEnumerable<string>)matches);
                });
            }
            else
            {
                fieldsWithMatches = result.WordsInFields
                    .GroupBy(t => t.field, t => t.match)
                    .Select(g => (g.Key, (IEnumerable<string>)g));
            }

            foreach (var wordsInField in fieldsWithMatches)
            {
                var fieldText = wordsInField.Key;
                if (fieldText == typePrefix && addedTypePrefix)
                {
                    // already added above
                    continue;
                }

                if (Highlights.Count > 0)
                {
                    Highlights.Add(" ");
                }

                if (nameValueNode != null && fieldText.Equals(nameValueNode.Value) && !nameFound)
                {
                    Highlights.Add(nameValueNode.Name + " = ");
                }

                fieldText = TextUtilities.ShortenValue(fieldText, "...");

                var highlightSpans = TextUtilities.GetHighlightedSpansInText(fieldText, wordsInField.Occurrences);
                int index = 0;
                foreach (var span in highlightSpans)
                {
                    if (span.Start > index)
                    {
                        Highlights.Add(fieldText.Substring(index, span.Start - index));
                    }

                    Highlights.Add(new HighlightedText { Text = fieldText.Substring(span.Start, span.Length) });
                    index = span.End;
                }

                if (index < fieldText.Length)
                {
                    Highlights.Add(fieldText.Substring(index, fieldText.Length - index));
                }

                if (nameValueNode != null && fieldText.Equals(nameValueNode.Name))
                {
                    if (!valueFound)
                    {
                        Highlights.Add(" = " + TextUtilities.ShortenValue(nameValueNode.Value, "..."));
                    }
                    else
                    {
                        Highlights.Add(" = ");
                    }
                }

                if (namedNode != null && namedNode.Name == fieldText)
                {
                    if (GetNodeDifferentiator(node) is object differentiator)
                    {
                        Highlights.Add(differentiator);
                    }
                }
            }

            AddDuration(result);

            if (Highlights.Count == 0)
            {
                if (Original is Target or Task or AddItem or RemoveItem)
                {
                    Highlights.Add(OriginalType + " ");
                }

                Highlights.Add(Title);
            }
        }

        private object GetNodeDifferentiator(BaseNode node)
        {
            if (node is Project project)
            {
                var result = "";

                if (!string.IsNullOrEmpty(project.AdornmentString))
                {
                    result += " " + project.AdornmentString;
                }

                if (!string.IsNullOrEmpty(project.TargetsDisplayText))
                {
                    result += " " + project.TargetsDisplayText;
                }

                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }
            }

            return null;
        }

        private void AddDuration(SearchResult result)
        {
            if (result.StartTime != default)
            {
                Highlights.Add(new HighlightedText { Text = " " + TextUtilities.Display(result.StartTime), Style = "time" });
            }

            if (result.EndTime != default)
            {
                Highlights.Add(new HighlightedText { Text = " " + TextUtilities.Display(result.EndTime), Style = "time" });
            }

            if (result.Duration != default)
            {
                Highlights.Add(new HighlightedText { Text = " " + TextUtilities.DisplayDuration(result.Duration), Style = "time" });
            }
        }

        public string OriginalType
        {
            get
            {
                if (Original is Task)
                {
                    return nameof(Task);
                }

                if (Original == null)
                {
                    return "Folder";
                }

                return Original.TypeName ?? Original.GetType().Name;
            }
        }

        public string ProjectExtension => GetProjectFileExtension();

        private string GetProjectFileExtension()
        {
            string result = null;

            if (Original is Project project)
            {
                result = string.IsNullOrEmpty(project.ProjectFileExtension) ? "other" : project.ProjectFileExtension;
            }
            else if (Original is ProjectEvaluation evaluation)
            {
                result = string.IsNullOrEmpty(evaluation.ProjectFileExtension) ? "other" : evaluation.ProjectFileExtension;
            }

            return result;
        }

        public override string TypeName => nameof(ProxyNode);

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var highlight in Highlights)
            {
                sb.Append(highlight.ToString());
            }

            return sb.ToString();
        }
    }
}
