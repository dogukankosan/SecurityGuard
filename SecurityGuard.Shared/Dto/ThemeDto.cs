namespace SecurityGuard.Shared.Dto
{
    /// <summary>UI teması (DevExpress skin + renk paleti).</summary>
    public sealed class ThemeDto
    {
        /// <summary>DevExpress skin adı (ör. "Office 2019 Black").</summary>
        public string SkinName { get; set; }

        /// <summary>Vektör skinlerde renk paleti adı; yoksa boş.</summary>
        public string PaletteName { get; set; }
    }
}
