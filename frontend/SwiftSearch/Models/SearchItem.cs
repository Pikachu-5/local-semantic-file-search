using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SwiftSearch.Models
{
    public class SearchItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ChunkText { get; set; } = string.Empty;
        public float RelevanceScore { get; set; }
        public string Query { get; set; } = string.Empty;

        public string FormattedScore
        {
            get
            {
                // LancDB cosine similarity can be mapped to percentages nicely
                float pct = RelevanceScore * 100f;
                if (pct < 0) pct = 0;
                if (pct > 100f) pct = 100f;
                return $"{pct:F1}% Match";
            }
        }

        public string FileIcon
        {
            get
            {
                string ext = Path.GetExtension(FilePath).ToLowerInvariant();
                switch (ext)
                {
                    case ".txt":
                        return "\uE8A5"; // Document
                    case ".pdf":
                        return "\uEA90"; // PDF
                    case ".py":
                    case ".ipynb":
                    case ".js":
                    case ".ts":
                    case ".cs":
                    case ".cpp":
                    case ".h":
                    case ".html":
                    case ".css":
                        return "\uE943"; // Code / DeveloperTools
                    case ".md":
                        return "\uEC50"; // Markdown/Page
                    case ".json":
                    case ".yaml":
                    case ".yml":
                    case ".xml":
                        return "\uE943"; // Code
                    case ".doc":
                    case ".docx":
                        return "\uE1A5"; // Document
                    case ".xls":
                    case ".xlsx":
                        return "\uEC06"; // Spreadsheet
                    case ".ppt":
                    case ".pptx":
                        return "\uE1A5"; // PowerPoint
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".gif":
                    case ".bmp":
                        return "\uEB9F"; // Photo / Image
                    default:
                        return "\uE1A5"; // Generic File
                }
            }
        }

        public Brush FileIconColor
        {
            get
            {
                string ext = Path.GetExtension(FilePath).ToLowerInvariant();
                Color color;
                switch (ext)
                {
                    case ".pdf":
                        color = Color.FromArgb(255, 208, 74, 2); // Orange/Red
                        break;
                    case ".txt":
                        color = Color.FromArgb(255, 0, 120, 212); // Windows Blue
                        break;
                    case ".py":
                    case ".ipynb":
                        color = Color.FromArgb(255, 16, 124, 65); // Python Green
                        break;
                    case ".md":
                        color = Color.FromArgb(255, 122, 36, 183); // Purple
                        break;
                    case ".json":
                    case ".yaml":
                    case ".yml":
                    case ".xml":
                        color = Color.FromArgb(255, 196, 89, 17); // Amber/Orange
                        break;
                    case ".doc":
                    case ".docx":
                        color = Color.FromArgb(255, 24, 90, 189); // Word Blue
                        break;
                    case ".xls":
                    case ".xlsx":
                        color = Color.FromArgb(255, 16, 124, 65); // Excel Green
                        break;
                    case ".ppt":
                    case ".pptx":
                        color = Color.FromArgb(255, 216, 59, 1); // PowerPoint Red
                        break;
                    default:
                        color = Color.FromArgb(255, 115, 115, 115); // Slate Gray
                        break;
                }
                return new SolidColorBrush(color);
            }
        }
    }
}
