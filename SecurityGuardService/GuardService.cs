using System.ServiceProcess;
using System.Threading;

namespace SecurityGuardService
{
    public partial class GuardService : ServiceBase
    {
        private readonly GuardHost _host = new GuardHost(isWindowsService: true);

        public GuardService()
        {
            InitializeComponent();
            CanShutdown = true;
            // UI'den durdurma: servis kendi Stop'unu çağırır (ileride Windows'tan dışarıdan durdurma kapatılacak)
            _host.StopRequested += () => new Thread(Stop) { IsBackground = true }.Start();
        }

        protected override void OnStart(string[] args)
        {
            _host.Start();
        }

        protected override void OnStop()
        {
            RequestAdditionalTime(30000); // kapanış maili için
            _host.Stop();
        }

        protected override void OnShutdown()
        {
            _host.Stop(shutdown: true);
        }
    }
}
