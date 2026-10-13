using System.Threading.Tasks;
using SecurityGuard.Shared.Contracts;
using SecurityGuard.Shared.Dto;

namespace SecurityGuardService.Ipc.Handlers
{
    internal static class MailHandlers
    {
        public static void Register(CommandRouter router)
        {
            router.Register(Commands.MailGetSettings, true, true,
                r => Task.FromResult(PipeResponse.Ok(r.Guard.Mail.GetSettings())));

            router.Register(Commands.MailSaveSettings, true, true, async r =>
            {
                await r.Guard.Mail.SaveSettingsAsync(r.Payload<MailSettingsDto>(), r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Mail.GetSettings());
            });

            // SMTP bağlantısı uzun sürebilir: pipe iş parçacığını bloklamamak için arka planda
            router.Register(Commands.MailTest, true, true, async r =>
            {
                var req = r.Payload<MailTestRequest>();
                await Task.Run(() => r.Guard.Mail.SendTest(req, r.Session.UserName)).ConfigureAwait(false);
                return PipeResponse.Ok();
            });

            router.Register(Commands.MailRecipientsList, true, true,
                r => Task.FromResult(PipeResponse.Ok(r.Guard.Mail.ListRecipients())));

            router.Register(Commands.MailRecipientAdd, true, true, async r =>
            {
                var req = r.Payload<RecipientAddRequest>();
                await r.Guard.Mail.AddRecipientAsync(req.Email, req.DisplayName, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Mail.ListRecipients());
            });

            router.Register(Commands.MailRecipientDelete, true, true, async r =>
            {
                await r.Guard.Mail.DeleteRecipientsAsync(r.Payload<IdListRequest>().Ids, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(r.Guard.Mail.ListRecipients());
            });

            router.Register(Commands.MailRecipientSetEnabled, true, true, async r =>
            {
                var req = r.Payload<RecipientSetEnabledRequest>();
                await r.Guard.Mail.SetRecipientEnabledAsync(req.Id, req.IsEnabled, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok();
            });

            router.Register(Commands.MailSignatureGet, true, true,
                r => Task.FromResult(PipeResponse.Ok(r.Guard.Mail.GetSignature())));

            router.Register(Commands.MailSignatureSave, true, true, async r =>
            {
                await r.Guard.Mail.SaveSignatureAsync(r.Payload<SignatureSaveRequest>().Html, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok();
            });

            router.Register(Commands.MailSignatureAddImage, true, true, async r =>
            {
                var req = r.Payload<SignatureImageDto>();
                var added = await r.Guard.Mail.AddImageAsync(req.FileName, req.Base64, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok(added);
            });

            router.Register(Commands.MailSignatureDeleteImage, true, true, async r =>
            {
                await r.Guard.Mail.DeleteImageAsync(r.Payload<ContentIdRequest>().ContentId, r.Session.UserName).ConfigureAwait(false);
                return PipeResponse.Ok();
            });
        }
    }
}
