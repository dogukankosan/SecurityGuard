using System;
using System.IO;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using SecurityGuard.Shared.Enums;

namespace SecurityGuardService.Mail
{
    /// <summary>SMTP bağlantı bilgisi (şifre çözülmüş halde, sadece bellekte).</summary>
    internal sealed class SmtpConfig
    {
        public string Host;
        public int Port;
        public MailSecurity Security;
        public string UserName;
        public string Password;
        public string FromAddress;
        public string FromName;
    }

    /// <summary>MailKit ile SMTP bağlantısı ve hataların Türkçe açıklaması.</summary>
    internal static class SmtpSender
    {
        private const int TimeoutMs = 30000;

        /// <summary>Bağlanır ve (kullanıcı adı varsa) kimlik doğrular. Çağıran Dispose etmeli.</summary>
        public static SmtpClient Connect(SmtpConfig cfg)
        {
            var client = new SmtpClient { Timeout = TimeoutMs };
            try
            {
                client.Connect(cfg.Host, cfg.Port, Map(cfg.Security));
                if (!string.IsNullOrEmpty(cfg.UserName))
                    client.Authenticate(cfg.UserName, cfg.Password ?? "");
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public static void Disconnect(SmtpClient client)
        {
            try
            {
                if (client.IsConnected) client.Disconnect(true);
            }
            catch
            {
                // kapanış hatası önemsiz
            }
            client.Dispose();
        }

        private static SecureSocketOptions Map(MailSecurity security)
        {
            switch (security)
            {
                case MailSecurity.SslOnConnect: return SecureSocketOptions.SslOnConnect;
                case MailSecurity.StartTls: return SecureSocketOptions.StartTls;
                case MailSecurity.None: return SecureSocketOptions.None;
                default: return SecureSocketOptions.Auto;
            }
        }

        /// <summary>Gönderim hatasını kullanıcının anlayacağı dile çevirir (şifre içermez).</summary>
        public static string Explain(Exception ex)
        {
            if (ex is AuthenticationException)
                return "Kullanıcı adı veya şifre SMTP sunucusu tarafından kabul edilmedi. (Gmail / Office 365 kullanıyorsanız normal şifre yerine \"uygulama şifresi\" gerekir.)";
            if (ex is ServiceNotAuthenticatedException)
                return "SMTP sunucusu kimlik doğrulaması istiyor. Kullanıcı adı ve şifre girin.";
            if (ex is SslHandshakeException)
                return "Güvenli bağlantı (SSL/TLS) kurulamadı. Güvenlik ayarını ve portu kontrol edin (465 → SSL, 587 → STARTTLS). Sunucu sertifikası geçersiz de olabilir.";
            if (ex is SocketException)
                return "SMTP sunucusuna bağlanılamadı. Sunucu adı / port yanlış olabilir veya firewall engelliyor. (" + ex.Message + ")";
            var cmd = ex as SmtpCommandException;
            if (cmd != null)
                return "SMTP sunucusu isteği reddetti (" + (int)cmd.StatusCode + "): " + cmd.Message;
            if (ex is SmtpProtocolException)
                return "SMTP sunucusuyla iletişim hatası: " + ex.Message;
            if (ex is TimeoutException || ex is IOException || ex is OperationCanceledException)
                return "SMTP sunucusu zamanında cevap vermedi veya bağlantı koptu.";
            return ex.Message;
        }
    }
}
