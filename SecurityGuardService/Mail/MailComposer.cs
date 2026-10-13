using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using MimeKit;

namespace SecurityGuardService.Mail
{
    /// <summary>İmza resmi (DB'deki MailImages kaydı).</summary>
    internal sealed class MailImage
    {
        public string ContentId;
        public string FileName;
        public string MimeType;
        public byte[] Data;
    }

    /// <summary>
    /// Mail içeriğini oluşturur: HTML gövde + imza, imzadaki resimler maile gömülü (cid:),
    /// HTML gösteremeyen programlar için düz metin alternatifi.
    /// </summary>
    internal static class MailComposer
    {
        public static MimeMessage Build(SmtpConfig cfg, IEnumerable<string> to, string subject, string bodyHtml,
            string signatureHtml, IList<MailImage> images)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(cfg.FromName) ? "SecurityGuard" : cfg.FromName, cfg.FromAddress));
            foreach (var address in to)
                message.To.Add(MailboxAddress.Parse(address));
            message.Subject = subject;

            string html = Wrap(bodyHtml, signatureHtml);
            var builder = new BodyBuilder();

            // Sadece HTML'de kullanılan resimler eklenir
            if (images != null)
            {
                foreach (var img in images)
                {
                    if (html.IndexOf("cid:" + img.ContentId, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var resource = builder.LinkedResources.Add(img.FileName, img.Data, ContentType.Parse(img.MimeType));
                    resource.ContentId = img.ContentId;
                }
            }

            builder.HtmlBody = html;
            builder.TextBody = HtmlToText(html);
            message.Body = builder.ToMessageBody();
            return message;
        }

        private static string Wrap(string bodyHtml, string signatureHtml)
        {
            return "<html><head><meta charset=\"utf-8\"></head>" +
                   "<body style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;color:#222222\">" +
                   (bodyHtml ?? "") +
                   (string.IsNullOrWhiteSpace(signatureHtml) ? "" : "<br><br>" + signatureHtml) +
                   "</body></html>";
        }

        /// <summary>Basit HTML → düz metin (satır sonları korunur, etiketler atılır).</summary>
        public static string HtmlToText(string html)
        {
            // CultureInvariant şart: Türkçe kültürde "<LI>", "<DIV>" gibi büyük harfli etiketler eşleşmez (I → ı)
            const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            string text = Regex.Replace(html ?? "", @"<(script|style)[^>]*>.*?</\1>", "", Options | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<br\s*/?>|</p>|</div>|</tr>|</h\d>|</li>", "\n", Options);
            text = Regex.Replace(text, @"<[^>]+>", "");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }
    }
}
