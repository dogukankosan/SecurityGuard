using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SecurityGuardService.Engine.Firewall
{
    /// <summary>
    /// Windows Firewall COM (HNetCfg.FwPolicy2) yardımcıları.
    /// Kural koleksiyonu (Rules) her alındığında Windows tüm kuralların kopyasını çıkarır; koleksiyon, numaralandırıcı ve
    /// her kural nesnesi açıkça bırakılmazsa bu kopyalar bellekte kalır (çağrı başına ~1 MB). Bu yüzden:
    /// - koleksiyon / numaralandırıcı / kural nesneleri burada tek yerden bırakılır,
    /// - özellikler "dynamic" yerine geç bağlama (InvokeMember) ile okunur (dynamic her nesne için ek sarmalayıcı tutar).
    /// </summary>
    internal static class FirewallCom
    {
        public static object CreatePolicy()
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true));
        }

        public static object Get(object com, string property, params object[] args)
        {
            return com.GetType().InvokeMember(property, BindingFlags.GetProperty, null, com, args.Length == 0 ? null : args);
        }

        public static void Set(object com, string property, object value)
        {
            com.GetType().InvokeMember(property, BindingFlags.SetProperty, null, com, new[] { value });
        }

        public static object Call(object com, string method, params object[] args)
        {
            return com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args);
        }

        public static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }

        /// <summary>Policy'nin kural koleksiyonuyla çalışır ve koleksiyonu bırakır.</summary>
        public static T WithRules<T>(object policy, Func<object, T> work)
        {
            object rules = Get(policy, "Rules");
            try
            {
                return work(rules);
            }
            finally
            {
                Release(rules);
            }
        }

        /// <summary>Tüm kuralları gezer; her kural, numaralandırıcı ve koleksiyon bırakılır. Tek kuraldaki hata diğerlerini etkilemez.</summary>
        public static void ForEachRule(object policy, Action<object> body)
        {
            WithRules<object>(policy, rules =>
            {
                IEnumerator en = ((IEnumerable)rules).GetEnumerator();
                try
                {
                    while (en.MoveNext())
                    {
                        object rule = en.Current;
                        try
                        {
                            body(rule);
                        }
                        finally
                        {
                            Release(rule);
                        }
                    }
                }
                finally
                {
                    // Numaralandırıcı (IEnumVARIANT) koleksiyonun kopyasını canlı tutar: o da bırakılır
                    var adapter = en as ICustomAdapter;
                    if (adapter != null) Release(adapter.GetUnderlyingObject());
                    var disposable = en as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
                return null;
            });
        }

        /// <summary>Adıyla kural; yoksa null. Dönen nesneyi çağıran bırakır.</summary>
        public static object FindRule(object policy, string name)
        {
            return WithRules(policy, rules =>
            {
                try
                {
                    return Call(rules, "Item", name);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is COMException || ex.InnerException is System.IO.FileNotFoundException)
                {
                    return null;
                }
                catch (COMException)
                {
                    return null;
                }
                catch (System.IO.FileNotFoundException)
                {
                    return null;
                }
            });
        }

        public static string Str(object com, string property)
        {
            return Get(com, property) as string;
        }

        public static int Int(object com, string property)
        {
            return Convert.ToInt32(Get(com, property));
        }

        public static bool Bool(object com, string property)
        {
            return Convert.ToBoolean(Get(com, property));
        }
    }
}
