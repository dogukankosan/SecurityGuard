namespace SecurityGuard.Shared.Contracts
{
    /// <summary>UI'den servise giden istek.</summary>
    public sealed class PipeRequest
    {
        /// <summary>Komut adı (<see cref="Commands"/>).</summary>
        public string Command { get; set; }

        /// <summary>Oturum token'ı. Giriş gerektirmeyen komutlarda boş.</summary>
        public string Token { get; set; }

        /// <summary>Komuta özel veri (JSON).</summary>
        public string Payload { get; set; }
    }
}
