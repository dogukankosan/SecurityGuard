using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SecurityGuard.Shared;
using SecurityGuardService.Engine.Firewall;

namespace SecurityGuardService.Engine.Ban
{
    /// <summary>Ban kurallarının firewall tarafı (test için arayüz; gerçeği <see cref="WindowsBanFirewall"/>).</summary>
    internal interface IBanFirewall
    {
        /// <summary>Bizim kurallarımız (ad ön eki <see cref="GuardConstants.FirewallRulePrefix"/>): ad → uzak adresler.</summary>
        Dictionary<string, List<string>> ListRules();

        /// <summary>Kuralı verilen adreslerle oluşturur veya adreslerini günceller.</summary>
        void SetRule(string name, IList<string> addresses);

        void DeleteRule(string name);
    }

    /// <summary>
    /// Windows Firewall (HNetCfg.FwPolicy2 COM). Sadece adı "SecurityGuard-Block-" ile başlayan kurallara dokunur;
    /// başka hiçbir firewall kuralı / ayarı okunup değiştirilmez. Kural: gelen, engelle, tüm protokol / port / profil.
    /// Yönetici (servis: SYSTEM) yetkisi gerekir.
    /// </summary>
    internal sealed class WindowsBanFirewall : IBanFirewall
    {
        private const int DirectionIn = 1;
        private const int ActionBlock = 0;
        private const int ProtocolAny = 256;
        private const int ProfilesAll = 0x7FFFFFFF;
        private const string Grouping = "SecurityGuard";

        public Dictionary<string, List<string>> ListRules()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            object policy = FirewallCom.CreatePolicy();
            try
            {
                FirewallCom.ForEachRule(policy, rule =>
                {
                    string name = FirewallCom.Str(rule, "Name");
                    if (name == null || !IsOurs(name)) return;
                    result[name] = ParseAddresses(FirewallCom.Str(rule, "RemoteAddresses"));
                });
            }
            finally
            {
                FirewallCom.Release(policy);
            }
            return result;
        }

        public void SetRule(string name, IList<string> addresses)
        {
            if (!IsOurs(name)) throw new InvalidOperationException("Sadece SecurityGuard kurallarına dokunulur: " + name);
            if (addresses == null || addresses.Count == 0) throw new ArgumentException("Adres listesi boş.", "addresses");

            string remote = string.Join(",", addresses);
            object policy = FirewallCom.CreatePolicy();
            try
            {
                object existing = FirewallCom.FindRule(policy, name);
                if (existing != null)
                {
                    try
                    {
                        // Adres dışındaki alanlar da her seferinde düzeltilir (biri kuralı gevşettiyse geri gelir)
                        FirewallCom.Set(existing, "RemoteAddresses", remote);
                        FirewallCom.Set(existing, "Direction", DirectionIn);
                        FirewallCom.Set(existing, "Action", ActionBlock);
                        FirewallCom.Set(existing, "Profiles", ProfilesAll);
                        FirewallCom.Set(existing, "Enabled", true);
                    }
                    finally
                    {
                        FirewallCom.Release(existing);
                    }
                    return;
                }

                object rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
                try
                {
                    FirewallCom.Set(rule, "Name", name);
                    FirewallCom.Set(rule, "Description", "SecurityGuard ban kuralı. Arayüzden yönetilir; elle değiştirmeyin.");
                    FirewallCom.Set(rule, "Grouping", Grouping);
                    FirewallCom.Set(rule, "Protocol", ProtocolAny);
                    FirewallCom.Set(rule, "Direction", DirectionIn);
                    FirewallCom.Set(rule, "Action", ActionBlock);
                    FirewallCom.Set(rule, "Profiles", ProfilesAll);
                    FirewallCom.Set(rule, "RemoteAddresses", remote);
                    FirewallCom.Set(rule, "Enabled", true);
                    FirewallCom.WithRules<object>(policy, rules => FirewallCom.Call(rules, "Add", rule));
                }
                finally
                {
                    FirewallCom.Release(rule);
                }
            }
            finally
            {
                FirewallCom.Release(policy);
            }
        }

        public void DeleteRule(string name)
        {
            if (!IsOurs(name)) throw new InvalidOperationException("Sadece SecurityGuard kurallarına dokunulur: " + name);
            object policy = FirewallCom.CreatePolicy();
            try
            {
                // Aynı adla birden fazla kural olabilir (elle kopyalanmışsa): hepsi silinir
                while (true)
                {
                    object existing = FirewallCom.FindRule(policy, name);
                    if (existing == null) break;
                    FirewallCom.Release(existing);
                    FirewallCom.WithRules<object>(policy, rules => FirewallCom.Call(rules, "Remove", name));
                }
            }
            finally
            {
                FirewallCom.Release(policy);
            }
        }

        internal static bool IsOurs(string name)
        {
            return name.StartsWith(GuardConstants.FirewallRulePrefix + "-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Firewall adresleri normalize eder: "1.2.3.4/255.255.255.255" → "1.2.3.4". Karşılaştırma bu biçimle yapılır.
        /// </summary>
        internal static List<string> ParseAddresses(string remote)
        {
            if (string.IsNullOrWhiteSpace(remote) || remote == "*") return new List<string>();
            return remote.Split(',')
                .Select(a => a.Trim())
                .Where(a => a.Length > 0)
                .Select(a => a.EndsWith("/255.255.255.255", StringComparison.Ordinal) ? a.Substring(0, a.Length - 16)
                           : a.EndsWith("/128", StringComparison.Ordinal) ? a.Substring(0, a.Length - 4) : a)
                .ToList();
        }
    }
}
