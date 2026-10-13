namespace SecurityGuard.Shared.Contracts
{
    /// <summary>Servisten UI'ye dönen cevap.</summary>
    public sealed class PipeResponse
    {
        public bool Success { get; set; }

        /// <summary>Hata kodu (<see cref="ErrorCodes"/>). Başarılıysa boş.</summary>
        public string ErrorCode { get; set; }

        /// <summary>Kullanıcıya gösterilecek Türkçe hata mesajı.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Komuta özel sonuç (JSON).</summary>
        public string Payload { get; set; }

        public static PipeResponse Ok(object payload = null)
        {
            return new PipeResponse
            {
                Success = true,
                Payload = payload == null ? null : PipeProtocol.ToJson(payload)
            };
        }

        public static PipeResponse Fail(string errorCode, string errorMessage)
        {
            return new PipeResponse { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
        }
    }
}
