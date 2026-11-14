using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class GoogleConnectionService(GoogleAuth auth, GoogleSheets sheets, YouTubeDestination youtube) : IGoogleConnectionService
{
    public async Task<SheetSelection> SelectSheetAsync(CancellationToken ct)
    {
        var result = await auth.ConnectAsync(true, ct);
        try { return await sheets.SelectAsync(result, ct); }
        catch { await auth.DisconnectLocalAsync(result.CredentialId); throw; }
    }
    public async Task<ChannelTarget> SelectChannelAsync(CancellationToken ct)
    {
        var result = await auth.ConnectAsync(false, ct);
        try { return await youtube.ChannelAsync(result.CredentialId, ct); }
        catch { await auth.DisconnectLocalAsync(result.CredentialId); throw; }
    }
    public Task CheckSheetAsync(SheetTarget target, CancellationToken ct) => sheets.PreflightAsync(target, ct);
    public Task ForgetAsync(string credential, CancellationToken ct) => auth.DisconnectLocalAsync(credential, ct);
}
