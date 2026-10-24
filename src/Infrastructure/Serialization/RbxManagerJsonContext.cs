namespace RbxManager.Infrastructure.Serialization;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Backup;
using RbxManager.Infrastructure.Servers;
using RbxManager.Infrastructure.Storage;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AdvancedSettings))]
[JsonSerializable(typeof(UiStateSettings))]
[JsonSerializable(typeof(List<AccountDto>))]
[JsonSerializable(typeof(AccountDto))]
[JsonSerializable(typeof(List<EncryptedBackupService.BackupAccountDto>))]
[JsonSerializable(typeof(EncryptedBackupService.BackupAccountDto))]
[JsonSerializable(typeof(RobloxAuthService.AuthenticatedUserDto))]
[JsonSerializable(typeof(ServerBrowserService.RobloxServerListResponse))]
[JsonSerializable(typeof(ServerBrowserService.RobloxServerItem))]
internal sealed partial class RbxManagerJsonContext : JsonSerializerContext
{
    public static RbxManagerJsonContext Indented { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true
    });
}
