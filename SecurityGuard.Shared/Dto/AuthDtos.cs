namespace SecurityGuard.Shared.Dto
{
    public sealed class CreateAdminRequest
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public sealed class LoginRequest
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    public sealed class LoginResult
    {
        public string Token { get; set; }
        public string UserName { get; set; }

        /// <summary>İşlem yapılmazsa oturumun kapanacağı süre (dakika).</summary>
        public int IdleTimeoutMinutes { get; set; }
    }
}
