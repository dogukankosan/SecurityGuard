using System;
using System.Collections.Generic;
using System.Threading;
using SecurityGuard.Shared.Dto;
using SecurityGuard.Shared.Enums;
using SecurityGuardService.Core.Logging;
using SecurityGuardService.Core.Settings;

namespace SecurityGuardService.Checks
{
    /// <summary>
    /// Bir taramanın çalışma ortamı: maddeler buraya eklenir. Her kontrol <see cref="Run"/> ile çalışır:
    /// biri hata verirse sadece o madde "Kontrol edilemedi" olur, tarama devam eder.
    /// </summary>
    internal sealed class CheckContext
    {
        private const string Module = "Tarama";
        private readonly Action<string> _progress;

        public CheckContext(SettingsStore settings, CancellationToken token, Action<string> progress)
        {
            Settings = settings;
            Token = token;
            _progress = progress ?? (s => { });
            Items = new List<CheckItemDto>();
            Shared = new Dictionary<string, object>();
        }

        public SettingsStore Settings { get; private set; }
        public CancellationToken Token { get; private set; }
        public List<CheckItemDto> Items { get; private set; }

        /// <summary>Kontroller arası paylaşılan bilgi (ör. SQL veri klasörleri → antivirüs kontrolü).</summary>
        public Dictionary<string, object> Shared { get; private set; }

        public void Progress(string text)
        {
            _progress(text);
        }

        public void Add(CheckItemDto item)
        {
            if (item != null) Items.Add(item);
        }

        /// <summary>Kontrolü güvenli çalıştırır. Kontrol null dönerse madde eklenmez (ör. uygulanamaz).</summary>
        public void Run(string checkId, string area, string title, Func<CheckItemDto> check)
        {
            Token.ThrowIfCancellationRequested();
            Progress(area + ": " + title);
            try
            {
                var item = check();
                if (item == null) return;
                item.CheckId = item.CheckId ?? checkId;
                item.Area = item.Area ?? area;
                item.Title = item.Title ?? title;
                Add(item);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (Token.IsCancellationRequested) throw new OperationCanceledException(Token);
                AppLog.Warn(Module, "Kontrol yapılamadı: " + area + " / " + title + ": " + ex.Message);
                Add(Item(checkId, area, title, CheckStatus.Hata, "Kontrol yapılamadı: " + ex.Message));
            }
        }

        public static CheckItemDto Item(string checkId, string area, string title, CheckStatus status, string summary)
        {
            return new CheckItemDto { CheckId = checkId, Area = area, Title = title, Status = status, Summary = summary };
        }
    }
}
