using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace eShop.WebApp.Chatbot;

public static partial class MessageProcessor
{
    public static MarkupString AllowImages(string message)
    {
        // Process markdown to allow both images and hyperlinks for product navigation

        var result = new StringBuilder();
        var prevEnd = 0;
        message = message.Replace("&lt;", "<").Replace("&gt;", ">");

        foreach (Match match in FindMarkdownImages().Matches(message))
        {
            var contentToHere = message.Substring(prevEnd, match.Index - prevEnd);
            result.Append(HtmlEncoder.Default.Encode(contentToHere));
            
            var isMarkdownImage = match.Value.StartsWith('!');
            var labelText = match.Groups[1].Value;
            var targetUrl = match.Groups[2].Value;
            
            if (isMarkdownImage)
            {
                result.Append($"<img title=\"{(HtmlEncoder.Default.Encode(labelText))}\" src=\"{(HtmlEncoder.Default.Encode(targetUrl))}\" />");
            }
            else
            {
                result.Append($"<a href=\"{(HtmlEncoder.Default.Encode(targetUrl))}\" class=\"chat-product-link\">{(HtmlEncoder.Default.Encode(labelText))}</a>");
            }

            prevEnd = match.Index + match.Length;
        }
        result.Append(HtmlEncoder.Default.Encode(message.Substring(prevEnd)));

        return new MarkupString(result.ToString());
    }

    [GeneratedRegex(@"\!?\[([^\]]+)\]\s*\(([^\)]+)\)")]
    private static partial Regex FindMarkdownImages();
}
