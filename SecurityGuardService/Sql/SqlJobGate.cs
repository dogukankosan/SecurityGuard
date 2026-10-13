using System;
using System.Threading;

namespace SecurityGuardService.Sql
{
    /// <summary>
    /// Ağır SQL işleri (bakım ve yedekleme) aynı anda çalışmaz: biri sürerken diğeri bekler.
    /// İkisi de diski ve SQL Server'ı yoğun kullanır; birlikte çalışınca ikisi de uzar ve kullanıcılar yavaşlar.
    /// </summary>
    internal static class SqlJobGate
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static string _holder;

        /// <summary>Şu an süren iş (yoksa null).</summary>
        public static string Holder { get { return _holder; } }

        /// <summary>Kapıyı alır; diğer iş sürüyorsa bekler (en fazla <paramref name="maxWait"/>). false = alınamadı.</summary>
        public static bool Enter(string who, TimeSpan maxWait, CancellationToken token, Action<string> waiting)
        {
            if (Gate.Wait(0)) { _holder = who; return true; }
            if (waiting != null) waiting(_holder ?? "başka bir SQL işi");
            try
            {
                if (!Gate.Wait(maxWait, token)) return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            _holder = who;
            return true;
        }

        public static void Exit()
        {
            _holder = null;
            Gate.Release();
        }
    }
}
