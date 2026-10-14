; SecurityGuard kurulum paketi (Inno Setup 6)
; Derleme: Inno Setup'ı kurun, sonra bu dosyayı ISCC.exe ile derleyin ya da Inno Setup Compiler'da açıp Build (F9).
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SecurityGuard.iss
; Üretilen setup.exe: installer\Output\SecurityGuard-Setup-<surum>.exe
;
; ÖNEMLİ: Önce Release derlemesi alın (bin\Release\SecurityGuard\{Service,UI} dolu olmalı).
; Servis KORUMALI olduğundan kaldırma/güncelleme kendi UI'si üzerinden kimlikli yapılır (aşağıdaki [Code]).

#define AppName "SecurityGuard"
#define AppVersion "1.0.0"
#define AppPublisher "SecurityGuard"
#define SrcRoot "..\bin\Release\SecurityGuard"
#define ServiceExe "SecurityGuardService.exe"
#define UiExe "SecurityGuardUI.exe"
#define ServiceName "SecurityGuard"

[Setup]
AppId={{A7F3C2E1-5B9D-4E6A-8C1F-3D2B7A9E4F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SecurityGuard-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
; Windows Server 2012 / Windows 8 ve üstü (6.2+)
MinVersion=6.2
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\UI\{#UiExe}
WizardStyle=modern

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Files]
; Servis klasörü (exe + bağımlılıklar + GeoIP + SQLite x64/x86). guard.db KOPYALANMAZ: her makinede sıfırdan oluşur.
Source: "{#SrcRoot}\Service\*"; DestDir: "{app}\Service"; Flags: recursesubdirs ignoreversion; Excludes: "*.pdb"
; Arayüz klasörü (DevExpress + UI + SgAutoTest)
Source: "{#SrcRoot}\UI\*"; DestDir: "{app}\UI"; Flags: recursesubdirs ignoreversion; Excludes: "*.pdb"
; Merkezi güncelleme aracı (kök altındaki Updater klasöründe; Service/UI değiştirilirken kilitlenmez)
Source: "{#SrcRoot}\SecurityGuardUpdater.exe"; DestDir: "{app}\Updater"; Flags: ignoreversion

[Icons]
Name: "{group}\SecurityGuard"; Filename: "{app}\UI\{#UiExe}"
Name: "{group}\SecurityGuard Güncelle"; Filename: "{app}\Updater\SecurityGuardUpdater.exe"
Name: "{commondesktop}\SecurityGuard"; Filename: "{app}\UI\{#UiExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Kısayollar:"

[Run]
; Windows servisini kur + başlat (servis exe kendi --install --start komutuyla; kurtarma ayarları da burada yapılır).
Filename: "{app}\Service\{#ServiceExe}"; Parameters: "--install --start"; StatusMsg: "SecurityGuard servisi kuruluyor ve başlatılıyor..."; Flags: runhidden waituntilterminated
; Kurulum sonrası arayüzü aç (ilk kurulum sihirbazı: SQL bağlantısı, mail, lisans).
; shellexec: UI yönetici yetkisi ister (manifest requireAdministrator). Düz CreateProcess bunu kaldıramaz (kod 740);
; ShellExecute manifest'i dikkate alıp yükseltir (installer zaten admin olduğundan ekstra UAC çıkmaz).
Filename: "{app}\UI\{#UiExe}"; Description: "SecurityGuard'ı aç ve kurulumu tamamla (SQL / mail / lisans)"; Flags: nowait postinstall skipifsilent shellexec

[Code]
const
  NET48_RELEASE = 528040;

var
  ErrorCodeDummy: Integer;

// .NET Framework 4.8 kurulu mu? (kayıt defteri Release >= 528040)
function Net48Installed(): Boolean;
var
  release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release)
            and (release >= NET48_RELEASE);
end;

// SecurityGuard servisi Windows'a kurulu mu? (sc query çıkış 0)
function ServiceInstalled(): Boolean;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCodeDummy)
            and (ErrorCodeDummy = 0);
end;

// Güncelleme: servis kuruluysa çalışan exe kilitli olur. Yeni dosyaları kopyalamadan önce kimlikli durdur.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  uiExe: string;
  resultCode: Integer;
begin
  Result := '';
  uiExe := ExpandConstant('{app}\UI\{#UiExe}');
  if ServiceInstalled() and FileExists(uiExe) then
  begin
    if not Exec(uiExe, '--stop', '', SW_SHOW, ewWaitUntilTerminated, resultCode) then
      Result := 'Guncelleme icin servis durdurulamadi (arac calismadi).'
    else if resultCode <> 0 then
      Result := 'Guncelleme iptal edildi: servisi durdurmak icin SG yonetici girisi / sifresi gerekli.';
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not Net48Installed() then
  begin
    if MsgBox('SecurityGuard için Microsoft .NET Framework 4.8 gerekli ama bu sunucuda kurulu değil.' + #13#10 + #13#10 +
              'İndirme sayfasını açmak ister misiniz? (Kurduktan sonra bu kurulumu tekrar çalıştırın.)',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOW, ewNoWait, ErrorCodeDummy);
    Result := False;
  end;
end;

// Kaldırma: servis KORUMALI olduğundan sc ile durdurulamaz. UI'nin kimlikli --uninstall modu çalışır:
// SG yönetici girişi + şifre ister, servise kendini sildirir. Vazgeçilir / şifre yanlışsa kaldırma durur.
procedure CurUninstallStepChanged(CurStep: TUninstallStep);
var
  resultCode: Integer;
  params: string;
  uiExe: string;
begin
  if CurStep = usUninstall then
  begin
    uiExe := ExpandConstant('{app}\UI\{#UiExe}');
    if not FileExists(uiExe) then
      Exit; // arayüz yoksa (yarım kurulum) normal dosya temizliğiyle devam

    if MsgBox('Veritabanını ve logları da silmek ister misiniz?' + #13#10 +
              '(Hayır = korunur, tekrar kurulduğunda kullanılabilir)',
              mbConfirmation, MB_YESNO) = IDYES then
      params := '--uninstall --delete-data'
    else
      params := '--uninstall';

    if not Exec(uiExe, params, '', SW_SHOW, ewWaitUntilTerminated, resultCode) then
    begin
      MsgBox('Kaldırma aracı çalıştırılamadı. Kaldırma durduruldu.', mbError, MB_OK);
      Abort;
    end;

    if resultCode <> 0 then
    begin
      MsgBox('Kaldırma iptal edildi veya tamamlanamadı (SG yönetici girişi / şifre gerekli).' + #13#10 +
             'Kaldırma durduruldu; SecurityGuard kurulu kaldı.', mbError, MB_OK);
      Abort;
    end;
  end;
end;
