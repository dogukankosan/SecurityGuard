using System.Collections.Generic;

namespace SecurityGuard.Shared.Dto
{
    /// <summary>Genel Ayarlar sayfasındaki bir ayar: değer + nasıl düzenleneceği.</summary>
    public sealed class GeneralSettingDto
    {
        public string Key { get; set; }
        public string Group { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }

        /// <summary>Düzenleyici tipi: "int", "bool", "time" (HH:mm) veya "choice".</summary>
        public string Kind { get; set; }

        /// <summary>Metin olarak değer: int → "30", bool → "true"/"false", time → "20:00", choice → seçenek değeri.</summary>
        public string Value { get; set; }

        public string DefaultValue { get; set; }

        /// <summary>int için alt / üst sınır.</summary>
        public int Min { get; set; }
        public int Max { get; set; }

        /// <summary>int için birim (dakika, %...).</summary>
        public string Unit { get; set; }

        /// <summary>choice için seçenekler.</summary>
        public List<SettingOptionDto> Options { get; set; }
    }

    public sealed class SettingOptionDto
    {
        public string Value { get; set; }
        public string Text { get; set; }
    }

    public sealed class GeneralSettingsSaveRequest
    {
        public List<SettingValueDto> Values { get; set; }
    }

    public sealed class SettingValueDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
