namespace SecurityGuard.Shared
{
    /// <summary>
    /// Lisans sunucusu varsayılan adresi: YER TUTUCU (repoda gerçek adres tutulmaz).
    /// Gerçek adres için bu dosyanın yanına LicenseServer.Local.cs oluşturun (git'e girmez):
    ///   internal static class LicenseServer { public const string DefaultApiUrl = "https://lisans.firmaniz.com/"; }
    /// O dosya varsa bu dosya derlenmez (SecurityGuard.Shared.csproj).
    /// </summary>
    internal static class LicenseServer
    {
        public const string DefaultApiUrl = "http://localhost:5080/";
    }
}
