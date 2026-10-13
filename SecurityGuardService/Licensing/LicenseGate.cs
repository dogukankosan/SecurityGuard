namespace SecurityGuardService.Licensing
{
    /// <summary>
    /// Koruma çalışsın mı? Lisans yoksa / bittiyse / iptal edildiyse false: olay kaydı, ban, rapor, yedek, bakım, tarama,
    /// servis takibi durur (mevcut ban kuralları firewall'da kalır). Arayüzde sadece giriş, Lisans ve Servis sayfaları çalışır.
    /// <see cref="LicenseManager"/> yönetir; açılışta kayıtlı lisans okunana kadar false.
    /// </summary>
    internal static class LicenseGate
    {
        private static volatile bool _active;

        public static bool Active
        {
            get { return _active; }
            internal set { _active = value; }
        }

        /// <summary>Lisans durumu olayları kapı kapalıyken de kaydedilir / maillenir.</summary>
        public static bool AlwaysRecorded(string eventType)
        {
            return eventType != null && eventType.StartsWith("Guard.License", System.StringComparison.Ordinal);
        }
    }
}
