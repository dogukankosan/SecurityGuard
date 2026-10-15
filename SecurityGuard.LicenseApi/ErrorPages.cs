namespace SecurityGuard.LicenseApi;

/// <summary>
/// Özel 404/500 HTML sayfaları gömülü (dosya/klasör bağımlılığı yok; in-process'te UseStatusCodePages ile servis edilir).
/// </summary>
internal static class ErrorPages
{
    public static string Html(int code)
    {
        var (title, message, accent, extraIcon) = code switch
        {
            404 => ("Sayfa bulunamadı", "Aradığınız sayfa taşınmış veya hiç var olmamış olabilir.", "#4f8cff", ""),
            _   => ("Sunucu hatası", "Beklenmeyen bir hata oluştu. Lütfen biraz sonra tekrar deneyin.", "#ff6b6b",
                    "<line x1='12' y1='9' x2='12' y2='13'/><line x1='12' y1='16' x2='12.01' y2='16'/>"),
        };

        return $@"<!doctype html>
<html lang=""tr"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>{title} — SecurityGuard</title>
<style>
  :root {{ color-scheme: dark; }}
  * {{ box-sizing: border-box; }}
  body {{
    margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
    background: #0f1420; color: #e6ebf5;
    font-family: ""Segoe UI"", system-ui, -apple-system, Roboto, Arial, sans-serif; padding: 24px;
  }}
  .card {{
    max-width: 440px; width: 100%; text-align: center;
    background: #161d2e; border: 1px solid #243049; border-radius: 16px;
    padding: 40px 32px; box-shadow: 0 20px 60px rgba(0,0,0,.45);
  }}
  .shield {{ width: 64px; height: 64px; margin: 0 auto 20px; color: {accent}; }}
  .code {{ font-size: 64px; font-weight: 800; letter-spacing: -2px; margin: 0; color: {accent}; line-height: 1; }}
  h1 {{ font-size: 20px; margin: 14px 0 8px; font-weight: 600; }}
  p {{ margin: 0 0 24px; color: #9aa7bd; font-size: 15px; line-height: 1.5; }}
  a {{
    display: inline-block; text-decoration: none; background: #4f8cff; color: #fff;
    padding: 11px 22px; border-radius: 10px; font-weight: 600; font-size: 14px; transition: background .15s;
  }}
  a:hover {{ background: #3d78e8; }}
  .foot {{ margin-top: 22px; font-size: 12px; color: #5c6a85; }}
</style>
</head>
<body>
  <div class=""card"">
    <svg class=""shield"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""1.8"" stroke-linecap=""round"" stroke-linejoin=""round"">
      <path d=""M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z""/>{extraIcon}
    </svg>
    <p class=""code"">{code}</p>
    <h1>{title}</h1>
    <p>{message}</p>
    <a href=""/"">Panele dön</a>
    <div class=""foot"">SecurityGuard</div>
  </div>
</body>
</html>";
    }
}
