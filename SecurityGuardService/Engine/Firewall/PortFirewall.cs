using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using SecurityGuard.Shared;

namespace SecurityGuardService.Engine.Firewall
{
    /// <summary>Port kısıtının tek firewall kuralı (gelen, engelle, tek protokol).</summary>
    internal sealed class PortRule
    {
        public const int Tcp = 6;
        public const int Udp = 17;

        public string Name;
        public string Description;
        public int Protocol;
        public string LocalPorts;
        public string RemoteAddresses;

        // Sadece okunan kuralda
        public bool Enabled = true;
        public bool IsBlockIn = true;
        public bool AllProfiles = true;
    }

    /// <summary>Port kısıtı kurallarının firewall tarafı (test için arayüz; gerçeği <see cref="WindowsPortFirewall"/>).</summary>
    internal interface IPortFirewall
    {
        /// <summary>Bizim kısıt kurallarımız (ad ön eki <see cref="GuardConstants.PortRestrictionRulePrefix"/>).</summary>
        List<PortRule> ListRules();

        void SetRule(PortRule rule);

        void DeleteRule(string name);

        /// <summary>Etkin profillerden birinde firewall kapalıysa açıklama, değilse null.</summary>
        string FirewallOffWarning();
    }

    /// <summary>
    /// Windows Firewall (HNetCfg COM). Sadece adı "SecurityGuard-Restrict-" ile başlayan kurallara dokunur.
    /// Kural: gelen, ENGELLE, tek protokol, belirli portlar, uzak adres = izinli IP'lerin dışındaki her şey.
    /// Engelle kuralı izin kurallarından önce gelir; Windows'un kendi "Uzak Masaüstü" izin kuralı açık kalsa da kısıt geçerlidir.
    /// </summary>
    internal sealed class WindowsPortFirewall : IPortFirewall
    {
        private const int DirectionIn = 1;
        private const int ActionBlock = 0;
        private const int ProfilesAll = 0x7FFFFFFF;
        private const string Grouping = "SecurityGuard";

        public List<PortRule> ListRules()
        {
            var result = new List<PortRule>();
            object policy = FirewallCom.CreatePolicy();
            try
            {
                FirewallCom.ForEachRule(policy, rule =>
                {
                    string name = FirewallCom.Str(rule, "Name");
                    if (name == null || !IsOurs(name)) return;
                    result.Add(new PortRule
                    {
                        Name = name,
                        Description = FirewallCom.Str(rule, "Description"),
                        Protocol = FirewallCom.Int(rule, "Protocol"),
                        LocalPorts = FirewallCom.Str(rule, "LocalPorts"),
                        RemoteAddresses = FirewallCom.Str(rule, "RemoteAddresses"),
                        Enabled = FirewallCom.Bool(rule, "Enabled"),
                        IsBlockIn = FirewallCom.Int(rule, "Direction") == DirectionIn && FirewallCom.Int(rule, "Action") == ActionBlock,
                        AllProfiles = (FirewallCom.Int(rule, "Profiles") & 7) == 7
                    });
                });
            }
            finally
            {
                FirewallCom.Release(policy);
            }
            return result;
        }

        public void SetRule(PortRule spec)
        {
            if (!IsOurs(spec.Name)) throw new InvalidOperationException("Sadece SecurityGuard kurallarına dokunulur: " + spec.Name);
            if (string.IsNullOrEmpty(spec.RemoteAddresses)) throw new ArgumentException("Adres listesi boş.");

            object policy = FirewallCom.CreatePolicy();
            try
            {
                object existing = FirewallCom.FindRule(policy, spec.Name);
                if (existing != null)
                {
                    bool sameProtocol;
                    try
                    {
                        sameProtocol = FirewallCom.Int(existing, "Protocol") == spec.Protocol;
                        if (sameProtocol)
                        {
                            // Tüm alanlar her seferinde yazılır (biri kuralı gevşettiyse geri gelir)
                            FirewallCom.Set(existing, "LocalPorts", spec.LocalPorts);
                            FirewallCom.Set(existing, "RemoteAddresses", spec.RemoteAddresses);
                            FirewallCom.Set(existing, "Direction", DirectionIn);
                            FirewallCom.Set(existing, "Action", ActionBlock);
                            FirewallCom.Set(existing, "Profiles", ProfilesAll);
                            FirewallCom.Set(existing, "Description", spec.Description);
                            FirewallCom.Set(existing, "Enabled", true);
                        }
                    }
                    finally
                    {
                        FirewallCom.Release(existing);
                    }
                    if (sameProtocol) return;
                    RemoveAll(policy, spec.Name); // protokol değişmiş: port alanı protokole bağlı, yeniden oluşturulur
                }

                object rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
                try
                {
                    FirewallCom.Set(rule, "Name", spec.Name);
                    FirewallCom.Set(rule, "Description", spec.Description);
                    FirewallCom.Set(rule, "Grouping", Grouping);
                    FirewallCom.Set(rule, "Protocol", spec.Protocol); // port alanından önce
                    FirewallCom.Set(rule, "LocalPorts", spec.LocalPorts);
                    FirewallCom.Set(rule, "Direction", DirectionIn);
                    FirewallCom.Set(rule, "Action", ActionBlock);
                    FirewallCom.Set(rule, "Profiles", ProfilesAll);
                    FirewallCom.Set(rule, "RemoteAddresses", spec.RemoteAddresses);
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
                RemoveAll(policy, name);
            }
            finally
            {
                FirewallCom.Release(policy);
            }
        }

        public string FirewallOffWarning()
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true);
            object policy = Activator.CreateInstance(type);
            try
            {
                int current = (int)type.InvokeMember("CurrentProfileTypes", BindingFlags.GetProperty, null, policy, null);
                var off = new List<string>();
                foreach (var p in new[] { Tuple.Create(1, "Etki alanı"), Tuple.Create(2, "Özel"), Tuple.Create(4, "Genel") })
                {
                    if ((current & p.Item1) == 0) continue;
                    if (!(bool)type.InvokeMember("FirewallEnabled", BindingFlags.GetProperty, null, policy, new object[] { p.Item1 })) off.Add(p.Item2);
                }
                return off.Count == 0 ? null
                    : "Windows Firewall şu an etkin profilde KAPALI (" + string.Join(", ", off) + "). Kapalıyken port kısıtları da ban kuralları da çalışmaz.";
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }

        internal static bool IsOurs(string name)
        {
            return name.StartsWith(GuardConstants.PortRestrictionRulePrefix + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveAll(object policy, string name)
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
    }
}
